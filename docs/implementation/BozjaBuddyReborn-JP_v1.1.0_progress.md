# BozjaBuddyReborn-JP v1.1.0 実装進捗

最終更新: 2026-09-01  
branch: `feat/bocchi-navigation`  
main baseline: `038faf8d70b2aea7189143f7fd46a8c135cb0484`

## ステータス定義

- `DONE`: build + 必要な検証まで完了
- `PARTIAL`: コード基礎あり。要件を満たすには追加実装/検証が必要
- `TODO`: 未着手または完成コードなし
- `RESEARCH`: 技術調査待ち
- `WAITING_LIVE_TEST`: build済みだが実機確認待ち
- `BLOCKED`: 前提task未完了で進めない

## 現在地点の重要事項

現在branchにはv1.1 foundation codeが既に入っているが、**完成版ではない**。

特に以下を誤ってDONE扱いしないこと。

- `FieldTravelRouter` はAethernet基礎ありだが、BOCCHI graph/traversal完全移植ではない。
- `UseReturnRouting` はconfigにあるがReturn route未完成。
- `EnemyStrengthResolver` は名前/region fallback主体で、raw icon mappingは実機未確定。
- Survival auto-useは基礎ありだがInitialize/Cache transfer/rollback/refillは未完成。
- Test build関連ファイルはあるが、各実装commit後にversion/manifest/CIを確認する必要がある。

## Task Packet一覧

| Packet | Status | 内容 | 主な既存コード/備考 |
|---|---|---|---|
| P0-01 | TODO | baseline audit + current build確認 | branchに多数の途中変更あり。最初にbuildを固定する |
| P1-01 | PARTIAL | AGPL/notice検証 | `LICENSE`, `THIRD-PARTY-NOTICES.md` は既に変更あり |
| P1-02 | PARTIAL | Test version 1.0.90.x統一 | `test-build.yml`, `pluginmaster-test.json`, `dist-test` 基礎あり |
| P2-01 | PARTIAL | Vendored BOCCHI traversal model | `Vendor/BOCCHI/NavigationConstants.cs` のみ明確に導入済み。現routerは距離近似 |
| P2-02 | TODO | ReturnTeleportWalk | config fieldのみ。route mode未実装 |
| P2-03 | PARTIAL | route retry / blacklist | `Movement` stall recoveryは既存。v1.1 route-level blacklist要確認/完成必要 |
| P2-04 | TODO | manual movement yield | 未完 |
| P3-01 | PARTIAL | enemy rank diagnostics/live data | `EnemyStrengthResolver` raw pair取得基礎あり。実機採取未完 |
| P3-02 | BLOCKED | direct rank mapping判断 | P3-01実機データ待ち |
| P3-03 | PARTIAL | danger rank integration/overlay | `AggroAvoidance` rank連携基礎あり。overlay未完 |
| P4-01 | PARTIAL | remote CE signup/commence state | `SignUpRunner`変更あり。実機でremote signup flow要検証 |
| P4-02 | PARTIAL | ActivityPlanner | `TargetSelector`変更あり。独立planner/最終優先順は未確定実装 |
| P4-03 | TODO | RelicFarmPlanner continuation | 未完 |
| P4-04 | PARTIAL | farm target staging | 既存IdleSpotあり。aetheryte最適stagingへ完成必要 |
| P5-01 | RESEARCH | Cache/Holster transfer特定 | 最大blocker。直接memory write禁止 |
| P5-02 | BLOCKED | HolsterInventory abstraction | P5-01待ち |
| P5-03 | BLOCKED | Initialize正常系 | P5-02待ち |
| P5-04 | BLOCKED | Initialize rollback | P5-03待ち |
| P6-01 | PARTIAL | low-watermark model | survival/config基礎あり。Supply manager未実装 |
| P6-02 | BLOCKED | differential refill | transfer API待ち |
| P6-03 | BLOCKED | Supply vs CE arbitration | Supply manager待ち |
| P7-01 | PARTIAL | Reraiser risk-window | `SurvivalPolicy` priorityあり。crossing semantics要完成 |
| P7-02 | PARTIAL | Essence Initialize integration | policyあり、Initialize待ち |
| P7-03 | PARTIAL | mounted invariant | `HolsterDriver` はmounted時Abandon/skip実装済み。テスト追加が必要 |
| P8-01 | TODO | TextAdvance wrapper | 未完 |
| P8-02 | TODO | DeathRecovery state machine | 未完 |
| P8-03 | BLOCKED | TextAdvance live test | P8-01/02待ち |
| P9-01 | TODO | DependencySupervisor abstraction | 未完 |
| P9-02 | TODO | required 60s recovery | 未完 |
| P9-03 | TODO | timeout safe stop | 未完 |
| P9-04 | PARTIAL | Lifestream optional policy | routerは即fallback基礎あり。30s context policy未完 |
| P10-01 | RESEARCH | social request dialog識別 | 未完 |
| P10-02 | BLOCKED | strict social reject | P10-01待ち |
| P10-03 | BLOCKED | false positive live test | P10-02待ち |
| P11-01 | PARTIAL | UI tab再編 | `ConfigWindow`変更あり。最終6カテゴリ未確認 |
| P11-02 | PARTIAL | main status | `MainWindow`変更あり。全診断項目未完 |
| P11-03 | TODO | DiagnosticsRecorder | 未完 |
| P11-04 | TODO | clipboard diagnostics | 未完 |
| P11-05 | TODO | debug world overlay | 未完 |
| P11-06 | PARTIAL | visible English全日本語化 | localization変更あり。未翻訳残存を全走査する必要あり |
| P12-01 | TODO | config migration function | config Version更新基礎あり。明示migration要確認 |
| P12-02 | TODO | character state split | 未完 |
| P12-03 | TODO | migration failure backup | 未完 |
| P13-01 | TODO | weekly BOCCHI monitor | 未完 |
| P14-01 | PARTIAL | Test repository publish | manifest/ZIP/workflow基礎あり。実update検証必要 |
| P14-02 | TODO | stable fallback案内 | 未完 |
| P15-01 | BLOCKED | 南方受入 | 機能完成待ち |
| P15-02 | BLOCKED | ザトゥノル受入 | 機能完成待ち |
| P15-03 | BLOCKED | cross-cutting受入 | 機能完成待ち |
| P15-04 | BLOCKED | RC review/user approval | 全受入待ち |

