# BozjaBuddyReborn-JP v1.1.0 実装進捗

- 最終更新: 2026-09-06
- branch: `feat/bocchi-navigation`
- 初期main baseline: `038faf8d70b2aea7189143f7fd46a8c135cb0484`
- 最新CI検証commit: `2a796e7ffb6ef0871a0b11e2a11d2272064de778`
- 最新CI検証Test候補version: `1.0.90.165`（artifactのみ。公開feedは`1.0.90.162`）
- Test version生成: GitHub Actions run numberから `1.0.90.x` を自動採番

## ステータス定義

- `DONE`: コード/静的検証/CIで完了できる範囲を完了
- `PARTIAL`: コード基礎あり。要件を満たすには追加実装または最終確認が必要
- `TODO`: 未着手または完成コードなし
- `RESEARCH`: 技術調査継続中
- `WAITING_LIVE_TEST`: コード側は準備済みだがゲーム実データなしでは最終確定不能
- `BLOCKED`: 外部仕様が確定するまで安全に実装できない

> 方針: ユーザー確認/実機検証を通常の作業停止条件にしない。公開コード・ClientStructs・CIで確定できる作業を先行し、実機でしか得られない情報だけ最後まで `WAITING_LIVE_TEST/BLOCKED` として隔離する。

## 現在地点の重要事項

- Debug / Release build、packet冪等性検証、static contract、日本語UI audit、test ZIP、manifest version検証まで最新CI成功済み。現在のworkflowはartifactのみ生成し、公開feedを自動で上書きしない。
- Test候補はGitHub Actions run numberから毎回異なる `1.0.90.x` を生成する。配信には検証済みZIPのタグ固定とfeed更新が別途必要で、現時点の公開版は `1.0.90.162`。
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
| P2-01 | DONE | Vendored BOCCHI traversal model | BOCCHI constants/TraversalCandidate/Return/single-departure規則をvendor化。出発walkと選択inbound→goal walkはbounded vnavmesh実経路長cacheを利用し、未測定時のみ水平距離へfail-openする |
| P2-02 | DONE | ReturnTeleportWalk | `FieldTravelRouter.Returning`、Return確認、base→Aethernet→walk実装済み |
| P2-03 | DONE | route retry / blacklist | 3回stall後spawn blacklist、FATE消滅時prune、Start時clear |
| P2-04 | DONE | manual movement yield | WASD/矢印/左右マウス同時押し + 3秒quiet window |
| P3-01 | WAITING_LIVE_TEST | enemy rank raw diagnostics | raw `NamePlateIconId` / `CharacterData.Icon`取得・診断基盤済み |
| P3-02 | BLOCKED | direct raw rank mapping | raw pair実データが得られるまで固定mappingしない |
| P3-03 | DONE | danger rank integration/overlay | IV/V/★/unknown回避 + ★追加clearance + debug world overlay |
| P4-01 | WAITING_LIVE_TEST | remote CE signup/commence state | 遠隔選択144件、行/名前/登録IDを照合するrunner等167件成功。画面定義に基づきボタン順依存を解消。実クライアントの複製行/文字列書式・登録先・転送は両フィールドで未確認 |
| P4-02 | DONE | ActivityPlanner | route-cost、80% cutoff、大規模戦闘最優先、Relic filter実装済み |
| P4-03 | DONE | RelicFarmPlanner continuation | current-territory auto-continue実装・build済み |
| P4-04 | DONE | farm target staging | farm対象不在時のAethernet staging実装済み |
| P5-01 | RESEARCH | Cache/Holster transfer特定 | `docs/research/lost-finds-cache-transfer.md`。公開手段未発見。Test-only read-only probeは手動1個転送の前後snapshotを後続framework tickで相関し、重複eventを曖昧として除外する。実機captureと独立したserver acknowledgement確認待ち |
| P5-02 | DONE | HolsterInventory abstraction | `LostItemBoxInventory`, snapshot, `SurvivalLoadoutPlanner` 実装済み |
| P5-03 | BLOCKED | Initialize正常系 | target planningまでは完成。transfer effectのみP5-01待ち |
| P5-04 | BLOCKED | Initialize rollback | snapshot/transaction設計済み。実transfer確定待ち |
| P6-01 | DONE | low-watermark model | `SupplyManager` + target counts実装済み。2026-09-06に使用可能数と持込在庫数を分離、使用禁止品による危機判定漏れ・通常補給の反復を合成ホストで検査 |
| P6-02 | BLOCKED | differential refill | transfer effect待ち |
| P6-03 | DONE | Supply vs CE arbitration | critical即中断 / routine現スカーミッシュ完走 / CE登録継続 / critical時のみCommence保留 / Cache自動移動・openまでCI済み |
| P7-01 | DONE | Reraiser risk-window | emergencyへのedgeで1回のみ候補化 |
| P7-02 | BLOCKED | Essence Initialize integration | priority/bring/autouse/overwrite policyあり。transfer effect待ち |
| P7-03 | WAITING_LIVE_TEST | mounted invariant | mounted中survival Lost Actionを発火しない。2026-09-06に移動中FightBack分岐・設定UIを除去し、旧設定を逃走固定へ補正。静的/Debug/Release検証済み、両フィールド実機確認は未実施 |
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
| P11-01 | PARTIAL | UI tab再編 | Lost Action独立subtabあり。要件14.2の周回/生存/ロストアクション/移動/Relic/詳細設定への再編は未実装（audit GAP-01） |
| P11-02 | DONE | main status | route/CE/dependency/survival supply/blacklist表示を拡張済み |
| P11-03 | DONE | DiagnosticsRecorder | state/status 32件 + warning 16件 ring buffer |
| P11-04 | DONE | clipboard diagnostics | 個人情報を除外した診断コピー実装済み |
| P11-05 | DONE | debug world overlay | goal/Aethernet route/danger cone+ring描画、default OFF |
| P11-06 | DONE | visible English全日本語化 | 主要画面/runtime/multibox/duty/relic/settingsを日本語化し、direct ImGui literalのstrict CI auditを追加 |
| P12-01 | DONE | config migration | schema v4 migration + threshold/nav normalization |
| P12-02 | DONE | character state split | Relic farm targetを`PlayerState.ContentId`単位で保存 |
| P12-03 | DONE | migration failure backup | raw config backup + notification + safe defaults fallback |
| P13-01 | DONE | weekly BOCCHI monitor | `.github/workflows/check-bocchi-upstream.yml` |
| P14-01 | DONE | Test repository publish | CIで自動採番したZIP/manifestを検証し、固定tagとfeedで配布。CIは候補artifact生成のみでfeedを自動更新しない |
| P14-02 | DONE | stable fallback案内 | test build UIへStable復帰手順 |
| P15-01 | WAITING_LIVE_TEST | 南方受入 | 最終受入まで延期。通常開発を止めない |
| P15-02 | WAITING_LIVE_TEST | ザトゥノル受入 | 同上 |
| P15-03 | WAITING_LIVE_TEST | cross-cutting受入 | 同上 |
| P15-04 | BLOCKED | RC review/user approval | main merge前の最終工程。自動merge禁止 |

