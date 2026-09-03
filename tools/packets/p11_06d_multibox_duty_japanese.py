from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def replace_many(path: str, pairs: list[tuple[str, str]]) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    changed = 0
    for old, new in pairs:
        if old in text:
            text = text.replace(old, new)
            changed += 1
    if changed:
        p.write_text(text, encoding="utf-8")
    print(f"{path}: localized {changed} multibox/duty UI group(s)")


replace_many("Windows/MultiboxerWindow.cs", [
    (': base("Bozja Multiboxer###BozjaBuddyRebornMultiboxer")', ': base("ボズヤ マルチボックス###BozjaBuddyRebornMultiboxer")'),
    ('ImGui.TextColored(Yellow, "Multibox is off - this panel drives only this box.");', 'ImGui.TextColored(Yellow, "マルチボックスはOFFです。この画面では現在のクライアントだけを操作します。");'),
    ('ImGui.TextColored(Grey, "Turn it on in the main window to control the whole group.");', 'ImGui.TextColored(Grey, "グループ全体を操作する場合はメイン画面でマルチボックスを有効にしてください。");'),
    ('ImGui.TextColored(Yellow, "This box is a client.");', 'ImGui.TextColored(Yellow, "このクライアントは子機です。");'),
    ('"Only the host can send instructions to the group - tick \\"This client is the host\\"\\n" +\n                "on whichever box you actually sit at. Buttons here still drive this box."', '"グループ全体へ指示できるのはホストだけです。操作する1クライアントだけで\\n" +\n                "「このクライアントをホストにする」をONにしてください。この画面の自機操作は引き続き使えます。"'),
    ('peers > 0 ? $"Host - {peers} box{(peers == 1 ? "" : "es")} connected" : "Host - nobody connected yet"', 'peers > 0 ? $"ホスト - {peers}クライアント接続中" : "ホスト - まだ他クライアントは接続していません"'),
    ('ImGui.TextColored(Grey, "Everything below, for every box at once:");', 'ImGui.TextColored(Grey, "以下の操作を全クライアントへ一括送信します:");'),
    ('? "   window buttons: none found"\n                        : $"   window buttons: {string.Join(", ", _signUps.LastButtons)}"', '? "   ボズヤファインダーのボタンを検出できません"\n                        : $"   検出ボタン: {string.Join(", ", _signUps.LastButtons)}"'),
    ('ImGui.Button("Party support ON, all boxes", new Vector2(280, 0))', 'ImGui.Button("全クライアントでパーティ支援ON", new Vector2(280, 0))'),
    ('ImGui.Button("OFF, all boxes")', 'ImGui.Button("全クライアントでOFF")'),
    ('"   Buffs and heals the party from every box at once. It spends farmed charges for as\\n" +\n                "   long as it runs, which is why the OFF button sits next to the ON one rather than\\n" +\n                "   in a menu, and why each box also stops itself the moment it runs out."', '"   全クライアントからパーティへのバフ・回復を行います。ロストアクションのチャージを消費するため、\\n" +\n                "   ON/OFFをすぐ切り替えられるよう並べています。在庫切れになったクライアントは自動停止します。"'),
    ('ImGui.Button("Cancel errands")', 'ImGui.Button("全クライアントの移動指示を中止")'),
    ('box.IsSelf ? $"{box.Name} (you)" : box.Name', 'box.IsSelf ? $"{box.Name}（自分）" : box.Name'),
    ('ImGui.TextColored(Grey, $"   {_controller.Status}");', 'ImGui.TextColored(Grey, $"   {Loc.Runtime(_controller.Status)}");'),
    ('ImGui.SmallButton(support.Active ? "Stop party support" : "Start party support")', 'ImGui.SmallButton(support.Active ? "パーティ支援を停止" : "パーティ支援を開始")'),
    ('ImGui.SmallButton("Support on")', 'ImGui.SmallButton("支援ON")'),
    ('ImGui.SmallButton("Support off")', 'ImGui.SmallButton("支援OFF")'),
    ('ImGui.SmallButton("Start")', 'ImGui.SmallButton("開始")'),
    ('ImGui.SmallButton("Stop")', 'ImGui.SmallButton("停止")'),
    ('ImGui.SmallButton("Cancel")', 'ImGui.SmallButton("移動中止")'),
    ('"Only this box is listed. Peers appear once they connect - see the main window\'s\\n" +\n                "Multibox tab if the link is not coming up."', '"現在はこのクライアントだけが表示されています。他クライアントは接続後に表示されます。\\n" +\n                "接続されない場合はメイン画面の「マルチボックス」を確認してください。"'),
    ('return "(empty)";', 'return "（空）";'),
    ('"A loadout is the two Lost Actions to keep in the duty slots, plus optionally the Essence\\n" +\n            "to be running, plus which box it is for. Applying one sets those slots on that box - it\\n" +\n            "does NOT buy or transfer anything, so a box that does not hold the action reports that\\n" +\n            "instead of silently looking configured."', '"ロードアウトはDuty Action 2枠、任意のEssence、適用先クライアントをまとめた設定です。\\n" +\n            "適用してもアイテム購入やCache↔Holster転送は行いません。必要なアクションを所持していない場合は\\n" +\n            "設定済みに見せかけず、そのクライアント側で不足として表示します。"'),
    ('"The two duty slots only move an icon onto the bar. The Essence is an ITEM: applying it\\n" +\n            "SPENDS a copy. It is skipped when that Essence\'s buff is already running, so re-applying\\n" +\n            "a loadout - or pushing one to a group where some boxes are already buffed - costs\\n" +\n            "nothing on the boxes that do not need it."', '"Duty Action 2枠の設定自体は消費しませんが、Essenceはアイテムなので適用すると1個消費します。\\n" +\n            "同じEssence効果が既に有効な場合は再使用しないため、ロードアウトの再適用で無駄に消費しません。"'),
    ('? "-> this box"\n                : lo.Target == Loadout.AllBoxes ? "-> all boxes" : $"-> {lo.Target}"', '? "→ このクライアント"\n                : lo.Target == Loadout.AllBoxes ? "→ 全クライアント" : $"→ {lo.Target}"'),
    ('$"   Essence: {_catalog.Name(lo.Essence)}{EssenceNote(lo.Essence)}"', '$"   Essence: {_catalog.Name(lo.Essence)}{EssenceNote(lo.Essence)}"'),
    ('ImGui.InputTextWithHint("###newloadout", "new loadout name", ref _newName, 64);', 'ImGui.InputTextWithHint("###newloadout", "新しいロードアウト名", ref _newName, 64);'),
    ('DrawPicker("Duty slot 1", lo.Slot0, _catalog.DutyActions,\n                "(leave this slot alone)"', 'DrawPicker("Duty Action 1", lo.Slot0, _catalog.DutyActions,\n                "（この枠は変更しない）"'),
    ('DrawPicker("Duty slot 2", lo.Slot1, _catalog.DutyActions,\n                "(leave this slot alone)"', 'DrawPicker("Duty Action 2", lo.Slot1, _catalog.DutyActions,\n                "（この枠は変更しない）"'),
    ('DrawPicker("Essence", lo.Essence, _catalog.Essences,\n                "(leave my Essence alone)"', 'DrawPicker("Essence", lo.Essence, _catalog.Essences,\n                "（現在のEssenceを変更しない）"'),
    ('"   Spends a copy when applied, unless that Essence\'s buff is already running."', '"   適用時に1個消費します。同じEssence効果が既に有効な場合は消費しません。"'),
    ('"   A plain and a Deep Essence of the same name share one status effect, so this\\n" +\n                    "   cannot tell an upgrade from a repeat and will not spend one over the other."', '"   通常版とDeep版が同じステータスを共有する場合、上位版への更新か単なる重複か判別できないため、\\n" +\n                    "   既存効果の上からは自動使用しません。"'),
    ('ImGui.InputTextWithHint("##find", "search", ref filter, 64,', 'ImGui.InputTextWithHint("##find", "検索", ref filter, 64,'),
    ('return remaining > 0f ? $"  (running, {FormatRemaining(remaining)} left)" : "  (running)";', 'return remaining > 0f ? $"  （有効中、残り{FormatRemaining(remaining)}）" : "  （有効中）";'),
    ('? "Apply here"\n            : lo.Target == Loadout.AllBoxes ? "Apply to all boxes" : $"Apply to {lo.Target}";', '? "このクライアントへ適用"\n            : lo.Target == Loadout.AllBoxes ? "全クライアントへ適用" : $"{lo.Target}へ適用";'),
    ('blocked = "only the host can address the group";', 'blocked = "グループ全体への指示はホストのみ可能です";'),
    ('blocked = "only the host can drive another box";', 'blocked = "他クライアントへの指示はホストのみ可能です";'),
    ('blocked = $"{lo.Target} is not connected";', 'blocked = $"{lo.Target} は未接続です";'),
    ('? "This box"\n            : lo.Target == Loadout.AllBoxes ? "All boxes" : lo.Target;', '? "このクライアント"\n            : lo.Target == Loadout.AllBoxes ? "全クライアント" : lo.Target;'),
    ('ImGui.BeginCombo("Apply to", preview)', 'ImGui.BeginCombo("適用先", preview)'),
    ('ImGui.Selectable("This box", lo.Target.Length == 0)', 'ImGui.Selectable("このクライアント", lo.Target.Length == 0)'),
    ('ImGui.Selectable("All boxes", lo.Target == Loadout.AllBoxes)', 'ImGui.Selectable("全クライアント", lo.Target == Loadout.AllBoxes)'),
    ('$"{lo.Target}  (not connected)"', '$"{lo.Target}  （未接続）"'),
    ('"Send a box to the nearest object of a kind and interact with it. The box walks there\\n" +\n            "with vnavmesh, so it must be able to path to it - errands do not teleport."', '"指定した種類のオブジェクトで最寄りのものへ移動し、操作します。\\n" +\n            "通常の周回と同じBOCCHI式経路を使用し、必要ならフィールド内Aethernetも利用します。"'),
    ('"Bozja and Zadnor have no Teleport-style fast travel (no Aetheryte rows, no teleport\\n" +\n            "coordinates), but a \\"Bozjan aetheryte\\" is a real interactable object in the world, so\\n" +\n            "walking to one and using it is exactly what this does."', '"南方ボズヤ戦線・ザトゥノル高原のフィールド内AethernetはLifestream連携で使用します。\\n" +\n            "Lifestreamが利用できない場合はvnavmeshの地上移動へフォールバックします。"'),
    ('$"- {t.Note}"', '$"- {Loc.Runtime(t.Note)}"'),
    ('? $"   nearest to this box: {Movement.DistanceToPlayer(n.Position):F0}y"\n                : "   none visible from this box"', '? $"   このクライアントから最寄り: {Movement.DistanceToPlayer(n.Position):F0}y"\n                : "   現在見える範囲にありません"'),
    ('ImGui.SmallButton("Send this box")', 'ImGui.SmallButton("このクライアントを移動")'),
    ('ImGui.SmallButton("Send all boxes")', 'ImGui.SmallButton("全クライアントを移動")'),
    ('$"This box: {_errands.Status}"', '$"このクライアント: {Loc.Runtime(_errands.Status)}"'),
    ('ImGui.Button("Cancel this box\'s errand")', 'ImGui.Button("このクライアントの移動指示を中止")'),
    ('_errands.Cancel("Cancelled from the panel.");', '_errands.Cancel("操作画面から移動指示を中止しました。");'),
])

