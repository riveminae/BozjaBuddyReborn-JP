from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CFG = ROOT / "Windows/ConfigWindow.cs"
PLUGIN = ROOT / "Plugin.cs"


def patch(path: Path, old: str, new: str, marker: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if marker in text:
        print(f"{path.relative_to(ROOT)}: already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"{path.relative_to(ROOT)} anchor missing: {old[:180]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path.relative_to(ROOT)}: patched ({marker})")


patch(
    CFG,
    """    private readonly RegionResolver _regions;\n    private readonly AggroAvoidance _avoidance;\n\n    public ConfigWindow(\n        Configuration config,\n        LostActionCatalog lostActions,\n        RegionResolver regions,\n        AggroAvoidance avoidance)\n""",
    """    private readonly RegionResolver _regions;\n    private readonly AggroAvoidance _avoidance;\n    private readonly SupplyManager _supplies;\n\n    public ConfigWindow(\n        Configuration config,\n        LostActionCatalog lostActions,\n        RegionResolver regions,\n        AggroAvoidance avoidance,\n        SupplyManager supplies)\n""",
    "private readonly SupplyManager _supplies;",
)

patch(
    CFG,
    """        _lostActions = lostActions;\n        _regions = regions;\n        _avoidance = avoidance;\n        SizeConstraints = new WindowSizeConstraints\n""",
    """        _lostActions = lostActions;\n        _regions = regions;\n        _avoidance = avoidance;\n        _supplies = supplies;\n        SizeConstraints = new WindowSizeConstraints\n""",
    "_supplies = supplies;",
)

patch(
    CFG,
    """        DrawRole(\"タンク\", ref _config.TankSurvivalHealFraction, ref _config.TankSurvivalEmergencyFraction);\n        DrawRole(\"ヒーラー\", ref _config.HealerSurvivalHealFraction, ref _config.HealerSurvivalEmergencyFraction);\n        DrawRole(\"DPS\", ref _config.DpsSurvivalHealFraction, ref _config.DpsSurvivalEmergencyFraction);\n\n        return;\n\n        void DrawRole(string role, ref float heal, ref float emergency)\n""",
    """        DrawRole(\"タンク\", ref _config.TankSurvivalHealFraction, ref _config.TankSurvivalEmergencyFraction);\n        DrawRole(\"ヒーラー\", ref _config.HealerSurvivalHealFraction, ref _config.HealerSurvivalEmergencyFraction);\n        DrawRole(\"DPS\", ref _config.DpsSurvivalHealFraction, ref _config.DpsSurvivalEmergencyFraction);\n\n        ImGui.Separator();\n        ImGui.TextUnformatted(\"生存補給\");\n        ImGui.TextColored(Grey,\n            \"各項目が「補給開始」未満になると補給を予約し、Cacheから「目標数」まで戻す想定です。\\n\" +\n            \"現在はCache↔Holsterの安全な転送手段が未確定のため、移動・在庫判定までを自動化しています。\");\n\n        DrawSupply(\"Potion Kit\", ref _config.SupplyPotionKitLow, ref _config.SupplyPotionKitTarget);\n        DrawSupply(\"Reraiser\", ref _config.SupplyReraiserLow, ref _config.SupplyReraiserTarget);\n        DrawSupply(\"主回復\", ref _config.SupplyMainHealLow, ref _config.SupplyMainHealTarget);\n        DrawSupply(\"Manawall\", ref _config.SupplyEmergencyDefenseLow, ref _config.SupplyEmergencyDefenseTarget);\n\n        ImGui.Spacing();\n        if (_supplies.CacheUnavailableForInstanceCount > 0)\n            ImGui.TextColored(Yellow,\n                $\"このインスタンスでCache在庫なしとして記録中: {_supplies.CacheUnavailableForInstanceCount}項目\");\n        else\n            ImGui.TextColored(Grey, \"このインスタンスでCache在庫なしの記録はありません。\");\n\n        if (ImGui.SmallButton(\"Cache在庫なし記録をクリアして再確認\"))\n            _supplies.ResetInstanceCacheAvailability();\n        if (ImGui.IsItemHovered())\n            ImGui.SetTooltip(\"手動でCacheへ補充した後など、同じフィールドインスタンス内でも在庫を再確認したい場合に使用します。\");\n\n        return;\n\n        void DrawSupply(string label, ref int low, ref int target)\n        {\n            ImGui.PushID(label);\n            ImGui.SetNextItemWidth(180);\n            var nextLow = low;\n            if (ImGui.SliderInt(\"補給開始##low\", ref nextLow, 0, 99))\n            {\n                low = Math.Clamp(nextLow, 0, 99);\n                target = Math.Max(target, low);\n                Save();\n            }\n            ImGui.SameLine();\n            ImGui.TextUnformatted(label);\n\n            ImGui.SetNextItemWidth(180);\n            var nextTarget = target;\n            if (ImGui.SliderInt(\"目標数##target\", ref nextTarget, 0, 99))\n            {\n                target = Math.Clamp(nextTarget, low, 99);\n                Save();\n            }\n            ImGui.PopID();\n        }\n\n        void DrawRole(string role, ref float heal, ref float emergency)\n""",
    "Cache在庫なし記録をクリアして再確認",
)

patch(
    PLUGIN,
    """        _configWindow = new ConfigWindow(_config, _lostActions, _regions, _aggroAvoidance)\n""",
    """        _configWindow = new ConfigWindow(_config, _lostActions, _regions, _aggroAvoidance, _supplies)\n""",
    "new ConfigWindow(_config, _lostActions, _regions, _aggroAvoidance, _supplies)",
)
