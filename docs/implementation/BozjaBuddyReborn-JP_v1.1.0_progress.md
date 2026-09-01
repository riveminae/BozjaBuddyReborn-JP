# BozjaBuddyReborn-JP v1.1.0 実装進捗

最終更新: 2026-09-01  
branch: `feat/bocchi-navigation`  
main baseline: `038faf8d70b2aea7189143f7fd46a8c135cb0484`  
最新CI検証commit: `21c55df003eb966044a17f57b62bc4bf3bdf015e`  
Test version: `1.0.90.4`

## ステータス定義

- `DONE`: コード/静的検証/CIで完了できる範囲を完了
- `PARTIAL`: コード基礎あり。要件を満たすには追加実装または最終確認が必要
- `TODO`: 未着手または完成コードなし
- `RESEARCH`: 技術調査継続中
- `WAITING_LIVE_TEST`: コード側は準備済みだがゲーム実データなしでは最終確定不能
- `BLOCKED`: 外部仕様が確定するまで安全に実装できない

> 方針変更: ユーザー確認/実機検証を通常の作業停止条件にしない。公開コード・ClientStructs・CIで確定できる作業を先行し、実機でしか得られない情報だけ最後まで `WAITING_LIVE_TEST/BLOCKED` として隔離する。

## 現在地点の重要事項

- Debug / Release build、test ZIP、manifest version検証、test repository publishまでCI成功済み。
- BOCCHI-style Direct / Aethernet / Return 経路は実装済み。ただし歩行コストはfull BOCCHI graphではなくBBR adapterの水平距離近似を残している。
- 敵 I〜V/★ は名前/region fallbackで安全側に判定でき、unknownも危険扱い。raw icon直接対応だけ未確定。
- Survival auto-use、Reraiser risk-window、role別閾値、mounted invariantは実装済み。
- Lost Finds Cache/Holsterの読み取り・target planning・low-watermark評価は実装済み。
- **最大の残blockerはCache↔Holsterの正規サーバー転送手段**。公開ClientStructs/公開Dalamud実装から確定できず、推測callbackや直接memory writeは行わない。
- `DiagnosticsRecorder` を追加し、直近state transition 32件 / warning 16件をprivacy-safeに保持する実装を追加中。

## Task Packet一覧

