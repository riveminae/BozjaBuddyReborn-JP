using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

using BozjaBuddyReborn;

namespace BozjaBuddyReborn.Windows;

/// <summary>
/// The operator's console: every box in the group on one surface, with the actions you would
/// otherwise have to focus each game window to perform.
///
/// The design rule is that nothing here needs the target box to be running the orchestrator.
/// Applying a loadout or sending a box to the cache is exactly the sort of thing you want to do
/// to a box that is parked, and having to start a farm run first would defeat the purpose.
///
/// Instructions are one-shot and addressed (see <see cref="BoxCommand"/>) - they are deliberately
/// NOT shared state, so nothing here is replayed to a box that reconnects later.
/// </summary>
public sealed class MultiboxerWindow : Window
{
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);

    private readonly Configuration _config;
    private readonly MultiboxLink _link;
    private readonly DutyActionSync _sync;
    private readonly BozjaController _controller;
    private readonly LostActionCatalog _catalog;
    private readonly ErrandRunner _errands;
    private readonly SignUpRunner _signUps;

    private int _editing = -1;
    private string _newName = string.Empty;

    /// <summary>
    /// Search text for the three loadout pickers.
    ///
    /// Plain fields rather than a dictionary keyed by picker, because only one loadout's editor is
    /// open at a time - _editing is a single index - so there are only ever these three live. They
    /// are cleared whenever the editor changes, so opening a different loadout does not inherit
    /// the last one's search.
    /// </summary>
    private string _findSlot0 = string.Empty;
    private string _findSlot1 = string.Empty;
    private string _findEssence = string.Empty;

    public MultiboxerWindow(
        Configuration config,
        MultiboxLink link,
        DutyActionSync sync,
        BozjaController controller,
        LostActionCatalog catalog,
        ErrandRunner errands,
        SignUpRunner signUps)
        : base("ボズヤ マルチボックス###BozjaBuddyRebornMultiboxer")
    {
        _config = config;
        _link = link;
        _sync = sync;
        _controller = controller;
        _catalog = catalog;
        _errands = errands;
        _signUps = signUps;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 300),
            MaximumSize = new Vector2(1100, 1000),
        };
    }

    private void Save() => ConfigSaver.Save(_config);

    /// <summary>Only the host can address the group; a client can still drive itself.</summary>
    private bool CanCommand => _config.MultiboxEnabled && _config.MultiboxIsHost;

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        if (ImGui.BeginTabBar("###bbrmboxtabs"))
        {
            if (ImGui.BeginTabItem(Loc.T("Boxes", "クライアント")))
            {
                DrawBoxes();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Loc.T("Loadouts", "ロードアウト")))
            {
                DrawLoadouts();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Loc.T("Errands", "移動・操作")))
            {
                DrawErrands();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        if (!_config.MultiboxEnabled)
        {
            ImGui.TextColored(Yellow, "マルチボックスはOFFです。この画面では現在のクライアントだけを操作します。");
            ImGui.TextColored(Grey, "グループ全体を操作する場合はメイン画面でマルチボックスを有効にしてください。");
            return;
        }

        if (!_config.MultiboxIsHost)
        {
            ImGui.TextColored(Yellow, "このクライアントは子機です。");
            ImGui.TextColored(Grey,
                "グループ全体へ指示できるのはホストだけです。操作する1クライアントだけで\n" +
                "「このクライアントをホストにする」をONにしてください。この画面の自機操作は引き続き使えます。");
            return;
        }

        var peers = _link.PeerCount;
        ImGui.TextColored(peers > 0 ? Green : Grey,
            peers > 0 ? $"ホスト - {peers}クライアント接続中" : "ホスト - まだ他クライアントは接続していません");
    }

    // -------------------------------------------------------------------- boxes

    private void DrawBoxes()
    {
        if (CanCommand)
        {
            ImGui.TextColored(Grey, "以下の操作を全クライアントへ一括送信します:");

            // The one the operator reaches for under time pressure - a registration window is
            // short, so it gets its own row and the widest button.
            if (ImGui.Button(Loc.T("Sign up ALL for the engagement", "CEに全クライアントで参加申請"), new Vector2(280, 0)))
                _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.SignUp, ""));
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Sign up this box", "このクライアントだけ参加申請")))
                _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.SignUp, ""));

            if (_signUps.Status.Length > 0)
                ImGui.TextColored(_signUps.Active ? Yellow : Grey, $"   {Loc.Runtime(_signUps.Status)}");

            // The phase and the window's real button labels, because "it did nothing" needs to be
            // separable into "no button" / "wrong label" / "clicked and ignored". Joining is a
            // TWO-step flow - Register, then Commence once the lottery picks you - so a box
            // sitting in AwaitingSelection is working correctly, not stuck.
            if (_signUps.Active)
            {
                ImGui.TextColored(Grey, $"   フェーズ: {Loc.Phase(_signUps.Phase)}");
                ImGui.TextColored(Grey,
                    _signUps.LastButtons.Count == 0
                        ? "   ボズヤファインダーのボタンを検出できません"
                        : $"   検出ボタン: {string.Join(", ", _signUps.LastButtons)}");
            }

            ImGui.Spacing();

            if (ImGui.Button("全クライアントでパーティ支援ON", new Vector2(280, 0)))
                _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.PartySupport, "1"));
            ImGui.SameLine();
            if (ImGui.Button("全クライアントでOFF"))
                _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.PartySupport, "0"));

            ImGui.TextColored(Grey,
                "   全クライアントからパーティへのバフ・回復を行います。ロストアクションのチャージを消費するため、\n" +
                "   ON/OFFをすぐ切り替えられるよう並べています。在庫切れになったクライアントは自動停止します。");

            ImGui.Spacing();

            if (ImGui.Button(Loc.T("Start all", "全クライアント開始"))) _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Start, ""));
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Stop all", "全クライアント停止"))) _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Stop, ""));
            ImGui.SameLine();
            if (ImGui.Button("全クライアントの移動指示を中止")) _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Cancel, ""));
            ImGui.Separator();
        }

        var rows = _sync.Snapshot();
        foreach (var box in rows)
        {
            ImGui.PushID(box.Name);

            ImGui.TextColored(box.IsSelf ? Green : Grey, box.IsSelf ? $"{box.Name}（自分）" : box.Name);

            // Slot summary in words - the icons live in the hotbar window; here the useful thing
            // is whether the box is actually loaded for what you are about to do.
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"   {Describe(box.Slot0)}  |  {Describe(box.Slot1)}");

            if (box.IsSelf)
            {
                ImGui.TextColored(Grey, $"   {Loc.Runtime(_controller.Status)}");
                if (_errands.Active)
                    ImGui.TextColored(Yellow, $"   移動指示: {Loc.Runtime(_errands.Status)}");

                var support = _controller.PartySupport;
                if (support.Active || support.Status.Length > 0)
                    ImGui.TextColored(support.Active ? Green : Grey, $"   パーティ支援: {Loc.Runtime(support.Status)}");

                if (ImGui.SmallButton(support.Active ? "パーティ支援を停止" : "パーティ支援を開始"))
                    support.Toggle();
            }
            else if (CanCommand)
            {
                if (ImGui.SmallButton("支援ON"))
                    _link.SendCommand(new BoxCommand(box.Name, BoxVerb.PartySupport, "1"));
                ImGui.SameLine();
                if (ImGui.SmallButton("支援OFF"))
                    _link.SendCommand(new BoxCommand(box.Name, BoxVerb.PartySupport, "0"));
                ImGui.SameLine();
                if (ImGui.SmallButton("開始")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.Start, ""));
                ImGui.SameLine();
                if (ImGui.SmallButton("停止")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.Stop, ""));
                ImGui.SameLine();
                if (ImGui.SmallButton("移動中止")) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.Cancel, ""));
                ImGui.SameLine();
                if (ImGui.SmallButton(Loc.T("Sign up", "CE参加申請"))) _link.SendCommand(new BoxCommand(box.Name, BoxVerb.SignUp, ""));
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (rows.Count <= 1 && _config.MultiboxEnabled)
        {
            ImGui.TextColored(Grey,
                "現在はこのクライアントだけが表示されています。他クライアントは接続後に表示されます。\n" +
                "接続されない場合はメイン画面の「マルチボックス」を確認してください。");
        }

        if (_controller.LastCommandResult.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Grey, $"直近の操作結果: {Loc.Runtime(_controller.LastCommandResult)}");
        }
    }

    private string Describe(DutySlot s)
    {
        if (!s.IsSet)
            return "（空）";
        var (name, _) = DutyActions.Describe(s.ActionId);
        return s.MaxCharges > 1 ? $"{name} {s.CurCharges}/{s.MaxCharges}" : name;
    }

    // ----------------------------------------------------------------- loadouts

    private void DrawLoadouts()
    {
        ImGui.TextColored(Grey,
            "ロードアウトはDuty Action 2枠、任意のEssence、適用先クライアントをまとめた設定です。\n" +
            "適用してもアイテム購入やCache↔Holster転送は行いません。必要なアクションを所持していない場合は\n" +
            "設定済みに見せかけず、そのクライアント側で不足として表示します。");
        ImGui.TextColored(Yellow,
            "Duty Action 2枠の設定自体は消費しませんが、Essenceはアイテムなので適用すると1個消費します。\n" +
            "同じEssence効果が既に有効な場合は再使用しないため、ロードアウトの再適用で無駄に消費しません。");
        ImGui.Separator();

        for (var i = 0; i < _config.Loadouts.Count; i++)
        {
            var lo = _config.Loadouts[i];
            ImGui.PushID(i);

            ImGui.TextColored(Green, lo.Name);
            ImGui.SameLine();
            ImGui.TextColored(Grey, lo.Target.Length == 0
                ? "→ このクライアント"
                : lo.Target == Loadout.AllBoxes ? "→ 全クライアント" : $"→ {lo.Target}");
            ImGui.TextColored(Grey, $"   1: {_catalog.Name(lo.Slot0)}    2: {_catalog.Name(lo.Slot1)}");
            if (lo.Essence != 0)
                ImGui.TextColored(Grey, $"   エッセンス: {_catalog.Name(lo.Essence)}{EssenceNote(lo.Essence)}");

            DrawApplyButton(lo);

            ImGui.SameLine();
            if (ImGui.SmallButton(_editing == i ? Loc.T("Done", "完了") : Loc.T("Edit", "編集")))
            {
                _editing = _editing == i ? -1 : i;
                _findSlot0 = _findSlot1 = _findEssence = string.Empty;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Delete", "削除")))
            {
                _config.Loadouts.RemoveAt(i);
                Save();
                ImGui.PopID();
                break;
            }

            if (_editing == i)
                DrawEditor(lo);

            ImGui.Separator();
            ImGui.PopID();
        }

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("###newloadout", "新しいロードアウト名", ref _newName, 64);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Add", "追加")) && _newName.Trim().Length > 0)
        {
            _config.Loadouts.Add(new Loadout { Name = _newName.Trim() });
            _newName = string.Empty;
            Save();
        }
    }

    private void DrawEditor(Loadout lo)
    {
        ImGui.Indent();

        DrawTargetPicker(lo);
        ImGui.Spacing();

        // Duty slots take action-type entries only; the Essence picker takes the 36 Essence rows.
        // The two lists are disjoint by construction - see LostActionCatalog - so neither picker
        // can produce a loadout that applies cleanly and then does nothing.
        if (DrawPicker("Duty Action 1", lo.Slot0, _catalog.DutyActions,
                "（この枠は変更しない）", ref _findSlot0, out var newSlot0))
        {
            lo.Slot0 = newSlot0;
            Save();
        }

        if (DrawPicker("Duty Action 2", lo.Slot1, _catalog.DutyActions,
                "（この枠は変更しない）", ref _findSlot1, out var newSlot1))
        {
            lo.Slot1 = newSlot1;
            Save();
        }

        if (DrawPicker("Essence", lo.Essence, _catalog.Essences,
                "（現在のEssenceを変更しない）", ref _findEssence, out var newEssence))
        {
            lo.Essence = newEssence;
            Save();
        }

        if (lo.Essence != 0)
        {
            ImGui.TextColored(Yellow,
                "   適用時に1個消費します。同じEssence効果が既に有効な場合は消費しません。");
            if (LostActionStatuses.SharesStatusWithDeeper(lo.Essence))
            {
                ImGui.TextColored(Grey,
                    "   通常版とDeep版が同じステータスを共有する場合、上位版への更新か単なる重複か判別できないため、\n" +
                    "   既存効果の上からは自動使用しません。");
            }
        }

        ImGui.Unindent();
    }

    /// <summary>
    /// A combo with a search box at the top of it.
    ///
    /// The list is 33 duty actions or 36 Essences long and in row order, which is release order -
    /// so scrolling it to find "Lost Font of Power" is the slow part of building a loadout. The
    /// filter takes keyboard focus the moment the combo opens, making the interaction
    /// click-then-type rather than click-then-aim, and Enter commits the first remaining match,
    /// which is the whole point once two or three characters have narrowed it.
    /// </summary>
    /// <returns>True when the user picked something new, with the row id in <paramref name="picked"/>.</returns>
    private bool DrawPicker(
        string label,
        byte current,
        IEnumerable<LostActionCatalog.Entry> options,
        string clearLabel,
        ref string filter,
        out byte picked)
    {
        picked = current;

        ImGui.SetNextItemWidth(260);
        if (!ImGui.BeginCombo(label, current == 0 ? clearLabel : _catalog.Name(current)))
            return false;

        // Focus the search box on the frame the popup appears, and ONLY that frame - taking focus
        // every frame would leave the list itself unclickable.
        if (ImGui.IsWindowAppearing())
        {
            filter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        var enter = ImGui.InputTextWithHint("##find", "検索", ref filter, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.Separator();

        var needle = filter.Trim();
        var changed = false;

        // The clear row is hidden while searching: it matches nothing typed, and leaving it at the
        // top would put it under an Enter meant for the first result.
        if (needle.Length == 0 && ImGui.Selectable(clearLabel, current == 0))
        {
            picked = 0;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        if (!changed)
        {
            var first = true;
            foreach (var e in options)
            {
                if (needle.Length > 0 &&
                    e.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Enter commits the top match only when something was actually typed - on an empty
                // box it would silently pick whatever happens to be first in row order.
                var take = ImGui.Selectable(e.Name, current == e.RowId)
                           || (enter && first && needle.Length > 0);
                first = false;

                if (!take)
                    continue;

                picked = e.RowId;
                changed = true;
                ImGui.CloseCurrentPopup();
                break;
            }
        }

        // Enter with nothing left to match closes rather than leaving a popup that ignores the key.
        if (enter && !changed)
            ImGui.CloseCurrentPopup();

        ImGui.EndCombo();
        return changed && picked != current;
    }

    /// <summary>Live "you already have this" note beside a loadout's Essence, when it is running.</summary>
    private string EssenceNote(byte row)
    {
        if (!_catalog.TryGet(row, out var e) || !e.HasStatus)
            return string.Empty;

        if (!LostActionStatuses.IsActive(e.StatusId, out var remaining))
            return string.Empty;

        return remaining > 0f ? $"  （有効中、残り{FormatRemaining(remaining)}）" : "  （有効中）";
    }

    private static string FormatRemaining(float seconds)
    {
        var total = (int)seconds;
        return total >= 60 ? $"{total / 60}m{total % 60:00}s" : $"{total}s";
    }

    /// <summary>
    /// One button, labelled with where the loadout actually goes.
    ///
    /// It replaced a fixed "Apply here" / "Apply to all" pair once loadouts learned to name a box.
    /// Two buttons that ignore the loadout's own target would be two ways to do the wrong thing,
    /// and a button that says "Apply" without saying to whom is the one thing an operator driving
    /// four clients cannot afford to guess at.
    /// </summary>
    private void DrawApplyButton(Loadout lo)
    {
        var label = lo.Target.Length == 0
            ? "このクライアントへ適用"
            : lo.Target == Loadout.AllBoxes ? "全クライアントへ適用" : $"{lo.Target}へ適用";

        // A peer is only reachable from the host, and only while it is actually connected. Both
        // failures are silent at the wire - a BoxCommand addressed to nobody is simply ignored by
        // everyone - so they are caught here and named instead.
        var blocked = string.Empty;
        if (lo.Target == Loadout.AllBoxes && !CanCommand)
            blocked = "グループ全体への指示はホストのみ可能です";
        else if (lo.TargetsPeer && !CanCommand)
            blocked = "他クライアントへの指示はホストのみ可能です";
        else if (lo.TargetsPeer && !IsConnected(lo.Target))
            blocked = $"{lo.Target} は未接続です";

        if (blocked.Length > 0)
        {
            ImGui.BeginDisabled();
            ImGui.SmallButton(label);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(Yellow, $"({blocked})");
            return;
        }

        if (!ImGui.SmallButton(label))
            return;

        if (lo.Target.Length == 0)
            ApplyLocally(lo);
        else
            _link.SendCommand(new BoxCommand(lo.Target, BoxVerb.Loadout, lo.Encode()));
    }

    private bool IsConnected(string name)
    {
        foreach (var box in _sync.Snapshot())
            if (string.Equals(box.Name, name, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Pick which box a loadout is for.
    ///
    /// Listed live from the roster rather than typed, so the names can only ever be ones that
    /// exist - and a saved name whose box has since gone offline still shows, marked, instead of
    /// vanishing and silently retargeting the loadout at this box.
    /// </summary>
    private void DrawTargetPicker(Loadout lo)
    {
        var preview = lo.Target.Length == 0
            ? "このクライアント"
            : lo.Target == Loadout.AllBoxes ? "全クライアント" : lo.Target;

        ImGui.SetNextItemWidth(260);
        if (!ImGui.BeginCombo("適用先", preview))
            return;

        if (ImGui.Selectable("このクライアント", lo.Target.Length == 0))
        {
            lo.Target = string.Empty;
            Save();
        }

        if (ImGui.Selectable("全クライアント", lo.Target == Loadout.AllBoxes))
        {
            lo.Target = Loadout.AllBoxes;
            Save();
        }

        ImGui.Separator();

        var sawSaved = false;
        foreach (var box in _sync.Snapshot())
        {
            if (box.IsSelf)
                continue;

            if (string.Equals(box.Name, lo.Target, StringComparison.Ordinal))
                sawSaved = true;

            if (ImGui.Selectable(box.Name, lo.Target == box.Name))
            {
                lo.Target = box.Name;
                Save();
            }
        }

        // Keep a saved-but-offline box in the list, marked. Dropping it would leave the combo
        // showing a name it does not offer, and re-picking it would be impossible until that box
        // happened to be up.
        if (lo.TargetsPeer && !sawSaved)
            ImGui.TextColored(Yellow, $"{lo.Target}  （未接続）");

        ImGui.EndCombo();
    }

    private void ApplyLocally(Loadout lo) =>
        _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.Loadout, lo.Encode()));

    // ------------------------------------------------------------------ errands

    private void DrawErrands()
    {
        ImGui.TextColored(Grey,
            "指定した種類のオブジェクトで最寄りのものへ移動し、操作します。\n" +
            "通常の周回と同じBOCCHI式経路を使用し、必要ならフィールド内Aethernetも利用します。");

        ImGui.TextColored(Grey,
            "南方ボズヤ戦線・ザトゥノル高原のフィールド内AethernetはLifestream連携で使用します。\n" +
            "Lifestreamが利用できない場合はvnavmeshの地上移動へフォールバックします。");
        ImGui.Separator();

        foreach (var t in Interactables.Known)
        {
            ImGui.PushID((int)t.DataId);

            ImGui.TextColored(Green, t.Label);
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"- {Loc.Runtime(t.Note)}");

            var near = Interactables.Nearest(t.DataId);
            ImGui.TextColored(Grey, near is { } n
                ? $"   このクライアントから最寄り: {Movement.DistanceToPlayer(n.Position):F0}y"
                : "   現在見える範囲にありません");

            if (ImGui.SmallButton("このクライアントを移動"))
                _link.SendCommandLocal(new BoxCommand(_sync.SelfName, BoxVerb.Interact, t.DataId.ToString()));

            if (CanCommand)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("全クライアントを移動"))
                    _link.SendCommand(new BoxCommand(BoxCommand.All, BoxVerb.Interact, t.DataId.ToString()));
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (_errands.Active)
        {
            ImGui.TextColored(Yellow, $"このクライアント: {Loc.Runtime(_errands.Status)}");
            if (ImGui.Button("このクライアントの移動指示を中止"))
                _errands.Cancel("操作画面から移動指示を中止しました。");
        }
        else if (_errands.Status.Length > 0)
        {
            ImGui.TextColored(Grey, $"このクライアント: {Loc.Runtime(_errands.Status)}");
        }
    }
}
