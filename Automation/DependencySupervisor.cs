using System;
using System.Collections.Generic;
using BozjaBuddyReborn.External;

namespace BozjaBuddyReborn.Automation;

public enum DependencyHealth : byte
{
    Healthy = 0,
    WaitingRequired = 1,
    TimedOut = 2,
}

public readonly record struct DependencySnapshot(
    DependencyHealth Health,
    IReadOnlyList<string> Missing,
    TimeSpan UnavailableFor,
    TimeSpan Remaining)
{
    public bool Ready => Health == DependencyHealth.Healthy;
    public string MissingText => Missing.Count == 0 ? string.Empty : string.Join(", ", Missing);
}

/// <summary>
/// Tracks the three dependencies required by the v1.1 unattended runner: vnavmesh, Rotation
/// Solver Reborn, and BossMod/BossMod Reborn.  Detection and timing live here; the controller
/// decides whether it is safe to wait, survive the current fight, return to camp, or stop.
/// </summary>
public sealed class DependencySupervisor(NavmeshIpc navmesh, CombatDirector combat)
{
    public static readonly TimeSpan RequiredRecoveryWindow = TimeSpan.FromSeconds(60);

    private readonly NavmeshIpc _navmesh = navmesh;
    private readonly CombatDirector _combat = combat;
    private long _missingSinceMs;

    public DependencySnapshot Snapshot()
    {
        List<string> missing = [];
        if (!_navmesh.Available)
            missing.Add("vnavmesh");
        if (!_combat.RotationAvailable)
            missing.Add("Rotation Solver Reborn");
        if (!_combat.AvoidanceAvailable)
            missing.Add("BossMod / BossMod Reborn");

        if (missing.Count == 0)
        {
            _missingSinceMs = 0;
            return new DependencySnapshot(
                DependencyHealth.Healthy,
                missing,
                TimeSpan.Zero,
                RequiredRecoveryWindow);
        }

        var now = Environment.TickCount64;
        if (_missingSinceMs == 0)
            _missingSinceMs = now;

        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, now - _missingSinceMs));
        var remaining = RequiredRecoveryWindow - elapsed;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return new DependencySnapshot(
            elapsed >= RequiredRecoveryWindow ? DependencyHealth.TimedOut : DependencyHealth.WaitingRequired,
            missing,
            elapsed,
            remaining);
    }

    /// <summary>Forget an old outage when a run is explicitly restarted or the territory changes.</summary>
    public void Reset() => _missingSinceMs = 0;
}
