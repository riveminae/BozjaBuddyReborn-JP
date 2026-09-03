# BozjaBuddyReborn-JP v1.1.0 実装手順書

対象: `feat/bocchi-navigation`  
上位文書:

- `docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md`
- `docs/design/BozjaBuddyReborn-JP_v1.1.0_detailed-design.md`

## 0. 実行ルール

この手順書は、AI実装が長時間化してタイムアウトすることを前提としている。

### 0.1 1回の作業単位

原則として **Task Packet 1個だけ** を1回のセッションで実施する。

1 Packetの終了条件:

1. 対象範囲以外を極力変更しない。
2. ローカルまたはGitHub Actionsでbuild結果を確認する。
3. `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_progress.md` を更新する。
4. 1つの意味のあるcommitを作る。
5. 次Packetを開始せず終了する。

### 0.2 実機が必要なPacket

コードだけでは完了できないPacketには `[LIVE]` を付ける。

その場合:

- build成功までAI側で行う。
- Test Custom Repoへpublishする。
- progressを `WAITING_LIVE_TEST` にする。
- 実機結果が来るまで推測で次の危険な依存taskへ進まない。

### 0.3 調査だけのPacket

ゲーム内部仕様を確定するPacketには `[RESEARCH]` を付ける。

調査結果は必ず `docs/research/` に残す。コードに推測を埋め込んで終わらせない。

---

# Phase 0: Baseline固定

## P0-01 現branch baseline audit

目的: 途中実装を壊さず、現在地点を記録する。

対象:

- `feat/bocchi-navigation`
- `main`との差分
- `.github/workflows/test-build.yml`

手順:

1. `main...feat/bocchi-navigation` のdiff一覧を取得。
2. 追加/変更ファイルをprogress trackerへ記録。
3. current test versionを確認。
4. build workflowを起動または最新runを確認。
5. compile errorがある場合は、機能追加をせずcompile fixだけ行う。

Done:

- current branchがbuild可能。
- baseline commit SHAがprogressに記載されている。

---

# Phase 1: 配布・ライセンス・version基盤

## P1-01 ライセンス検証

対象:

- `LICENSE`
- `THIRD-PARTY-NOTICES.md`
- `Vendor/BOCCHI/*`
- Ocelot由来sourceがある場合そのheader

手順:

1. fork全体AGPL-3.0であることを確認。
2. 元BBR MIT notice保持確認。
3. BOCCHI AGPL attribution確認。
4. Ocelotコードを直接含む場合MIT notice確認。
5. sourceごとの由来コメントを補う。

Done:

- license conflictなし。
- vendored codeの由来が追跡可能。

## P1-02 Test versionを1.0.90.xへ統一

対象:

- `BozjaBuddyReborn.csproj`
- `BozjaBuddyReborn.json`
- `.github/workflows/test-build.yml`
- `pluginmaster-test.json`

手順:

1. test version source-of-truthを1箇所へ寄せる。
2. AssemblyVersion/FileVersion/manifestを同じ値にする。
3. workflowでhard-codeされた旧1.0.28.1等を除去。
4. ZIP名/DownloadLinkをtest branch用へ確認。

Done:

- test build manifestが現在DLL versionと一致。
- Dalamudが前test buildからupdate判定できる。

---

# Phase 2: BOCCHI Navigation完成

## P2-01 Vendored BOCCHI traversal model

目的: 現在の「BOCCHI定数 + 水平距離近似」を本来のTraversalCandidate構造へ寄せる。

対象候補:

- `Vendor/BOCCHI/NavigationConstants.cs`
- 新規 `Vendor/BOCCHI/PathStep.cs`
- 新規 `Vendor/BOCCHI/TraversalCandidate.cs`
- 新規 `Vendor/BOCCHI/WalkTeleportWalkCalculator.cs`
- 必要最小限のNode/metadata
- `Automation/FieldTravelRouter.cs`

手順:

1. BOCCHI upstreamの対象commitを固定して記録。
2. 必要sourceをできる限り原形のままVendorへ追加。
3. namespaceのみBBR向けへ調整。
4. BBR固有データ変換はAdapterへ置く。
5. 既存 `Movement` はwalking executorとして残す。

禁止:

- BOCCHIを見ながら同等ロジックをゼロから書き直さない。
- BOCCHI全repoをProjectReferenceしない。

Done:

- DirectとWalkTeleportWalkが同じcandidate/cost modelで比較される。
- build成功。

## P2-02 ReturnTeleportWalk実装

対象:

- vendored `ReturnTeleportWalkCalculator`相当
- `Automation/FieldTravelRouter.cs`
- Return実行用game/API wrapper