## CI evidence

### 要件差異修正: 持込と自動使用の独立性（2026-09-06）

- 要件8.1/8.2、9、10、14.2、P7/P6/P11。設定画面の調査で、通常使用とパーティ支援が自動使用許可を無視し、装填後の許可取消も見ていない差異を発見した。既存の辞書を各経路へ接続し、マウント中の支援も止めた。既存戦闘制御と設定スキーマは未変更。
- ホルダーが空でも装填済みアクションを使う経路を修正した。補給は持込許可、危機判定は自動使用可能な回復手段で評価する。持込不可でも手元の使用可能品は利用でき、使用禁止品を実所持数不足と取り違えて往復しない。欠品記録は維持した。
- 本番方針・通常/生存/支援・補給判定をリンクした `tests/SurvivalPermissions` は修正前exit 1、修正後103件exit 0。同じ検査入力で6不具合を個別に入れ、全てexit 1で拒否。各試行は外側30秒以内、復元後103件成功。詳細・ハッシュ・改行正規化の扱いは[受入記録](lost-action-permission-acceptance-20260906.md)。
- 既存405件を含む計508件、静的契約、日本語UI監査、Debug/Release、diff検査はexit 0。packetは初回の改行正規化を検出して内容確認後、2回連続で176入力のハッシュ一致。既存検査・入力・packetは弱めていない。ローカルNU1900警告は残る。
- 試験票55項目と結果コピーに在庫値の解釈を補足した。試験票のLatin全文検査と7不正入力検査も成功。**実機・独立レビューは未確認。** 六分類と許可設定の画面は未実装のためP11-01はPARTIALを維持し、転送や最終受入を完了扱いにしない。

