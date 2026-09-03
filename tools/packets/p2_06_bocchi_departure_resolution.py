from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONST = ROOT / "Vendor/BOCCHI/NavigationConstants.cs"
ROUTER = ROOT / "Automation/FieldTravelRouter.cs"

# Keep the exact graph snap radius beside the other vendored BOCCHI traversal constants.
text = CONST.read_text(encoding="utf-8-sig")
if "GraphSnapRadius" not in text:
    anchor = "    public const float CampRadius = 80f;\n"
    if anchor not in text:
        raise RuntimeError("NavigationConstants CampRadius anchor missing")
    text = text.replace(anchor, anchor + "    public const float GraphSnapRadius = 45f;\n", 1)
    CONST.write_text(text, encoding="utf-8")
    print("Vendor/BOCCHI/NavigationConstants.cs: GraphSnapRadius added")
else:
    print("Vendor/BOCCHI/NavigationConstants.cs: GraphSnapRadius already present")

text = ROUTER.read_text(encoding="utf-8-sig")
if "ResolveDepartureNode(" in text:
    print("Automation/FieldTravelRouter.cs: BOCCHI departure resolution already applied")
    raise SystemExit(0)

# EstimateCost: BOCCHI resolves exactly one departure node from the current position. It does not
# globally optimize across every possible departure shard.
old = """        var best = direct;\n        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2)\n        {\n            foreach (var departure in nodes)\n            foreach (var inbound in nodes)\n            {\n                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                    continue;\n                best = MathF.Min(best,\n                    Movement.HorizontalDistance(start, departure.Position)\n                    + hopCost\n                    + Movement.HorizontalDistance(inbound.Position, destination));\n            }\n        }\n\n"""
new = """        var best = direct;\n        var resolvedDeparture = ResolveDepartureNode(nodes, start);\n        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2\n            && resolvedDeparture is { } departure)\n        {\n            var walkToDeparture = Movement.HorizontalDistance(start, departure.Position);\n            foreach (var inbound in nodes)\n            {\n                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                    continue;\n\n                // BOCCHI leaves field -> base-camp travel to ReturnTeleportWalk rather than\n                // paying an aethernet hop back to the base shard.\n                if (inbound.IsBaseCamp && !departure.IsBaseCamp)\n                    continue;\n\n                best = MathF.Min(best,\n                    walkToDeparture\n                    + hopCost\n                    + Movement.HorizontalDistance(inbound.Position, destination));\n            }\n        }\n\n"""
if old not in text:
    raise RuntimeError("FieldTravelRouter EstimateCost aethernet loop anchor missing")
text = text.replace(old, new, 1)

# Plan: same single-departure rule for the actual route candidate.
old = """        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2)\n        {\n            foreach (var departure in nodes)\n            {\n                var walkToDeparture = Movement.HorizontalDistance(start, departure.Position);\n                foreach (var inbound in nodes)\n                {\n                    if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                        continue;\n\n                    var walkFromInbound = Movement.HorizontalDistance(inbound.Position, finalDestination);\n                    var cost = walkToDeparture + hopCost + walkFromInbound;\n                    if (cost >= best)\n                        continue;\n\n                    best = cost;\n                    bestMode = FieldTravelMode.WalkToAetheryte;\n                    bestDeparture = departure;\n                    bestInbound = inbound;\n                }\n            }\n        }\n\n"""
new = """        var resolvedDeparture = ResolveDepartureNode(nodes, start);\n        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2\n            && resolvedDeparture is { } departure)\n        {\n            var walkToDeparture = Movement.HorizontalDistance(start, departure.Position);\n            foreach (var inbound in nodes)\n            {\n                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                    continue;\n                if (inbound.IsBaseCamp && !departure.IsBaseCamp)\n                    continue;\n\n                var walkFromInbound = Movement.HorizontalDistance(inbound.Position, finalDestination);\n                var cost = walkToDeparture + hopCost + walkFromInbound;\n                if (cost >= best)\n                    continue;\n\n                best = cost;\n                bestMode = FieldTravelMode.WalkToAetheryte;\n                bestDeparture = departure;\n                bestInbound = inbound;\n            }\n        }\n\n"""
if old not in text:
    raise RuntimeError("FieldTravelRouter Plan aethernet loop anchor missing")
