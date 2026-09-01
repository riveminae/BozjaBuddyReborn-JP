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
    """    public bool IsRoutingTo(Vector3 destination) =>
        _goal != Vector3.Zero && Movement.HorizontalDistance(_goal, destination) <= GoalIdentityRadius;

    public void Reset()
""",
    """    public bool IsRoutingTo(Vector3 destination) =>
        _goal != Vector3.Zero && Movement.HorizontalDistance(_goal, destination) <= GoalIdentityRadius;

    /// <summary>Estimate the cheapest currently usable BOCCHI-style route without mutating route state.</summary>
    public float EstimateCost(Vector3 start, Vector3 destination)
    {
        var direct = Movement.HorizontalDistance(start, destination);
        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)
            return direct;

        var nodes = FieldAethernet.ForTerritory(Svc.ClientState.TerritoryType);
        if (nodes.Count == 0)
            return direct;

        var maxDirect = _config.NavigationMaxDirectWalkDistance > 0
            ? _config.NavigationMaxDirectWalkDistance
            : NavigationConstants.MaxDirectWalkDistance;
        if (direct <= maxDirect)
            return direct;

        var hopCost = _config.NavigationAethernetHopCost > 0
            ? _config.NavigationAethernetHopCost
            : NavigationConstants.AethernetHopCost;
        var returnCost = _config.NavigationReturnCost > 0
            ? _config.NavigationReturnCost
            : NavigationConstants.ReturnCost;

        var best = direct;
        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2)
        {
            foreach (var departure in nodes)
            foreach (var inbound in nodes)
            {
                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)
                    continue;
                best = MathF.Min(best,
                    Movement.HorizontalDistance(start, departure.Position)
                    + hopCost
                    + Movement.HorizontalDistance(inbound.Position, destination));
            }
        }

        var camp = FieldAethernet.BaseCamp(Svc.ClientState.TerritoryType);
        if (_config.UseReturnRouting
            && camp is { } baseCamp
            && Movement.HorizontalDistance(start, baseCamp.Position) > NavigationConstants.CampRadius
            && !Svc.Condition[ConditionFlag.InCombat]
            && GeneralActions.ReturnReady())
        {
            best = MathF.Min(best, returnCost + Movement.HorizontalDistance(baseCamp.Position, destination));
            if (_config.UseAethernetTravel && _lifestream.Available)
            {
                foreach (var inbound in nodes)
                {
                    if (inbound.IsBaseCamp)
                        continue;
                    best = MathF.Min(best, returnCost + hopCost
                        + Movement.HorizontalDistance(inbound.Position, destination));
                }
            }
        }

        return best;
    }

    public void Reset()
""",
)

# Ensure Plan resets the pending Return confirmation flag too. Later packets add their own reset
# fields around this exact location, so inspect the Plan block semantically instead of matching a
# brittle sequence of neighbouring assignments.
p = ROOT / "Automation/FieldTravelRouter.cs"
text = p.read_text(encoding="utf-8-sig")
plan_start = text.find("    private void Plan(")
plan_end = text.find("        var territory = Svc.ClientState.TerritoryType;", plan_start)
if plan_start < 0 or plan_end < 0:
    raise RuntimeError("FieldTravelRouter Plan block missing")
plan_prefix = text[plan_start:plan_end]
if "_returnConfirmationSent = false;" in plan_prefix:
    print("Automation/FieldTravelRouter.cs: Plan confirmation reset already applied")
else:
    anchor = "        _returnStartedMs = 0;\n"
    pos = text.find(anchor, plan_start, plan_end)
    if pos < 0:
        raise RuntimeError("FieldTravelRouter Plan return-start reset missing")
    pos += len(anchor)
    text = text[:pos] + "        _returnConfirmationSent = false;\n" + text[pos:]
    p.write_text(text, encoding="utf-8")
    print("Automation/FieldTravelRouter.cs: Plan confirmation reset patched")

