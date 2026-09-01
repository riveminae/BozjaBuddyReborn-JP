from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    if new in text:
        print(f"{path}: already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: patched")


# Pure candidate cost: same constants/options as the live router, but no route-state mutation.
patch(
    "Automation/FieldTravelRouter.cs",
    """    public bool IsRoutingTo(Vector3 destination) =>\n        _goal != Vector3.Zero && Movement.HorizontalDistance(_goal, destination) <= GoalIdentityRadius;\n\n    public void Reset()\n""",
    """    public bool IsRoutingTo(Vector3 destination) =>\n        _goal != Vector3.Zero && Movement.HorizontalDistance(_goal, destination) <= GoalIdentityRadius;\n\n    /// <summary>Estimate the cheapest currently usable BOCCHI-style route without mutating route state.</summary>\n    public float EstimateCost(Vector3 start, Vector3 destination)\n    {\n        var direct = Movement.HorizontalDistance(start, destination);\n        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)\n            return direct;\n\n        var nodes = FieldAethernet.ForTerritory(Svc.ClientState.TerritoryType);\n        if (nodes.Count == 0)\n            return direct;\n\n        var maxDirect = _config.NavigationMaxDirectWalkDistance > 0\n            ? _config.NavigationMaxDirectWalkDistance\n            : NavigationConstants.MaxDirectWalkDistance;\n        if (direct <= maxDirect)\n            return direct;\n\n        var hopCost = _config.NavigationAethernetHopCost > 0\n            ? _config.NavigationAethernetHopCost\n            : NavigationConstants.AethernetHopCost;\n        var returnCost = _config.NavigationReturnCost > 0\n            ? _config.NavigationReturnCost\n            : NavigationConstants.ReturnCost;\n\n        var best = direct;\n        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2)\n        {\n            foreach (var departure in nodes)\n            foreach (var inbound in nodes)\n            {\n                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                    continue;\n                best = MathF.Min(best,\n                    Movement.HorizontalDistance(start, departure.Position)\n                    + hopCost\n                    + Movement.HorizontalDistance(inbound.Position, destination));\n            }\n        }\n\n        var camp = FieldAethernet.BaseCamp(Svc.ClientState.TerritoryType);\n        if (_config.UseReturnRouting\n            && camp is { } baseCamp\n            && Movement.HorizontalDistance(start, baseCamp.Position) > NavigationConstants.CampRadius\n            && !Svc.Condition[ConditionFlag.InCombat]\n            && GeneralActions.ReturnReady())\n        {\n            best = MathF.Min(best, returnCost + Movement.HorizontalDistance(baseCamp.Position, destination));\n            if (_config.UseAethernetTravel && _lifestream.Available)\n            {\n                foreach (var inbound in nodes)\n                {\n                    if (inbound.IsBaseCamp)\n                        continue;\n                    best = MathF.Min(best, returnCost + hopCost\n                        + Movement.HorizontalDistance(inbound.Position, destination));\n                }\n            }\n        }\n\n        return best;\n    }\n\n    public void Reset()\n""",
)

# Ensure Plan resets the pending confirmation flag too.
patch(
    "Automation/FieldTravelRouter.cs",
    """        _teleportAttempts = 0;\n        _returnStartedMs = 0;\n\n        var territory = Svc.ClientState.TerritoryType;\n""",
    """        _teleportAttempts = 0;\n        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n\n        var territory = Svc.ClientState.TerritoryType;\n""",
)

patch(
    "Automation/Movement.cs",
    """    public static float DistanceToPlayer(Vector3 world)\n    {\n        var me = Svc.Objects.LocalPlayer;\n        return me == null ? float.MaxValue : HorizontalDistance(me.Position, world);\n    }\n\n""",
    """    public static float DistanceToPlayer(Vector3 world)\n    {\n        var me = Svc.Objects.LocalPlayer;\n        return me == null ? float.MaxValue : HorizontalDistance(me.Position, world);\n    }\n\n    /// <summary>Current BOCCHI-style yalm-equivalent cost used to rank fresh skirmish destinations.</summary>\n    public float EstimateTravelCost(Vector3 world)\n    {\n        var me = Svc.Objects.LocalPlayer;\n        return me == null ? float.MaxValue : _fieldRouter.EstimateCost(me.Position, world);\n    }\n\n""",
)