text = text.replace(old, new, 1)

# Optional-Lifestream hypothetical uses the same resolved departure, so outage behavior does not
# compare against a route that would never be considered once Lifestream comes back.
old = """            var hypothetical = best;\n            foreach (var departure in nodes)\n            foreach (var inbound in nodes)\n            {\n                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                    continue;\n                var candidate = Movement.HorizontalDistance(start, departure.Position)\n                                + hopCost\n                                + Movement.HorizontalDistance(inbound.Position, finalDestination);\n                hypothetical = MathF.Min(hypothetical, candidate);\n            }\n\n"""
new = """            var hypothetical = best;\n            if (resolvedDeparture is { } waitDeparture)\n            {\n                var walkToDeparture = Movement.HorizontalDistance(start, waitDeparture.Position);\n                foreach (var inbound in nodes)\n                {\n                    if (waitDeparture.CustomAetheryteId == inbound.CustomAetheryteId)\n                        continue;\n                    if (inbound.IsBaseCamp && !waitDeparture.IsBaseCamp)\n                        continue;\n                    var candidate = walkToDeparture\n                                    + hopCost\n                                    + Movement.HorizontalDistance(inbound.Position, finalDestination);\n                    hypothetical = MathF.Min(hypothetical, candidate);\n                }\n            }\n\n"""
if old not in text:
    raise RuntimeError("FieldTravelRouter optional-Lifestream hypothetical anchor missing")
text = text.replace(old, new, 1)

# Helper mirrors BOCCHI ResolveDeparture precedence without importing the Occult-Crescent graph:
# base camp when already in camp, then a teleport node within the 45y graph snap radius, otherwise
# the nearest teleport node. Horizontal distance chooses the node; p2_07 refines the walk cost with
# vnavmesh.Nav.Pathfind without changing this identity rule.
anchor = """    private void FallBack(string reason)\n"""
helper = """    private static FieldAethernet.Node? ResolveDepartureNode(\n        IReadOnlyList<FieldAethernet.Node> nodes, Vector3 start)\n    {\n        FieldAethernet.Node? baseCamp = null;\n        FieldAethernet.Node? nearest = null;\n        FieldAethernet.Node? snapped = null;\n        var nearestDistance = float.MaxValue;\n        var snappedDistance = float.MaxValue;\n\n        foreach (var node in nodes)\n        {\n            var distance = Movement.HorizontalDistance(start, node.Position);\n            if (node.IsBaseCamp)\n                baseCamp = node;\n\n            if (distance < nearestDistance)\n            {\n                nearest = node;\n                nearestDistance = distance;\n            }\n\n            if (distance <= NavigationConstants.GraphSnapRadius && distance < snappedDistance)\n            {\n                snapped = node;\n                snappedDistance = distance;\n            }\n        }\n\n        if (baseCamp is { } camp\n            && Movement.HorizontalDistance(start, camp.Position) <= NavigationConstants.CampRadius)\n            return camp;\n\n        return snapped ?? nearest;\n    }\n\n"""
if anchor not in text:
    raise RuntimeError("FieldTravelRouter FallBack anchor missing")
text = text.replace(anchor, helper + anchor, 1)

# IReadOnlyList namespace.
if "using System.Collections.Generic;" not in text:
    text = text.replace("using System;\n", "using System;\nusing System.Collections.Generic;\n", 1)

ROUTER.write_text(text, encoding="utf-8")
print("Automation/FieldTravelRouter.cs: BOCCHI single-departure traversal rule applied")
