from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ROUTER = ROOT / "Automation/FieldTravelRouter.cs"
MOVEMENT = ROOT / "Automation/Movement.cs"
text = ROUTER.read_text(encoding="utf-8-sig")

if "PathCostPlanningWaitMs" in text:
    print("Automation/FieldTravelRouter.cs: measured path-cost planning already applied")
    raise SystemExit(0)


def patch(old: str, new: str, label: str) -> None:
    global text
    if old not in text:
        raise RuntimeError(f"FieldTravelRouter.cs anchor missing for {label}: {old[:180]!r}")
    text = text.replace(old, new, 1)
    print(f"Automation/FieldTravelRouter.cs: patched {label}")


patch(
    """    Returning = 5,\n    WaitingForLifestream = 6,\n}\n""",
    """    Returning = 5,\n    WaitingForLifestream = 6,\n    Planning = 7,\n}\n""",
    "Planning mode",
)

patch(
    """/// The candidate costs deliberately use BOCCHI's yalm-equivalent constants. The BBR adapter still\n/// uses horizontal-distance estimates for walk legs instead of importing BOCCHI's entire graph\n/// service; Movement executes every walk leg and therefore retains vnavmesh snapping, stall\n/// recovery and IV/V/★ avoidance.\n/// </summary>\npublic sealed class FieldTravelRouter(LifestreamIpc lifestream, Configuration config)\n{\n    private readonly LifestreamIpc _lifestream = lifestream;\n    private readonly Configuration _config = config;\n""",
    """/// The candidate costs deliberately use BOCCHI's yalm-equivalent constants. For the departure\n/// walk, BBR now measures the actual vnavmesh ground path with the same Nav.Pathfind primitive\n/// BOCCHI/Ocelot uses. Inbound-to-goal costs remain horizontal estimates because BBR does not vendor\n/// the entire zone graph. Movement still executes every walk leg, preserving stall recovery and\n/// IV/V/★ avoidance.\n/// </summary>\npublic sealed class FieldTravelRouter(LifestreamIpc lifestream, Configuration config, NavmeshIpc navmesh)\n{\n    private readonly LifestreamIpc _lifestream = lifestream;\n    private readonly Configuration _config = config;\n    private readonly NavmeshIpc _navmesh = navmesh;\n    private readonly NavPathCostCache _pathCosts = new(navmesh);\n""",
    "router navmesh dependency",
)

patch(
    """    private long _optionalLifestreamWaitStartedMs;\n    private bool _fallbackForGoal;\n\n    private const float GoalIdentityRadius = 10f;\n""",
    """    private long _optionalLifestreamWaitStartedMs;\n    private bool _fallbackForGoal;\n\n    // Short-lived cost-probe state. The character is held still only for a new long-distance\n    // route and only while this plugin's cancelable Nav.Pathfind query is alive.\n    private Vector3 _planningStart;\n    private Vector3 _planningDeparturePoint;\n    private long _planningStartedMs;\n    private bool _planningCancelSent;\n\n    private const float GoalIdentityRadius = 10f;\n""",
    "planning state",
)

patch(
    """    private const long ReturnTimeoutMs = 25_000;\n    private const long OptionalLifestreamWaitMs = 30_000;\n""",
    """    private const long ReturnTimeoutMs = 25_000;\n    private const long OptionalLifestreamWaitMs = 30_000;\n    private const long PathCostPlanningWaitMs = 750;\n""",
    "planning timeout",
)

patch(
    """        _optionalLifestreamWaitStartedMs = 0;\n        _fallbackForGoal = false;\n        RouteDescription = \"直接移動\";\n""",
    """        _optionalLifestreamWaitStartedMs = 0;\n        _fallbackForGoal = false;\n        _planningStart = Vector3.Zero;\n        _planningDeparturePoint = Vector3.Zero;\n        _planningStartedMs = 0;\n        _planningCancelSent = false;\n        RouteDescription = \"直接移動\";\n""",
    "Reset planning state",
)

