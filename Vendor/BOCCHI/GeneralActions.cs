using FFXIVClientStructs.FFXIV.Client.Game;

namespace BozjaBuddyReborn.Vendor.BOCCHI;

/// <summary>
/// Minimal general-action helper derived from BOCCHI ActionHelpers/Actions.cs + Action.cs.
///
/// Upstream: KanoNoUta/BOCCHI commit 2f7026ae31712b1b969362a2831df8f795607736,
/// itself a BOCCHI fork. BOCCHI is distributed under GNU AGPL-3.0; this fork is AGPL-3.0.
/// Only Return is retained because v1.1 uses it for BOCCHI-style traversal and death recovery.
/// </summary>
public static unsafe class GeneralActions
{
    public const uint ReturnActionId = 8;

    public static bool ReturnReady()
    {
        try
        {
            var manager = ActionManager.Instance();
            if (manager == null)
                return false;
            var recast = manager->GetRecastTime(ActionType.GeneralAction, ReturnActionId);
            var elapsed = manager->GetRecastTimeElapsed(ActionType.GeneralAction, ReturnActionId);
            return recast - elapsed <= 0f;
        }
        catch
        {
            return false;
        }
    }

    public static bool CastReturn()
    {
        try
        {
            var manager = ActionManager.Instance();
            return manager != null
                   && manager->UseAction(ActionType.GeneralAction, ReturnActionId);
        }
        catch
        {
            return false;
        }
    }
}
