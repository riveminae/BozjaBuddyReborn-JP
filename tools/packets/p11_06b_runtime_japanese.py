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
    print(f"{path}: localized {changed} runtime string group(s)")


replace_many("Automation/BozjaController.cs", [
    ('Status = "Starting.";', 'Status = "開始処理中です。";'),
    ('public void Stop(string reason = "Stopped.")', 'public void Stop(string reason = "停止しました。")'),
    ('_signUps.Cancel("Stopped.");', '_signUps.Cancel("停止しました。");'),
    ('_errands.Cancel("Errand abandoned - the character died.");', '_errands.Cancel("キャラクターが戦闘不能になったため移動指示を中止しました。");'),
    ('Status = "Not logged in.";', 'Status = "ログイン状態を確認できません。";'),
    ('Status = "Zoning / cutscene.";', 'Status = "エリア移動またはカットシーン終了を待っています。";'),
    ('? $"Building navmesh for this zone ({progress * 100f:F0}%)."', '? $"このエリアのnavmeshを構築中です（{progress * 100f:F0}%）。"'),
    (': "Waiting for the zone navmesh.";', ': "このエリアのnavmeshを待っています。";'),
    ('? "Waiting for the host to pick an objective."', '? "ホストが次の目的地を選択するのを待っています。"'),
    ('?? "No engagement or skirmish available.";', '?? "現在参加可能なCEまたはスカーミッシュがありません。";'),
    ('Status = $"In \\"{ce.Name}\\" ({region}) - {ce.StateText}, {ce.Progress}% " +\n                 $"({ce.Participants}/{ce.MaxParticipants}).";', 'Status = $"「{ce.Name}」で戦闘中（{region}）- {Loc.CeState(ce.State)} / 進行度 {ce.Progress}% " +\n                 $"（{ce.Participants}/{ce.MaxParticipants}人）。";'),
    ('Status = $"In \\"{ce.Name}\\" - dismounting.";', 'Status = $"「{ce.Name}」で戦闘するためマウントから降りています。";'),
    ('Status = $"{reason} Holding position.";', 'Status = $"{reason} その場で待機します。";'),
    ('Status = $"{reason} Waiting in {label}.";', 'Status = $"{reason} {label}で待機しています。";'),
    ('Status = $"{reason} Yielding to BossMod - dodging a mechanic.";', 'Status = $"{reason} BossModへ移動制御を渡してギミックを回避しています。";'),
    ('Status = $"{reason} Moving to the {label} staging point " +\n                 $"({Movement.DistanceToPlayer(spot):F0}y).";', 'Status = $"{reason} 待機地点 {label} へ移動中 " +\n                 $"（残り {Movement.DistanceToPlayer(spot):F0}y）。";'),
    ('Status = $"Travelling to {Describe(objective)} ({distance:F0}y) - outrunning " +\n                         $"{attackers} attacker{(attackers == 1 ? "" : "s")}, not stopping to fight.";', 'Status = $"{Describe(objective)}へ移動中（残り{distance:F0}y）- " +\n                         $"{attackers}体に追跡されていますが、停止せず逃走を継続します。";'),
    ('(_movement.RejectedIssues > 0 ? $", {_movement.RejectedIssues} refused" : "")', '(_movement.RejectedIssues > 0 ? $", 経路要求拒否 {_movement.RejectedIssues}回" : "")'),
    ('(_movement.RefusedDetours > 0 ? $", {_movement.RefusedDetours} detours refused" : "")', '(_movement.RefusedDetours > 0 ? $", 迂回失敗 {_movement.RefusedDetours}回" : "")'),
    ('Status = $"Under attack ({attackers}) - clearing before continuing to {Describe(objective)}.";', 'Status = $"{attackers}体から攻撃されています。{Describe(objective)}への移動再開前に排除します。";'),
    ('Status = $"Under attack ({attackers}) - closing on {closing}.";', 'Status = $"{attackers}体から攻撃されています。{closing}へ接近中です。";'),
    ('Status = $"At {Describe(objective)} - waiting to be registered.{_arrivalNote}";', 'Status = $"{Describe(objective)}付近で参加処理を待っています。{_arrivalNote}";'),
    ('Status = "Skirmish finished - picking the next objective.";', 'Status = "スカーミッシュが終了しました。次の対象を選択します。";'),
    ('Status = $"At {Describe(objective)} - dismounting.";', 'Status = $"{Describe(objective)}で戦闘するためマウントから降りています。";'),
    ('Status = $"Fighting {Describe(objective)}.";', 'Status = $"{Describe(objective)}で戦闘中です。";'),
    ('Status = $"Fighting {Describe(objective)} - closing on {closing} " +\n                             $"({_approach.ShortfallYalms:F0}y out of range).";', 'Status = $"{Describe(objective)}で戦闘中 - {closing}へ接近しています " +\n                             $"（射程まであと{_approach.ShortfallYalms:F0}y）。";'),
    ('Status = $"At {Describe(_lastObjective)} - waiting for the group.";', 'Status = $"{Describe(_lastObjective)}に到着済み - グループを待っています。";'),
    ('Status = $"At {Describe(_lastObjective)} - waiting for group ({arrived}/{peers} arrived).";', 'Status = $"{Describe(_lastObjective)}に到着済み - グループ待機中（{arrived}/{peers}到着）。";'),
    ('Stop("Stopped by the multibox host.");', 'Stop("マルチボックスのホストから停止されました。");'),
    ('Stop("Stopped from the multibox panel.");', 'Stop("マルチボックス操作画面から停止されました。");'),
    ('LastCommandResult = $"Loadout: {_loadouts.LastResult}";', 'LastCommandResult = $"ロストアクション構成: {_loadouts.LastResult}";'),
    ('LastCommandResult = "Loadout: could not read the requested actions.";', 'LastCommandResult = "ロストアクション構成: 指定内容を読み取れませんでした。";'),
    ('LastCommandResult = $"Errand: {_errands.Status}";', 'LastCommandResult = $"移動指示: {_errands.Status}";'),
    ('LastCommandResult = $"Sign-up: already in engagement #{joined}.";', 'LastCommandResult = $"参加申請: 既にイベント #{joined} へ参加中です。";'),
    ('$"Sign-up: not in a field zone (in {BozjaZones.Name(Svc.ClientState.TerritoryType)}).";', '$"参加申請: 対応フィールド外です（現在地: {BozjaZones.Name(Svc.ClientState.TerritoryType)}）。";'),
    ('LastCommandResult = $"Sign-up: {_signUps.Status}";', 'LastCommandResult = $"参加申請: {_signUps.Status}";'),
    ('_errands.Cancel("Cancelled from the multibox panel.");', '_errands.Cancel("マルチボックス操作画面から中止されました。");'),
    ('_signUps.Cancel("Cancelled from the multibox panel.");', '_signUps.Cancel("マルチボックス操作画面から中止されました。");'),
    ('LastCommandResult = "Errand cancelled.";', 'LastCommandResult = "移動指示を中止しました。";'),
    ('_partySupport.Stop("Party support stopped from the panel.");', '_partySupport.Stop("マルチボックス操作画面からパーティ支援を停止しました。");'),
    ('LastCommandResult = $"Party support: {_partySupport.Status}";', 'LastCommandResult = $"パーティ支援: {_partySupport.Status}";'),
    ('LastCommandResult = $"Duty action {dutySlot + 1}: {press.Message}";', 'LastCommandResult = $"Duty Action {dutySlot + 1}: {press.Message}";'),
    ('LastCommandResult = "Duty action: could not read which slot to press.";', 'LastCommandResult = "Duty Action: 使用するスロットを読み取れませんでした。";'),
    ('return "nothing";', 'return "目的地なし";'),
    ('return $"CE \\"{_catalog.Name((ushort)objective.Id)}\\"";', 'return $"CE「{_catalog.Name((ushort)objective.Id)}」";'),
    ('return $"skirmish \\"{fate.Name.TextValue}\\"";', 'return $"スカーミッシュ「{fate.Name.TextValue}」";'),
    ('return $"skirmish #{objective.Id}";', 'return $"スカーミッシュ #{objective.Id}";'),
    ('? $" Arrived as close as the navmesh allows - {_movement.SnapDrift:F0}y from the marker centre."', '? $" navmeshで到達可能な最寄り地点に到着しました（マーカー中心から{_movement.SnapDrift:F0}y）。"'),
])

