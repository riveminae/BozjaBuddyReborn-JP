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
    """    private readonly DeathRecoveryDriver _deathRecovery;\n    private readonly DependencySupervisor _dependencies;\n\n""",
    """    private readonly DeathRecoveryDriver _deathRecovery;\n    private readonly DependencySupervisor _dependencies;\n    private readonly SafeStopCoordinator _safeStop = new();\n\n""",
)
repl(
    """        _dependencies.Reset();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n""",
    """        _dependencies.Reset();\n        _safeStop.Reset();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n""",
)
repl(
    """            // P9-03 adds the safe-return policy. Until that packet is present, timeout fails closed.\n            Stop($\"必須プラグインが60秒以内に復帰しませんでした: {dependency.MissingText}。\");\n            return;\n        }\n\n        if (!_navmesh.MeshReady)\n""",
    """            var safeStop = _safeStop.Tick(Svc.Condition[ConditionFlag.InCombat]);\n            Status = safeStop.JapaneseStatus + $\" ({dependency.MissingText})\";\n            if (safeStop.StopNow)\n                Stop(Status);\n            return;\n        }\n\n        // A recovered dependency cancels any pending pre-stop Return state.\n        _safeStop.Reset();\n\n        if (!_navmesh.MeshReady)\n""",
)

P.write_text(text, encoding="utf-8")
print("Automation/BozjaController.cs: safe dependency-stop policy wired")
