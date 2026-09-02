// SPDX-License-Identifier: AGPL-3.0-only
// Derived from OhKannaDuh/BOCCHI
// BOCCHI.Common/Data/Zones/NavigationApproach.cs
// commit 7847b00c313d6a7ddfe9ee126e46e10f547db9da.
//
// This intentionally vendors only the constants needed by the BBR field travel
// planner.  Keeping the values and their meaning identical makes route decisions
// comparable to BOCCHI without importing its unrelated Occult Crescent systems.

namespace BozjaBuddyReborn.Vendor.BOCCHI;

public static class NavigationConstants
{
    public const float MaxDirectWalkDistance = 80f;
    public const float ReturnCost = 40f;
    public const float AethernetHopCost = 50f;
    public const float CampRadius = 80f;
    public const float GraphSnapRadius = 45f;
    public const float MountMinDistance = 20f;
}
