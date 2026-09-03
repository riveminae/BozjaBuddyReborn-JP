# BozjaBuddyReborn-JP v1.1.0 要件定義

作成日: 2026-09-01  
対象ブランチ: `feat/bocchi-navigation`  
対象リリース: Stable `1.1.0.0` / Test `1.0.90.x`

## 1. 目的

`BozjaBuddyReborn-JP` を、南方ボズヤ戦線およびザトゥノル高原で、スカーミッシュ・クリティカルエンゲージメント（CE）・補給・死亡復旧までを含めて長時間自動周回できるプラグインへ拡張する。

既存BBRの以下の強みは維持する。

- Resistance Relic 進捗・素材追跡
- CE検出・参加処理
- スカーミッシュ戦闘
- BossMod / BossMod Reborn連携
- Rotation Solver Reborn連携
- vnavmesh連携
- Lost Action / Lost Holster関連機能
- Party Support / Multibox関連機能

特に、現時点で実機動作確認済みのスカーミッシュ戦闘フローは不要に作り直さず、ナビゲーション・状態遷移・補給・生存制御を中心に拡張する。

## 2. 対象範囲

### 2.1 対応エリア

- 南方ボズヤ戦線: Territory `920`
- ザトゥノル高原: Territory `975`

フィールド外から両エリアへ入場する処理は本リリースの対象外とする。対応エリア外で `Start` された場合は開始を拒否する。Running中に対応エリア外へ退出・強制排出された場合は自動操作を停止する。

### 2.2 大規模戦闘

カストルム・ラクスリトレおよび旗艦ダル・リアータは既存設定でONになっている場合のみ参加対象とする。ONの場合は通常CE・Resistance Relic対象CEを含め、最優先とする。

終了後はデフォルトでMaintenanceを実行し、通常周回へ自動復帰する。自動復帰は設定でOFFにできる。

## 3. 基本状態遷移

通常の利用者操作は `Start` 1回のみとする。

```text
Start
  -> 対応エリア確認
  -> 必須依存確認
  -> Lost Action構成の整合性確認
  -> 必要ならCacheへ移動
  -> 必要ならInitialize
  -> Activity選択
  -> 移動
  -> スカーミッシュ戦闘 / CE申請・突入
  -> Maintenance
  -> 次Activity選択
  -> 繰り返し
```

`Initialize` は独立した通常操作にはせず、Start時に必要な場合だけ自動実行する。Advancedに `強制Initialize` を用意する。

Initializeを省略できる条件は以下すべてを満たす場合とする。

- 前回Initialize時と現在ジョブ/ロールが一致
- Duty Action 2枠が期待構成と一致
- Essence状態が許容状態
- 生存用ホルダー在庫が最低基準以上

## 4. Activity選択

### 4.1 スカーミッシュ

Farm対象が指定されていない通常周回では、原則として以下を満たすスカーミッシュから選ぶ。

- 進行度 `< 80%`
- 到達可能
- BOCCHI式の経路コストが最小

移動中に以下のいずれかを満たした場合は目的地を即キャンセルし、再選択する。

- イベント消滅
- 進行度100%
- 残り時間不足
- 経路失敗によりBlacklist化

### 4.2 CE

CEは現地へ移動して申請するのではなく、可能な限り現在地から `参加希望` を行う。

抽選待ち中もスカーミッシュ等の通常行動を継続する。当選した場合は原則として即 `戦闘突入` を実行する。

同時に申請するCEは1件のみとする。

優先順位は以下とする。

1. 設定ONの大規模戦闘
2. 明示されたResistance Relic Farm対象に該当するCE
3. `PriorityEngagements` の既存優先順位
4. その他CE

### 4.3 Resistance Relic Farm

最初のFarm対象は必ずユーザーが明示指定する。未指定の場合は通常周回を行い、自動でFarm対象を選ばない。

一度Farmを開始した後、指定素材が必要数に達した場合は次の不足素材へ自動継続できる。Farm対象切替は即時に行う。

現在進行中のActivityについては次のルールとする。

- 次素材でも同じZone/Activityが有効: 現在Activityを継続
- 次素材では無関係: 現在Activityを中断して新しい対象へ移行

次素材が別territoryを要求する場合、自動でフィールド外へ移動しない。現在territory内で取得可能な次素材があればそちらを優先し、取得可能対象が無ければStopする。

