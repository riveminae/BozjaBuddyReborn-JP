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
        raise RuntimeError(f"anchor missing in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: patched")

patch(
    "Game/SurvivalPolicy.cs",
    """    public IEnumerable<string> EmergencyPriority(bool travelling)\n    {\n        // Resistance Reraiser is an instant item. Lost Reraise is the all-job action fallback but\n        // has a cast, so never attempt it while travel is intentionally continuing.\n        yield return \"Resistance Reraiser\";\n        if (!travelling)\n            yield return \"Lost Reraise\";\n\n""",
    """    public IEnumerable<string> EmergencyPriority(bool travelling, bool includeReraiser)\n    {\n        // Reraiser is offered only on the edge into an emergency risk window. Remaining below the\n        // threshold must not consume another one merely because the previous status disappeared.\n        if (includeReraiser)\n            yield return \"Resistance Reraiser\";\n        if (!travelling)\n            yield return \"Lost Reraise\";\n\n""",
)

patch(
    "Automation/HolsterDriver.cs",
    """    private long _lastUseMs;\n    private long _lastSurvivalUseMs;\n\n""",
    """    private long _lastUseMs;\n    private long _lastSurvivalUseMs;\n    private bool _emergencyRiskWindow;\n\n""",
)

patch(
    "Automation/HolsterDriver.cs",
    """        var hp = SurvivalPolicy.HpFraction();\n        var list = hp <= _survival.EmergencyThreshold\n            ? _survival.EmergencyPriority(travelling)\n            : hp <= _survival.HealThreshold\n                ? _survival.HealPriority(travelling)\n                : null;\n\n""",
    """        var hp = SurvivalPolicy.HpFraction();\n        var emergency = hp <= _survival.EmergencyThreshold;\n        var enteredEmergency = emergency && !_emergencyRiskWindow;\n        if (enteredEmergency)\n            _emergencyRiskWindow = true;\n        else if (!emergency && hp > _survival.HealThreshold)\n            _emergencyRiskWindow = false;\n\n        var list = emergency\n            ? _survival.EmergencyPriority(travelling, enteredEmergency)\n            : hp <= _survival.HealThreshold\n                ? _survival.HealPriority(travelling)\n                : null;\n\n""",
)

patch(
    "Automation/HolsterDriver.cs",
    """        _lastUseMs = 0;\n        LastResult = string.Empty;\n        Abandon();\n""",
    """        _lastUseMs = 0;\n        _emergencyRiskWindow = false;\n        LastResult = string.Empty;\n        Abandon();\n""",
)