手順:

1. `UseReturnRouting` OFFならcandidate非生成。
2. base camp近傍ではReturn候補を抑止。
3. `ReturnCost`をcostへ加算。
4. Return -> optional aethernet -> walkをleg化。
5. zoning/return完了をstate machineで待つ。
6. Return失敗時は別candidateへfallback。

Done:

- router diagnosticsにReturn routeが出せる。
- direct/aethernet/returnの3候補比較が可能。

## P2-03 Route retry / blacklist完成

対象:

- `FieldTravelRouter.cs`
- `Movement.cs`
- `BozjaController.cs`
- blacklist holder

手順:

1. 同一legのpath失敗をcount。
2. 3回でroute replan。
3. 全route失敗でActivity blacklist。
4. blacklist lifetimeをcurrent spawn消滅までにする。
5. 全候補blacklist時はstagingへ。

Done:

- 1つの地形不良で無限retryしない。

## P2-04 Manual movement yield

対象:

- `Movement.cs`
- input service
- controller/router

手順:

1. manual movement input timestampを取得。
2. input中vnav制御をyield。
3. 3秒inputなしでreplan/resume。
4. manual target変更は無視。

Done:

- userとvnavmeshが綱引きしない。

---

# Phase 3: 敵ランク確定

## P3-01 [RESEARCH][LIVE] I〜V/★ raw field採取

目的: `EnemyStrengthResolver` の暫定name fallbackを実機データで補強する。

対象:

- `Game/EnemyStrengthResolver.cs`
- Diagnostics UI/log
- `docs/research/bozja-enemy-strength.md`

実装:

1. unseen raw pairを1回だけlog。
2. logに以下を含める。
   - territory
   - nameId
   - English name
   - region
   - NamePlateIconId
   - CharacterData.Icon
   - provisional strength
3. Debug画面にも一覧表示できれば追加。

実機試験:

- I/II/III/IV/V/★を南方・ザトゥノルで採取。

Done:

- raw fieldと表示rankの対応表がresearch docへ残る。

## P3-02 Direct rank mapping採用判断

前提: P3-01完了。

手順:

1. raw fieldがrankを一意に識別できるか検証。
2. 一意ならdirect mappingを第一優先。
3. 一意でなければname/region fallbackを維持。
4. unknownは常にdangerous。

Done:

- 推測ではなく根拠付きrank resolver。

## P3-03 Danger overlay / ★margin

対象:

- `AggroAvoidance.cs`
- Debug overlay
- `Configuration.cs`

Done:

- IV/V/unknown footprint表示。
- ★は `DangerStarExtraClearance` を加算。
- I〜IIIはroute detourしない。

---

# Phase 4: Activity / CE / Relic Planner

## P4-01 CE remote registration state整理

対象:

- `SignUpRunner.cs`
- `CriticalEngagements.cs`
- `BozjaController.cs`

手順:

1. CE registrationに現地移動を要求しない。
2. 参加希望中もskirmish selection/travel/combat継続。
3. selected/commence状態は最優先で処理。
4. 当選時原則即 `戦闘突入`。
5. 同時登録は1CEのみ。

Done:

- remote signup -> skirmish継続 -> commenceのstateが分離される。

## P4-02 ActivityPlanner抽出

新規推奨:

- `Automation/ActivityPlanner.cs`

優先順:

1. enabled large-scale
2. selected Relic target CE
3. PriorityEngagements
4. other CE
5. matching skirmish / normal skirmish

Skirmish:

- new target progress < 80%
- travel ETA/costで到達可能
- blacklist除外

Done:

- `TargetSelector`/Controllerの条件分岐が一貫したplannerへ集約。

## P4-03 RelicFarmPlanner

対象:

- `Relic/*`
- 新規 `Game/RelicFarmPlanner.cs` 等
- config character state

手順:

1. 最初のtargetは手動のみ。
2. material必要数達成を検出。
3. next outstanding materialを算出。
4. same zone/activityならcurrent継続。
5. unrelatedならcurrent中断。
6. current territoryに取得候補なしならStop。
7. territory外移動はしない。

Done:

- Relic進捗に応じる自動継続が可能。

## P4-04 Farm target不在時 staging

手順:

1. matching spawnが0ならunrelated skirmishを選ばない。
2. 次target regionに最も有利なFieldAethernet nodeを選ぶ。
3. そこへ移動してwait。

Done:

- farm中に無関係イベントへ流れない。

---

# Phase 5: Lost Finds Cache / Holster転送

## P5-01 [RESEARCH] Cache/Holster UI transfer特定

最重要blocker。

対象調査:

- FFXIVClientStructs `AgentMycItemBox`
- `MycItemBoxData`
- Lost Finds Cache addon callback
- public Dalamud plugin source

手順:

1. 実在inventory read構造をdocument。
2. 「Cache -> Holster 1個移動」のUI callback/functionを特定。
3. 「Holster -> Cache 1個返却」を特定。
4. count/row/slot引数を確認。
5. server acknowledgmentを何で判定するか決定。
6. `docs/research/lost-finds-cache-transfer.md`へ記録。

禁止:

- `MycItem.Count`等の直接memory write。
- callback引数の当てずっぽう。

Done:

- 1個の手動操作と同等の安全なprogrammatic transfer方法が根拠付きで確定。

## P5-02 HolsterInventory abstraction

前提: P5-01完了。

新規推奨:

- `Game/HolsterInventory.cs`

API例:

```csharp
Capture()
GetCacheCount(row)
GetHolsterCount(row)
BeginReturn(row, count)
BeginWithdraw(row, count)
IsTransferSettled(...)
```

Done:

- transfer detailsがcontrollerから隠蔽される。

## P5-03 Initialize planner

新規推奨:

- `Automation/InitializationCoordinator.cs`
- `Automation/HolsterTransaction.cs`

実装:

- snapshot
- preflight
- return all
- role policy
- bring permission
- target counts
- duty slots
- essence overwrite policy

このPacketではrollbackまで実装しない。正常系だけstate machine化してbuild。

## P5-04 Initialize rollback

手順:

1. failure injection pointを作る/test-only。
2. snapshot構成へrestore。
3. duty slot restore。
4. verify。
5. rollback失敗のみStart拒否。

Done:

- 中途半端な空HolsterでStartしない。

---

# Phase 6: Supply Manager

## P6-01 Low-watermark model

対象:

- config
- survival policy
- supply manager

default:

- Potion Kit 2
- Reraiser 1
- heal uses 5
- emergency defense 1 set

Done:

- `NormalLow`, `CriticalEmpty`, `Healthy` を判定可能。

## P6-02 Differential refill

前提: P5 transfer完成。

手順:

1. desired - current = deficit。
2. deficitのみwithdraw。
3. missing cache itemをinstance missing cacheへ。
4. force initializeでmissing cache clear。

Done:

- 通常補給で全Holsterを空にしない。

## P6-03 Supply vs CE arbitration

- supply中もCE registration継続
- CE selected + survival available -> immediate commence
- CE selected + CriticalEmpty -> minimum refill first

Done:

- dangerous intermediate stateでCEへ飛ばない。

---

# Phase 7: Survival仕上げ

## P7-01 Reraiser once-per-risk-window

現在 `SurvivalPolicy` / `HolsterDriver` にpriority基礎あり。

追加:

- emergency thresholdを初めてcrossした時だけReraiser候補。
- 既にreraiser statusありなら再消費しない。
- mounted中は保留。

## P7-02 Essence Initialize integration

- current essenceあり + overwrite OFF -> preserve
- overwrite ON -> role priority
- Deep permission OFFならskip

## P7-03 Mounted invariant test

unit/logic test可能なら追加。

Invariant:

```text
Mount.IsMounted => BBR survival driver never issues Lost Action
```

Done:

- route中のactionで勝手にdismountしない。

---

# Phase 8: Death Recovery / TextAdvance

## P8-01 TextAdvance IPC wrapper

新規:

- `External/TextAdvanceIpc.cs`

必要API:

- availability
- enabled state
- temporary enable
- restore

直接IPCでenable不可ならsafe command fallbackをwrapper内部だけで使用。

## P8-02 DeathRecovery state machine

新規:

- `Automation/DeathRecovery.cs`

ルール:

- CE: event endまでrelease禁止
- skirmish: 30秒 raise wait
- travel: 10秒 raise wait

Done:

- controllerからdeath branchを分離。

## P8-03 [LIVE] TextAdvance recovery実機試験

ケース:

1. TextAdvance元ON -> death -> raise -> ONのまま
2. 元OFF -> temporary ON -> recovery -> OFFへ戻る
3. TextAdvance absent -> Japanese error + Stop
4. CE death -> camp releaseしない
5. travel death -> timeout release

結果をprogressへ記録。

---

# Phase 9: Dependency Supervisor

## P9-01 Dependency state abstraction

新規:

- `Automation/DependencySupervisor.cs`

状態:

```text
Healthy
WaitingRequired
OptionalUnavailable
TimedOut
```

## P9-02 required dependency 60s recovery

- vnavmesh
- RSR
- BossMod/Reborn

behavior:

- 60s timer
- survivalは可能な範囲で継続
- restored -> resume previous state

