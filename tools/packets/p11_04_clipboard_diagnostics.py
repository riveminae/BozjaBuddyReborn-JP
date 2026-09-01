from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Windows/MainWindow.cs"
text = P.read_text(encoding="utf-8-sig")


def repl(old: str, new: str) -> None:
    global text
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"MainWindow.cs anchor missing: {old[:120]!r}")
    text = text.replace(old, new, 1)

repl("using System.Reflection;\n", "using System.Reflection;\nusing System.Text;\n")
repl(
    """        DrawPartySupport();\n        DrawZonePicker();\n    }\n""",
    """        if (ImGui.SmallButton(\"診断情報をコピー\"))\n            ImGui.SetClipboardText(BuildDiagnostics());\n        if (ImGui.IsItemHovered())\n            ImGui.SetTooltip(\"現在の状態・依存関係・経路・CE状態を個人情報なしでコピーします。\");\n\n        DrawPartySupport();\n        DrawZonePicker();\n    }\n""",
)
repl(
    """    private static string FormatSeconds(uint seconds)\n""",
    """    private string BuildDiagnostics()\n    {\n        var sb = new StringBuilder();\n        sb.AppendLine(\"BozjaBuddyReborn-JP diagnostics\");\n        sb.AppendLine($\"version={AssemblyVersion}\");\n        sb.AppendLine($\"territory={Svc.ClientState.TerritoryType}\");\n        sb.AppendLine($\"running={_controller.Running}\");\n        sb.AppendLine($\"state={_controller.State}\");\n        sb.AppendLine($\"status={_controller.Status}\");\n        sb.AppendLine($\"routeMode={_controller.TravelMode}\");\n        sb.AppendLine($\"route={_controller.TravelRoute}\");\n        sb.AppendLine($\"vnavmesh={_navmesh.Available}\");\n        sb.AppendLine($\"lifestream={_controller.LifestreamAvailable}\");\n        sb.AppendLine($\"rotationSolver={_director.RotationAvailable}\");\n        sb.AppendLine($\"bossMod={_director.AvoidanceAvailable}\");\n        sb.AppendLine($\"bossModFork={_director.Avoidance.Fork}\");\n\n        var me = Svc.Objects.LocalPlayer;\n        if (me != null && me.MaxHp > 0)\n        {\n            sb.AppendLine($\"hpPercent={me.CurrentHp * 100f / me.MaxHp:F1}\");\n            sb.AppendLine($\"role={SurvivalPolicy.CurrentRole()}\");\n        }\n\n        sb.AppendLine($\"ceCount={_controller.Engagements.Count}\");\n        foreach (var ce in _controller.Engagements)\n            sb.AppendLine($\"ce={ce.EventId},state={ce.State},left={ce.SecondsLeft},progress={ce.Progress}\");\n\n        // Intentionally excluded: character name, world, chat, party member names and any free-form user text.\n        return sb.ToString();\n    }\n\n    private static string FormatSeconds(uint seconds)\n""",
)

P.write_text(text, encoding="utf-8")
print("Windows/MainWindow.cs: privacy-safe clipboard diagnostics added")
