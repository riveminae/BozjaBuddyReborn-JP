from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Windows/MainWindow.cs"
text = P.read_text(encoding="utf-8-sig")

replacements = [
    (
        'ImGui.TextUnformatted(Loc.Ja ? $"レジスタンスランク {rank}" : $"Resistance Rank {rank}");',
        'ImGui.TextUnformatted($"レジスタンスランク {rank}");',
    ),
    (
        '$"rotation: {_director.Rotation.CurrentMode?.ToString() ?? "未設定"} " +',
        '$"ローテーション: {_director.Rotation.CurrentMode?.ToString() ?? "未設定"} " +',
    ),
    (
        'ImGui.TextColored(ok ? Green : Red, ok ? "OK  " : "未接続  ");',
        'ImGui.TextColored(ok ? Green : Red, ok ? "正常  " : "未接続  ");',
    ),
]

changed = 0
for old, new in replacements:
    if new in text:
        continue
    if old not in text:
        raise RuntimeError(f"MainWindow diagnostic localization anchor missing: {old!r}")
    text = text.replace(old, new, 1)
    changed += 1

P.write_text(text, encoding="utf-8")
print(f"Windows/MainWindow.cs: localized {changed} remaining diagnostic label(s)")
