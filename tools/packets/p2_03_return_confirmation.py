from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/FieldTravelRouter.cs"
text = P.read_text(encoding="utf-8-sig")

def repl(old: str, new: str) -> None:
    global text
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"FieldTravelRouter.cs anchor missing: {old[:100]!r}")
    text = text.replace(old, new, 1)

repl(
    """    private int _teleportAttempts;\n    private long _returnStartedMs;\n    private bool _fallbackForGoal;\n""",
    """    private int _teleportAttempts;\n    private long _returnStartedMs;\n    private bool _returnConfirmationSent;\n    private bool _fallbackForGoal;\n""",
)
# Both Reset() and Plan() contain this exact reset sequence; replace all occurrences safely.
old_reset = """        _returnStartedMs = 0;\n        _fallbackForGoal = false;\n"""
new_reset = """        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n        _fallbackForGoal = false;\n"""
if new_reset not in text:
    if old_reset not in text:
        raise RuntimeError("return reset anchor missing")
    text = text.replace(old_reset, new_reset)

repl(
    """                if (_returnStartedMs != 0)\n                {\n                    if (now - _returnStartedMs > ReturnTimeoutMs)\n                    {\n""",
    """                if (_returnStartedMs != 0)\n                {\n                    // BOCCHI treats Return as cast + owned SelectYesno confirmation. Confirm only\n                    // while this router has a live Return pending flag; GeneralActions never clicks\n                    // a generic dialog on its own.\n                    if (!_returnConfirmationSent && GeneralActions.TryConfirmPendingReturn())\n                    {\n                        _returnConfirmationSent = true;\n                        Svc.Log.Information(\"[BozjaBuddyReborn] Confirmed pending Return traversal dialog.\");\n                    }\n\n                    if (now - _returnStartedMs > ReturnTimeoutMs)\n                    {\n""",
)
repl(
    """                _returnStartedMs = now;\n                Svc.Log.Information(\"[BozjaBuddyReborn] BOCCHI-style Return traversal started.\");\n""",
    """                _returnStartedMs = now;\n                _returnConfirmationSent = false;\n                Svc.Log.Information(\"[BozjaBuddyReborn] BOCCHI-style Return traversal started.\");\n""",
)

P.write_text(text, encoding="utf-8")
print("Automation/FieldTravelRouter.cs: Return confirmation wired")
