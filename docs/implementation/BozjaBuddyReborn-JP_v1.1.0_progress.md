# BozjaBuddyReborn-JP v1.1.0 実装進捗

最終更新: 2026-09-02  
branch: `feat/bocchi-navigation`  
main baseline: `038faf8d70b2aea7189143f7fd46a8c135cb0484`  
最新CI検証commit: `04c701acc45e0f8d9c6de0d3810f427f40e330db`  
最新CI検証Test version: `1.0.90.151`  
Test version生成: GitHub Actions run numberから `1.0.90.x` を自動採番

## ステータス定義

- `DONE`: コード/静的検証/CIで完了できる範囲を完了
- `PARTIAL`: コード基礎あり。要件を満たすには追加実装または最終確認が必要
- `TODO`: 未着手または完成コードなし
- `RESEARCH`: 技術調査継続中
- `WAITING_LIVE_TEST`: コード側は準備済みだがゲーム実データなしでは最終確定不能
- `BLOCKED`: 外部仕様が確定するまで安全に実装できない

> 方針: ユーザー確認/実機検証を通常の作業停止条件にしない。公開コード・ClientStructs・CIで確定できる作業を先行し、実機でしか得られない情報だけ最後まで `WAITING_LIVE_TEST/BLOCKED` として隔離する。

## 現在地点の重要事項

- Debug / Release build、packet冪等性検証、static contract、日本語UI audit、test ZIP、manifest version検証、test repository publishまでCI成功済み。
- Test版はGitHub Actions run numberから毎回異なる `1.0.90.x` を生成するため、同一versionのZIP差し替えではなくDalamudが更新判定できる形になった。
- `tools/packets/run_all.py` はWindows runnerでもUTF-8固定で実行し、CIではpacketを2回連続適用して2回目のGit treeが完全不変であることを検査する。1回目だけ成功するbrittle packetをcompile前に検出できる。
- `tools/validate_v110_contract.py` をCIへ組み込み、CE安全クリック、mounted invariant、補給優先順位、依存復旧、敵ランク安全側判定、BOCCHI経路計測の安全性、AGPL/provenance等の設計不変条件をcompile前に検査する。
- BOCCHI-style Direct / Aethernet / Return 経路は実装済み。出発AethernetはBOCCHIと同じ `base camp → 45y graph snap → nearest node` で1ノードに解決する。
- 長距離Aethernet候補では `vnavmesh.Nav.PathfindCancelable` を使って出発ノードまでの実地上経路長を非同期計測する。最大1本・最大750msで、遅い計測はcancel完了を待ってから水平距離fallbackへ戻す。Stop/目的地変更時も古い計測をdrainしてから新しい移動を開始する。
- Direct / Aethernet / Return はvendored `TraversalCandidate`の同一cost modelで比較する。inbound Aethernet→最終目的地はfull BOCCHI zone graphをvendorしていないため、現在も水平距離近似。
- Lifestreamはイベント移動中に欠落すれば即徒歩fallback、待機地点/補給など非緊急移動では最大30秒復帰待ち。
- 到達不能スカーミッシュは同一spawnだけBlacklistし、FATE消滅後に自動解除する。
- 手動移動はWASD/矢印および左右マウス同時押しを検出し、3秒quietになるまでvnavmeshをyieldする。
- 敵 I〜V/★ は名前/region fallbackで安全側に判定でき、unknownも危険扱い。raw icon直接対応だけ未確定。
- Debug world overlayで目的地/Aethernet経路/IV・V・★・unknown敵の感知形状を可視化可能。
- Survival auto-use、Reraiser risk-window、role別閾値、mounted invariantは実装済み。
- required dependencyは60秒復帰待ち。その間は非マウント時の生存Lost Actionを継続し、timeout後は戦闘終了待ち→可能ならReturn→停止、Return不能ならその場で停止。
- `SupplyManager` のlow-watermark判定をControllerへ配線済み。**CriticalNoRecoveryならスカーミッシュを即中断**、通常不足なら**到着済みの現在スカーミッシュだけ完走してから**Lost Finds Cacheへ向かう。
- CE参加申請は補給移動より先に継続する。CE当選後はCriticalNoRecoveryの場合だけCommenceを保留し、それ以外は即戦闘突入する。既に開始済みのCEは補給より常に優先する。
- `SupplyRecoveryDriver` はBOCCHI/Lifestream経路で拠点へ戻り、実際のLost Finds Cacheを開くところまで自動化済み。server-backed在庫の転送は行わない。
- 生存在庫評価はframework tickで1回だけ行いControllerへcacheする。MainWindowは `Potion Kit / Reraiser / 主回復 / Manawall` と補給状態を表示し、ImGui描画中にMYC inventory memoryを直接読まない。
- Lost Finds Cache/Holsterの読み取り・target planning・low-watermark評価は実装済み。
- **最大の残blockerはCache↔Holsterの正規サーバー転送手段**。公開ClientStructs/公開Dalamud実装から確定できず、推測callbackや直接memory writeは行わない。
- `DiagnosticsRecorder` は直近state/status 32件 / warning 16件をprivacy-safeに保持し、診断コピーへ含める。
- config migration失敗時は元configをtimestamp付きJSONへbackupし、安全なdefaultへfallbackする。
- UIは日本語固定。設定画面はカテゴリ整理済みで、ロストアクション配下も `Duty Actionバー / 自動使用 / パーティ支援` を独立subtab化した。自動使用OFFでもパーティ支援設定は消えない。
- 直接表示される英語ImGui literalは `tools/audit_visible_japanese.py` のstrict CI gateで新規混入を防止する。Runtime内部の英語メッセージはログ/診断用に保持し、UI表示時だけ日本語化する。
- AGPL本体、元BBR MIT、BOCCHI、KanoNoUta BOCCHI maintenance fork、Ocelot MIT、ECommons MITのprovenance/noticeを整理済み。