replace_many("Windows/DutyActionWindow.cs", [
    (': base("Duty Actions###BozjaBuddyRebornDutyActions")', ': base("Duty Action###BozjaBuddyRebornDutyActions")'),
    ('"The two Duty Action slots only exist inside a field operation. Your own row fills\\n" +\n                "in as soon as you are in Bozja or Zadnor with actions loaded."', '"Duty Action 2枠はフィールド内でのみ存在します。南方ボズヤ戦線またはザトゥノル高原で\\n" +\n                "アクションをロードすると、自分の行へ反映されます。"'),
    ('$"Read: {_sync.Diagnostic}"', '$"読み取り状態: {_sync.Diagnostic}"'),
    ('? "No other box has connected yet - showing your own slots only."\n                    : "Link down - showing your own slots only."', '? "まだ他クライアントは接続していません。自分の枠だけ表示します。"\n                    : "マルチボックス接続が切れています。自分の枠だけ表示します。"'),
    ('ImGui.TextColored(Grey, "Multibox is off - showing your own slots only.");', 'ImGui.TextColored(Grey, "マルチボックスはOFFです。自分の枠だけ表示します。");'),
    ('$"Duty Action {index + 1}: empty"', '$"Duty Action {index + 1}: 空"'),
    ('$"Charges: {slot.CurCharges}/{slot.MaxCharges}\\n"', '$"チャージ: {slot.CurCharges}/{slot.MaxCharges}\\n"'),
    ('$"Next charge in {slot.CooldownRemaining:F1}s\\n"', '$"次のチャージまで {slot.CooldownRemaining:F1}秒\\n"'),
    ('"Ready\\n"', '"使用可能\\n"'),
    ('peer.IsSelf ? "Click to use it." : $"Click to have {peer.Name} use it."', 'peer.IsSelf ? "クリックして使用します。" : $"クリックすると {peer.Name} で使用します。"'),
    ('return (false, "Nothing loaded in this slot.");', 'return (false, "この枠には何もロードされていません。");'),
    ('return (false, "Click-to-use is off - turn it back on under Settings, Lost Actions.");', 'return (false, "クリック使用がOFFです。設定の「ロストアクション」で有効にしてください。");'),
    ('return (false, "Multibox is off - only your own slots can be pressed.");', 'return (false, "マルチボックスがOFFのため、自分の枠だけ操作できます。");'),
    ('return (false, "This box is a client. Only the host can press another box\'s slot.");', 'return (false, "このクライアントは子機です。他クライアントの枠を操作できるのはホストだけです。");'),
    ('_sent = $"Told {peer.Name} to use {DutyActions.Describe(slot.ActionId).Name}.";', '_sent = $"{peer.Name} に {DutyActions.Describe(slot.ActionId).Name} の使用を指示しました。";'),
])

# One runtime CE approach status remained English after the first localization pass.
replace_many("Automation/BozjaController.cs", [
    ('Status = $"In \\"{ce.Name}\\" ({region}) - closing on {closing} " +\n                     $"({_approach.ShortfallYalms:F0}y out of range).";', 'Status = $"「{ce.Name}」で戦闘中（{region}）- {closing}へ接近中 " +\n                     $"（射程まであと{_approach.ShortfallYalms:F0}y）。";'),
])
