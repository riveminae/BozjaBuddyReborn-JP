from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/SafeStopCoordinator.cs"
text = P.read_text(encoding="utf-8-sig")

marker = '安全停止のためマウントから降りています。'
if marker in text:
    print("Automation/SafeStopCoordinator.cs: safe-stop dismount already applied")
    raise SystemExit(0)

old = """        if (!GeneralActions.ReturnReady())
            return new SafeStopStatus(true, "必須プラグインが復帰せず、デジョンも使用できないため停止します。");

        if (!GeneralActions.CastReturn())
"""
new = """        // This is an intentional shutdown traversal, not combat/survival automation. Return
        // cannot be relied on to start while mounted, so explicitly dismount before the safe-stop
        // cast just like FieldTravelRouter does. No Lost Action or combat action is fired here.
        if (Mount.IsMounted && !Mount.EnsureDismounted())
            return new SafeStopStatus(false, "安全停止のためマウントから降りています。");

        if (!GeneralActions.ReturnReady())
            return new SafeStopStatus(true, "必須プラグインが復帰せず、デジョンも使用できないため停止します。");

        if (!GeneralActions.CastReturn())
"""
if old not in text:
    raise RuntimeError("SafeStopCoordinator ReturnReady anchor missing")
P.write_text(text.replace(old, new, 1), encoding="utf-8")
print("Automation/SafeStopCoordinator.cs: deliberate safe-stop dismount patched")
