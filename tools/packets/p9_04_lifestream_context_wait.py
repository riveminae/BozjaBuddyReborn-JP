from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str, marker: str | None = None) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    marker = marker or new
    if marker in text:
        print(f"{path}: Lifestream context policy already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:160]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: Lifestream context policy patched")


patch(
    "Automation/FieldTravelRouter.cs",
    """    FallbackDirect = 4,\n    Returning = 5,\n}\n""",
    """    FallbackDirect = 4,\n    Returning = 5,\n    WaitingForLifestream = 6,\n}\n""",
    "WaitingForLifestream = 6",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """    private long _returnStartedMs;\n    private bool _returnConfirmationSent;\n    private bool _fallbackForGoal;\n""",
    """    private long _returnStartedMs;\n    private bool _returnConfirmationSent;\n    private long _optionalLifestreamWaitStartedMs;\n    private bool _fallbackForGoal;\n""",
    "_optionalLifestreamWaitStartedMs",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """    private const long TeleportTimeoutMs = 20_000;\n    private const long ReturnTimeoutMs = 25_000;\n""",
    """    private const long TeleportTimeoutMs = 20_000;\n    private const long ReturnTimeoutMs = 25_000;\n    private const long OptionalLifestreamWaitMs = 30_000;\n""",
    "OptionalLifestreamWaitMs",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n        _fallbackForGoal = false;\n""",
    """        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n        _optionalLifestreamWaitStartedMs = 0;\n        _fallbackForGoal = false;\n""",
    "_optionalLifestreamWaitStartedMs = 0;\n        _fallbackForGoal = false;",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """    public FieldTravelDirective Resolve(Vector3 finalDestination, float finalRange)\n    {\n        var me = Svc.Objects.LocalPlayer;\n""",
    """    public FieldTravelDirective Resolve(\n        Vector3 finalDestination,\n        float finalRange,\n        bool waitForOptionalLifestream = false)\n    {\n        var me = Svc.Objects.LocalPlayer;\n""",
    "bool waitForOptionalLifestream = false",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """        if (!IsRoutingTo(finalDestination))\n            Plan(me.Position, finalDestination, finalRange);\n\n        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)\n""",
    """        if (!IsRoutingTo(finalDestination))\n            Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream);\n\n        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)\n""",
    "Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream);",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """        if (_fallbackForGoal || _departure is null || _inbound is null)\n            return Direct(finalDestination, finalRange,\n                _fallbackForGoal ? FieldTravelMode.FallbackDirect : FieldTravelMode.Direct,\n                _fallbackForGoal ? \"高速移動失敗のため直接移動\" : \"直接移動\");\n\n        var departure = _departure.Value;\n""",
    """        if (_mode == FieldTravelMode.WaitingForLifestream)\n        {\n            var now = Environment.TickCount64;\n            RouteDescription = \"Lifestream復帰待ち（最大30秒）\";\n\n            if (_lifestream.Available)\n            {\n                Svc.Log.Information(\"[BozjaBuddyReborn] Optional Lifestream recovered during nonurgent wait; replanning route.\");\n                Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream: false);\n                return Resolve(finalDestination, finalRange, waitForOptionalLifestream: false);\n            }\n\n            if (_optionalLifestreamWaitStartedMs == 0)\n                _optionalLifestreamWaitStartedMs = now;\n\n            if (now - _optionalLifestreamWaitStartedMs >= OptionalLifestreamWaitMs)\n            {\n                FallBack(\"optional Lifestream did not recover within the 30-second nonurgent window\");\n                return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect,\n                    \"Lifestreamが30秒以内に復帰しないため直接移動\");\n            }\n\n            return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);\n        }\n\n        if (_fallbackForGoal || _departure is null || _inbound is null)\n            return Direct(finalDestination, finalRange,\n                _fallbackForGoal ? FieldTravelMode.FallbackDirect : FieldTravelMode.Direct,\n                _fallbackForGoal ? \"高速移動失敗のため直接移動\" : \"直接移動\");\n\n        var departure = _departure.Value;\n""",
    "if (_mode == FieldTravelMode.WaitingForLifestream)",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """    private void Plan(Vector3 start, Vector3 finalDestination, float finalRange)\n    {\n""",
    """    private void Plan(\n        Vector3 start,\n        Vector3 finalDestination,\n        float finalRange,\n        bool waitForOptionalLifestream = false)\n    {\n""",
    "private void Plan(\n        Vector3 start,",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n\n        var territory = Svc.ClientState.TerritoryType;\n""",
    """        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n        _optionalLifestreamWaitStartedMs = 0;\n\n        var territory = Svc.ClientState.TerritoryType;\n""",
    "_returnConfirmationSent = false;\n        _optionalLifestreamWaitStartedMs = 0;\n\n        var territory",
)

patch(
    "Automation/FieldTravelRouter.cs",
    """        if (bestDeparture is null || bestInbound is null || bestMode == FieldTravelMode.Direct)\n        {\n            _mode = FieldTravelMode.Direct;\n            RouteDescription = \"直接移動\";\n            return;\n        }\n""",
    """        // In a nonurgent context (idle staging, cache errands and future supply runs), wait\n        // briefly for optional Lifestream only when an aethernet route WOULD actually beat the\n        // best route that is usable right now. Activity travel never passes this flag, so a CE or\n        // skirmish cannot lose 30 seconds to an optional plugin outage.\n        if (waitForOptionalLifestream\n            && _config.UseAethernetTravel\n            && !_lifestream.Available\n            && nodes.Count >= 2)\n        {\n            var hypothetical = best;\n            foreach (var departure in nodes)\n            foreach (var inbound in nodes)\n            {\n                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)\n                    continue;\n                var candidate = Movement.HorizontalDistance(start, departure.Position)\n                                + hopCost\n                                + Movement.HorizontalDistance(inbound.Position, finalDestination);\n                hypothetical = MathF.Min(hypothetical, candidate);\n            }\n\n            if (_config.UseReturnRouting\n                && baseCamp is { } waitCamp\n                && Movement.HorizontalDistance(start, waitCamp.Position) > NavigationConstants.CampRadius\n                && !Svc.Condition[ConditionFlag.InCombat]\n                && GeneralActions.ReturnReady())\n            {\n                foreach (var inbound in nodes)\n                {\n                    if (inbound.IsBaseCamp)\n                        continue;\n                    var candidate = returnCost + hopCost\n                                    + Movement.HorizontalDistance(inbound.Position, finalDestination);\n                    hypothetical = MathF.Min(hypothetical, candidate);\n                }\n            }\n\n            if (hypothetical < best)\n            {\n                _mode = FieldTravelMode.WaitingForLifestream;\n                _optionalLifestreamWaitStartedMs = Environment.TickCount64;\n                RouteDescription = \"Lifestream復帰待ち（最大30秒）\";\n                Svc.Log.Information(\n                    $\"[BozjaBuddyReborn] Nonurgent route can benefit from Lifestream; waiting up to 30 seconds \" +\n                    $\"before direct fallback (current={best:F0}y, hypothetical={hypothetical:F0}y).\");\n                return;\n            }\n        }\n\n        if (bestDeparture is null || bestInbound is null || bestMode == FieldTravelMode.Direct)\n        {\n            _mode = FieldTravelMode.Direct;\n            RouteDescription = \"直接移動\";\n            return;\n        }\n""",
    "Nonurgent route can benefit from Lifestream",
)

patch(
    "Automation/Movement.cs",
    """    public bool TravelTo(Vector3 destination, float range)\n    {\n""",
    """    public bool TravelTo(\n        Vector3 destination,\n        float range,\n        bool waitForOptionalDependencies = false)\n    {\n""",
    "bool waitForOptionalDependencies = false",
)

patch(
    "Automation/Movement.cs",
    """        var directive = _fieldRouter.Resolve(destination, range);\n""",
    """        var directive = _fieldRouter.Resolve(destination, range, waitForOptionalDependencies);\n""",
    "_fieldRouter.Resolve(destination, range, waitForOptionalDependencies)",
)

patch(
    "Automation/BozjaController.cs",
    """        _movement.TravelTo(spot, _config.IdleArriveRange);\n        Status = $\"{reason} 待機地点 {label} へ移動中 \" +\n                 $\"（残り {Movement.DistanceToPlayer(spot):F0}y）。\";\n""",
    """        _movement.TravelTo(spot, _config.IdleArriveRange, waitForOptionalDependencies: true);\n        if (_movement.TravelMode == FieldTravelMode.WaitingForLifestream)\n        {\n            Status = $\"{reason} Lifestreamの復帰を最大30秒待っています。\";\n            return;\n        }\n\n        Status = $\"{reason} 待機地点 {label} へ移動中 \" +\n                 $\"（残り {Movement.DistanceToPlayer(spot):F0}y）。\";\n""",
    "waitForOptionalDependencies: true",
)

patch(
    "Automation/ErrandRunner.cs",
    """        _movement.TravelTo(obj.Position, InteractRange - 0.5f);\n""",
    """        _movement.TravelTo(\n            obj.Position,\n            InteractRange - 0.5f,\n            waitForOptionalDependencies: true);\n""",
    "waitForOptionalDependencies: true",
)
