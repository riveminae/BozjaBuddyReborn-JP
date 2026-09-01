from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if new in text:
        print(f"{path}: already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor not found in {path}: {old[:80]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: patched")


# SurvivalPolicy: let the caller decide whether this tick is allowed to spend a Reraiser.
replace_once(
    ROOT / "Game/SurvivalPolicy.cs",
    """    public IEnumerable<string> EmergencyPriority(bool travelling)\n    {\n        // Resistance Reraiser is an instant item. Lost Reraise is the all-job action fallback but\n        // has a cast, so never attempt it while travel is intentionally continuing.\n        yield return \"Resistance Reraiser\";\n        if (!travelling)\n            yield return \"Lost Reraise\";\n""",
    """    public IEnumerable<string> EmergencyPriority(bool travelling, bool includeReraiser)\n    {\n        // Reraiser is intentionally edge-triggered by HolsterDriver: remaining below the emergency\n        // threshold for several ticks must not consume one every time the prior attempt has no\n        // recognisable status. A fresh risk window begins only after HP leaves and re-enters the\n        // emergency band. Lost Reraise remains a normal emergency fallback when standing still.\n        if (includeReraiser)\n            yield return \"Resistance Reraiser\";\n        if (!travelling)\n            yield return \"Lost Reraise\";\n""",
)

# HolsterDriver: remember whether we were already inside the emergency band.
replace_once(
    ROOT / "Automation/HolsterDriver.cs",
    """    private long _lastUseMs;\n    private long _lastSurvivalUseMs;\n\n    private Phase _phase;\n""",
    """    private long _lastUseMs;\n    private long _lastSurvivalUseMs;\n    private bool _insideEmergencyRiskWindow;\n\n    private Phase _phase;\n""",
)

replace_once(
    ROOT / "Automation/HolsterDriver.cs",
    """        var hp = SurvivalPolicy.HpFraction();\n        var list = hp <= _survival.EmergencyThreshold\n            ? _survival.EmergencyPriority(travelling)\n            : hp <= _survival.HealThreshold\n                ? _survival.HealPriority(travelling)\n                : null;\n\n        if (list == null)\n            return false;\n""",
    """        var hp = SurvivalPolicy.HpFraction();\n        var emergency = hp <= _survival.EmergencyThreshold;\n        var enteredEmergency = emergency && !_insideEmergencyRiskWindow;\n        _insideEmergencyRiskWindow = emergency;\n\n        var list = emergency\n            ? _survival.EmergencyPriority(travelling, includeReraiser: enteredEmergency)\n            : hp <= _survival.HealThreshold\n                ? _survival.HealPriority(travelling)\n                : null;\n\n        if (list == null)\n            return false;\n""",
)

replace_once(
    ROOT / "Automation/HolsterDriver.cs",
    """    public void Reset()\n    {\n        _lastUseMs = 0;\n        LastResult = string.Empty;\n        Abandon();\n    }\n""",
    """    public void Reset()\n    {\n        _lastUseMs = 0;\n        _lastSurvivalUseMs = 0;\n        _insideEmergencyRiskWindow = false;\n        LastResult = string.Empty;\n        Abandon();\n    }\n""",
)