## Task Packet一覧

| Packet | Status | 内容 | 現状 |
|---|---|---|---|
| P0-01 | DONE | baseline audit + build確認 | Debug/Release/package/publish成功 |
| P1-01 | DONE | AGPL/notice検証 | root AGPL、BBR/BOCCHI/KanoNoUta fork/Ocelot/ECommons provenanceを明記 |
| P1-02 | DONE | Test version 1.0.90.x統一 | workflow run numberによる自動version、manifest/assembly/package同期 |
| P2-01 | DONE | Vendored BOCCHI traversal model | BOCCHI constants/TraversalCandidate/Return/single-departure規則をvendor化。出発walkはvnavmesh実経路長を計測。inbound→goalはfull zone graph未vendorのため水平距離近似 |
| P2-02 | DONE | ReturnTeleportWalk | `FieldTravelRouter.Returning`、Return確認、base→Aethernet→walk実装済み |
| P2-03 | DONE | route retry / blacklist | 3回stall後spawn blacklist、FATE消滅時prune、Start時clear |
| P2-04 | DONE | manual movement yield | WASD/矢印/左右マウス同時押し + 3秒quiet window |
| P3-01 | WAITING_LIVE_TEST | enemy rank raw diagnostics | raw `NamePlateIconId` / `CharacterData.Icon`取得・診断基盤済み |
| P3-02 | BLOCKED | direct raw rank mapping | raw pair実データが得られるまで固定mappingしない |
| P3-03 | DONE | danger rank integration/overlay | IV/V/★/unknown回避 + ★追加clearance + debug world overlay |
| P4-01 | WAITING_LIVE_TEST | remote CE signup/commence state | 遠隔signup/lottery/commenceコード完成。最終ゲーム挙動のみ未確認 |
| P4-02 | DONE | ActivityPlanner | route-cost、80% cutoff、大規模戦闘最優先、Relic filter実装済み |
| P4-03 | DONE | RelicFarmPlanner continuation | current-territory auto-continue実装・build済み |
| P4-04 | DONE | farm target staging | farm対象不在時のAethernet staging実装済み |
| P5-01 | RESEARCH | Cache/Holster transfer特定 | `docs/research/lost-finds-cache-transfer.md`。公開手段未発見 |
| P5-02 | DONE | HolsterInventory abstraction | `LostItemBoxInventory`, snapshot, `SurvivalLoadoutPlanner` 実装済み |
| P5-03 | BLOCKED | Initialize正常系 | target planningまでは完成。transfer effectのみP5-01待ち |
| P5-04 | BLOCKED | Initialize rollback | snapshot/transaction設計済み。実transfer確定待ち |
| P6-01 | DONE | low-watermark model | `SupplyManager` + target counts実装済み |
| P6-02 | BLOCKED | differential refill | transfer effect待ち |
| P6-03 | DONE | Supply vs CE arbitration | critical即中断 / routine現スカーミッシュ完走 / CE登録継続 / critical時のみCommence保留 / Cache自動移動・openまでCI済み |
| P7-01 | DONE | Reraiser risk-window | emergencyへのedgeで1回のみ候補化 |
| P7-02 | BLOCKED | Essence Initialize integration | priority/bring/autouse/overwrite policyあり。transfer effect待ち |
| P7-03 | DONE | mounted invariant | mounted中survival Lost Actionを発火しない |
| P8-01 | DONE | TextAdvance wrapper | `External/TextAdvanceIpc.cs` 実装済み |
| P8-02 | DONE | DeathRecovery state machine | CE待機、skirmish 30s、travel 10s、Return+TextAdvance委譲 |
| P8-03 | WAITING_LIVE_TEST | TextAdvance death flow | 最終ゲーム挙動のみ未確認 |
| P9-01 | DONE | DependencySupervisor abstraction | `DependencySupervisor.cs` 実装済み |
| P9-02 | DONE | required 60s recovery | required依存の60秒復帰窓実装済み |
| P9-03 | DONE | timeout safe stop | combat終了待ち→Return→Stop、不能時fail-closed |
| P9-04 | DONE | Lifestream optional policy | event即fallback / nonurgent最大30秒wait |
| P10-01 | DONE | social request識別 | Party agent強識別 + prompt subject/request二重判定 |
| P10-02 | DONE | strict social reject | Running中のみ識別済みsocial requestをNo。generic YesNoは触らない |
| P10-03 | WAITING_LIVE_TEST | false positive確認 | 最終ゲーム表示差分のみ未確認 |
| P11-01 | DONE | UI tab再編 | top-levelカテゴリ + Lost Action独立subtabまで整理済み |
| P11-02 | DONE | main status | route/CE/dependency/survival supply/blacklist表示を拡張済み |
| P11-03 | DONE | DiagnosticsRecorder | state/status 32件 + warning 16件 ring buffer |
| P11-04 | DONE | clipboard diagnostics | 個人情報を除外した診断コピー実装済み |
| P11-05 | DONE | debug world overlay | goal/Aethernet route/danger cone+ring描画、default OFF |
| P11-06 | DONE | visible English全日本語化 | 主要画面/runtime/multibox/duty/relic/settingsを日本語化し、direct ImGui literalのstrict CI auditを追加 |
| P12-01 | DONE | config migration | schema v4 migration + threshold/nav normalization |
| P12-02 | DONE | character state split | Relic farm targetを`PlayerState.ContentId`単位で保存 |
| P12-03 | DONE | migration failure backup | raw config backup + notification + safe defaults fallback |
| P13-01 | DONE | weekly BOCCHI monitor | `.github/workflows/check-bocchi-upstream.yml` |
| P14-01 | DONE | Test repository publish | 自動採番ZIP/manifestをCIでpublish |
| P14-02 | DONE | stable fallback案内 | test build UIへStable復帰手順 |
| P15-01 | WAITING_LIVE_TEST | 南方受入 | 最終受入まで延期。通常開発を止めない |
| P15-02 | WAITING_LIVE_TEST | ザトゥノル受入 | 同上 |
| P15-03 | WAITING_LIVE_TEST | cross-cutting受入 | 同上 |
| P15-04 | BLOCKED | RC review/user approval | main merge前の最終工程。自動merge禁止 |

