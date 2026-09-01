using System;
using Dalamud.Game.ClientState.Keys;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Gives direct player movement priority over automatic field navigation.  The gate observes the
/// common movement keys from Dalamud's game key-state service, remembers the last manual input,
/// and remains yielded for a short quiet period so vnavmesh does not immediately fight the player
/// for control when the key is released.
/// </summary>
public sealed class ManualMovementYield
{
    private static readonly VirtualKey[] MovementKeys =
    [
        VirtualKey.W, VirtualKey.A, VirtualKey.S, VirtualKey.D,
        VirtualKey.UP, VirtualKey.DOWN, VirtualKey.LEFT, VirtualKey.RIGHT,
    ];

    private long _lastManualInputMs;

    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromSeconds(3);

    public bool ManualInputNow
    {
        get
        {
            try
            {
                foreach (var key in MovementKeys)
                    if (Svc.KeyState[key])
                        return true;
            }
            catch
            {
                // If the key-state service cannot answer during zoning, do not invent user input.
            }
            return false;
        }
    }

    public bool ShouldYield()
    {
        var now = Environment.TickCount64;
        if (ManualInputNow)
        {
            _lastManualInputMs = now;
            return true;
        }

        return _lastManualInputMs != 0
               && now - _lastManualInputMs < Math.Max(0, QuietPeriod.TotalMilliseconds);
    }

    public void Reset() => _lastManualInputMs = 0;
}
