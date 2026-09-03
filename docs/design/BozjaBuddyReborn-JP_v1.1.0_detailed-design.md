# BozjaBuddyReborn-JP v1.1.0 詳細設計

作成日: 2026-09-01  
対象ブランチ: `feat/bocchi-navigation`  
上位文書: `docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md`

## 0. この文書の目的

この文書は、v1.1.0 の実装を「長時間の一括実装」ではなく、短い実装セッションに分割して安全に進めるための詳細設計である。

原則:

- 1つの実装タスクは 1 セッションで完結できる大きさにする。
- 1タスクにつき原則1コミットとする。
- 各コミット後に少なくともビルドを通す。
- 実機確認が必要な機能は、推測で完成扱いしない。
- `main` は安定版として維持し、実装・検証は `feat/bocchi-navigation` 上で行う。
- Stable受入条件を満たし、ユーザー承認を得るまで `main` へmergeしない。

## 1. 現在の実装状態

`feat/bocchi-navigation` には既に基礎実装が存在する。以下は「土台」であり、v1.1.0 完成を意味しない。

### 1.1 実装済み/基礎実装あり

- AGPL-3.0 へのライセンス切替と第三者notice更新
- Test build用workflow / test manifest / test ZIP 配布の基礎
- `External/LifestreamIpc.cs`
- `Game/FieldAethernet.cs`
- `Vendor/BOCCHI/NavigationConstants.cs`
- `Automation/FieldTravelRouter.cs`
  - Direct
  - Walk -> Aethernet -> Walk
  - Lifestream 1回retry後fallback
- `Game/EnemyStrengthResolver.cs`
  - I〜V/★の論理型
  - 名前/地域テーブルによる暫定分類
  - raw nameplate icon pair の診断/学習基盤
  - unknown=危険
- `Automation/AggroAvoidance.cs` の敵ランク連携基礎
- `Game/SurvivalPolicy.cs`
  - Tank / Healer / DPS
  - HP閾値
  - Essence優先順位
  - Deep系デフォルトOFF
  - Lost Action持込/自動使用permission
- `Automation/HolsterDriver.cs`
  - マウント中は自動Lost Actionを発動しない
  - Potion Kit維持
  - HP閾値に応じた回復/緊急Action選択
  - 徒歩移動中は詠唱Actionを避ける
- `Configuration.cs` のv1.1設定フィールド基礎
- 一部UI日本語化とv1.1設定表示

### 1.2 未完/暫定実装

以下は完成扱いしない。

1. BOCCHIナビの完全統合
   - 現 `FieldTravelRouter` は BOCCHI のコスト定数を使うが、walk leg は水平距離で近似している。
   - BOCCHIのZoneGraph / TraversalCandidate / ReturnTeleportWalk相当を十分に取り込めていない。
   - `UseReturnRouting` 設定は存在するが、Return経路は未実装。

2. 敵ランクの確定取得
   - 現 `EnemyStrengthResolver` の名前/地域テーブルは安全な暫定策。
   - `NamePlateIconId` / `CharacterData.Icon` 等の実機値と I〜V/★の直接対応を確定していない。

3. Lost Finds Cache <-> Holster 自動転送
   - 読み取り基盤は利用可能だが、安全な転送callback/game functionが未確定。
   - Initialize / rollback / 差分補給は未完成。

4. CE/Relicの最終Activity選択
   - 大規模戦闘最優先
   - Relic明示Farm対象
   - PriorityEngagements
   - 通常CE
   - Farm完了後の自動継続
   - Farm対象不在時の最適aetheryte待機
   を1つの一貫したselectorへ統合する必要がある。

5. 死亡復旧
   - TextAdvance一時enable/restore
   - CE中はreleaseしない
   - skirmish 30秒 / travel 10秒 Raise待ち
   - respawn後Maintenance/再開
   が未完成。

6. Dependency復旧state
   - 必須依存60秒待機
   - Lifestream 30秒/即fallbackの場面分け
   - 60秒後の安全停止
   が未完成。

7. ソーシャル要求拒否
   - Party / CWPT / Alliance / Friend / LS / CWLS / Trade等を「識別できた場合のみ」拒否する処理が未完成。