Farm対象イベントが現在存在しない場合、無関係なスカーミッシュには参加せず、次spawnに有利なエーテライト付近で待機する。

デフォルト停止条件は無限周回とする。設定で以下を選択可能にする。

- 指定素材完了で停止
- 現Relic段階完了で停止
- 完了後も次対象へ継続

## 5. BOCCHIナビゲーション統合

### 5.1 方針

BOCCHIのナビゲーション実装はクリーンルーム再実装せず、必要なソースを直接取り込み、BBR向けの薄いアダプタを追加する。

想定配置:

```text
Vendor/BOCCHI/
```

直接流用したファイルには由来・ライセンスを明示する。

### 5.2 経路候補

以下を同一のコストモデルで比較する。

- 徒歩 / マウント直行
- 最寄りエーテライトまで移動 -> Aethernet -> 目的地
- Return -> ベースキャンプ -> Aethernet -> 目的地

BOCCHI既定値を初期値として採用する。

- MaxDirectWalkDistance: `80y`
- AethernetHopCost: `50y相当`
- ReturnCost: `40y相当`

Advancedで値を変更できるようにする。

Return経路は設定可能とし、デフォルトON。

### 5.3 Lifestream

Lifestreamはoptional dependencyとする。

- Aethernet teleport失敗時: 1回だけ再試行
- イベント移動中の失敗: 即vnavmesh直行へfallback
- 補給・待機など時間制約の弱い場面: 最大30秒復帰待ち後fallback

Lifestream不在を理由に周回全体をStopしない。

### 5.4 vnavmeshスタック

同一経路の再試行を最大3回行い、失敗したらAethernet/Returnを含め別経路を再計算する。

それでも到達不能の場合、そのActivityを消滅までBlacklistする。同一spawnには再挑戦しないが、次回spawnは新規Activityとして扱う。

全候補が到達不能の場合は拠点/適切なエーテライトへ移動し、新しいActivity spawnを待つ。

### 5.5 手動操作

Running中にユーザーの手動移動入力を検知した場合はナビ制御を一時yieldする。入力停止後、数秒（初期値3秒程度）で自動復帰する。

手動ターゲット変更にはBBRは干渉しない。

## 6. 危険敵回避

ボズヤ固有の敵強度表示を基準にする。

- `I / II / III`: 回避対象外。aggroしても原則無視して移動継続
- `IV / V / ★`: 回避対象
- 敵ランク取得不能: 危険扱い

★敵はIV/Vより大きい安全マージンを持たせる。追加マージンは設定可能とし、デフォルトは通常 `DangerClearance + 5y` 程度を想定する。

移動中に危険敵を誤ってaggroしても原則戦わず、leashを狙って逃走する。

特にマウント中は、Lost Action・攻撃Action・回復Actionを含め、マウント解除の原因になり得るActionをBBRから発動しない。

徒歩へ落とされた場合は生存ロジックを再度有効化する。

既存の `npc.Level >= DangerousEnemyMinLevel` 判定はボズヤ用途から廃止する。

## 7. Lost Action Initialize

### 7.1 目的

ロールに応じた「生存最優先」のLost Action構成を自動構築する。

ロール区分は以下3系統を設定単位とする。

- Tank
- Healer
- DPS（Melee / Physical Ranged / Casterを同一閾値設定とする）

### 7.2 Initialize手順

1. Lost Finds Cache / Holster / Duty Action / Essence状態をsnapshot
2. 全返却が可能かpreflight
3. Holster内容をCacheへ返却
4. 現在ジョブからロール判定
5. ロール別プリセットを選択
6. Cache在庫を確認してfallback候補を選択
7. Holsterを目標構成まで再構築
8. Duty Action 2枠を設定
9. 必要に応じてEssence/Potion Kit等を適用
10. 完了状態を記録

Initializeはtransactionとして扱う。途中失敗時は変更前snapshotへrollbackする。rollback不能の場合のみStartを拒否する。

Cache/Holsterのサーバー状態を直接メモリ書換えしてはならない。正式なゲームUI処理または安全性を確認したcallback/操作を利用する。

### 7.3 ホルダー容量

目標は99重量付近まで生存系候補をバランスよく積載する。単一アイテムだけで埋めず、ロール別の目標個数を持つ。