replace_many("Automation/TargetSelector.cs", [
    ('$"{BozjaZones.Name(target.Territory)} is where that material drops - you are in " +\n                $"{BozjaZones.Name(territory)}.";', '$"この素材の入手場所は {BozjaZones.Name(target.Territory)} です。現在地は " +\n                $"{BozjaZones.Name(territory)} です。";'),
    ('FarmFilterNote = $"Nothing available in {FieldRegions.Label(territory, restrictedRegion)} right now.";', 'FarmFilterNote = $"{FieldRegions.Label(territory, restrictedRegion)} に現在参加可能な対象がありません。";'),
])

replace_many("Automation/ErrandRunner.cs", [
    ('Status = $"Looking for the nearest {Interactables.Label(dataId)}.";', 'Status = $"最寄りの {Interactables.Label(dataId)} を探しています。";'),
    ('public void Cancel(string reason = "Errand cancelled.")', 'public void Cancel(string reason = "移動指示を中止しました。")'),
    ('Cancel($"Gave up reaching the {Interactables.Label(_dataId)} after 90s.");', 'Cancel($"{Interactables.Label(_dataId)} へ90秒以内に到達できなかったため中止しました。");'),
    ('Cancel($"No {Interactables.Label(_dataId)} in range of this box.");', 'Cancel($"このクライアントの周辺に {Interactables.Label(_dataId)} が見つかりません。");'),
    ('Status = $"Interacted with the {Interactables.Label(_dataId)}.";', 'Status = $"{Interactables.Label(_dataId)} を操作しました。";'),
    ('Status = $"At the {Interactables.Label(_dataId)} - the game refused the interaction, retrying.";', 'Status = $"{Interactables.Label(_dataId)} に到着しましたが操作が受理されなかったため再試行します。";'),
    ('Cancel("vnavmesh is not ready, so the errand cannot travel.");', 'Cancel("vnavmeshの準備ができていないため移動指示を中止しました。");'),
    ('Status = $"Walking to the {Interactables.Label(_dataId)} ({distance:F0}y).";', 'Status = $"{Interactables.Label(_dataId)} へ移動中（残り{distance:F0}y）。";'),
])
