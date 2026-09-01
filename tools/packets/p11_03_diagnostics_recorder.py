from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str, marker: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    if marker in text:
        print(f"{path}: diagnostics recorder already wired")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: diagnostics recorder wired")


patch(
    "Plugin.cs",
    """        try\n        {\n            _controller.Tick();\n        }\n        catch (Exception ex)\n        {\n            Svc.Log.Error(ex, \"[BozjaBuddyReborn] Controller tick failed; stopping for safety.\");\n            _controller.Stop(\"Stopped after an internal error - see /xllog.\");\n        }\n""",
    """        try\n        {\n            _controller.Tick();\n            DiagnosticsRecorder.Observe(_controller.State, _controller.Status);\n        }\n        catch (Exception ex)\n        {\n            Svc.Log.Error(ex, \"[BozjaBuddyReborn] Controller tick failed; stopping for safety.\");\n            _controller.Stop(\"内部エラーのため停止しました。/xllog を確認してください。\");\n            DiagnosticsRecorder.Warning(\"内部エラーのためコントローラーを停止しました。\");\n            DiagnosticsRecorder.Observe(_controller.State, _controller.Status);\n        }\n""",
    "DiagnosticsRecorder.Observe(_controller.State, _controller.Status);",
)

patch(
    "Windows/MainWindow.cs",
    """        if (_config.AutoUseLostActions && _controller.LastLostAction.Length > 0)\n            ImGui.TextColored(Grey, $\"ロストアクション: {_controller.LastLostAction}\");\n\n        if (ImGui.SmallButton(\"診断情報をコピー\"))\n""",
    """        if (_config.AutoUseLostActions && _controller.LastLostAction.Length > 0)\n            ImGui.TextColored(Grey, $\"ロストアクション: {_controller.LastLostAction}\");\n\n        var latestWarning = DiagnosticsRecorder.LatestWarning;\n        if (!string.IsNullOrWhiteSpace(latestWarning))\n            ImGui.TextColored(Yellow, $\"直近の警告: {latestWarning}\");\n\n        if (ImGui.SmallButton(\"診断情報をコピー\"))\n""",
    "var latestWarning = DiagnosticsRecorder.LatestWarning;",
)

patch(
    "Windows/MainWindow.cs",
    """        sb.AppendLine($\"ceCount={_controller.Engagements.Count}\");\n        foreach (var ce in _controller.Engagements)\n            sb.AppendLine($\"ce={ce.EventId},state={ce.State},left={ce.SecondsLeft},progress={ce.Progress}\");\n\n        // Intentionally excluded: character name, world, chat, party member names and any free-form user text.\n""",
    """        sb.AppendLine($\"ceCount={_controller.Engagements.Count}\");\n        foreach (var ce in _controller.Engagements)\n            sb.AppendLine($\"ce={ce.EventId},state={ce.State},left={ce.SecondsLeft},progress={ce.Progress}\");\n\n        sb.AppendLine(\"stateTransitions:\");\n        foreach (var entry in DiagnosticsRecorder.StateTransitions)\n            sb.AppendLine($\"  {entry.Timestamp:O} state={entry.State} status={entry.Message}\");\n\n        sb.AppendLine(\"warnings:\");\n        foreach (var entry in DiagnosticsRecorder.WarningHistory)\n            sb.AppendLine($\"  {entry.Timestamp:O} state={entry.State} warning={entry.Message}\");\n\n        // Intentionally excluded: character name, world, chat, party member names and any free-form user text.\n""",
    "sb.AppendLine(\"stateTransitions:\");",
)