Duty Actionは生存候補が2つ揃う場合は両枠とも生存用。揃わない場合のみUtility/火力へfallbackする。

## 8. Lost Actionプリセット

### 8.1 Essence優先候補

Tank:

1. Deep Bloodsucker
2. Bloodsucker
3. Deep Guardian
4. Guardian

DPS:

1. Deep Beast
2. Beast
3. Deep Platebearer / Deep Veteran系
4. 通常Platebearer / Veteran系

Healer:

1. Deep Templar
2. Templar
3. Deep Veteran
4. Veteran

Deep系は候補順位では通常版より上とするが、デフォルト設定は全Deep系 `持込OFF / 自動使用OFF` とする。ユーザーがONにした場合のみ最優先候補として使用する。

既存Essence上書きは設定可能とし、デフォルトOFF。

### 8.2 共通生存候補

- Resistance Potion Kit
- Resistance Reraiser
- Lost Manawall
- ロールに適したLost Cure系

Tank/DPSでは Lost Cure IV を主回復候補とする。

各Lost Actionに以下2トグルを独立して持つ。

- `持込`: Initialize/補給でHolsterへ入れてよい
- `自動使用`: Holsterに存在する場合、条件成立時に自動使用してよい

`持込OFF / 自動使用ON` も有効とし、既にHolsterに存在する場合のみ自動使用可能とする。

UIは生存プリセット候補を通常表示し、全Lost ActionはAdvancedで検索・設定可能とする。

## 9. 自動生存制御

### 9.1 HP閾値

ロールごとに `通常回復` / `緊急` の2閾値を持つ。

初期値:

| Role | 通常回復 | 緊急 |
|---|---:|---:|
| Tank | 55% | 30% |
| Healer | 70% | 45% |
| DPS | 65% | 40% |

設定画面から個別変更可能とする。

### 9.2 Potion Kit

Initialize時に自動使用許可ONかつ在庫があれば適用する。効果が切れた場合は徒歩/戦闘待機など自然にAction可能なタイミングで再適用する。

マウントを降りてまで再適用しない。

### 9.3 Reraiser

自動使用許可ONの場合、各ロールの緊急HPラインを初めて割った時点で使用する。マウント中は保留する。

### 9.4 Manawall等

Lost Manawallは全ロールの緊急防御候補とする。

## 10. 自動補給

### 10.1 Low-watermark

アイテム/Actionごとに最低残数を設定可能とする。

初期値:

- Resistance Potion Kit: `2`
- Resistance Reraiser: `1`
- 主回復Action: 残り使用回数 `5` 相当
- 緊急防御Action: 残り `1セット` 相当

通常の不足では現在Activityを完走してから補給する。

`Potion Kitなし AND 使用可能な自己回復Lost Actionなし` の場合は「生存手段ゼロ」とみなし、現在スカーミッシュを中断して即補給へ向かう。

### 10.2 補給方式

通常補給はInitializeのような全返却を行わず、足りないものだけ目標個数まで差分補充する。

Cache側にも候補が無い場合は、そのインスタンス中の欠品として記録し、同じ物を取りに無限往復しない。強制Initialize時に欠品キャッシュをクリアする。

生存手段ゼロかつCacheにも補給品が無い場合はStopする。それ以外は警告を表示して周回継続する。

### 10.3 CEとの競合

補給/Initialize中でもCE申請は可能な限り即行う。

CE当選時:

- 生存手段あり: 即 `戦闘突入`
- 生存手段ゼロ: 最低限の補給を優先してから突入

Initialize中のCE当選では、Holsterが空になる等の危険な中間状態で突入しない。安全なtransaction境界でInitializeを中断してから突入する。

## 11. 死亡復旧

### 11.1 CE中

CE中に死亡した場合は、CE終了までキャンプリスポーンしない。Raise受諾を待つ。

### 11.2 スカーミッシュ/移動中

- スカーミッシュ中: Raiseを30秒待機
- 移動中: Raiseを10秒待機

タイムアウト後はキャンプリスポーンを行う。

### 11.3 TextAdvance

TextAdvanceはoptional dependencyとする。

死亡復旧に必要な場面でTextAdvanceが導入済みかつOFFなら、一時的に有効化し、Raise/respawn UI処理をTextAdvanceへ委譲する。復旧後は元のON/OFF状態へ戻す。

