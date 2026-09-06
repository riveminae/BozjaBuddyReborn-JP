# ロストアクション許可設定の実行経路

基準日: 2026-09-06。起点: `84f8c17`。要件8.1/8.2、9、10、14.2、詳細設計7.2/8、P7-01/P6/P11-01。

## 設定画面の前に見つかった差異

- 通常のHolsterDriverとパーティ支援は、既存の対象リストを読み、独立した自動使用許可を見ていない。
- 読み込み待ちの途中で許可を取り消しても、発動直前に再確認しない。
- SupplyManagerは手元の回復手段の計数に持込許可を使う。持込しないが自動使用可能な在庫を、回復不能と誤判定しうる。
- SurvivalPolicyは名前の索引がまだ読めないとき、深度の高い秘薬か判定できないことを通常品と同じ許可へ変換する。
- パーティ支援にはマウント中の停止条件がなく、移動中のアクション禁止要件に反する経路がある。

この共通処理を直してから六分類の設定画面へ許可トグルを接続する。設定画面そのものを完了したとは扱わない。

## 凍結した受入条件

1. `dotnet run --project tests/SurvivalPermissions/SurvivalPermissions.Tests.csproj` が終了コード0。本番の許可方針、通常/生存自動使用、パーティ支援、補給判定をリンクし、ホストサービスだけを置換する。
2. 持込と自動使用の四通りは独立。既存の明示値を保持し、通常生存候補の既定は許可、Deep系の既定は両方不許可。名前データが取得不能/不完全なら、明示許可のない対象を勝手に許可しない。取得可能になれば再試行する。
3. 自動使用不許可は通常・生存・支援の新規使用と待機中の発動を止める。持込不許可だけでは手元の自動使用可能な在庫を使うことを妨げない。通常の優先リスト、発動の明示許可、支援の明示開始は保持する。
4. マウント中は各自動経路から読み込み/アイテム使用/発動を送らない。待機中の発動も破棄する。既存戦闘制御を変更しない。
5. 自動使用可能な手元の回復在庫・装填済みチャージ・既存の自動回復効果を危機判定へ反映する。持込許可とは分離する。補給先候補は持込許可が必要で、critical回復候補は自動使用も許可されたものに限る。通常の欠品記録とインスタンス単位の再試行抑制は維持する。
6. 同じ検査入力で、許可を無視する使用、取消後の発動、持込と利用可能在庫の混同、マウント中の支援をそれぞれ不具合として入れ、終了コード1で拒否する。外側で各試行を30秒以内に制限し、正常ソースへ復元後は終了コード0。未捕捉例外によるダイアログは禁止。
7. 既存405件のC#検査、静的契約、日本語UI監査、packet二回適用の差分不変、Debug/Release、`git diff --check` はすべて終了コード0。既存検査・入力・packetを弱めず維持する。
8. 実ゲームでの使用・補給転送・実機受入は未確認として残す。機械検証をゲーム内の成立に読み替えない。

## 証跡

### 本番経路と判定範囲

- 許可判定は既存の二つの辞書を使う。通常使用・生存使用・パーティ支援で自動使用許可を確認し、装填待ちの発動直前にも再確認する。既存の対象リスト、通常発動の追加許可、支援の明示開始は維持した。
- 生存・通常使用はホルダーが空でも装填済みアクションを使用できる。マウント中は支援の装填・発動も止める。`BozjaController`、既存戦闘制御、設定スキーマ、転送処理は変更していない。
- 補給評価は「自動使用できる回復手段」と「持込を許可された品の実在庫不足」を分離した。使用禁止品の所持を危機からの回復手段とせず、所持している使用禁止品を何度も補給しに戻らない。補給先での欠品記録・インスタンス単位の抑制は保持した。
- メイン画面の補足、試験コピー、実機試験票へ、在庫表示は自動使用可能な手段の評価値である旨を追加した。実所持数の全量や転送確定の証拠ではない。

### 機械検証

ローカルの .NET 10.0.400 / Python 3.13 を使用。各コマンドの終了コードを個別に確認した。

| コマンド | 終了コード | 観測結果 |
|---|---:|---|
| `dotnet run --no-restore --project tests/SurvivalPermissions/SurvivalPermissions.Tests.csproj`（修正前） | 1 | 検査7、分類不能時に希少品を許可する問題を拒否 |
| 同上（修正後・以下の不具合試行を復元後） | 0 | 103検査成功。本番方針と三つの利用経路をリンク、ゲーム境界のみ合成 |
| `dotnet run --no-restore --project tests/CeSelection/CeSelection.Tests.csproj` | 0 | 144/144 |
| `dotnet run --no-restore --project tests/LiveReview/LiveReview.Tests.csproj` | 0 | 94/94。失敗は外側の例外境界から終了コード1を返す |
| `dotnet run --no-restore --project tests/Recruitment/Recruitment.Tests.csproj` | 0 | 167/167 |
| `python -B tools/validate_v110_contract.py` | 0 | 生存・補給を含む既存静的契約成功 |
| `python -B tools/audit_visible_japanese.py` | 0 | 直接記述された画面文字列の既存監査成功 |
| `python -B tools/packets/run_all.py`（正規化確認後の連続2回） | 0 / 0 | 各回176入力ファイルのSHA-256が前後一致 |
| `dotnet build BozjaBuddyReborn.csproj -c Debug --no-restore --nologo` | 0 | エラー0、NU1900警告1 |
| `dotnet build BozjaBuddyReborn.csproj -c Release --no-restore --nologo` | 0 | エラー0、NU1900警告1 |
| `git diff --check` | 0 | 空白エラーなし |

