from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/BozjaController.cs"
text = P.read_text(encoding="utf-8-sig")

# Later localization/context-policy packets legitimately change the status strings around the
# staging branch. The stable semantic markers are the call and the helper itself.
if "TryGetAethernetIdleSpot(territory, region" in text and "private bool TryGetAethernetIdleSpot" in text:
    print("Automation/BozjaController.cs: aethernet idle staging already wired")
    raise SystemExit(0)


def repl(old: str, new: str) -> None:
    global text
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"BozjaController.cs anchor missing: {old[:100]!r}")
    text = text.replace(old, new, 1)

repl(
    """        if (region == FieldRegionId.Unknown || !TryGetIdleSpot(territory, region, out var spot))
        {
            Status = $"{reason} Holding position.";
            _movement.Stop();
            return;
        }

        var label = FieldRegions.Label(territory, region);
""",
    """        // v1.1 stages at the field aethernet whenever possible. A Relic restriction picks
        // a node classified inside that exact region; ordinary idle uses the nearest node. This
        // lets the same BOCCHI router exploit the aethernet instantly when the next activity pops.
        Vector3 spot;
        string label;
        if (TryGetAethernetIdleSpot(territory, region, out spot, out var aethernetLabel))
        {
            label = aethernetLabel;
        }
        else if (region != FieldRegionId.Unknown && TryGetIdleSpot(territory, region, out spot))
        {
            label = FieldRegions.Label(territory, region);
        }
        else
        {
            Status = $"{reason} その場で待機します。";
            _movement.Stop();
            return;
        }
""",
)

repl(
    """    /// <summary>Resolve a configured staging point to a ground position, cached per key.</summary>
    private bool TryGetIdleSpot(uint territory, FieldRegionId region, out Vector3 spot)
""",
    """    /// <summary>Choose an aethernet node for idle staging, resolving its actual navmesh floor.</summary>
    private bool TryGetAethernetIdleSpot(
        uint territory,
        FieldRegionId preferredRegion,
        out Vector3 spot,
        out string label)
    {
        spot = Vector3.Zero;
        label = string.Empty;
        var nodes = FieldAethernet.ForTerritory(territory);
        if (nodes.Count == 0)
            return false;

        FieldAethernet.Node? best = null;
        var bestDistance = float.MaxValue;
        foreach (var node in nodes)
        {
            if (preferredRegion != FieldRegionId.Unknown
                && FieldRegions.ClassifyByPosition(territory, node.Position) != preferredRegion)
                continue;

            var distance = Movement.DistanceToPlayer(node.Position);
            if (best == null || distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }

        if (best is not { } selected)
            return false;

        var grounded = _navmesh.ResolveGroundPoint(selected.Position.X, selected.Position.Z);
        if (grounded == Vector3.Zero)
            grounded = selected.Position;
        spot = grounded;

        try
        {
            var place = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>()?
                .GetRowOrDefault(selected.PlaceNameId)?.Name.ExtractText();
            label = string.IsNullOrWhiteSpace(place) ? "エーテライト" : place;
        }
        catch
        {
            label = "エーテライト";
        }

        return true;
    }

    /// <summary>Resolve a configured staging point to a ground position, cached per key.</summary>
    private bool TryGetIdleSpot(uint territory, FieldRegionId region, out Vector3 spot)
""",
)

P.write_text(text, encoding="utf-8")
print("Automation/BozjaController.cs: aethernet idle staging wired")
