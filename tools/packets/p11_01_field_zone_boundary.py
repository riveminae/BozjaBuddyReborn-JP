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
    """    public void Start()\n    {\n        Running = true;\n""",
    """    public void Start()\n    {\n        if (Svc.Objects.LocalPlayer == null)\n        {\n            Running = false;\n            State = ControllerState.Blocked;\n            Status = \"ログイン後に開始してください。\";\n            return;\n        }\n\n        if (!FieldState.InFieldZone)\n        {\n            Running = false;\n            State = ControllerState.Blocked;\n            Status = \"南方ボズヤ戦線またはザトゥノル高原の中で開始してください。\";\n            return;\n        }\n\n        Running = true;\n""",
)

repl(
    """        if (!FieldState.InFieldZone)\n        {\n            State = ControllerState.Blocked;\n            Status = $\"Not in a Bozja field zone (in {BozjaZones.Name(Svc.ClientState.TerritoryType)}).\";\n            _director.Disengage();\n            _approach.Release();\n            _movement.Stop();\n            return;\n        }\n""",
    """        if (!FieldState.InFieldZone)\n        {\n            Stop(\"対応エリア外へ移動したため自動周回を停止しました。\");\n            return;\n        }\n""",
)

P.write_text(text, encoding="utf-8")
print("Automation/BozjaController.cs: field-zone boundary enforced")