# Planning must be handled before the ordinary null-departure fallback because no inbound is chosen
# until the measured departure walk is available (or safely cancelled on timeout).
resolve_anchor = """        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)\n            return Direct(finalDestination, finalRange, FieldTravelMode.Direct, \"直接移動\");\n\n        if (_mode == FieldTravelMode.WaitingForLifestream)\n"""
resolve_new = """        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)\n            return Direct(finalDestination, finalRange, FieldTravelMode.Direct, \"直接移動\");\n\n        if (_mode == FieldTravelMode.Planning)\n        {\n            var territory = Svc.ClientState.TerritoryType;\n            var now = Environment.TickCount64;\n\n            if (_pathCosts.TryGet(territory, _planningStart, _planningDeparturePoint, out var measured))\n            {\n                Svc.Log.Debug($\"[BozjaBuddyReborn] vnavmesh measured departure walk at {measured:F1}y; finalizing BOCCHI route.\");\n                var planStart = _planningStart;\n                Plan(planStart, finalDestination, finalRange, waitForOptionalLifestream, allowPathCostWait: false);\n                return Resolve(finalDestination, finalRange, waitForOptionalLifestream);\n            }\n\n            if (now - _planningStartedMs < PathCostPlanningWaitMs)\n            {\n                RouteDescription = \"vnavmeshで実経路コストを計算中\";\n                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);\n            }\n\n            if (!_planningCancelSent)\n            {\n                _pathCosts.Cancel(territory, _planningStart, _planningDeparturePoint);\n                _planningCancelSent = true;\n                RouteDescription = \"実経路コスト計算を打ち切り中\";\n                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);\n            }\n\n            // Cancellation is cooperative inside vnavmesh. Never overlap the real movement path\n            // with a telemetry Pathfind that has not actually stopped yet.\n            if (_pathCosts.IsPending(territory, _planningStart, _planningDeparturePoint))\n            {\n                RouteDescription = \"実経路コスト計算の終了待ち\";\n                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);\n            }\n\n            Svc.Log.Debug(\"[BozjaBuddyReborn] Departure path-cost probe exceeded 750ms; using horizontal fallback for this route.\");\n            var fallbackStart = _planningStart;\n            Plan(fallbackStart, finalDestination, finalRange, waitForOptionalLifestream, allowPathCostWait: false);\n            return Resolve(finalDestination, finalRange, waitForOptionalLifestream);\n        }\n\n        if (_mode == FieldTravelMode.WaitingForLifestream)\n"""
patch(resolve_anchor, resolve_new, "Planning resolve state")

patch(
    """    private void Plan(\n        Vector3 start,\n        Vector3 finalDestination,\n        float finalRange,\n        bool waitForOptionalLifestream = false)\n""",
    """    private void Plan(\n        Vector3 start,\n        Vector3 finalDestination,\n        float finalRange,\n        bool waitForOptionalLifestream = false,\n        bool allowPathCostWait = true)\n""",
    "Plan allowPathCostWait parameter",
)

patch(
    """        _returnConfirmationSent = false;\n        _optionalLifestreamWaitStartedMs = 0;\n\n        var territory = Svc.ClientState.TerritoryType;\n""",
    """        _returnConfirmationSent = false;\n        _optionalLifestreamWaitStartedMs = 0;\n        _planningStart = Vector3.Zero;\n        _planningDeparturePoint = Vector3.Zero;\n        _planningStartedMs = 0;\n        _planningCancelSent = false;\n\n        var territory = Svc.ClientState.TerritoryType;\n""",
    "Plan planning reset",
)

