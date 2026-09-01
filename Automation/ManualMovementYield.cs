using System;
using Dalamud.Game.ClientState.Keys;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Gives direct player movement priority over automatic field navigation. The gate observes the
/// common movement keys plus FFXIV's standard two-mouse-button forward movement, remembers the
/// last manual input, and remains yielded for a short quiet period so vnavmesh does not immediately
/// fight the player for control when the input is released.
///
/// This intentionally detects only inputs that are unambiguously movement. A single mouse button
/// is camera/selection input and must not pause navigation merely because the player is looking
/// around; left+right together is the game's conventional mouse-forward gesture and therefore does.
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

                // FFXIV's default mouse-forward movement is both primary buttons held together.
                // Do not treat either button alone as movement: that would make ordinary camera
                // control or UI interaction continuously seize navigation.
                if (Svc.KeyState[VirtualKey.LBUTTON] && Svc.KeyState[VirtualKey.RBUTTON])
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
