from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/SignUpRunner.cs"
text = P.read_text(encoding="utf-8-sig")

marker = "_clickSettleUntilMs = Environment.TickCount64 + ClickSettleMs;"
if marker in text and "private unsafe bool Click(" in text:
    print("Automation/SignUpRunner.cs: click settle interlock already applied")
else:
    old_sig = "private static unsafe bool Click(AtkUnitBase* addon, LabelledButton button, string semantic)"
    new_sig = "private unsafe bool Click(AtkUnitBase* addon, LabelledButton button, string semantic)"
    if old_sig not in text:
        raise RuntimeError("SignUpRunner Click signature anchor missing")
    text = text.replace(old_sig, new_sig, 1)

    old_fire = """                addon->ReceiveEvent(evt.State.EventType, (int)evt.Param, evt.AtkEvent, &data);\n                Svc.Log.Information($\"[BozjaBuddyReborn] Sign-up: clicked {semantic} button \\\"{button.Text}\\\".\");\n                return true;\n"""
    new_fire = """                addon->ReceiveEvent(evt.State.EventType, (int)evt.Param, evt.AtkEvent, &data);\n\n                // Safety interlock: this is one button whose label changes Register -> Withdraw ->\n                // Commence. The UI lags the click by a frame or two, so clicking again before it\n                // settles can hit the newly changed Withdraw action and cancel our registration.\n                // _clicks also arms AnswerConfirmation(), which must never accept a Yes/No before\n                // this runner has actually caused a recruitment-window click.\n                _clicks++;\n                _clickSettleUntilMs = Environment.TickCount64 + ClickSettleMs;\n\n                Svc.Log.Information($\"[BozjaBuddyReborn] Sign-up: clicked {semantic} button \\\"{button.Text}\\\".\");\n                return true;\n"""
    if old_fire not in text:
        raise RuntimeError("SignUpRunner ReceiveEvent anchor missing")
    text = text.replace(old_fire, new_fire, 1)
    P.write_text(text, encoding="utf-8")
    print("Automation/SignUpRunner.cs: click settle interlock restored")