# TargetSelector gets the same Movement whose router will execute the chosen destination.
patch(
    "Automation/TargetSelector.cs",
    """public sealed class TargetSelector(CeCatalog catalog, Configuration config, RegionResolver regions)\n{\n    private readonly CeCatalog _catalog = catalog;\n    private readonly Configuration _config = config;\n    private readonly RegionResolver _regions = regions;\n""",
    """public sealed class TargetSelector(CeCatalog catalog, Configuration config, RegionResolver regions, Movement movement)\n{\n    private readonly CeCatalog _catalog = catalog;\n    private readonly Configuration _config = config;\n    private readonly RegionResolver _regions = regions;\n    private readonly Movement _movement = movement;\n""",
)

# CE registration is remote: explicit priority then stable event id, never player distance.
patch(
    "Automation/TargetSelector.cs",
    """        CeSnapshot? best = null;\n        var bestRank = int.MaxValue;\n        var bestDistance = float.MaxValue;\n\n        foreach (var ce in engagements)\n        {\n            if (!IsEligible(ce))\n                continue;\n\n            var largeScale = _catalog.IsLargeScale(ce.EventId);\n            var rank = largeScale ? int.MinValue : PriorityRank(ce.EventId);\n            var distance = ce.HasPosition ? Movement.DistanceToPlayer(ce.Position) : float.MaxValue;\n\n            if (best == null || Better(rank, ce.EventId, distance, bestRank, best.Value.EventId, bestDistance, deterministic))\n            {\n                best = ce;\n                bestRank = rank;\n                bestDistance = distance;\n            }\n        }\n\n        return best;\n""",
    """        CeSnapshot? best = null;\n        var bestRank = int.MaxValue;\n\n        foreach (var ce in engagements)\n        {\n            if (!IsEligible(ce))\n                continue;\n\n            var largeScale = _catalog.IsLargeScale(ce.EventId);\n            var rank = largeScale ? int.MinValue : PriorityRank(ce.EventId);\n            if (best == null || rank < bestRank || (rank == bestRank && ce.EventId < best.Value.EventId))\n            {\n                best = ce;\n                bestRank = rank;\n            }\n        }\n\n        return best;\n""",
)

# Fresh skirmishes use route cost, not straight-line distance.
patch(
    "Automation/TargetSelector.cs",
    """        var bestPosition = Vector3.Zero;\n        var bestDistance = float.MaxValue;\n""",
    """        var bestPosition = Vector3.Zero;\n        var bestCost = float.MaxValue;\n""",
)
patch(
    "Automation/TargetSelector.cs",
    """                var distance = Movement.DistanceToPlayer(fate.Position);\n\n                var better = bestId == 0\n                    || (!deterministic && distance < bestDistance)\n                    || (deterministic && fate.FateId < bestId);\n""",
    """                var cost = _movement.EstimateTravelCost(fate.Position);\n\n                var better = bestId == 0\n                    || (!deterministic && (cost < bestCost || (cost == bestCost && fate.FateId < bestId)))\n                    || (deterministic && fate.FateId < bestId);\n""",
)
patch(
    "Automation/TargetSelector.cs",
    """                bestPosition = fate.Position;\n                bestDistance = distance;\n""",
    """                bestPosition = fate.Position;\n                bestCost = cost;\n""",
)

patch(
    "Plugin.cs",
    """        _selector = new TargetSelector(_catalog, _config, _regions);\n""",
    """        _selector = new TargetSelector(_catalog, _config, _regions, _movement);\n""",
)
