from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Windows/ConfigWindow.cs"
text = P.read_text(encoding="utf-8-sig")

MARKER = 'ImGui.BeginTabBar("##bbr_lostaction_tabs")'
if MARKER in text:
    print("Windows/ConfigWindow.cs: Lost Action subtabs already applied")
    raise SystemExit(0)


def method_span(source: str, signature: str) -> tuple[int, int]:
    start = source.find(signature)
    if start < 0:
        raise RuntimeError(f"ConfigWindow.cs method missing: {signature}")
    brace = source.find("{", start)
    if brace < 0:
        raise RuntimeError(f"ConfigWindow.cs opening brace missing: {signature}")
    depth = 0
    i = brace
    in_string = False
    while i < len(source):
        ch = source[i]
        if in_string:
            if ch == "\\":
                i += 2
                continue
            if ch == '"':
                in_string = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return start, i + 1
        i += 1
    raise RuntimeError(f"ConfigWindow.cs closing brace missing: {signature}")


start, end = method_span(text, "    private void DrawLostActions()")
replacement = r'''    private void DrawLostActions()
    {
        // These are independent features. In particular, party support must stay configurable
        // even when ordinary combat auto-use is disabled; the old single long page returned early
        // on AutoUseLostActions=false and accidentally hid the party-support controls as collateral.
        if (!ImGui.BeginTabBar("##bbr_lostaction_tabs"))
            return;

        if (ImGui.BeginTabItem("Duty Actionバー"))
        {
            DrawDutyActionBarSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("自動使用"))
        {
            DrawLostActionAutoUseSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("パーティ支援"))
        {
            DrawPartySupport();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawDutyActionBarSettings()
    {
        var click = _config.DutyActionClickToUse;
        if (ImGui.Checkbox("Duty Actionバーのクリックでアクションを使用する", ref click))
        {
            _config.DutyActionClickToUse = click;
            Save();
        }
        ImGui.TextColored(Grey,
            "初期値はONです。自分の2枠は常に操作でき、他クライアントの枠は\n" +
            "ホストからのみ操作できます。OFFにすると\n" +
            "Duty Actionウィンドウは表示専用になります。");

        ImGui.Spacing();

        var clear = _config.DutyActionTransparent;
        if (ImGui.Checkbox("Duty Actionウィンドウの背景を透明にする", ref clear))
        {
            _config.DutyActionTransparent = clear;
            Save();
        }
        ImGui.TextColored(Grey,
            "初期値はONです。ゲーム画面上のオーバーレイとして表示します。\n" +
            "タイトルバーは残るため、背景を透明にしても\n" +
            "そこをドラッグして移動できます。");
    }

    private void DrawLostActionAutoUseSettings()
    {
        var auto = _config.AutoUseLostActions;
        if (ImGui.Checkbox(Loc.T("Automatically use Lost Actions in combat", "戦闘中にロストアクションを自動使用する"), ref auto))
        {
            _config.AutoUseLostActions = auto;
            Save();
        }
        ImGui.TextColored(Grey,
            "通常の自動使用設定です。ロストアクションは有限資源なので、\n" +
            "下記の優先順と使用間隔に従って1つずつ使用します。\n" +
            "既に同じバフが有効なものはスキップし、次候補へ進みます。\n" +
            "Essenceを無駄に上書きしません。");

        if (!auto)
        {
            ImGui.TextColored(Grey, "自動使用はOFFです。パーティ支援とDuty Actionバー設定には影響しません。");
            return;
        }

        ImGui.Spacing();

        var fire = _config.AutoFireLostActions;
        if (ImGui.Checkbox("…Duty Actionも実際に発動してチャージを消費する", ref fire))
        {
            _config.AutoFireLostActions = fire;
            Save();
        }
        ImGui.TextColored(Yellow,
            "初期値はOFFです。有効にする前に以下を確認してください。\n" +
            "\n" +
            "Holsterには消費方法が異なる2種類があります。アイテム型（Essence、各種Potion/Ether/\n" +
            "Medi Kit、Dynamis Dice、Reraiser、Lodestone、Light Curtain、Resistance Elixir等）は、\n" +
            "Holsterから使用した時点でそのまま消費されます。アクション型（Lost Cure、\n" +
            "Lost Font of Power、各種Banner等）は、HolsterからDuty Action枠へロードしただけでは\n" +
            "チャージを消費しません。\n" +
            "\n" +
            "このスイッチをONにすると、下で選択したアクション型もDuty Actionとして実際に発動し、\n" +
            "戦闘中は設定した使用間隔ごとにチャージを消費します。OFFのままなら従来どおり、\n" +
            "アイテム型だけを使用し、アクション型は発動しません。\n" +
            "\n" +
            "ロードが必要な場合はDuty Action 1を使用するため、そこに置いていたロードアウトが\n" +
            "置き換わることがあります。既にどちらかの枠にあるアクションはその枠から発動します。");

        ImGui.Spacing();

        var cooldown = _config.LostActionCooldownMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("最低使用間隔 (ms)", ref cooldown, 2000, 60000))
        {
            _config.LostActionCooldownMs = cooldown;
            Save();
        }

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "自動使用を許可する項目を選択します。「アイテム」はHolsterから直接消費される種類です。\n" +
            "Duty Action発動設定とは独立して消費されます。");

        if (!ImGui.BeginChild("##bbr_lostactions", new Vector2(0, 300), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var entry in _lostActions.All)
        {
            var selected = _config.AutoLostActions.Contains(entry.RowId);
            var kind = entry.IsItem ? ", アイテム" : string.Empty;
            var label = $"{entry.Name}  (重量 {entry.Weight}{kind})##la{entry.RowId}";
            if (ImGui.Checkbox(label, ref selected))
            {
                if (selected)
                    _config.AutoLostActions.Add(entry.RowId);
                else
                    _config.AutoLostActions.Remove(entry.RowId);
                Save();
            }

            if (selected && !entry.IsItem && !fire)
            {
                ImGui.SameLine();
                ImGui.TextColored(Yellow, "（発動しません）");
            }
        }

        ImGui.EndChild();
    }'''

P.write_text(text[:start] + replacement + text[end:], encoding="utf-8")
print("Windows/ConfigWindow.cs: Lost Action settings split into independent subtabs")