NU1900はローカルから脆弱性情報を取得できない警告。監査設定を無効化しておらず、脆弱性情報取得が成功したとは扱わない。

試験票の検査は終了コード0。55試験が連番で、各試験に準備・操作・合格条件・記録がある。本文全体をNFKD正規化し、Unicode名にLATINを含む文字がないことを確認した。同じ判定に `Start`、全角文字、ローマ数字、数式文字、アクセント文字2種、隠れたURLを追加した7入力はすべて拒否した。再実行コマンドは次のとおり。

```powershell
python -B -c "from pathlib import Path; import re,unicodedata as u; s=Path('docs/testing/実機受入試験票.md').read_text(encoding='utf-8'); cases=re.findall(r'^### 試験(\d{2})[^\n]*\n(.*?)(?=^### |^## |\Z)',s,re.M|re.S); assert [n for n,_ in cases]==[f'{i:02}' for i in range(1,56)]; assert all(all(label in body for label in ('準備：','操作：','合格条件：','記録：')) for _,body in cases); bad=lambda text: any('LATIN' in u.name(c,'') for c in u.normalize('NFKD',text)); assert not bad(s); assert all(bad(s+m) for m in ('Start','ＣＥ','Ⅳ','𝑥','é','ł','[日本語](https://example.com)')); print('PASS: 55 complete cases; no Latin; 7 forbidden-text mutants rejected')"
```

### 不具合入力を拒否できることの確認

同じ103検査のソース・入力を固定し、以下を本番ソースへ一つずつ入れて復元した。各試行は上記 `dotnet run --no-restore` を `ProcessStartInfo` から起動し、出力を非同期で回収しながら外側で `WaitForExit(30000)` を実施した。期限切れなら起動した試験プロセスのみを停止して124、通常は子プロセスの終了コードを返す。全試行は期限内に終了し、未捕捉例外のダイアログは発生していない。

| 入れた不具合 | 終了コード | 拒否した検査 |
|---|---:|---|
| 通常使用の自動使用許可を無視 | 1 | 13: ordinary item use obeys only auto-use permission |
| 装填後の許可取消を無視 | 1 | 22: ordinary pending permission revocation stops the press |
| 主回復の使用可能数を持込許可で数える | 1 | 32: critical recovery counts usable held heals independently of bring |
| マウント中の支援停止条件を無効化 | 1 | 29: mounted support never loads or fires |
| 分類不能を既定許可へ変換 | 1 | 7: missing classification never enables a rare item |
| 空のホルダーを理由に生存処理を早期終了 | 1 | 45: loaded-only survival recovery uses charges with an empty holster and independent permissions |
| 全復元後 | 0 | 103検査成功 |

試行を通じて固定したSHA-256:

- `tests/SurvivalPermissions/Program.cs`: `DF5A271FAE0964275C8DC71EEFDA659D1CADA54F360C1D841B640E9367280821`
- `tests/SurvivalPermissions/HostStubs.cs`: `2817603696DC33BB983811280CC112C46221C3D70F7EB9898212E182C4771B4E`
- `tests/SurvivalPermissions/SurvivalPermissions.Tests.csproj`: `DEF2A5AC22E3DE8A5582BFB4D6992B31CC8B15913098E0E2E0FBDEEA7A85C5D0`

復元時の `SupplyManager.cs` は復元箇所5行の改行のみCRLFからLFになった。メモリ上でその改行だけを戻すと試行前SHA-256 `EA09B673B2C2A5160A9E327B9CB8C64F60152F5C9CC52E9E9B1E320F7FE9A842` に一致した。ソース内容差がないことを確認し、最終ファイルは `E9DB441FE7A0BAED9364FAF5D61E445452CB9664B8264EC1FF2562C27D8DAC1A`。他の本番3ファイルは復元前後のハッシュ一致。試験入力の変更で成功にしていない。

最初のpacket再適用では、既存packetが `MainWindow.cs` の混在改行を正規化し、厳密な前後比較は終了コード1だった。意味的な変更は予定した補足2行のみで、重複挿入や取消はなかった。内容確認後に改めて2回実行し、各回176入力のハッシュ一致を確認した。ファイル読込失敗を成功扱いしないよう `$ErrorActionPreference='Stop'` と `git -c core.quotePath=false ls-files` を用いた。既存packetや検証条件は変更していない。

### 未確認・次工程

- 直接レビューでは上記差分に未解決の重大指摘なし。ただし独立したゼロコンテキストレビューは未実施。実装者の確認を独立レビューとしない。
- 実ゲームの装填・使用、マウント中の挙動、補給転送、画面の操作・クリップボード貼付、第三者の読解、両フィールドの長時間受入は未確認。
- 六分類の設定画面と独立許可の操作画面は次工程。P11-01はPARTIALを維持する。安全な転送機構が未確立のP5/P6を完了へ変えない。
- 本書は安定版昇格の宣言ではない。公開feed・mainの変更も含まない。
