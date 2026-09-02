from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/FieldTravelRouter.cs"
text = P.read_text(encoding="utf-8-sig")

MARKER = "private bool _planningDiscardResult;"
if MARKER in text:
    print("Automation/FieldTravelRouter.cs: path-cost probe drain already applied")
    raise SystemExit(0)


def patch(old: str, new: str, label: str) -> None:
    global text
    if old not in text:
        raise RuntimeError(f"FieldTravelRouter.cs anchor missing for {label}: {old[:180]!r}")
    text = text.replace(old, new, 1)
    print(f"Automation/FieldTravelRouter.cs: patched {label}")


patch(
    """    private Vector3 _planningStart;\n    private Vector3 _planningDeparturePoint;\n    private long _planningStartedMs;\n    private bool _planningCancelSent;\n""",
    """    private Vector3 _planningStart;\n    private Vector3 _planningDeparturePoint;\n    private uint _planningTerritory;\n    private long _planningStartedMs;\n    private bool _planningCancelSent;\n    private bool _planningDiscardResult;\n""",
    "planning drain state",
)

# Stop/reset requests cancellation but does not block the caller. A later Plan sees HasPending and
# enters the drain state before any real movement path is allowed to start.
patch(
    """    public void Reset()\n    {\n        _goal = Vector3.Zero;\n""",
    """    public void Reset()\n    {\n        _pathCosts.CancelAllPending();\n\n        _goal = Vector3.Zero;\n""",
    "Reset cancels telemetry",
)

patch(
    """        _planningStart = Vector3.Zero;\n        _planningDeparturePoint = Vector3.Zero;\n        _planningStartedMs = 0;\n        _planningCancelSent = false;\n        RouteDescription = \"直接移動\";\n""",
    """        _planningStart = Vector3.Zero;\n        _planningDeparturePoint = Vector3.Zero;\n        _planningTerritory = 0;\n        _planningStartedMs = 0;\n        _planningCancelSent = false;\n        _planningDiscardResult = false;\n        RouteDescription = \"直接移動\";\n""",
    "Reset planning drain fields",
)

# The drain branch goes first. It deliberately ignores any measured result from the old goal and
# waits for the old cancelable query to actually finish before computing the new route.
patch(
    """        if (_mode == FieldTravelMode.Planning)\n        {\n            var territory = Svc.ClientState.TerritoryType;\n            var now = Environment.TickCount64;\n\n            if (_pathCosts.TryGet(territory, _planningStart, _planningDeparturePoint, out var measured))\n""",
    """        if (_mode == FieldTravelMode.Planning)\n        {\n            var now = Environment.TickCount64;\n\n            if (_planningDiscardResult)\n            {\n                _pathCosts.CancelAllPending();\n                if (_pathCosts.HasPending)\n                {\n                    RouteDescription = \"旧実経路コスト計算の終了待ち\";\n                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);\n                }\n\n                _planningDiscardResult = false;\n                Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream, allowPathCostWait: true);\n                return Resolve(finalDestination, finalRange, waitForOptionalLifestream);\n            }\n\n            var territory = _planningTerritory;\n            if (territory == 0)\n                territory = Svc.ClientState.TerritoryType;\n\n            if (_pathCosts.TryGet(territory, _planningStart, _planningDeparturePoint, out var measured))\n""",
    "Planning drains stale probes",
)

# Before Plan overwrites the old planning identity, turn any still-running probe into a drain for
# the new destination. This covers objective switches while Planning is active and Start shortly
# after Stop while cancellation is still propagating through vnavmesh.
patch(
    """    {\n        _goal = finalDestination;\n        _goalRange = finalRange;\n        _departure = null;\n""",
    """    {\n        if (_pathCosts.HasPending)\n        {\n            _pathCosts.CancelAllPending();\n            _goal = finalDestination;\n            _goalRange = finalRange;\n            _departure = null;\n            _inbound = null;\n            _mode = FieldTravelMode.Planning;\n            _planningStart = Vector3.Zero;\n            _planningDeparturePoint = Vector3.Zero;\n            _planningTerritory = 0;\n            _planningStartedMs = Environment.TickCount64;\n            _planningCancelSent = true;\n            _planningDiscardResult = true;\n            RouteDescription = \"旧実経路コスト計算の終了待ち\";\n            return;\n        }\n\n        _goal = finalDestination;\n        _goalRange = finalRange;\n        _departure = null;\n""",
    "Plan drains previous probe",
)

# Normal Plan reset. Keep the established Return/Lifestream block contiguous for p9_04.
patch(
    """        _planningStart = Vector3.Zero;\n        _planningDeparturePoint = Vector3.Zero;\n        _planningStartedMs = 0;\n        _planningCancelSent = false;\n        _returnStartedMs = 0;\n""",
    """        _planningStart = Vector3.Zero;\n        _planningDeparturePoint = Vector3.Zero;\n        _planningTerritory = 0;\n        _planningStartedMs = 0;\n        _planningCancelSent = false;\n        _planningDiscardResult = false;\n        _returnStartedMs = 0;\n""",
    "Plan resets drain state",
)

# Remember the territory that owns the query. ClientState.TerritoryType can change while the task
# is running; using the live territory would miss the cached key and fail to cancel the old probe.
patch(
    """                    _planningStart = start;\n                    _planningDeparturePoint = probePoint;\n                    _planningStartedMs = Environment.TickCount64;\n                    _planningCancelSent = false;\n""",
    """                    _planningStart = start;\n                    _planningDeparturePoint = probePoint;\n                    _planningTerritory = territory;\n                    _planningStartedMs = Environment.TickCount64;\n                    _planningCancelSent = false;\n                    _planningDiscardResult = false;\n""",
    "probe remembers territory",
)

P.write_text(text, encoding="utf-8")
print("Automation/FieldTravelRouter.cs: stale path-cost probes drain before replanning movement")