TextAdvanceが無く、自動死亡復旧できない場合は日本語エラーを表示してStopする。

## 12. Dependency管理

必須:

- vnavmesh
- Rotation Solver Reborn
- BossMod または BossMod Reborn

optional:

- Lifestream
- TextAdvance

必須依存が途中で利用不能になった場合は最大60秒復帰を待つ。戦闘中でも待機し、その間BBR自身のLost Action生存処理は可能な範囲で継続する。

60秒で復帰しない場合:

- 安全に拠点へ戻れる状況: 拠点へ戻してStop
- 戦闘等で即復帰できない状況: 戦闘終了/死亡まで生存処理を継続し、その後Stop
- 上記処理自体が不能: 即Stop

Lifestreamのみは本条件の必須依存停止対象外とする。

## 13. ソーシャル操作拒否

Running中のみ、BBRが種類を明確に識別できた対人要求を自動拒否する。

対象例:

- Party / Cross-world Party / Alliance系招待
- Friend申請
- Linkshell / CWLS等の加入招待
- Trade等の対人要求

汎用 `SelectYesno` を無差別に `No` へ送ってはならない。addon名・表示テキスト・ゲーム状態等から対象を識別できた場合のみ拒否する。

既にパーティに入った状態でStartされた場合は離脱せず、そのまま周回を許可する。

拒否した事実はStatus/Warning履歴に小さく記録するが、Dalamud通知は不要。

## 14. UI / Localization

### 14.1 言語

ユーザー向けUIはクライアント/Dalamud UI言語に関係なく日本語固定とする。

対象:

- Window title
- Tab
- Button
- Setting
- Tooltip
- Status
- Warning
- Error
- Dependencies
- Resistance Relic表示
- Initialize/補給進捗

トラブルシューティング用ログは英語原文を維持する。

### 14.2 UI構成

主要カテゴリ:

- 周回
- 生存
- ロストアクション
- 移動
- Relic
- 詳細設定

メイン画面には以下をコンパクト表示する。

- 現在State
- 現在目的地
- 次のAction
- HP
- Role
- Essence
- Potion Kit
- Reraiser
- CE申請/当選状態
- 現在経路方式（Direct / Aethernet / Return等）

### 14.3 Diagnostics

Diagnosticsに以下を表示する。

- 現在State
- 目的地
- 経路方式
- CE状態
- Lost Action/Holster在庫
- Dependency状態
- 直近Warning履歴
- 状態遷移履歴

`診断情報をクリップボードへコピー` を提供する。

通常ログは主要state transitionのみ。`Verbose diagnostics` ON時は詳細ログを英語で出力する。

重要エラーは以下で通知する。

- Status
- 色付き警告
- Dalamud notification

自動復旧した問題は黄色Warningとして履歴に残し、現在Statusは正常へ戻す。

## 15. Debug / Feature Flags

Test buildでは以下の操作をAdvancedに提供する。

- 強制Initialize
- 経路再計算
- 現目的地Blacklist
- Cacheへ補給
- Legacy Movement / BOCCHI Navigation切替
- その他実装切り分け用feature flags

Stableでは原則削除し、以下のみ残す。

- 強制Initialize
- Legacy Movement非常口

Debug ON時はworld overlayに以下を描画可能とする。

- 目的地
- 計画経路
- IV/V/★およびunknown危険敵の回避領域
- 選択Aethernet

StableではDebug表示デフォルトOFF。

## 16. ライセンス

BOCCHIコードを直接取り込むため、フォーク全体の配布ライセンスをAGPL-3.0へ変更する。

以下のnoticeを保持する。

- Bozja Buddy Reborn由来コード: 元MIT copyright/notice
- BOCCHI由来コード: AGPL-3.0および原作者notice
- Ocelot由来コードを取り込む場合: 元MIT copyright/notice

第三者コード由来が分かるよう `THIRD-PARTY-NOTICES` を更新する。

## 17. Upstream監視

BOCCHI upstreamをGitHub Actionsで週1回確認する。

取り込んだナビゲーション関連ファイルに変更があった場合のみ、手動確認用のIssue等を生成する。自動マージは行わない。

## 18. 設定migration

`1.0.28.1` の既存設定を可能な限り維持し、新規設定へ自動migrationする。