## CI evidence

### latest validated baseline

- workflow: `Build v1.1 test repository`
- run: `33578822721` / run number `151`
- validated bot commit: `04c701acc45e0f8d9c6de0d3810f427f40e330db`
- version: `1.0.90.151`
- first packet application: pass
- second packet replay / Git tree idempotency: pass
- static v1.1 contract: pass
- visible Japanese UI audit: pass
- diff check: pass
- restore: pass
- Debug build: pass
- Release build: pass
- test package: pass
- assembly version verification: pass
- artifact upload: pass
- test repository publish: pass

Current user commits after that validation are intentionally pushed frequently; the newest workflow run is the authority for whether they have been incorporated into the next validated bot commit.

## 技術調査

### Lost Finds Cache transfer

成果物: `docs/research/lost-finds-cache-transfer.md`

確定事項:

- `AgentMycItemBox.ItemBoxData` からCache/HolsterのActionId/Countはread可能。
- ClientStructsに公開transfer member functionは無い。
- `AgentMycItemBag` / `MYCItemBox` / `MYCItemBag` / `MYCItemBagTrade` の存在は確認済みだが、正規transfer callback/functionの引数契約は未確定。
- `kaleidocli/BozjaBuddy` のMYCItemBox/MYCItemBagTrade実装も再確認したが、フィルタ/overlay/在庫read中心で、自動転送callbackの根拠は得られなかった。
- server-backed countへの直接writeは禁止。
- `MycItemBoxCallbackProbe` は実ゲーム自身のcallbackを採取するための診断手段として残す。

### BOCCHI / vnavmesh path cost

- BOCCHI/Ocelotが利用する `vnavmesh.Nav.Pathfind` / `vnavmesh.Nav.PathfindCancelable` の公開IPC契約を確認済み。
- BBRでは実移動とは別のbounded telemetryとして利用し、framework tickを同期blockしない。
- 同時queryは最大1、待機は最大750ms。timeout/Stop/対象変更ではcancel→Task終了確認→replanの順にする。
- Cache keyはterritoryを含み、別エリア同座標の測定値を再利用しない。
- full BOCCHI graph serviceはvendorしていないため、現時点ではdeparture walkのみ実経路長、inbound→goalは水平距離近似。

### Enemy rank

`EnemyStrengthResolver` は、raw mappingが無くても territory + region + English BNpcName seedでI〜V/★を判定できる。判定不能はunknown=危険とするため、安全側の自動周回は先行可能。

## 次の実装優先順位

ユーザー確認/実機確認を要求せず、以下を順次進める。

1. P5-01 Cache transferの公開根拠探索を継続
2. Cache側在庫不足のread-only診断・同一instanceでの再補給loop防止をtransfer executorと独立して追加
3. P2-01 inbound→goalの実経路コスト化が過度なPathfind負荷なしで可能か設計・検証
4. RC前static acceptance checklistを拡張
5. 実機依存項目は最後にまとめて確認

## 実機検証方針

実機確認は途中の通常ゲートにしない。最終RC付近でまとめて確認する。

それ以前に実データが必要になった場合は、プラグイン側に診断採取機能を先に実装し、他タスクを継続する。

## main merge

`main` には自動mergeしない。最終RC結果を提示し、ユーザーの明示承認後のみmergeする。