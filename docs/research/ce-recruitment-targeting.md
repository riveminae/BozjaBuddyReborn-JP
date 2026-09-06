# CE申請対象とUIボタンの対応

更新: 2026-09-06。対象: P4-01、要件4.2 / 10.3。

## 確認済みの公開構造

[FFXIVClientStructs AgentMycBattleAreaInfo（確認したcommit）](https://github.com/aers/FFXIVClientStructs/blob/d8633414de71407f9eb45da830472e6e0fe26a08/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentMycBattleAreaInfo.cs)には以下が定義されている。

- `AgentMycBattleAreaInfo.MycDynamicEventData`
- `MycDynamicEventData.Count`と最大3要素の`Array`
- 各要素の`Id`、`Name`、`State`、`TimeLeft`
- Stateは`None / Register / Commence / Underway`

これらは申請ウィンドウ側の対象をread-onlyで識別する根拠になる。ただし、配列indexとUIボタン順・callback引数が一致する証拠ではない。

## 現在の未達

`TargetSelector.SelectRegistration`は対象IDを返し、Controllerは`SignUpRunner.Begin(id)`へ渡す。
一方、`SignUpRunner.StepRegister`はラベルに一致する最初のボタンを選ぶ。
優先IDと`FirstRegisteringEventId()`が違っても警告のみであり、対象IDでボタンを選び直していない。

`FirstRegisteringEventId()`も全DynamicEventの最小IDであり、UIの行順を示すものではない。
したがって、selector単体の優先順位テストが成功しても、実際に正しいCEへ申請する保証にはならない。

## 次の実装前に確定すること

1. Agentに公開されたID・名前・状態と、描画中のUI行/ボタンの対応を確認する。
2. 対象を一意に識別できない場合の挙動を明示し、別CEへの申請を成功扱いしない。
3. 既存の実イベント付きボタン、非nullのevent/input data、click-settle制御を保持する。
4. 複数CE・大規模戦闘優先・ボタン非活性・受付終了・既登録・当選状態を検証する。
5. 南方/ザトゥノルで実際の登録IDと選択IDの一致を観測する。

この文書は転送/登録APIの確定や実機合格を意味しない。推測したrow番号やcallback引数を送ってはならない。