### 要件差異修正: 参加希望先と画面行の対応（2026-09-06）

- 要件4.2/10.3、P4-01。ディスク上の画面定義を既存Luminaで読み、行内の名前欄と操作欄の親子関係を確認した。ゲーム入力やプロセスメモリには触れていない。資産本体は保存/配布せず、[調査記録](../research/ce-recruitment-targeting.md)に構造と識別ハッシュを残した。
- 本番ではAgentのID/名前/状態と、可視な同一行の名前/有効ボタンを一致させる。先頭ボタンへの代替を削除し、重複・不一致・未知の画面構造では操作しない。実登録IDの不一致は中止、送信と登録確認を区別、抽選失効と戦闘突入も希望先に限定した。既存の具体的event/input dataと連打防止・補給例外は保持した。
- `tests/Recruitment` は本番runner/collector/targetingをリンクして167件、exit 0。行順依存・二重申請・ボタン消失だけで成功する3不具合は同じ入力でexit 1。復元後も167件成功し、ソースと試験入力ハッシュ一致。30秒の外側制限でも1/1/1/0を確認した。
- 既存144件/94件、静的契約、日本語UI監査、packet適用2回の170ファイル差分不変、Debug/Release、diff検査はexit 0。詳細は[受入記録](ce-ui-binding-acceptance-20260906.md)。ローカルNU1900警告は維持し、監査を無効化していない。
- **P4-01の実機部分は未確認。** 動的な行/文字列書式、複数CEの実登録IDと転送を両フィールドで確認する。合成ホスト試験を実機の合格に読み替えない。独立レビューも未実施。テストfeedの現在版は変えていない。

### 追加依頼: 日本語の実機試験票と結果コピー（2026-09-06）

- 実施者向け成果物: [実機受入試験票](../testing/実機受入試験票.md)。55項目に準備・操作・合格条件・記録を設定し、両エリアと異常系を含めた。準備担当者が埋める環境票、損失上限、中止・復元、条件ごとの未判定/実施不可を明記した。
- 本文全体のLatin文字検査はexit 0。全角・互換文字・ローマ数字・アクセント文字・隠れたURLを含む7種類の不正入力を拒否した。公式用語の出典と要件対応は[担当者用の用語根拠](../research/japanese-review-terminology.md)に分離し、実施者向けには英語のコマンドやURLを渡さない。
- `LiveReviewRecorder`を追加。任意の記録開始/終了、試験番号/条件番号、実施者の判定、約1秒ごとの構造化状態を最大512件保存し、「試験結果をコピー」で共有する。チャット・名前・自由文のログ/状態・ユーザー入力は記録しない。欠落と上限超過を明示し、自動で合格にしない。既存の開発者向け診断コピーは維持。
- 本番recorderをリンクした`dotnet run --project tests/LiveReview/LiveReview.Tests.csproj`は94検査、exit 0。判定誤確定・未コピー上書き・無制限蓄積の3つの独立不具合は同じ検査がexit 1で拒否した。既存CIに同検査を追加し、以前の工程を削除していない。
- 初回の不具合入力検査でテスト実行器が例外を未捕捉にし、Windowsのエラーダイアログと実行ファイルロックを発生させた。該当テストプロセスだけを終了し、外側の例外境界でexit 1を返すよう修正した。元の検査本文・入力のSHA-256が一致することを確認し、再試行では3件ともダイアログなしのexit 1、その後正常入力は94件成功。製品のゲーム処理のクラッシュではない。
- 結果コピーの実機UI・実際のクリップボード貼り付け・第三者の日本語読解確認は未実施。試験票作成やmanagedテスト成功をP15実機受入のDONEへ読み替えない。条件・コマンドと限界は[追加依頼の受入記録](japanese-live-review-acceptance-20260906.md)に記載。
- `295d941`の[CI run 34021021656](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34021021656)は全工程success。その後、PowerShellの単一要素配列がobjectへ展開される配布一覧の不備を修正し、`dea238a`の[CI run 34021233543](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34021233543)も全工程success。配布候補`1.0.90.169`の出所とハッシュは[配布受入記録](test-feed-publication-20260906.md)を参照。
- `38e7f83`で`test-1.0.90.169`とfeature feedを同時公開。認証なしのraw feed取得はHTTP 200、配列形式・版番号・固定URLが一致し、公開ZIPのSHA-256はCI artifactと完全一致。公開commitの[CI run 34021457587](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34021457587)もsuccess。現在の配布版は`1.0.90.169`であり、以後のCI候補生成だけではfeedを変更しない。

