from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str, marker: str | None = None) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    marker = marker or new
    if marker in text:
        print(f"{path}: runtime display localization already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:180]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: runtime display localization patched")


patch(
    "Localization.cs",
    """    public static string T(string en, string ja) => ja;\n\n    public static string Controller(ControllerState s) => s switch\n""",
    """    public static string T(string en, string ja) => ja;\n\n    /// <summary>\n    /// Translate English operational messages only at the presentation boundary. Core drivers keep\n    /// their original English reason strings so /xllog remains useful when comparing with upstream\n    /// code and issue reports. Unknown strings are returned unchanged rather than guessed.\n    /// </summary>\n    public static string Runtime(string? text)\n    {\n        if (string.IsNullOrWhiteSpace(text))\n            return text ?? string.Empty;\n\n        return text switch\n        {\n            \"Nothing configured - tick some party actions under Settings, Lost Actions.\" =>\n                \"パーティ支援アクションが設定されていません。設定の「ロストアクション」で選択してください。\",\n            \"Starting party support.\" => \"パーティ支援を開始します。\",\n            \"Party support stopped.\" => \"パーティ支援を停止しました。\",\n            \"Party support stopped from the panel.\" => \"操作画面からパーティ支援を停止しました。\",\n            \"Party support stopped - no character.\" => \"キャラクター情報を取得できないためパーティ支援を停止しました。\",\n            \"Waiting - the character is dead.\" => \"戦闘不能のためパーティ支援を一時停止しています。\",\n            \"Waiting - zoning.\" => \"エリア移動中のためパーティ支援を一時停止しています。\",\n            \"Party support stopped - not in a field operation.\" => \"対応フィールド外のためパーティ支援を停止しました。\",\n            \"Waiting - nobody in range to support.\" => \"支援可能なパーティメンバーが範囲内にいません。\",\n            \"Holding - everyone is covered.\" => \"全員の支援状態が十分なため待機しています。\",\n            \"Party support stopped - none of the chosen actions are in the holster.\" =>\n                \"選択した支援アクションがHolsterにないためパーティ支援を停止しました。\",\n            \"nothing applied yet\" => \"まだ適用していません\",\n            \"nothing to do\" => \"変更不要です\",\n            \"refused: not on the framework thread\" => \"実行スレッドが不正なため拒否しました\",\n            \"no character loaded\" => \"キャラクター情報を取得できません\",\n            \"refused: the character is dead\" => \"戦闘不能のため使用できません\",\n            \"hotbar module unavailable\" => \"ホットバーモジュールを利用できません\",\n            \"no holster (not in a field operation)\" => \"Holsterを取得できません（対応フィールド外）\",\n            _ => RuntimePattern(text),\n        };\n    }\n\n    private static string RuntimePattern(string text)\n    {\n        if (text.StartsWith(\"used \", StringComparison.OrdinalIgnoreCase))\n            return $\"{text[5..]} を使用しました\";\n\n        if (text.StartsWith(\"the game refused to load \", StringComparison.OrdinalIgnoreCase))\n            return $\"{text[25..]} のロードをゲームに拒否されました\";\n\n        if (text.StartsWith(\"loading \", StringComparison.OrdinalIgnoreCase))\n        {\n            var duty = text.IndexOf(\" into duty slot \", StringComparison.OrdinalIgnoreCase);\n            if (duty > 8)\n                return $\"{text[8..duty]} をDuty Action枠 {text[(duty + 16)..]} にロードしています\";\n\n            var target = text.IndexOf(\" for \", StringComparison.OrdinalIgnoreCase);\n            if (target > 8)\n                return $\"{text[8..target]} を {text[(target + 5)..].TrimEnd('.')} 用にロードしています\";\n        }\n\n        if (text.StartsWith(\"waiting for duty slot \", StringComparison.OrdinalIgnoreCase))\n            return \"Duty Action枠へのロード完了を待っています。\";\n\n        if (text.StartsWith(\"duty slot \", StringComparison.OrdinalIgnoreCase)\n            && text.Contains(\"never came up as\", StringComparison.OrdinalIgnoreCase))\n            return \"Duty Action枠へのロードを確認できなかったため発動しませんでした。\";\n\n        if (text.StartsWith(\"slot \", StringComparison.OrdinalIgnoreCase) && text.EndsWith(\" is empty\", StringComparison.OrdinalIgnoreCase))\n            return $\"Duty Action {text[5..^9]} は空です\";\n\n        if (text.StartsWith(\"slot \", StringComparison.OrdinalIgnoreCase)\n            && text.Contains(\" now holds \", StringComparison.OrdinalIgnoreCase))\n            return \"Duty Action枠の内容が更新されているため、安全のため発動しませんでした。\";\n\n        if (text.EndsWith(\" has no charges\", StringComparison.OrdinalIgnoreCase))\n            return $\"{text[..^15]} のチャージがありません\";\n\n        var recharge = text.IndexOf(\" is recharging\", StringComparison.OrdinalIgnoreCase);\n        if (recharge > 0)\n            return $\"{text[..recharge]} はリキャスト中{text[(recharge + 14)..]}\";\n\n        var already = text.IndexOf(\" is already up\", StringComparison.OrdinalIgnoreCase);\n        if (already > 0)\n            return $\"{text[..already]} は既に有効{text[(already + 14)..]}\";\n\n        if (text.StartsWith(\"Holding - \", StringComparison.OrdinalIgnoreCase))\n            return $\"待機中 - {text[10..]}\";\n\n        if (text.StartsWith(\"Party support finished - out of actions after \", StringComparison.OrdinalIgnoreCase))\n            return \"支援アクションを使い切ったためパーティ支援を終了しました。\";\n\n        if (text.EndsWith(\" is no longer in the party - nothing fired.\", StringComparison.OrdinalIgnoreCase))\n            return $\"{text[..^45]} はパーティから離脱したため発動しませんでした。\";\n\n        if (text.Contains(\" is no longer reachable - \", StringComparison.OrdinalIgnoreCase))\n            return \"対象へ到達できなくなったため、ロード済みアクションは発動しませんでした。\";\n\n        if (text.Contains(\" no longer needs \", StringComparison.OrdinalIgnoreCase))\n            return \"対象がすでに回復・支援済みのため、ロード済みアクションは発動しませんでした。\";\n\n        if (text.StartsWith(\"essence: \", StringComparison.OrdinalIgnoreCase))\n            return \"Essence: \" + RuntimePattern(text[9..]);\n\n        if (text.StartsWith(\"slot 1: \", StringComparison.OrdinalIgnoreCase))\n            return \"Duty Action 1: \" + RuntimePattern(text[8..]);\n        if (text.StartsWith(\"slot 2: \", StringComparison.OrdinalIgnoreCase))\n            return \"Duty Action 2: \" + RuntimePattern(text[8..]);\n\n        if (text.EndsWith(\" not in the holster\", StringComparison.OrdinalIgnoreCase))\n            return $\"{text[..^19]} がHolsterにありません\";\n        if (text.EndsWith(\" has no action id\", StringComparison.OrdinalIgnoreCase))\n            return $\"{text[..^17]} のAction IDを取得できません\";\n\n        if (text.Contains(\" failed - \", StringComparison.OrdinalIgnoreCase))\n        {\n            var split = text.IndexOf(\" failed - \", StringComparison.OrdinalIgnoreCase);\n            return $\"{text[..split]} の実行に失敗しました（詳細は /xllog）\";\n        }\n\n        return text;\n    }\n\n    public static string Controller(ControllerState s) => s switch\n""",
    "public static string Runtime(string? text)",
)

