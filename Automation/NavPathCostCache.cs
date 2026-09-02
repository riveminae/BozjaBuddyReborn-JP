using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
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

    /// <summary>True while any telemetry-only path query has not finished cancelling/computing.</summary>
    public bool HasPending
    {
        get
        {
            Poll(Environment.TickCount64);
            return PendingCount() > 0;
        }
    }

    /// <summary>
    /// Return a measured ground-path distance when cached; otherwise return fallback immediately.
    /// When request=true, a missing entry may start one asynchronous cancelable vnavmesh query.
    /// </summary>
    public float Estimate(uint territory, Vector3 from, Vector3 to, float fallback, bool request)
    {
        var now = Environment.TickCount64;
        Poll(now);

        var key = Key.For(territory, from, to);
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.LastAccessMs = now;

            if (existing.Cost is { } cost && now <= existing.ExpiresMs)
                return cost;

            if (existing.Pending is not null)
                return fallback;

            if (now < existing.ExpiresMs)
                return fallback; // recent failure; do not hammer the same route

            existing.Cancellation?.Dispose();
            _entries.Remove(key);
        }

        if (!request || !_navmesh.MeshReady || PendingCount() >= MaxPending)
            return fallback;

        var cts = new CancellationTokenSource();
        var task = _navmesh.PathfindCancelable(from, to, cts.Token, fly: false);
        if (task is null)
        {
            cts.Dispose();
            return fallback;
        }

        _entries[key] = new Entry(from, to, task, cts, now);
        PruneIfNeeded();
        return fallback;
    }

    /// <summary>Return only a successfully measured cost; never starts a new query.</summary>
    public bool TryGet(uint territory, Vector3 from, Vector3 to, out float cost)
    {
        var now = Environment.TickCount64;
        Poll(now);

        if (_entries.TryGetValue(Key.For(territory, from, to), out var entry)
            && entry.Cost is { } measured
            && now <= entry.ExpiresMs)
        {
            entry.LastAccessMs = now;
            cost = measured;
            return true;
        }

        cost = 0f;
        return false;
    }

    /// <summary>True only while this exact telemetry path query is still running.</summary>
    public bool IsPending(uint territory, Vector3 from, Vector3 to)
    {
        Poll(Environment.TickCount64);
        return _entries.TryGetValue(Key.For(territory, from, to), out var entry)
               && entry.Pending is { IsCompleted: false };
    }

    /// <summary>Cancel exactly one cost probe, never a movement path.</summary>
    public bool Cancel(uint territory, Vector3 from, Vector3 to)
    {
        Poll(Environment.TickCount64);
        if (!_entries.TryGetValue(Key.For(territory, from, to), out var entry)
            || entry.Pending is null
            || entry.Cancellation is null)
            return false;

        try
        {
            entry.Cancellation.Cancel();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Request cancellation of every telemetry query but keep entries until their tasks actually
    /// finish. This is what lets the router drain probes before issuing a real movement path.
    /// </summary>
    public bool CancelAllPending()
    {
        Poll(Environment.TickCount64);
        var requested = false;
        foreach (var entry in _entries.Values)
        {
            if (entry.Pending is null || entry.Pending.IsCompleted || entry.Cancellation is null)
                continue;
            try
            {
                entry.Cancellation.Cancel();
                requested = true;
            }
            catch
            {
                // Another tick will observe completion/fault. Never escalate telemetry failure.
            }
        }
        return requested;
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
        foreach (var entry in _entries.Values)
        {
            try { entry.Cancellation?.Cancel(); }
            catch { /* best effort; these queries never own movement */ }
            ObserveFault(entry.Pending);
            entry.Cancellation?.Dispose();
        }
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
            entry.Cancellation?.Dispose();
            entry.Cancellation = null;

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

            // Empty/unreachable/faulted/cancelled path: fail open to the horizontal estimate and
            // retry only after a short cooldown. A telemetry failure must never make a destination
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
            {
                ObserveFault(entry.Pending);
                entry.Cancellation?.Dispose();
            }
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

    private readonly record struct Key(uint Territory, int FromX, int FromY, int FromZ, int ToX, int ToY, int ToZ)
    {
        public static Key For(uint territory, Vector3 from, Vector3 to) => new(
            territory,
            Q(from.X), Q(from.Y), Q(from.Z),
            Q(to.X), Q(to.Y), Q(to.Z));

        private static int Q(float value) => (int)MathF.Round(value / QuantizeYalms);
    }

    private sealed class Entry(
        Vector3 from,
        Vector3 to,
        Task<List<Vector3>> pending,
        CancellationTokenSource cancellation,
        long now)
    {
        public Vector3 From { get; } = from;
        public Vector3 To { get; } = to;
        public Task<List<Vector3>>? Pending { get; set; } = pending;
        public CancellationTokenSource? Cancellation { get; set; } = cancellation;
        public float? Cost { get; set; }
        public long ExpiresMs { get; set; }
        public long LastAccessMs { get; set; } = now;
    }
}