# Resolve one departure before candidate scoring, just like BOCCHI. For a new long route, give the
# cancelable pathfinder up to 750ms to replace only the departure-walk horizontal estimate.
old = """        var best = direct;\n        var bestMode = FieldTravelMode.Direct;\n        FieldAethernet.Node? bestDeparture = null;\n        FieldAethernet.Node? bestInbound = null;\n\n        // Walk -> aethernet -> walk candidate. This is offered only when Lifestream is currently\n        // answering; optional-dependency failure must never strand the route planner.\n        var resolvedDeparture = ResolveDepartureNode(nodes, start);\n        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2\n            && resolvedDeparture is { } departure)\n        {\n            var walkToDeparture = Movement.HorizontalDistance(start, departure.Position);\n"""
new = """        var best = direct;\n        var bestMode = FieldTravelMode.Direct;\n        FieldAethernet.Node? bestDeparture = null;\n        FieldAethernet.Node? bestInbound = null;\n\n        var resolvedDeparture = ResolveDepartureNode(nodes, start);\n        if (allowPathCostWait\n            && _config.UseAethernetTravel\n            && _lifestream.Available\n            && nodes.Count >= 2\n            && resolvedDeparture is { } probeDeparture)\n        {\n            var probePoint = ResolveNodePathPoint(probeDeparture, start.Y);\n            if (!_pathCosts.TryGet(territory, start, probePoint, out _))\n            {\n                var fallback = Movement.HorizontalDistance(start, probeDeparture.Position);\n                _pathCosts.Estimate(territory, start, probePoint, fallback, request: true);\n                if (_pathCosts.IsPending(territory, start, probePoint))\n                {\n                    _mode = FieldTravelMode.Planning;\n                    _departure = probeDeparture;\n                    _planningStart = start;\n                    _planningDeparturePoint = probePoint;\n                    _planningStartedMs = Environment.TickCount64;\n                    _planningCancelSent = false;\n                    RouteDescription = \"vnavmeshで実経路コストを計算中\";\n                    return;\n                }\n            }\n        }\n\n        // Walk -> aethernet -> walk candidate. This is offered only when Lifestream is currently\n        // answering; optional-dependency failure must never strand the route planner.\n        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2\n            && resolvedDeparture is { } departure)\n        {\n            var departurePoint = ResolveNodePathPoint(departure, start.Y);\n            var departureFallback = Movement.HorizontalDistance(start, departure.Position);\n            var walkToDeparture = _pathCosts.Estimate(\n                territory, start, departurePoint, departureFallback, request: false);\n"""
patch(old, new, "measured departure candidate")

# Ground the aethernet X/Z for the path-cost query. ResolveGroundPoint's raw fallback carries the
# 1024y seed; detect that and use the player's current floor rather than feeding an impossible Y.
helper_anchor = """    private static FieldAethernet.Node? ResolveDepartureNode(\n"""
helper = """    private Vector3 ResolveNodePathPoint(FieldAethernet.Node node, float fallbackY)\n    {\n        var resolved = _navmesh.ResolveGroundPoint(node.Position.X, node.Position.Z);\n        if (!float.IsFinite(resolved.X) || !float.IsFinite(resolved.Y) || !float.IsFinite(resolved.Z)\n            || resolved.Y > 900f)\n            return new Vector3(node.Position.X, fallbackY, node.Position.Z);\n        return resolved;\n    }\n\n"""
if helper_anchor not in text:
    raise RuntimeError("FieldTravelRouter ResolveDepartureNode helper anchor missing")
text = text.replace(helper_anchor, helper + helper_anchor, 1)
print("Automation/FieldTravelRouter.cs: added node ground resolver")

ROUTER.write_text(text, encoding="utf-8")

# The router needs the same NavmeshIpc instance Movement already owns; no second IPC wrapper or
# movement owner is created.
m = MOVEMENT.read_text(encoding="utf-8-sig")
old = "    private readonly FieldTravelRouter _fieldRouter = new(new LifestreamIpc(pluginInterface), config);\n"
new = "    private readonly FieldTravelRouter _fieldRouter = new(new LifestreamIpc(pluginInterface), config, navmesh);\n"
if new in m:
    print("Automation/Movement.cs: measured-cost router wiring already applied")
elif old in m:
    MOVEMENT.write_text(m.replace(old, new, 1), encoding="utf-8")
    print("Automation/Movement.cs: measured-cost router wiring applied")
else:
    raise RuntimeError("Movement.cs FieldTravelRouter constructor anchor missing")
