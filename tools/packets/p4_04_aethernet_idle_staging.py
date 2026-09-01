from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/BozjaController.cs"
text = P.read_text(encoding="utf-8-sig")

def repl(old: str, new: str) -> None:
    global text
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"BozjaController.cs anchor missing: {old[:100]!r}")
    text = text.replace(old, new, 1)

repl(
    """        if (region == FieldRegionId.Unknown || !TryGetIdleSpot(territory, region, out var spot))\n        {\n            Status = $\"{reason} Holding position.\";\n            _movement.Stop();\n            return;\n        }\n\n        var label = FieldRegions.Label(territory, region);\n""",
    """        // v1.1 stages at the field aethernet whenever possible. A Relic restriction picks\n        // a node classified inside that exact region; ordinary idle uses the nearest node. This\n        // lets the same BOCCHI router exploit the aethernet instantly when the next activity pops.\n        Vector3 spot;\n        string label;\n        if (TryGetAethernetIdleSpot(territory, region, out spot, out var aethernetLabel))\n        {\n            label = aethernetLabel;\n        }\n        else if (region != FieldRegionId.Unknown && TryGetIdleSpot(territory, region, out spot))\n        {\n            label = FieldRegions.Label(territory, region);\n        }\n        else\n        {\n            Status = $\"{reason} Holding position.\";\n            _movement.Stop();\n            return;\n        }\n""",
)

repl(
    """    /// <summary>Resolve a configured staging point to a ground position, cached per key.</summary>\n    private bool TryGetIdleSpot(uint territory, FieldRegionId region, out Vector3 spot)\n""",
    """    /// <summary>Choose an aethernet node for idle staging, resolving its actual navmesh floor.</summary>\n    private bool TryGetAethernetIdleSpot(\n        uint territory,\n        FieldRegionId preferredRegion,\n        out Vector3 spot,\n        out string label)\n    {\n        spot = Vector3.Zero;\n        label = string.Empty;\n        var nodes = FieldAethernet.ForTerritory(territory);\n        if (nodes.Count == 0)\n            return false;\n\n        FieldAethernet.Node? best = null;\n        var bestDistance = float.MaxValue;\n        foreach (var node in nodes)\n        {\n            // For a Relic/region restriction, never choose a node confidently belonging to a\n            // different region. The base camp is allowed only if its own position classifies into\n            // the requested region. Unknown classification is not trusted for a restricted farm.\n            if (preferredRegion != FieldRegionId.Unknown\n                && FieldRegions.ClassifyByPosition(territory, node.Position) != preferredRegion)\n                continue;\n\n            var distance = Movement.DistanceToPlayer(node.Position);\n            if (best == null || distance < bestDistance)\n            {\n                best = node;\n                bestDistance = distance;\n            }\n        }\n\n        if (best is not { } selected)\n            return false;\n\n        var grounded = _navmesh.ResolveGroundPoint(selected.Position.X, selected.Position.Z);\n        if (grounded == Vector3.Zero)\n            grounded = selected.Position;\n        spot = grounded;\n\n        try\n        {\n            var place = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>()?\n                .GetRowOrDefault(selected.PlaceNameId)?.Name.ExtractText();\n            label = string.IsNullOrWhiteSpace(place) ? \"エーテライト\" : place;\n        }\n        catch\n        {\n            label = \"エーテライト\";\n        }\n\n        return true;\n    }\n\n    /// <summary>Resolve a configured staging point to a ground position, cached per key.</summary>\n    private bool TryGetIdleSpot(uint territory, FieldRegionId region, out Vector3 spot)\n""",
)

P.write_text(text, encoding="utf-8")
print("Automation/BozjaController.cs: aethernet idle staging wired")
