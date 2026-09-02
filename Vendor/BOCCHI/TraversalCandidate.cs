// SPDX-License-Identifier: AGPL-3.0-only
// Derived from OhKannaDuh/BOCCHI
// BOCCHI.Common/Data/Zones/Graph/Traversal/GraphTraverser.cs
// commit 7847b00c313d6a7ddfe9ee126e46e10f547db9da.

namespace BozjaBuddyReborn.Vendor.BOCCHI;

/// <summary>
/// BOCCHI's comparable route-cost value. BBR keeps execution in FieldTravelRouter, so this
/// adapter intentionally carries only the shared candidate metric rather than BOCCHI's PathStep list.
/// </summary>
public readonly record struct TraversalCandidate(float TotalCost);