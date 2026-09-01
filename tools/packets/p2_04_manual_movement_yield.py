from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/Movement.cs"
text = P.read_text(encoding="utf-8-sig")

def repl(old: str, new: str) -> None:
    global text
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"Movement.cs anchor missing: {old[:100]!r}")
    text = text.replace(old, new, 1)

repl(
    """    private readonly AggroAvoidance _avoidance = avoidance;\n    private readonly FieldTravelRouter _fieldRouter = new(new LifestreamIpc(pluginInterface), config);\n\n""",
    """    private readonly AggroAvoidance _avoidance = avoidance;\n    private readonly FieldTravelRouter _fieldRouter = new(new LifestreamIpc(pluginInterface), config);\n    private readonly ManualMovementYield _manualYield = new();\n\n""",
)
repl(
    """    public bool LifestreamAvailable => _fieldRouter.LifestreamAvailable;\n\n    /// <summary>Distance from the local player""",
    """    public bool LifestreamAvailable => _fieldRouter.LifestreamAvailable;\n    public bool YieldingToManualMovement => _manualYield.ShouldYield();\n\n    /// <summary>Distance from the local player""",
)
repl(
    """    public bool TravelTo(Vector3 destination, float range)\n    {\n        if (_config.LegacyMovement || !_config.UseBocchiNavigation)\n            return TravelDirectTo(destination, range);\n\n        var directive = _fieldRouter.Resolve(destination, range);\n""",
    """    public bool TravelTo(Vector3 destination, float range)\n    {\n        // Direct player movement wins over both legacy and BOCCHI routing. Suspend preserves the\n        // current route/stall history while repeatedly pumping vnavmesh's global stop until the\n        // player has been quiet for the guard's three-second window. Crucially this happens before\n        // Resolve(), so pressing movement during an eligible Return route cannot trigger Return.\n        if (_manualYield.ShouldYield())\n        {\n            Suspend();\n            return true;\n        }\n\n        if (_config.LegacyMovement || !_config.UseBocchiNavigation)\n            return TravelDirectTo(destination, range);\n\n        var directive = _fieldRouter.Resolve(destination, range);\n""",
)
repl(
    """        _suspended = false;\n        _forceReissue = false;\n        ClearDetour();\n""",
    """        _suspended = false;\n        _forceReissue = false;\n        _manualYield.Reset();\n        ClearDetour();\n""",
)

P.write_text(text, encoding="utf-8")
print("Automation/Movement.cs: manual movement yield wired")
