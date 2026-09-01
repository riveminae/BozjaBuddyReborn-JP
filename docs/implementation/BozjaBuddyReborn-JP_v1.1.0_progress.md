# BozjaBuddyReborn-JP v1.1.0 実装進捗

最終更新: 2026-09-02  
branch: `feat/bocchi-navigation`  
main baseline: `038faf8d70b2aea7189143f7fd46a8c135cb0484`  
最新CI検証commit: `c4b77410b724202125e7dbb2a2c5360f7690ebdf`  
最新CI検証Test version: `1.0.90.114`  
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

- Debug / Release build、static contract、test ZIP、manifest version検証、test repository publishまでCI成功済み。
- Test版はGitHub Actions run numberから毎回異なる `1.0.90.x` を生成するため、同一versionのZIP差し替えではなくDalamudが更新判定できる形になった。
- `tools/validate_v110_contract.py` をCIへ組み込み、CE安全クリック、mounted invariant、補給優先順位、依存復旧、敵ランク安全側判定、AGPL/provenance等の設計不変条件をcompile前に検査する。
- BOCCHI-style Direct / Aethernet / Return 経路は実装済み。歩行コストだけはfull BOCCHI graphではなくBBR adapterの水平距離近似を残している。
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
- Lost Finds Cache/Holsterの読み取り・target planning・low-watermark評価は実装済み。
- **最大の残blockerはCache↔Holsterの正規サーバー転送手段**。公開ClientStructs/公開Dalamud実装から確定できず、推測callbackや直接memory writeは行わない。
- `DiagnosticsRecorder` は直近state/status 32件 / warning 16件をprivacy-safeに保持し、診断コピーへ含める。
- config migration失敗時は元configをtimestamp付きJSONへbackupし、安全なdefaultへfallbackする。
- UIは日本語固定。Runtime内部の英語メッセージはログ/診断用に保持し、UI表示時だけ日本語化する層を追加済み。
- AGPL本体、元BBR MIT、BOCCHI、KanoNoUta BOCCHI maintenance fork、Ocelot MIT、ECommons MITのprovenance/noticeを整理済み。

## Task Packet一覧

| Packet | Status | 内容 | 現状 |
|---|---|---|---|
| P0-01 | DONE | baseline audit + build確認 | Debug/Release/package/publish成功 |
| P1-01 | DONE | AGPL/notice検証 | root AGPL、BBR/BOCCHI/KanoNoUta fork/Ocelot/ECommons provenanceを明記 |
| P1-02 | DONE | Test version 1.0.90.x統一 | workflow run numberによる自動version、manifest/assembly/package同期 |
| P2-01 | PARTIAL | Vendored BOCCHI traversal model | BOCCHI constants/Return semanticsをvendor化。full graph importは未完 |
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
| P11-01 | PARTIAL | UI tab再編 | 日本語UI拡張済み。最終カテゴリ整理は残る |
| P11-02 | DONE | main status | route/CE/dependency/survival/blacklist表示を拡張済み |
| P11-03 | DONE | DiagnosticsRecorder | state/status 32件 + warning 16件 ring buffer |
| P11-04 | DONE | clipboard diagnostics | 個人情報を除外した診断コピー実装済み |
| P11-05 | DONE | debug world overlay | goal/Aethernet route/danger cone+ring描画、default OFF |
| P11-06 | PARTIAL | visible English全日本語化 | primary/main/runtime/multibox/duty UIを日本語化。残存literal最終走査のみ |
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
- run: `33528892610` / run number `114`
- validated bot commit: `c4b77410b724202125e7dbb2a2c5360f7690ebdf`
- version: `1.0.90.114`
- packet application: pass
- static v1.1 contract: pass
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
- `kaleidocli/BozjaBuddy` のMYCItemBox/MYCItemBagTrade実装も確認したが、自動転送callbackの根拠は得られなかった。
- server-backed countへの直接writeは禁止。
- `MycItemBoxCallbackProbe` は実ゲーム自身のcallbackを採取するための診断手段として残す。

### Enemy rank

`EnemyStrengthResolver` は、raw mappingが無くても territory + region + English BNpcName seedでI〜V/★を判定できる。判定不能はunknown=危険とするため、安全側の自動周回は先行可能。

## 次の実装優先順位

ユーザー確認/実機確認を要求せず、以下を順次進める。

1. P11-06 残存visible English literal最終走査 + CI audit
2. P11-01 UIカテゴリ最終整理
3. P5-01 Cache transferの公開根拠探索を継続
4. Cache側在庫不足のread-only診断・loop防止設計をtransfer executorと独立して追加
5. RC前static acceptance checklistを拡張
6. 実機依存項目は最後にまとめて確認

## 実機検証方針

実機確認は途中の通常ゲートにしない。最終RC付近でまとめて確認する。

それ以前に実データが必要になった場合は、プラグイン側に診断採取機能を先に実装し、他タスクを継続する。

## main merge

`main` には自動mergeしない。最終RC結果を提示し、ユーザーの明示承認後のみmergeする。