## 現在確認できているv1.1 foundation

### Navigation

存在:

- `Automation/FieldTravelRouter.cs`
- `External/LifestreamIpc.cs`
- `Game/FieldAethernet.cs`
- `Vendor/BOCCHI/NavigationConstants.cs`

現在のrouter mode:

- Direct
- WalkToAetheryte
- Teleporting
- WalkFromAetheryte
- FallbackDirect

未実装として扱う:

- Return route
- full BOCCHI graph traversal
- manual input yield
- complete route blacklist lifecycle

### Enemy strength

存在:

- `Game/EnemyStrengthResolver.cs`

既存仕様:

- logical rank I/II/III/IV/V/Star/Unknown
- IV/V/Star/Unknown = dangerous
- Japanese clientでもEnglish BNpcNameでfallback可能
- raw `NamePlateIconId` + `CharacterData.Icon` を取得可能

未確定:

- raw pairがrankを直接表すか
- 実機で全rankを観測した証拠

### Survival

存在:

- `Game/SurvivalPolicy.cs`
- `Automation/HolsterDriver.cs` survival path

確認できる設計反映:

- role threshold Tank 55/30, Healer 70/45, DPS 65/40
- Deep essence default false
- mounted時auto survival抑止
- Potion Kit maintain
- moving on footではcast healを避ける

未実装/未完:

- full Initialize
- Cache transfer
- rollback
- differential refill
- low-watermark coordinator

## Research成果物予定

| Research | Path | Status |
|---|---|---|
| Enemy I〜V/★ mapping | `docs/research/bozja-enemy-strength.md` | TODO |
| Lost Finds Cache transfer | `docs/research/lost-finds-cache-transfer.md` | TODO |
| Social request dialogs | `docs/research/social-request-dialogs.md` | TODO |
| TextAdvance integration notes | `docs/research/textadvance-death-recovery.md` | TODO |

## 実機検証記録予定

最終的に以下へ結果を残す。

```text
docs/test-results/
  v1.0.90.x-southern-front.md
  v1.0.90.x-zadnor.md
  v1.0.90.x-cross-cutting.md
```

各結果には最低限:

- plugin version
- commit SHA
- territory
- job/role
- scenario
- expected
- actual
- pass/fail
- relevant English logs
- screenshot/log note（必要な場合）

を記録する。

## 次にやるべきPacket

**P0-01: baseline audit + build確認**

理由:

現在branchは既に複数のfoundation実装が混在している。これ以上機能を足す前に、一度「現在headがコンパイルする」ことを固定しないと、後続Packetで発生したcompile errorとの区別がつかない。

P0-01がDONEになった後、並行可能な安全な次候補:

- P1-01 license audit
- P1-02 version/test repo整理
- P3-01 enemy diagnostics強化（ただし完了はLIVE待ち）
- P5-01 Cache transfer research
- P10-01 social dialog research

実装依存の大きいP5-03等へ先に飛ばないこと。

## 更新ルール

各Packet完了時、このファイルの該当Statusを更新し、以下を末尾へ追記する。

```text
### YYYY-MM-DD P?-??
- status: DONE / WAITING_LIVE_TEST / BLOCKED
- commit: <sha>
- build: pass/fail + run id if available
- summary: ...
- next: ...
```

## 作業ログ

### 2026-09-01 Documentation baseline

- requirement fixed after grill-me interview
- detailed design added
- timeout-safe execution plan added
- progress tracker initialized
- code implementation was intentionally not expanded in this documentation pass