# Presentation boundaries only. The producer strings and English log calls stay untouched.
patch(
    "Windows/MainWindow.cs",
    """        ImGui.TextWrapped(_controller.Status);\n""",
    """        ImGui.TextWrapped(Loc.Runtime(_controller.Status));\n""",
    "ImGui.TextWrapped(Loc.Runtime(_controller.Status));",
)
patch(
    "Windows/MainWindow.cs",
    """            ImGui.TextColored(Grey, $\"ロストアクション: {_controller.LastLostAction}\");\n""",
    """            ImGui.TextColored(Grey, $\"ロストアクション: {Loc.Runtime(_controller.LastLostAction)}\");\n""",
    "Loc.Runtime(_controller.LastLostAction)",
)
patch(
    "Windows/MainWindow.cs",
    """            task.Status.Length > 0 ? task.Status : \"パーティ支援は待機中です。\");\n""",
    """            task.Status.Length > 0 ? Loc.Runtime(task.Status) : \"パーティ支援は待機中です。\");\n""",
    "Loc.Runtime(task.Status)",
)

patch(
    "Windows/DutyActionWindow.cs",
    """        ImGui.TextColored(Grey, message);\n""",
    """        ImGui.TextColored(Grey, Loc.Runtime(message));\n""",
    "ImGui.TextColored(Grey, Loc.Runtime(message));",
)

patch(
    "Windows/MultiboxerWindow.cs",
    """            if (_signUps.Status.Length > 0)\n                ImGui.TextColored(_signUps.Active ? Yellow : Grey, $\"   {_signUps.Status}\");\n""",
    """            if (_signUps.Status.Length > 0)\n                ImGui.TextColored(_signUps.Active ? Yellow : Grey, $\"   {Loc.Runtime(_signUps.Status)}\");\n""",
    "Loc.Runtime(_signUps.Status)",
)
patch(
    "Windows/MultiboxerWindow.cs",
    """                    ImGui.TextColored(Yellow, $\"   Errand: {_errands.Status}\");\n""",
    """                    ImGui.TextColored(Yellow, $\"   移動指示: {Loc.Runtime(_errands.Status)}\");\n""",
    "Loc.Runtime(_errands.Status)",
)
patch(
    "Windows/MultiboxerWindow.cs",
    """                    ImGui.TextColored(support.Active ? Green : Grey, $\"   Party support: {support.Status}\");\n""",
    """                    ImGui.TextColored(support.Active ? Green : Grey, $\"   パーティ支援: {Loc.Runtime(support.Status)}\");\n""",
    "Loc.Runtime(support.Status)",
)
patch(
    "Windows/MultiboxerWindow.cs",
    """            ImGui.TextColored(Grey, $\"Last instruction here: {_controller.LastCommandResult}\");\n""",
    """            ImGui.TextColored(Grey, $\"直近の操作結果: {Loc.Runtime(_controller.LastCommandResult)}\");\n""",
    "Loc.Runtime(_controller.LastCommandResult)",
)

patch(
    "Automation/BozjaController.cs",
    """                    LastCommandResult = $\"ロストアクション構成: {_loadouts.LastResult}\";\n""",
    """                    LastCommandResult = $\"ロストアクション構成: {Loc.Runtime(_loadouts.LastResult)}\";\n""",
    "Loc.Runtime(_loadouts.LastResult)",
)