8. UI/Diagnostics
   - 6カテゴリ再編
   - メイン診断情報
   - Warning履歴
   - 診断情報copy
   - test-only feature flags
   - debug world overlay
   が未完成。

9. Config migration / BOCCHI upstream monitor / Stable acceptance
   - 未完成。

## 2. アーキテクチャ方針

### 2.1 BozjaController はオーケストレータに限定する

`Automation/BozjaController.cs` に新規ロジックを直接積み続けない。

Controllerの責務:

- 現在stateの決定
- 各subsystemのTick呼び出し
- state transition
- Stop/Pause/Resume
- 現在Activityのcommit/release

Controllerに持たせない責務:

- 経路コスト計算
- Cache/Holster transfer protocol
- Lost Actionプリセット選択
- Dependency timeout policy
- Social dialog text判定
- Relic素材の次ターゲット決定

これらは独立classに分ける。

### 2.2 推奨モジュール構成

```text
Automation/
  BozjaController.cs              # orchestration only
  ActivityPlanner.cs              # CE/Skirmish/Relic/large-scale priority
  FieldTravelRouter.cs            # high-level traversal state machine
  Movement.cs                     # actual vnavmesh execution + stall + detour
  AggroAvoidance.cs               # dangerous footprint detours
  InitializationCoordinator.cs    # Start -> preflight -> initialize
  HolsterTransaction.cs           # snapshot/return/refill/rollback
  SupplyManager.cs                # low-watermark + differential refill
  DeathRecovery.cs                # raise/release/TextAdvance state
  DependencySupervisor.cs         # required/optional dependency state
  SocialRequestGuard.cs           # identified social request rejection
  DiagnosticsRecorder.cs          # state/warning history

Game/
  EnemyStrengthResolver.cs
  FieldAethernet.cs
  SurvivalPolicy.cs
  LostActionCatalog.cs
  HolsterInventory.cs             # cache/holster snapshot abstraction
  RelicFarmPlanner.cs             # current farm + next material logic

External/
  LifestreamIpc.cs
  TextAdvanceIpc.cs
  ...existing IPCs

Vendor/BOCCHI/
  NavigationConstants.cs
  <required graph/traversal sources only>
```

既存ファイル名と衝突する場合は無理にこの名称へrenameしない。責務境界を守ることを優先する。

## 3. Runner State Model

既存stateを尊重しつつ、内部的には以下の状態を表現できる必要がある。

```text
Stopped
Starting
DependencyWait
NavigateToCache
Initializing
SelectingActivity
Travelling
WaitingForActivity
EngagedSkirmish
CeRegistered
CeSelected
EngagedCe
Maintenance
Supplying
DeadWaitingRaise
DeadRespawning
Recovering
StoppingSafely
PausedByManualInput
```

すべてを公開enumにする必要はない。重要なのは「長い処理を1Tickで完結させない」こと。

### 3.1 1 Tick 原則

UI callback、zoning、Lifestream、holster load、dependency復帰待ち等は非同期に状態が変わる。

禁止:

```csharp
DoStep1();
Thread.Sleep(...);
DoStep2();
while (!Complete) { ... }
```

推奨:

```csharp
switch (_phase)
{
    case Phase.Start:
        IssueOperation();
        _phase = Phase.WaitResult;
        return;
    case Phase.WaitResult:
        if (!Complete()) return;
        _phase = Phase.Next;
        return;
}
```

## 4. Activity Planner 詳細設計

### 4.1 入力

- territory
- CE一覧と状態
- skirmish一覧、進行度、残時間
- `EngageLargeScale`
- `FarmMaterialItemId`
- relic current stage / outstanding materials
- `PriorityEngagements`
- blacklist
- navigation estimated cost
- current objective / sticky state

### 4.2 CE優先順位

上から評価する。

1. Large-scale (`EngageLargeScale == true`)
2. 明示Relic Farm対象CE
3. `PriorityEngagements`
4. その他CE

同時登録は1CEのみ。

Registration中は通常skirmish行動を継続する。当選/selected状態を検出したら原則即Commence。

### 4.3 Skirmish選択

Farmなし:

- progress < `NewSkirmishMaxProgress` (default 80)
- blacklistでない
- 残時間内に到達可能
- navigation estimated cost最小

