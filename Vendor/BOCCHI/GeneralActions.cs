// SPDX-License-Identifier: AGPL-3.0-only
// Derived from KanoNoUta/BOCCHI commit 2f7026ae31712b1b969362a2831df8f795607736.
// That repository is an AGPL-3.0 BOCCHI maintenance fork.

using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BozjaBuddyReborn.Vendor.BOCCHI;

/// <summary>
/// Minimal general-action helper derived from BOCCHI ActionHelpers/Actions.cs + Action.cs and its
/// Return confirmation flow.
///
/// Upstream: KanoNoUta/BOCCHI commit 2f7026ae31712b1b969362a2831df8f795607736,
/// itself a BOCCHI maintenance fork distributed under GNU AGPL-3.0.
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

    /// <summary>
    /// Confirm the SelectYesno created by a Return cast. This method is intentionally generic at
    /// the addon level and therefore MUST only be called while the caller owns a short-lived
    /// "Return was just cast" pending window. BOCCHI uses the same ownership pattern rather than
    /// blindly accepting arbitrary Yes/No dialogs.
    /// </summary>
    public static bool TryConfirmPendingReturn()
    {
        try
        {
            if (!GenericHelpers.TryGetAddonMaster<AddonMaster.SelectYesno>(out var select)
                || !select.IsAddonReady)
                return false;

            select.RespectDisabledButtons = true;
            select.RespectHoldButtons = true;
            select.Yes();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
