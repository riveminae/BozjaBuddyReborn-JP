from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
files = {
    ROOT / "Windows/DutyActionWindow.cs": [
        (
            'ImGui.TextColored(peer.IsSelf ? Green : Grey, peer.IsSelf ? (Loc.Ja ? $"{peer.Name}（自分）" : $"{peer.Name} (you)") : peer.Name);',
            'ImGui.TextColored(peer.IsSelf ? Green : Grey, peer.IsSelf ? $"{peer.Name}（自分）" : peer.Name);',
        ),
    ],
    ROOT / "Windows/MultiboxerWindow.cs": [
        (
            'ImGui.TextColored(Grey, Loc.Ja ? $"   フェーズ: {Loc.Phase(_signUps.Phase)}" : $"   phase: {_signUps.Phase}");',
            'ImGui.TextColored(Grey, $"   フェーズ: {Loc.Phase(_signUps.Phase)}");',
        ),
        (
            'ImGui.TextColored(Grey, $"   Essence: {_catalog.Name(lo.Essence)}{EssenceNote(lo.Essence)}");',
            'ImGui.TextColored(Grey, $"   エッセンス: {_catalog.Name(lo.Essence)}{EssenceNote(lo.Essence)}");',
        ),
    ],
}

changed = 0
for path, replacements in files.items():
    text = path.read_text(encoding="utf-8-sig")
    for old, new in replacements:
        if new in text:
            continue
        if old not in text:
            raise RuntimeError(f"{path.name} dead-English anchor missing: {old!r}")
        text = text.replace(old, new, 1)
        changed += 1
    path.write_text(text, encoding="utf-8")

print(f"visible UI: removed/localized {changed} dead English branch(es)")