Farmあり:

- 指定素材をdropするActivityだけ候補
- 候補なしなら無関係なskirmishをしない
- 次spawnに有利なaetheryteへstage

### 4.4 Relic素材完了

指定素材必要数達成時:

1. 次の不足素材を求める。
2. 現Activityが次素材にも有効なら続行。
3. 無効ならcurrent objectiveをreleaseして再select。
4. 現territory内に次素材候補がなければStop。
5. territory間自動移動はしない。

停止モード:

- Infinite (default)
- StopAtSelectedMaterial
- StopAtCurrentRelicStage
- ContinueToNextMaterial

## 5. Navigation 詳細設計

### 5.1 責務分離

`FieldTravelRouter`:

- route候補生成
- route cost比較
- leg state管理
- Lifestream / Return leg発行
- diagnostics route mode

`Movement`:

- 指定座標への実移動
- vnavmesh path
- mount
- stall detection
- dangerous enemy detour
- manual-input yield

### 5.2 BOCCHI取り込み方針

現行の水平距離近似は暫定。

取り込む候補:

- NavigationConstants
- graph node / edge の最小セット
- WalkTeleportWalkCalculator相当
- ReturnTeleportWalkCalculator相当
- TraversalCandidate / PathStep相当

ただしBOCCHI全体をsubmodule依存にはしない。

`Vendor/BOCCHI` に原コードをできるだけ保った状態で置き、BBR固有の変換はAdapter側へ書く。

### 5.3 Route候補

最低3候補:

```text
Direct:
  path(start, goal)

WalkTeleportWalk:
  path(start, departure)
  + AethernetHopCost
  + path(inbound, goal)

ReturnTeleportWalk:
  ReturnCost
  + base camp transition
  + optional AethernetHopCost
  + path(inbound, goal)
```

`UseReturnRouting == false` の場合Return候補を生成しない。

### 5.4 Route failure

- Lifestream: initial + 1 retry
- vnavmesh same leg: 最大3再計算
- 3回失敗: 別route候補を再plan
- 全route失敗: current activityをspawn消滅までblacklist
- 全Activity失敗: staging aetheryteへ移動してspawn待ち

### 5.5 Lifestream unavailable

時間制約あり（eventへ移動中）:

- 即Direct fallback

時間制約弱い（supply / waiting）:

- 最大30秒待つ
- それでも不可ならDirect fallback

### 5.6 Manual input

移動入力を検知したらnavigationをyield。

- vnavmesh path stop/hold
- current route planは保持
- 最終入力から3秒でroute再plan/再開
- target変更は無視

## 6. Dangerous Enemy 詳細設計

### 6.1 最終判定

- I / II / III: safe-for-routing
- IV / V / ★: dangerous
- Unknown: dangerous

★だけ `DangerStarExtraClearance` を加える。

### 6.2 実データ確定手順

現在の名前/region表はfallbackとして残す。

Test buildで以下を1回だけ英語ログへ記録する。

```text
territory
BNpcName row id
English name
region
NamePlateIconId
CharacterData.Icon
resolved strength
source: direct-icon / learned-icon / name-fallback / unknown
```

実機で I〜V/★ をそれぞれ複数体観測し、raw fieldが一意に対応することを確認できたらdirect mappingを第一優先へ変更する。

一意性を証明できない場合は名前/region fallbackを維持する。

推測だけで `BNpcBase.Rank` 等へ置換しない。

### 6.3 Mounted safety

`Mount.IsMounted == true` の間:

- survival Lost Actionを発動しない
- combat actionをBBR側から発動しない
- aggro時もFightBackへ切り替えない（要件上KeepRunning）

Aethernetを使うために明示的にdismountする場合だけ例外。

## 7. Survival / Lost Action 詳細設計

### 7.1 Role

- Tank
- Healer
- DPS

閾値default:

| Role | Heal | Emergency |
|---|---:|---:|
| Tank | 0.55 | 0.30 |
| Healer | 0.70 | 0.45 |
| DPS | 0.65 | 0.40 |

### 7.2 Permission model

各MYCTemporaryItem rowごとに独立:

```text
BringAllowed
AutoUseAllowed
```

missing keyのdefault:

- Deep Essence: false / false
- その他生存候補: true / true

### 7.3 Essence

Tank:

1. Deep Bloodsucker
2. Bloodsucker
3. Deep Guardian
4. Guardian

Healer:

1. Deep Templar
2. Templar
3. Deep Veteran
4. Veteran

DPS:

1. Deep Beast
2. Beast
3. Deep Platebearer
4. Platebearer
5. Deep Veteran
6. Veteran

既存Essence上書きdefault OFF。

### 7.4 Automatic survival

自然にunmounted時:

1. Potion Kit buffなし -> permissionありなら適用
2. HP <= Emergency -> emergency priority
3. HP <= Heal -> heal priority
4. otherwise no action

マウント中は何もしない。

徒歩移動中は詠唱Actionを候補から除外する。

### 7.5 Initialize transaction

新規 `HolsterTransaction` 相当を導入する。

Phase:

```text
Snapshot
PreflightReturn
ReturnAll
VerifyEmptyOrExpected
BuildPlan
TransferDesiredItems
VerifyHolster
SetDutySlots
ApplyOptionalBuffs
Commit
```

失敗:

```text
RollbackPlan
RestoreTransferredItems
RestoreDutySlots
VerifyRollback
```

rollback失敗のみStart拒否。

直接memory mutationは禁止。

### 7.6 Inventory abstraction

transfer方式が確定するまで、CoordinatorからClientStructs/UI callbackへ直接触らない。

```csharp
interface IHolsterInventory
{
    HolsterSnapshot Capture();
    bool CanReturnAll(...);
    TransferOperation BeginReturn(...);
    TransferOperation BeginWithdraw(...);
}
```

実際のUI/game functionをAdapterに閉じ込める。

## 8. Supply Manager 詳細設計

### 8.1 Low-watermark

初期値:

- Potion Kit 2
- Reraiser 1
- main heal: 5 uses相当
- emergency defense: 1 set相当

### 8.2 Supply urgency

NormalLow:

- current activity完走後にSupply

CriticalEmpty:

```text
Potion Kit == 0
AND usable self-heal Lost Action == 0
```

- current skirmishを中断
- 即Supplyへ

Cacheにも無し:

- CriticalEmpty -> Stop
- それ以外 -> warning + continue

### 8.3 Differential refill

通常Supplyでは全返却しない。

- current count
- target count
- deficit

のみwithdrawする。

欠品はinstance scoped cacheへ入れ、強制Initializeでclearする。

## 9. Death Recovery 詳細設計

### 9.1 State

```text
Alive
DeadWaitingRaise
RespawnRequested
WaitingForZoneReady
Maintenance
```

### 9.2 CE

CE中:

- event終了まではreleaseしない
- Raise UIが来た場合はTextAdvanceに任せる

### 9.3 Skirmish / Travel

- skirmish: 30秒 Raise待ち
- travel: 10秒 Raise待ち
- timeout後 respawn

### 9.4 TextAdvance integration

必要な情報:

- installed/IPC available
- enabled state
- enable/disable method or safe command fallback

死亡時:

1. original enabled state snapshot
2. disabledならtemporary enable
3. Raise/respawn processingをTextAdvanceへ委譲
4. alive + UI settled後original stateへrestore

TextAdvanceなしで自動respawnできない場合:

- user-visible Japanese error
- English log
- Stop

## 10. Dependency Supervisor 詳細設計

Required:

- vnavmesh
- RSR
- BossMod/BossMod Reborn

Optional:

- Lifestream
- TextAdvance

Requiredが消失:

```text
start timer = 60s
continue survival Lost Action if possible
wait for restore
```

60秒以内:

- previous stateへresume

60秒超:

- safeにcampへ戻せる -> return/camp then Stop
- combat中 -> fight/death resolutionまでsurvival継続 -> Stop
- recovery不能 -> immediate Stop

## 11. Social Request Guard 詳細設計

Running中のみ有効。

対象:

- Party
- Cross-world Party
- Alliance
- Friend
- Linkshell
- CWLS
- Trade
- その他「対人要求」と明確に識別できるもの

原則:

