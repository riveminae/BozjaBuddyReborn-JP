from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CTRL = ROOT / "Automation/BozjaController.cs"
MAIN = ROOT / "Windows/MainWindow.cs"


def patch(path: Path, old: str, new: str, marker: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if marker in text:
        print(f"{path.relative_to(ROOT)}: already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"{path.relative_to(ROOT)} anchor missing: {old[:160]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path.relative_to(ROOT)}: patched ({marker})")


patch(
    CTRL,
    """    public bool LifestreamAvailable => _movement.LifestreamAvailable;\n    public int RouteBlacklistCount => _selector.RouteBlacklistedFateCount;\n\n""",
    """    public bool LifestreamAvailable => _movement.LifestreamAvailable;\n    public int RouteBlacklistCount => _selector.RouteBlacklistedFateCount;\n\n    /// <summary>Last framework-tick survival inventory evaluation; UI reads this cache only.</summary>\n    public SupplyStatus SupplyStatus { get; private set; } = new(false, false, false, 0, 0, 0, 0, []);\n\n""",
    "public SupplyStatus SupplyStatus { get; private set; }",
)

# Evaluate before live-CE dispatch so the status line remains fresh while fighting, but keep the
# existing ordering where a live CE always outranks any supply recovery action.
patch(
    CTRL,
    """        // --- already registered and fighting -------------------------------\n        var current = CriticalEngagements.Current(_catalog);\n""",
    """        // Cache supply on the framework tick for both arbitration and UI. This deliberately\n        // happens before live-CE dispatch so the main window remains informative during a CE; the\n        // CE branch below still returns before any refill decision can run.\n        SupplyStatus = _supplies.Evaluate();\n\n        // --- already registered and fighting -------------------------------\n        var current = CriticalEngagements.Current(_catalog);\n""",
    "SupplyStatus = _supplies.Evaluate();",
)

patch(
    CTRL,
    """        var supply = _supplies.Evaluate();\n        if (supply.InventoryAvailable && supply.CriticalNoRecovery)\n""",
    """        var supply = SupplyStatus;\n        if (supply.InventoryAvailable && supply.CriticalNoRecovery)\n""",
    "var supply = SupplyStatus;",
)

patch(
    MAIN,
    """            var me = Svc.Objects.LocalPlayer;\n            if (me != null && me.MaxHp > 0)\n                ImGui.TextColored(Grey, $\"HP: {me.CurrentHp * 100f / me.MaxHp:F0}% / ロール: {SurvivalPolicy.CurrentRole()}\");\n        }\n\n""",
    """            var me = Svc.Objects.LocalPlayer;\n            if (me != null && me.MaxHp > 0)\n                ImGui.TextColored(Grey, $\"HP: {me.CurrentHp * 100f / me.MaxHp:F0}% / ロール: {SurvivalPolicy.CurrentRole()}\");\n\n            var supply = _controller.SupplyStatus;\n            if (supply.InventoryAvailable)\n            {\n                var supplyColour = supply.CriticalNoRecovery ? Red : supply.NeedsRoutineRefill ? Yellow : Green;\n                var supplyState = supply.CriticalNoRecovery\n                    ? \"緊急補給が必要\"\n                    : supply.NeedsRoutineRefill ? \"補給が必要\" : \"正常\";\n                ImGui.TextColored(supplyColour,\n                    $\"生存在庫: Potion Kit {supply.PotionKits} / Reraiser {supply.Reraisers} / \" +\n                    $\"主回復 {supply.MainHealUnits} / Manawall {supply.EmergencyDefenseUnits}（{supplyState}）\");\n            }\n            else\n            {\n                ImGui.TextColored(Grey, \"生存在庫: 読み取り待ち\");\n            }\n        }\n\n""",
    "生存在庫: Potion Kit",
)
