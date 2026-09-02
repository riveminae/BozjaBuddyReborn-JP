using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using BozjaBuddyReborn.External;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Bounded, non-blocking cache for vnavmesh.Nav.Pathfind distances.
///
/// BOCCHI ranks traversal candidates with actual path lengths rather than straight-line distance.
/// The IPC returns a Task, so the controller tick must never wait for it. This cache starts at most
/// one query at a time, returns the caller's horizontal fallback until the task finishes, and then
/// reuses the measured path length for nearby endpoints.
///
/// It never starts movement and never calls Path.MoveTo/SimpleMove; it is cost telemetry only.
/// </summary>
internal sealed class NavPathCostCache(NavmeshIpc navmesh)
{
    private readonly NavmeshIpc _navmesh = navmesh;
    private readonly Dictionary<Key, Entry> _entries = [];

    private const float QuantizeYalms = 4f;
    private const int MaxEntries = 96;
    private const int MaxPending = 1;
    private const long SuccessTtlMs = 20_000;
    private const long FailureRetryMs = 3_000;

    public long Generation { get; private set; }

    /// <summary>
    /// Return a measured ground-path distance when cached; otherwise return fallback immediately.
    /// When request=true, a missing entry may start one asynchronous vnavmesh query.
    /// </summary>
    public float Estimate(Vector3 from, Vector3 to, float fallback, bool request)
    {
        var now = Environment.TickCount64;
        Poll(now);

        var key = Key.For(from, to);
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.LastAccessMs = now;

            if (existing.Cost is { } cost && now <= existing.ExpiresMs)
                return cost;

            if (existing.Pending is not null)
                return fallback;

            if (now < existing.ExpiresMs)
                return fallback; // recent failure; do not hammer the same route

            _entries.Remove(key);
        }

        if (!request || !_navmesh.MeshReady || PendingCount() >= MaxPending)
            return fallback;

        var task = _navmesh.Pathfind(from, to, fly: false);
        if (task is null)
            return fallback;

        _entries[key] = new Entry(from, to, task, now);
        PruneIfNeeded();
        return fallback;
    }

    /// <summary>Observe completed tasks without scheduling a new query.</summary>
    public bool Poll()
    {
        var before = Generation;
        Poll(Environment.TickCount64);
        return Generation != before;
    }

    public void Clear()
    {
        // We deliberately do not try to cancel vnavmesh's task here: this wrapper bound only the
        // ordinary Pathfind gate. Dropping references is enough; results from an old territory are
        // never consumed after the dictionary is cleared.
        foreach (var entry in _entries.Values)
            ObserveFault(entry.Pending);
        _entries.Clear();
        Generation++;
    }

    private void Poll(long now)
    {
        foreach (var entry in _entries.Values)
        {
            var pending = entry.Pending;
            if (pending is null || !pending.IsCompleted)
                continue;

            entry.Pending = null;
            if (pending.IsCompletedSuccessfully)
            {
                var distance = Measure(entry.From, entry.To, pending.Result);
                if (distance is { } measured && float.IsFinite(measured) && measured > 0f)
                {
                    entry.Cost = measured;
                    entry.ExpiresMs = now + SuccessTtlMs;
                    Generation++;
                    continue;
                }
            }
            else
            {
                ObserveFault(pending);
            }

            // Empty/unreachable/faulted path: fail open to the horizontal estimate and retry only
            // after a short cooldown. A transient pathfinder failure must never make a destination
            // appear infinitely expensive or strand the runner.
            entry.Cost = null;
            entry.ExpiresMs = now + FailureRetryMs;
            Generation++;
        }
    }

    private int PendingCount()
    {
        var count = 0;
        foreach (var entry in _entries.Values)
            if (entry.Pending is not null && !entry.Pending.IsCompleted)
                count++;
        return count;
    }

    private void PruneIfNeeded()
    {
        while (_entries.Count > MaxEntries)
        {
            Key? oldestKey = null;
            var oldest = long.MaxValue;
            foreach (var pair in _entries)
            {
                // Do not evict the one live query; it would continue running without a place to
                // observe its exception/result.
                if (pair.Value.Pending is not null && !pair.Value.Pending.IsCompleted)
                    continue;
                if (pair.Value.LastAccessMs >= oldest)
                    continue;
                oldest = pair.Value.LastAccessMs;
                oldestKey = pair.Key;
            }

            if (oldestKey is not { } key)
                return;

            if (_entries.TryGetValue(key, out var entry))
                ObserveFault(entry.Pending);
            _entries.Remove(key);
        }
    }

    private static float? Measure(Vector3 from, Vector3 to, IReadOnlyList<Vector3> waypoints)
    {
        if (waypoints.Count == 0)
            return null;

        var total = 0f;
        var previous = from;
        foreach (var point in waypoints)
        {
            total += Vector3.Distance(previous, point);
            previous = point;
        }

        // Nav.Pathfind normally includes its exact destination. Adding the final residual makes
        // the calculation robust to providers that terminate within a small tolerance.
        total += Vector3.Distance(previous, to);
        return total;
    }

    private static void ObserveFault(Task<List<Vector3>>? task)
    {
        if (task is { IsFaulted: true })
            _ = task.Exception;
    }

    private readonly record struct Key(int FromX, int FromY, int FromZ, int ToX, int ToY, int ToZ)
    {
        public static Key For(Vector3 from, Vector3 to) => new(
            Q(from.X), Q(from.Y), Q(from.Z),
            Q(to.X), Q(to.Y), Q(to.Z));

        private static int Q(float value) => (int)MathF.Round(value / QuantizeYalms);
    }

    private sealed class Entry(Vector3 from, Vector3 to, Task<List<Vector3>> pending, long now)
    {
        public Vector3 From { get; } = from;
        public Vector3 To { get; } = to;
        public Task<List<Vector3>>? Pending { get; set; } = pending;
        public float? Cost { get; set; }
        public long ExpiresMs { get; set; }
        public long LastAccessMs { get; set; } = now;
    }
}