- addon名だけでNoを押さない
- generic `SelectYesno` へ常時Noを送らない
- text/game state/招待source等を組み合わせてstrict match
- strict match不能なら触らない

拒否時:

- notification不要
- warning/historyへ1行
- logは英語

## 12. UI / Diagnostics 詳細設計

### 12.1 Main

常時表示:

- State
- Destination
- Next action
- HP / Role
- Essence
- Potion Kit
- Reraiser
- CE registration/selection
- Route mode

### 12.2 Tabs

```text
周回
生存
ロストアクション
移動
Relic
詳細設定
```

全UI文字列は日本語固定。

ログは英語固定。

### 12.3 Warning history

- Main: latest 1
- Diagnostics: recent N items
- auto recovered issueはYellow warningとして残す

### 12.4 Diagnostics copy

clipboard出力には最低限:

```text
Plugin/Test version
Territory
State
Current objective
Route mode
Route detail
Player role/HP/mounted
CE status
Dependency status
Duty actions
Holster summary
Survival thresholds
Last warnings
Last state transitions
Enemy resolver diagnostic summary
```

### 12.5 Test-only controls

Test buildのみ:

- Force Initialize
- Replan Route
- Blacklist Current Activity
- Supply Now
- feature flags
- debug overlay

Stableに残す:

- Force Initialize
- Legacy Movement

## 13. Configuration / Migration 詳細設計

現在の `Configuration.Version` からv1.1用versionへmigration functionを明示的に作る。

原則:

- existing valuesを維持
- `AllowFlight` はcompat fieldとして読めるが実行時は使用しない
- 新規値のみdefaultを補う
- migration失敗時は旧設定backup後、新規default

Character-specific stateとglobal configを分離する。

Global:

- HP thresholds
- Lost Action permissions
- navigation constants
- debug settings

Character:

- selected relic farm target
- relic automation continuation state
- character-specific learned/progress stateが必要なもの

## 14. Version / Test Repository

Test:

```text
1.0.90.1
1.0.90.2
...
```

Stable:

```text
1.1.0.0
```

Build時に必ず同一値へ同期:

- csproj Version
- AssemblyVersion
- FileVersion
- plugin manifest AssemblyVersion
- TestingAssemblyVersion if used
- ZIP metadata

Test repoとStable repoは同一InternalNameなので同時利用させない。

## 15. Logging rules

ユーザー表示:

- 日本語

`Svc.Log`:

- 英語

悪い例:

```csharp
Svc.Log.Warning(Loc.T("Lifestream failed", "簡易テレポ失敗"));
```

良い例:

```csharp
Svc.Log.Warning("[BozjaBuddyReborn] Lifestream aethernet teleport failed; falling back to direct travel.");
Status = "簡易テレポに失敗したため直接移動へ切り替えました。";
```

## 16. StableのDefinition of Done

上位要件の受入条件をすべて満たすこと。

特に以下はCIだけではDoneにしない。

- 敵IV/V/★判定
- Aethernet/Return経路
- Cache/Holster Initialize/rollback
- 差分補給
- CE remote registration/commence
- death recovery
- social request rejection

これらはTest Custom Repoから実機で確認し、結果を記録する。

## 17. 実装時の禁止事項

- `main` へ直接実験コードを書かない。
- Cache/Holster数値を直接メモリ書換えしない。
- ボズヤ敵ランクを `npc.Level` で代用しない。
- 未検証のゲーム内部fieldを事実として扱わない。
- generic `SelectYesno` を無条件でNoにしない。
- マウント移動中に生存Actionを発動しない。
- 現在動作確認済みのskirmish combatを大規模に書き直さない。
- BOCCHI由来コードのlicense/header/noticeを落とさない。
- test versionをstable versionより大きくしてstableへ戻せなくしない。

## 18. 実装者向け開始手順

新しいセッションは必ず以下から開始する。

1. `docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md` を読む。
2. 本書を読む。
3. `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_execution-plan.md` から未完了の最小タスクを1つ選ぶ。
4. そのタスクに関係するファイルだけ読む。
5. 実装。
6. build/CI。
7. progress trackerを更新。
8. 1コミットで終了。

巨大な「v1.1全部実装」を1セッションで試みない。