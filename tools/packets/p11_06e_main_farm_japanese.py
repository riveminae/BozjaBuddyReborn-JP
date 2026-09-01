from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Windows/MainWindow.cs"
text = P.read_text(encoding="utf-8-sig")

replacements = {
    "（RelicのFarm対象から自動設定）": "（Relicの周回対象から自動設定）",
    "現在のFarm対象: ": "現在の周回対象: ",
    "Farm対象は ": "周回対象は ",
}
changed = 0
for old, new in replacements.items():
    if old in text:
        text = text.replace(old, new)
        changed += 1

P.write_text(text, encoding="utf-8")
print(f"Windows/MainWindow.cs: normalized {changed} farm wording group(s)")
