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
        raise RuntimeError(f"MainWindow.cs anchor missing: {old[:100]!r}")
    text = text.replace(old, new, 1)

repl(
    "using System.Numerics;\n",
    "using System.Numerics;\nusing System.Reflection;\n",
)
repl(
    """    private readonly CeCatalog _catalog;\n\n""",
    """    private readonly CeCatalog _catalog;\n    private static readonly Version AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);\n    private static bool IsTestBuild => AssemblyVersion.Major == 1 && AssemblyVersion.Minor == 0 && AssemblyVersion.Build == 90;\n\n""",
)
repl(
    """    private void DrawControls()\n    {\n        var running = _controller.Running;\n\n""",
    """    private void DrawControls()\n    {\n        var running = _controller.Running;\n\n        if (IsTestBuild)\n        {\n            ImGui.TextColored(Yellow, $\"テスト版 v{AssemblyVersion} を使用中です。\");\n            if (ImGui.IsItemHovered())\n                ImGui.SetTooltip(\"不具合時は Dalamud のカスタムプラグインリポジトリから Test repo を無効化/削除し、Stable repo を有効化して Bozja Buddy Reborn JP を更新または再インストールしてください。\");\n        }\n\n""",
)

P.write_text(text, encoding="utf-8")
print("Windows/MainWindow.cs: test-build stable fallback guidance added")
