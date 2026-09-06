# CE申請対象とUIボタンの対応

更新: 2026-09-06。対象: P4-01、要件4.2 / 10.3。

## 確認済みの公開構造

[FFXIVClientStructs AgentMycBattleAreaInfo（確認したcommit）](https://github.com/aers/FFXIVClientStructs/blob/d8633414de71407f9eb45da830472e6e0fe26a08/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentMycBattleAreaInfo.cs)には以下が定義されている。

- `AgentMycBattleAreaInfo.MycDynamicEventData`
- `MycDynamicEventData.Count`と最大3要素の`Array`
- 各要素の`Id`、`Name`、`State`、`TimeLeft`
- Stateは`None / Register / Commence / Underway`

これらは申請ウィンドウ側の対象をread-onlyで識別する根拠になる。ただし、配列indexとUIボタン順・callback引数が一致する証拠ではない。

## 修正前の未達

`TargetSelector.SelectRegistration`は対象IDを返し、Controllerは`SignUpRunner.Begin(id)`へ渡す。
一方、`SignUpRunner.StepRegister`はラベルに一致する最初のボタンを選ぶ。
優先IDと`FirstRegisteringEventId()`が違っても警告のみであり、対象IDでボタンを選び直していない。

`FirstRegisteringEventId()`も全DynamicEventの最小IDであり、UIの行順を示すものではない。
したがって、selector単体の優先順位テストが成功しても、実際に正しいCEへ申請する保証にはならない。

## 画面定義の読み取り（2026-09-06）

ゲーム版 `2026.08.11.0000.0000` の `ui/uld/MYCBattleAreaInfo.uld` を、導入済みLumina `7.0.0.0` でディスクから読み取った。確認器は `tests/UiLayoutProbe`。ゲームのプロセス・入力・メモリに触れず、資産本体を保存/配布しない。引数には既存ゲームの `sqpack` ディレクトリだけを渡す。

- 資産識別用SHA-256: `00351a453d507f9a09154feba8c6e95d4bcdb8eb07a9d6c78a3c39635af461d6`。
- 行部品はcomponent `1007`（Custom）。node `1` がroot。
- node `5` はTextで、親はnode `1`。node `9` は操作欄のResで、親は同じnode `1`。
- node `10 / 11 / 12` はそれぞれButton component `1004 / 1002 / 1005`。すべて親がnode `9`。
- 画面側にあるテンプレート行は1個。動的な複製行の順序とAgent配列順の一致は仮定しない。

取得方法の公開根拠: [Lumina GameData](https://github.com/NotAdam/Lumina/blob/b92c0cbdb65c9e53b0ec5faafa83b2cc32aa9a7c/src/Lumina/GameData.cs)、[UldFile](https://github.com/NotAdam/Lumina/blob/b92c0cbdb65c9e53b0ec5faafa83b2cc32aa9a7c/src/Lumina/Data/Files/UldFile.cs)、[UldRoot](https://github.com/NotAdam/Lumina/blob/b92c0cbdb65c9e53b0ec5faafa83b2cc32aa9a7c/src/Lumina/Data/Parsing/Uld/UldRoot.cs)。nodeの公開read構造と複製元ID取得は [AtkResNode](https://github.com/aers/FFXIVClientStructs/blob/d8633414de71407f9eb45da830472e6e0fe26a08/FFXIVClientStructs/FFXIV/Component/GUI/AtkResNode.cs) を参照した。

## 実装した対応付け

`SignUpRunner.CollectButtons` は実行時にも上記の型と親子関係を照合し、可視な戦闘名と有効なボタンを同じ行から取得する。非表示の親も拒否する。`GetBaseNodeId()` は画面構造の認識にだけ使い、イベントIDやcallback引数へ変換しない。

`RecruitmentTargeting.Find` は、Agentから読んだID・名前・状態と行内の戦闘名を一致させる。希望ID以外の行、重複した名前/ID/操作候補、状態とラベルの不一致ではクリックしない。静的資産のnode `5` が動的タイトルであることは、**実行時のAgent名との完全一致**を必須にして確認する。名前の書式が異なるなら先頭行に代替せず未一致になる。

登録済みIDが希望先と違う場合は追加操作を中止する。送信直後は登録確認待ちと表示し、実際の登録IDが一致した後で抽選待ちと表示する。抽選失効と戦闘突入の判定も希望した行だけを見る。ボタン消失だけで完了にはせず、実際のcurrent eventが希望IDと一致する参加状態も必要とする。

既存の1500ms連打防止、600ms画面安定待ち、critical-supply例外、具体的なevent/input dataを維持した。ボタンに付属するclickイベントがない場合は、別種のイベントや推測したcallbackで代用しない。

本番runner/collector/targetingをリンクした合成ホスト試験167件と不具合3件の拒否を確認した。ただし、ホスト置換は実際のクライアント構造・文字列書式・サーバー確認の証明ではない。両フィールドの実機確認は未実施。詳細は[受入記録](../implementation/ce-ui-binding-acceptance-20260906.md)。

## 実機受入で残ること

1. Agent名と描画中のタイトルの書式が一致し、複製行でも実行時の型/親子関係が成立することを確認する。
2. 複数CE・大規模戦闘優先・ボタン非活性・受付終了・既登録・当選状態を実機でも検証する。
3. 南方/ザトゥノルで実際の登録IDと選択IDの一致、抽選中の通常行動継続、戦闘突入後の転送を観測する。

この文書は転送/登録APIの確定や実機合格を意味しない。推測したrow番号やcallback引数を送ってはならない。