### 要件差異修正: 遠隔CEの選択条件（2026-09-06）

- 要件4.2/4.3/10.3/18、P4-01、GAP-05/06。Register状態のCEを旧最低残時間設定・地図座標の有無で除外しない。実際のクリック可否は従来の有効ボタン取得と安全なSignUpRunnerへ任せる。
- Relic対象と現在地のterritoryが異なるのに、地域番号だけ一致した通常CEを選ぶ漏れも修正。大規模戦闘の明示ON時の最優先例外は維持した。
- `dotnet run --project tests/CeSelection/CeSelection.Tests.csproj`: 修正前exit 1（31/144）、修正後exit 0（144/144）。本番TargetSelector・RegionResolver・Configuration・CeSnapshot・Relic定義をリンクし、ホストサービスだけをstub化している。
- 静的契約・日本語UI監査・packet全適用2回・Debug/Release・diff検査はすべてexit 0。既存検査は維持し、同C#テストをCIへ追加した。ローカルbuildのNU1900警告は未解消。
- SignUpRunner・登録の単一実行guard・既存戦闘の本体はbaselineと同一。凍結条件と検証範囲は[CE eligibility acceptance](v1.1-ce-eligibility-acceptance-20260906.md)に記録した。
- **この時点ではP4-01は未完了。** 優先CEのIDは渡されるが、SignUpRunnerは異なる先頭候補を検出してもボタン順でクリックしていた。後続の「参加希望先と画面行の対応」で実装を修正した。両フィールドの実機受入は引き続き残る。
- 修正commit `c2a104f`の[CI run 34019213413](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34019213413)は全工程success。候補版は`1.0.90.167`。選択ロジック検査144件、packet適用・冪等性・静的契約・日本語UI・Debug/Release・version同期・artifact uploadを確認した。これは公開feed反映の証跡ではない。

### 要件差異修正: 移動中の反撃禁止（2026-09-06）

- 要件6・18、詳細設計6.3、P7-03 / audit GAP-02を対象に、移動中の`FightBack`分岐と選択UIを除去した。旧設定値はmigrationで`KeepRunning`へ補正し、runtimeはこの互換fieldを読まない。
- 到着後の`RunDefend`・`Commit`・敵検出latchの`UnderAttack`はbaselineと関数本体が完全一致。既存スカーミッシュ戦闘を変更していない。
- `python -B tools/validate_v110_contract.py`: 修正前exit 1、修正後exit 0。既存検証は変更せず追加のみ。5種類の独立した不具合入力を同一validatorがすべて拒否した。
- `python -B tools/audit_visible_japanese.py`、`dotnet build BozjaBuddyReborn.csproj -c Debug --no-restore --nologo`、同Release、`git diff --check`: すべてexit 0。NuGet脆弱性情報取得のNU1900警告あり、無効化はしていない。
- 凍結条件と証拠: [travel aggro acceptance](v1.1-travel-aggro-acceptance-20260906.md)。実機受入・独立した最終RCレビューは未実施であり、main merge合格を意味しない。
- 併せて古い監査のGAP-05/06とGAP-01を現コードで再確認した。P4-01とP11-01を`PARTIAL`へ訂正し、実装済みという誤った前提で次工程へ進まない。
- CI `34018208530` はpacket適用・冪等性・静的検証まで成功したが、Debugで`CS0101: RelicFarmStopMode`重複定義となった。既存packetがenum前へのXMLコメント追加を未適用と判定して再挿入したため、宣言間の追加コメントだけを除去した。互換field/valueの説明は維持し、packetや検証条件は変更していない。
- 修正後は`python -B tools/packets/run_all.py`をローカルで2回実行し、両方exit 0・追跡ソース差分不変。続く静的契約・日本語UI監査・Debug/Release再buildもすべてexit 0。
- 修正commit `2a796e7`の[CI run 34018380013](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34018380013)は全工程success。packet適用2回・冪等性・静的契約・日本語UI・Debug/Release・version同期・artifact uploadを確認した。候補 `1.0.90.165` はまだ公開feedに反映していない。

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

### RC前static acceptance拡張（2026-09-04）