| Packet | Status | 内容 | 現状 |
|---|---|---|---|
| P0-01 | DONE | baseline audit + build確認 | CI `33517819295` Debug/Release/package成功 |
| P1-01 | PARTIAL | AGPL/notice検証 | AGPL化・BBR/BOCCHI/Ocelot notice反映済み。最終license auditのみ残す |
| P1-02 | DONE | Test version 1.0.90.x統一 | workflow/manifest/assembly/package version同期・publish成功 |
| P2-01 | PARTIAL | Vendored BOCCHI traversal model | BOCCHI constants/Return semanticsをvendor化。full graph importは未完 |
| P2-02 | PARTIAL | ReturnTeleportWalk | `FieldTravelRouter.Returning`、Return確認、base→Aethernet→walk実装済み |
| P2-03 | PARTIAL | route retry / blacklist | Movement stall recovery + route retry基礎あり。spawn lifetime blacklist最終監査残り |
| P2-04 | PARTIAL | manual movement yield | `ManualMovementYield.cs` 導入済み。最終controller監査残り |
| P3-01 | WAITING_LIVE_TEST | enemy rank raw diagnostics | raw `NamePlateIconId` / `CharacterData.Icon`取得・学習基盤済み |
| P3-02 | BLOCKED | direct raw rank mapping | raw pair実データが得られるまで固定mappingしない |
| P3-03 | PARTIAL | danger rank integration/overlay | IV/V/★/unknown回避は実装済み。debug overlay残り |
| P4-01 | PARTIAL | remote CE signup/commence state | 遠隔signup/commence state実装済み。最終ゲーム挙動のみ未確定 |
| P4-02 | PARTIAL | ActivityPlanner | route-cost、80% cutoff、大規模戦闘最優先、Relic filter実装済み |
| P4-03 | DONE | RelicFarmPlanner continuation | `RelicFarmCoordinator` + current-territory auto-continue実装・build済み |
| P4-04 | PARTIAL | farm target staging | farm対象不在時のAethernet staging実装済み |
| P5-01 | RESEARCH | Cache/Holster transfer特定 | `docs/research/lost-finds-cache-transfer.md`。公開手段未発見 |
| P5-02 | PARTIAL | HolsterInventory abstraction | `LostItemBoxInventory`, snapshot, `SurvivalLoadoutPlanner` 実装済み |
| P5-03 | BLOCKED | Initialize正常系 | target planningまでは完成。transfer effectのみP5-01待ち |
| P5-04 | BLOCKED | Initialize rollback | snapshot/transaction設計済み。実transfer確定待ち |
| P6-01 | PARTIAL | low-watermark model | `SupplyManager` + target counts実装済み |
| P6-02 | BLOCKED | differential refill | transfer effect待ち |
| P6-03 | PARTIAL | Supply vs CE arbitration | evaluator/要件あり。実refill effect待ち |
| P7-01 | DONE | Reraiser risk-window | emergencyへのedgeで1回のみ候補化、CI build済み |
| P7-02 | PARTIAL | Essence Initialize integration | priority/bring/autouse/overwrite policyあり。transfer待ち |
| P7-03 | DONE | mounted invariant | mounted中survival Lost Actionを発火しない |
| P8-01 | DONE | TextAdvance wrapper | `External/TextAdvanceIpc.cs` 実装済み |
| P8-02 | PARTIAL | DeathRecovery state machine | CE待機、30s/10s、Return+TextAdvance委譲実装済み |
| P8-03 | WAITING_LIVE_TEST | TextAdvance death flow | 最終ゲーム挙動のみ未確認 |
| P9-01 | DONE | DependencySupervisor abstraction | `DependencySupervisor.cs` 実装済み |
| P9-02 | DONE | required 60s recovery | required依存の60秒復帰窓実装済み |
| P9-03 | PARTIAL | timeout safe stop | `SafeStopCoordinator` 導入済み。最終状態遷移監査残り |
| P9-04 | PARTIAL | Lifestream optional policy | event travel即fallback実装。非緊急30s policy最終監査残り |
| P10-01 | DONE | social request識別 | Party agent強識別 + prompt subject/request二重判定を実装 |
| P10-02 | PARTIAL | strict social reject | Running中のみNo、generic YesNoは拒否しない |
| P10-03 | WAITING_LIVE_TEST | false positive確認 | 最終ゲーム表示差分のみ未確認 |
| P11-01 | PARTIAL | UI tab再編 | 日本語設定UI拡張済み。6カテゴリ最終整理残り |
| P11-02 | PARTIAL | main status | route/CE/dependency/survival表示を拡張済み |
| P11-03 | PARTIAL | DiagnosticsRecorder | ring buffer実装commit作成済み。CI反映待ち |
| P11-04 | DONE | clipboard diagnostics | 個人情報を除外した診断コピー実装・CI build済み |
| P11-05 | TODO | debug world overlay | 未実装 |
| P11-06 | PARTIAL | visible English全日本語化 | `Loc` は日本語固定。残存literal全走査が必要 |
| P12-01 | DONE | config migration | schema v4 migration + threshold/nav normalization実装・CI build済み |
| P12-02 | DONE | character state split | Relic farm targetを`PlayerState.ContentId`単位で保存・CI build済み |
| P12-03 | TODO | migration failure backup | 未実装 |
| P13-01 | DONE | weekly BOCCHI monitor | `.github/workflows/check-bocchi-upstream.yml` 導入済み |
| P14-01 | DONE | Test repository publish | `1.0.90.4` ZIP/manifestをCIでpublish済み |
| P14-02 | DONE | stable fallback案内 | test build UIへStable復帰手順を追加・CI build済み |
| P15-01 | WAITING_LIVE_TEST | 南方受入 | 最終受入まで延期。通常開発を止めない |
| P15-02 | WAITING_LIVE_TEST | ザトゥノル受入 | 同上 |
| P15-03 | WAITING_LIVE_TEST | cross-cutting受入 | 同上 |
| P15-04 | BLOCKED | RC review/user approval | main merge前の最終工程。自動merge禁止 |

## CI evidence

### 2026-09-01 v1.0.90.4 validation

- workflow: `Build v1.1 test repository`
- run: `33517819295`
- packet application: pass
- diff check: pass
- restore: pass
- Debug build: pass
- Release build: pass
- test package: pass
- assembly version verification: pass
- artifact upload: pass
- test repository publish: pass
- validated bot commit: `21c55df003eb966044a17f57b62bc4bf3bdf015e`

## 技術調査

### Lost Finds Cache transfer

成果物: `docs/research/lost-finds-cache-transfer.md`

確定事項:

- `AgentMycItemBox.ItemBoxData` からCache/HolsterのActionId/Countはread可能。
- ClientStructsに公開transfer member functionは無い。
- `kaleidocli/BozjaBuddy` のMYCItemBox/MYCItemBagTrade実装も確認したが、自動転送callbackの根拠は得られなかった。
- `MYCItemBox`, `MYCItemBag`, `MYCItemBagTrade` addonは確認済み。
- server-backed countへの直接writeは禁止。
- `MycItemBoxCallbackProbe` は実ゲーム自身のcallbackを採取するための診断手段として残す。

### Enemy rank

`EnemyStrengthResolver` は、raw mappingが無くても territory + region + English BNpcName seedでI〜V/★を判定できる。判定不能はunknown=危険とするため、安全側の自動周回は先行可能。

## 次の実装優先順位

ユーザー確認/実機確認を要求せず、以下を順次進める。

1. P11-03 DiagnosticsRecorder / Warning history
2. P11-06 visible English literal監査と日本語化
3. P12-03 migration failure backup
4. P2-03 route blacklist lifecycle監査/完成
5. P9-03 safe-stop state transition監査
6. P9-04 Lifestream context policy監査
7. P11-05 debug overlay
8. P1-01 license final audit
9. Cache transferは公開根拠の探索を継続するが、他作業を止めない

## 実機検証方針

実機確認は途中の通常ゲートにしない。最終RC付近でまとめて確認する。

それ以前に実データが必要になった場合は、プラグイン側に診断採取機能を先に実装し、他タスクを継続する。

## main merge

`main` には自動mergeしない。最終RC結果を提示し、ユーザーの明示承認後のみmergeする。