migration失敗時は旧設定をバックアップしてから新規設定を生成する。

設定保存単位:

- Role別HP閾値・Lost Action許可: 共通設定
- Resistance Relic Farm対象/進捗に関する状態: Character別

## 19. Test配布 / Versioning

Test buildは `feat/bocchi-navigation` 専用Custom Repo URLから配布する。

Stable repoとTest repoは同じ `InternalName` を使用し、同時に有効化しない。Test利用時はStable repoを無効化する。

Version:

- Test: `1.0.90.1`, `1.0.90.2`, ...
- Stable: `1.1.0.0`

Dalamud更新検出のため、Build時にAssemblyVersion / manifest version / ZIP配布versionを必ず同期させる。

Test buildで重大障害が発生した場合、GUIからStable版へ戻す手順を確認できるようにする。

## 20. Stable受入条件

mainへmergeする前に最低限、実機で以下を確認する。

### 南方ボズヤ戦線

- 連続3スカーミッシュ完走
- CE参加希望 -> 当選 -> 戦闘突入 -> 終了 -> 通常周回復帰
- Aethernetを含む経路選択
- IV/V/★危険敵回避

### ザトゥノル高原

- 連続3スカーミッシュ完走
- CE参加希望 -> 当選 -> 戦闘突入 -> 終了 -> 通常周回復帰
- Aethernetを含む経路選択
- IV/V/★危険敵回避

### 共通

- Initialize成功
- Initialize失敗時rollback
- 差分補給
- Low-watermark補給
- 死亡 -> Raise受諾またはキャンプリスポーン -> 自動復帰
- Lifestream失敗fallback
- vnavmeshスタック復旧
- 必須Dependency一時切断/復旧
- ソーシャル招待/要求の限定的自動拒否
- 日本語UI / 英語ログ分離
- 既存設定migration
- CI build成功
- Test Custom RepoからDalamud更新成功

上記結果をまとめてユーザーへ提示し、明示承認後のみmainへmergeする。自動mergeは禁止。

## 21. 実装前に解決する技術調査

以下は要件上の意思決定ではなく、実装時にコード/実機から事実を確定する。

### 21.1 ボズヤ敵ランク

`I / II / III / IV / V / ★` のゲーム内部表現を特定する。

- `npc.Level` を代用しない
- 未確認の `BNpcBase.Rank` 等を推測で使用しない
- 実際のnameplate/object/game stateから取得元を特定する
- 取得失敗時は要件どおりunknown=危険扱い

### 21.2 Lost Finds Cache <-> Holster転送

Cache/Holsterの在庫readは既存ClientStructs等を利用可能だが、転送はサーバー状態のため直接メモリを書き換えない。

安全なUI callback、game function、既存OSS実装等を調査し、正規のゲーム操作と同等の経路を特定する。

必要ならTest buildへ一時的な診断機能を入れ、手動で1件移動した際のcallbackを観測して確定する。

### 21.3 TextAdvance死亡復旧

TextAdvanceの現在の挙動・有効化方法を実機/ソースで確認し、Raise受諾・キャンプリスポーンで期待どおり動くことをTestで確認する。

### 21.4 ソーシャル要求識別

Party/Friend/LS/CWLS/Trade等について、それぞれ誤判定なしで識別できるaddon/contextを確認してから自動拒否へ追加する。

---

## 22. 非目標

本リリースでは以下を実装しない。

- 南方ボズヤ戦線/ザトゥノル高原へのフィールド外からの自動入場
- Ban/検知回避機能
- BOCCHI upstreamの自動マージ
- Cache/Holsterの直接メモリ改変
- ボズヤ敵ランクの推測による判定
- UIの多言語対応（ユーザーUIは日本語固定）

## 23. Done定義

最終的に利用者が対応フィールド内で `Start` を押した後、通常操作なしで以下の循環が成立することをDoneとする。

```text
必要時Initialize
  -> 生存構成確認
  -> Activity選択
  -> BOCCHI式経路選択
  -> 危険敵回避
  -> スカーミッシュ戦闘
  -> CE遠隔申請
  -> CE当選時即突入
  -> 戦闘
  -> Maintenance / 必要時補給
  -> 死亡時自動復旧
  -> 次Activity
```

Test受入条件を満たし、ユーザーの明示承認を得た時点で `1.1.0.0` をmainへmergeする。