- 死亡復旧の固定契約（スカーミッシュ30秒 / 移動10秒のRaise待機、死亡context別timer配線、live CE中のReturn禁止、TextAdvance元状態復元）を `tools/validate_v110_contract.py` へ追加した。
- 既存validatorが受理する現実的な独立mutant 5件に対し、拡張後validatorが全件を拒否することをmutation gateで確認した。
- manager検証でpacket replay、static contract、日本語UI audit、Debug / Release buildが成功し、再reviewに未解決findingはない。
- これは静的/ビルド検証の証跡であり、P8-03の実機死亡復旧試験は引き続き `WAITING_LIVE_TEST` とする。
- ソーシャル要求拒否について、Running中限定、同一dialog再検証、PartyInvite agent識別、subject/request二重判定、単一の拒否sink、Dalamud通知禁止をstatic contractへ追加し、成功時の拒否をWarning履歴へ記録する欠落も修正した。
- 変更前validatorが受理した独立mutant 9件を変更後validatorが全件拒否することを隔離mutation gateで確認した。manager検証ではpacket replay、static contract、日本語UI audit、Debug / Release buildが成功し、reviewerの未解決static findingはない。
- これは静的/ビルド検証の証跡であり、P10-03の誤拒否を含む実機確認は引き続き `WAITING_LIVE_TEST` とする。
- Activity / Relic selectorについて、大規模戦闘の絶対優先、最初のFarm対象を自動生成しないこと、選択素材完了後だけの自動継続、active Relic filterを通常・sticky・multibox追従の全経路で再検査すること、対象不在時に汎用待機地点より先にAethernet待機を試すことをstatic contractへ追加した。判定不能regionをRelic farming中はfail-closedにし、multibox受信目的地も既存selector policyへ通すよう修正した。
- 変更前validatorが受理した現実的な独立mutant 8件を変更後validatorが全件拒否することを隔離mutation gateで確認した。manager検証ではstatic contract、日本語UI audit、Debug / Release build、diff checkがすべてexit 0。初回reviewで指摘された`SelectFate`直接filter経路とAethernet検証表現を修復し、再reviewは未解決findingなしで合格した。
- これは静的/ビルド検証の証跡のみであり、CE・Relic・multibox・待機移動の実機項目を `DONE` へ昇格するものではない。既存の `WAITING_LIVE_TEST` は維持する。

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
- `MycItemBoxCallbackProbe` は実ゲーム自身のcallbackを採取するための診断手段として残す。config ON時だけ前後snapshotをread-onlyで採取し、重複eventは根拠から除外する。snapshot安定/差分は因果またはserver acknowledgementを示さない。

### BOCCHI / vnavmesh path cost

- BOCCHI/Ocelotが利用する `vnavmesh.Nav.Pathfind` / `vnavmesh.Nav.PathfindCancelable` の公開IPC契約を確認済み。
- BBRでは実移動とは別のbounded telemetryとして利用し、framework tickを同期blockしない。
- 同時queryは最大1、待機は最大750ms。timeout/Stop/対象変更ではcancel→Task終了確認→replanの順にする。
- Cache keyはterritoryを含み、別エリア同座標の測定値を再利用しない。
- full BOCCHI graph serviceはvendorしていない。候補比較はdeparture walkと選択inbound→goal walkのfreshな実経路長cacheを利用し、未測定/timeout時のみ水平距離へfail-openする。

### Enemy rank

`EnemyStrengthResolver` は、raw mappingが無くても territory + region + English BNpcName seedでI〜V/★を判定できる。判定不能はunknown=危険とするため、安全側の自動周回は先行可能。

## 次の実装優先順位

ユーザー確認/実機確認を要求せず、以下を順次進める。

1. P11-01 / audit GAP-01/03/04: 要件の6カテゴリ、必須依存表示、生存候補の持込/自動使用の独立UIを実装
2. P4-01: 行/ボタンの対応実装は機械検証済み。両フィールドの実登録ID/当選後の転送を最終実機受入で確認
3. P5-01: 安全なCache転送根拠の確定。公開ClientStructsは2026-09-06にもread構造のみで、転送関数は未定義
4. Initialize / rollback / 差分補充と未達の実機受入を完了し、main側の配布修正を保持して競合解消・RC検証・mergeへ進む

## 実機検証方針

実機確認は途中の通常ゲートにしない。最終RC付近でまとめて確認する。

それ以前に実データが必要になった場合は、プラグイン側に診断採取機能を先に実装し、他タスクを継続する。

## main merge

`main` には自動mergeしない。最終RC結果を提示し、ユーザーの明示承認後のみmergeする。