patch(
    "Automation/Movement.cs",
    """    public static float DistanceToPlayer(Vector3 world)
    {
        var me = Svc.Objects.LocalPlayer;
        return me == null ? float.MaxValue : HorizontalDistance(me.Position, world);
    }

""",
    """    public static float DistanceToPlayer(Vector3 world)
    {
        var me = Svc.Objects.LocalPlayer;
        return me == null ? float.MaxValue : HorizontalDistance(me.Position, world);
    }

    /// <summary>Current BOCCHI-style yalm-equivalent cost used to rank fresh skirmish destinations.</summary>
    public float EstimateTravelCost(Vector3 world)
    {
        var me = Svc.Objects.LocalPlayer;
        return me == null ? float.MaxValue : _fieldRouter.EstimateCost(me.Position, world);
    }

""",
)

# TargetSelector gets the same Movement whose router will execute the chosen destination.
patch(
    "Automation/TargetSelector.cs",
    """public sealed class TargetSelector(CeCatalog catalog, Configuration config, RegionResolver regions)
{
    private readonly CeCatalog _catalog = catalog;
    private readonly Configuration _config = config;
    private readonly RegionResolver _regions = regions;
""",
    """public sealed class TargetSelector(CeCatalog catalog, Configuration config, RegionResolver regions, Movement movement)
{
    private readonly CeCatalog _catalog = catalog;
    private readonly Configuration _config = config;
    private readonly RegionResolver _regions = regions;
    private readonly Movement _movement = movement;
""",
)

# CE registration is remote: explicit priority then stable event id, never player distance.
patch(
    "Automation/TargetSelector.cs",
    """        CeSnapshot? best = null;
        var bestRank = int.MaxValue;
        var bestDistance = float.MaxValue;

        foreach (var ce in engagements)
        {
            if (!IsEligible(ce))
                continue;

            var largeScale = _catalog.IsLargeScale(ce.EventId);
            var rank = largeScale ? int.MinValue : PriorityRank(ce.EventId);
            var distance = ce.HasPosition ? Movement.DistanceToPlayer(ce.Position) : float.MaxValue;

            if (best == null || Better(rank, ce.EventId, distance, bestRank, best.Value.EventId, bestDistance, deterministic))
            {
                best = ce;
                bestRank = rank;
                bestDistance = distance;
            }
        }

        return best;
""",
    """        CeSnapshot? best = null;
        var bestRank = int.MaxValue;

        foreach (var ce in engagements)
        {
            if (!IsEligible(ce))
                continue;

            var largeScale = _catalog.IsLargeScale(ce.EventId);
            var rank = largeScale ? int.MinValue : PriorityRank(ce.EventId);
            if (best == null || rank < bestRank || (rank == bestRank && ce.EventId < best.Value.EventId))
            {
                best = ce;
                bestRank = rank;
            }
        }

        return best;
""",
)

# Fresh skirmishes use route cost, not straight-line distance.
patch(
    "Automation/TargetSelector.cs",
    """        var bestPosition = Vector3.Zero;
        var bestDistance = float.MaxValue;
""",
    """        var bestPosition = Vector3.Zero;
        var bestCost = float.MaxValue;
""",
)
patch(
    "Automation/TargetSelector.cs",
    """                var distance = Movement.DistanceToPlayer(fate.Position);

                var better = bestId == 0
                    || (!deterministic && distance < bestDistance)
                    || (deterministic && fate.FateId < bestId);
""",
    """                var cost = _movement.EstimateTravelCost(fate.Position);

                var better = bestId == 0
                    || (!deterministic && (cost < bestCost || (cost == bestCost && fate.FateId < bestId)))
                    || (deterministic && fate.FateId < bestId);
""",
)
patch(
    "Automation/TargetSelector.cs",
    """                bestPosition = fate.Position;
                bestDistance = distance;
""",
    """                bestPosition = fate.Position;
                bestCost = cost;
""",
)

patch(
    "Plugin.cs",
    """        _selector = new TargetSelector(_catalog, _config, _regions);
""",
    """        _selector = new TargetSelector(_catalog, _config, _regions, _movement);
""",
)
