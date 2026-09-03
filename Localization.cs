using System;
using BozjaBuddyReborn.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn;

public static class Loc
{
    // JP fork requirement: visible UI is Japanese regardless of Dalamud UI language.
    public static bool Ja => true;
    public static string T(string en, string ja) => ja;

    /// <summary>
    /// Translate English operational messages only at the presentation boundary. Core drivers keep
    /// their original English reason strings so /xllog remains useful when comparing with upstream
    /// code and issue reports. Unknown strings are returned unchanged rather than guessed.
    /// </summary>
    public static string Runtime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        return text switch
        {
            "Nothing configured - tick some party actions under Settings, Lost Actions." =>
                "パーティ支援アクションが設定されていません。設定の「ロストアクション」で選択してください。",
            "Starting party support." => "パーティ支援を開始します。",
            "Party support stopped." => "パーティ支援を停止しました。",
            "Party support stopped from the panel." => "操作画面からパーティ支援を停止しました。",
            "Party support stopped - no character." => "キャラクター情報を取得できないためパーティ支援を停止しました。",
            "Waiting - the character is dead." => "戦闘不能のためパーティ支援を一時停止しています。",
            "Waiting - zoning." => "エリア移動中のためパーティ支援を一時停止しています。",
            "Party support stopped - not in a field operation." => "対応フィールド外のためパーティ支援を停止しました。",
            "Waiting - nobody in range to support." => "支援可能なパーティメンバーが範囲内にいません。",
            "Holding - everyone is covered." => "全員の支援状態が十分なため待機しています。",
            "Party support stopped - none of the chosen actions are in the holster." =>
                "選択した支援アクションがHolsterにないためパーティ支援を停止しました。",
            "nothing applied yet" => "まだ適用していません",
            "nothing to do" => "変更不要です",
            "refused: not on the framework thread" => "実行スレッドが不正なため拒否しました",
            "no character loaded" => "キャラクター情報を取得できません",
            "refused: the character is dead" => "戦闘不能のため使用できません",
            "hotbar module unavailable" => "ホットバーモジュールを利用できません",
            "no holster (not in a field operation)" => "Holsterを取得できません（対応フィールド外）",
            _ => RuntimePattern(text),
        };
    }

    private static string RuntimePattern(string text)
    {
        if (text.StartsWith("used ", StringComparison.OrdinalIgnoreCase))
            return $"{text[5..]} を使用しました";

        if (text.StartsWith("the game refused to load ", StringComparison.OrdinalIgnoreCase))
            return $"{text[25..]} のロードをゲームに拒否されました";

        if (text.StartsWith("loading ", StringComparison.OrdinalIgnoreCase))
        {
            var duty = text.IndexOf(" into duty slot ", StringComparison.OrdinalIgnoreCase);
            if (duty > 8)
                return $"{text[8..duty]} をDuty Action枠 {text[(duty + 16)..]} にロードしています";

            var target = text.IndexOf(" for ", StringComparison.OrdinalIgnoreCase);
            if (target > 8)
                return $"{text[8..target]} を {text[(target + 5)..].TrimEnd('.')} 用にロードしています";
        }

        if (text.StartsWith("waiting for duty slot ", StringComparison.OrdinalIgnoreCase))
            return "Duty Action枠へのロード完了を待っています。";

        if (text.StartsWith("duty slot ", StringComparison.OrdinalIgnoreCase)
            && text.Contains("never came up as", StringComparison.OrdinalIgnoreCase))
            return "Duty Action枠へのロードを確認できなかったため発動しませんでした。";

        if (text.StartsWith("slot ", StringComparison.OrdinalIgnoreCase) && text.EndsWith(" is empty", StringComparison.OrdinalIgnoreCase))
            return $"Duty Action {text[5..^9]} は空です";

        if (text.StartsWith("slot ", StringComparison.OrdinalIgnoreCase)
            && text.Contains(" now holds ", StringComparison.OrdinalIgnoreCase))
            return "Duty Action枠の内容が更新されているため、安全のため発動しませんでした。";

        if (text.EndsWith(" has no charges", StringComparison.OrdinalIgnoreCase))
            return $"{text[..^15]} のチャージがありません";

        var recharge = text.IndexOf(" is recharging", StringComparison.OrdinalIgnoreCase);
        if (recharge > 0)
            return $"{text[..recharge]} はリキャスト中{text[(recharge + 14)..]}";

        var already = text.IndexOf(" is already up", StringComparison.OrdinalIgnoreCase);
        if (already > 0)
            return $"{text[..already]} は既に有効{text[(already + 14)..]}";

        if (text.StartsWith("Holding - ", StringComparison.OrdinalIgnoreCase))
            return $"待機中 - {text[10..]}";

        if (text.StartsWith("Party support finished - out of actions after ", StringComparison.OrdinalIgnoreCase))
            return "支援アクションを使い切ったためパーティ支援を終了しました。";

        if (text.EndsWith(" is no longer in the party - nothing fired.", StringComparison.OrdinalIgnoreCase))
            return $"{text[..^45]} はパーティから離脱したため発動しませんでした。";

        if (text.Contains(" is no longer reachable - ", StringComparison.OrdinalIgnoreCase))
            return "対象へ到達できなくなったため、ロード済みアクションは発動しませんでした。";

        if (text.Contains(" no longer needs ", StringComparison.OrdinalIgnoreCase))
            return "対象がすでに回復・支援済みのため、ロード済みアクションは発動しませんでした。";

        if (text.StartsWith("essence: ", StringComparison.OrdinalIgnoreCase))
            return "Essence: " + RuntimePattern(text[9..]);

        if (text.StartsWith("slot 1: ", StringComparison.OrdinalIgnoreCase))
            return "Duty Action 1: " + RuntimePattern(text[8..]);
        if (text.StartsWith("slot 2: ", StringComparison.OrdinalIgnoreCase))
            return "Duty Action 2: " + RuntimePattern(text[8..]);

        if (text.EndsWith(" not in the holster", StringComparison.OrdinalIgnoreCase))
            return $"{text[..^19]} がHolsterにありません";
        if (text.EndsWith(" has no action id", StringComparison.OrdinalIgnoreCase))
            return $"{text[..^17]} のAction IDを取得できません";

        if (text.Contains(" failed - ", StringComparison.OrdinalIgnoreCase))
        {
            var split = text.IndexOf(" failed - ", StringComparison.OrdinalIgnoreCase);
            return $"{text[..split]} の実行に失敗しました（詳細は /xllog）";
        }

        return text;
    }

    public static string Controller(ControllerState s) => s switch
    {
        ControllerState.Idle => "待機", ControllerState.Blocked => "停止中",
        ControllerState.Selecting => "行き先選択中", ControllerState.Travelling => "移動中",
        ControllerState.Holding => "待機位置", ControllerState.Engaged => "戦闘中", _ => s.ToString(),
    };

    public static string Phase(SignUpPhase s) => s switch
    {
        SignUpPhase.Idle => "待機", SignUpPhase.Opening => "ボズヤファインダーを開いています",
        SignUpPhase.Registering => "参加申請中", SignUpPhase.AwaitingSelection => "抽選待ち",
        SignUpPhase.Commencing => "戦闘突入中", SignUpPhase.Done => "完了", _ => s.ToString(),
    };

    public static string CeState(DynamicEventState s) => s switch
    {
        DynamicEventState.Inactive => "終了", DynamicEventState.Register => "参加募集中",
        DynamicEventState.Warmup => "開戦準備中", DynamicEventState.Battle => "戦闘中", _ => s.ToString(),
    };
}