## P9-03 timeout safe stop

60秒超:

- safe camp return可能 -> camp -> Stop
- combat -> survive until resolve -> Stop
- impossible -> Stop

## P9-04 Lifestream optional policy

- event travel -> immediate direct fallback
- supply/wait -> max30s wait -> fallback

---

# Phase 10: Social Request Guard

## P10-01 [RESEARCH] 対象dialog識別表

`docs/research/social-request-dialogs.md`

対象:

- Party
- CWPT
- Alliance
- Friend
- LS
- CWLS
- Trade

各対象について:

- addon name
- JP text/pattern
- discriminator
- No callback/action

を記録。

## P10-02 Strict reject implementation

新規:

- `Automation/SocialRequestGuard.cs`

ルール:

- Running中のみ
- strict matchのみreject
- generic YesNoは触らない
- reject history記録

## P10-03 [LIVE] false positive test

必ず確認:

- ordinary confirmation dialogを拒否しない
- CE commence等を拒否しない
- party inviteは拒否

---

# Phase 11: UI / Diagnostics / Localization

## P11-01 UI tab再編

最終構成:

- 周回
- 生存
- ロストアクション
- 移動
- Relic
- 詳細設定

既存機能を消さず配置換え。

## P11-02 Main status card

表示:

- State
- Destination
- Next action
- HP/role
- Essence
- Potion Kit/Reraiser
- CE state
- Route mode
- latest warning

## P11-03 DiagnosticsRecorder

- state transition ring buffer
- warning ring buffer
- dependency summary
- route summary
- holster summary

## P11-04 Clipboard diagnostics

`診断情報をコピー`。

個人情報/チャット内容等を混ぜない。

## P11-05 Debug world overlay

Test only default OFF:

- route
- selected aetheryte
- dangerous footprint
- destination

## P11-06 全visible English洗い出し

対象:

- MainWindow
- ConfigWindow
- Relic window
- Dependencies
- status/error

ユーザーvisibleは日本語固定。

`Svc.Log`は英語。

---

# Phase 12: Config migration

## P12-01 Migration function

- old config Version -> new version
- existing user choices preserve
- new fields default
- AllowFlight compatibility only

## P12-02 Character state split

Relic farm selection/continuationをcharacter-specific storeへ。

Global permissions/thresholdは共通。

## P12-03 Migration failure backup

- old config backup
- error English log
- Japanese notification
- safe default config

---

# Phase 13: BOCCHI upstream monitor

## P13-01 Weekly workflow

新規:

- `.github/workflows/check-bocchi-upstream.yml`

週1回:

1. pinned BOCCHI filesのupstream SHAを確認。
2. vendored sourceとの差分有無を判定。
3. 差分あり時のみIssue作成。
4. auto mergeしない。

Done:

- no-change時にIssue spamしない。

---

# Phase 14: Test repository / stable fallback

## P14-01 Test manifest publish確認

- test branch raw URL
- ZIP availability
- version update

## P14-02 Stableへ戻す案内

Test UI/READMEに:

1. Test repo disable/remove
2. Stable repo enable
3. Plugin update/reinstall

を日本語表示。

---

# Phase 15: Stable acceptance

## P15-01 [LIVE] 南方受入

記録:

- 3 consecutive skirmishes
- 1 CE registration/selection/commence/end/recovery
- aethernet route
- danger avoidance

## P15-02 [LIVE] ザトゥノル受入

同上。

## P15-03 [LIVE] Cross-cutting acceptance

- Initialize
- rollback
- refill
- low-watermark
- death recovery
- Lifestream fallback
- vnav stall recovery
- dependency recovery
- social guard
- Japanese UI / English logs
- migration
- test update

## P15-04 Release candidate review

1. progress trackerのBLOCKED/WAITINGを0にする。
2. branch build成功。
3. mainとの差分review。
4. release notes作成。
5. ユーザーへ実機結果を提示。
6. **明示OKが出るまでmainへmergeしない。**

---

# 実装者へ渡す短縮プロンプト

タイムアウトしやすい環境では、次の形で指示する。

```text
feat/bocchi-navigation で作業してください。
docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md
docs/design/BozjaBuddyReborn-JP_v1.1.0_detailed-design.md
docs/implementation/BozjaBuddyReborn-JP_v1.1.0_execution-plan.md
docs/implementation/BozjaBuddyReborn-JP_v1.1.0_progress.md
を読んでください。

progress上の次のTODO Task Packetを1個だけ実装してください。
範囲を広げないでください。
実装後build/CIを確認し、progressを更新して1コミットしてください。
mainへmergeしないでください。
```

これを各セッションの入口とする。