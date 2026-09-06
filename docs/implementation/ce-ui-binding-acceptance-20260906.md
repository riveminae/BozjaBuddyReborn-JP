# P4-01: 参加先と画面行の対応

基準日: 2026-09-06。対象要件: 4.2 / 10.3、詳細設計4、実装計画P4-01。

## 調査段階の凍結条件

- ゲームの画面定義は既存のLuminaでディスクから読み取る。プロセス・ゲーム入力・callback・サーバー側数量へ触れない。出力は画面ノードの構造と識別用ハッシュだけに限定し、ゲーム資産本体を保存・配布しない。
- `dotnet run --project tests/UiLayoutProbe/UiLayoutProbe.csproj -- <sqpack-directory>` が終了コード0で、実際の参加画面のノード・親・型を示す。引数なしは終了コード1。いずれも未捕捉例外のダイアログを発生させない。
- 配列順・IDの大小・ボタン順を対応の根拠にしない。公開AgentのID/名前/状態と、同じ画面行内の名前/ボタンの対応を確認してから本番処理を変更する。
- 静的レイアウトの確認だけでは、動的なタイトルとボタンの対応や実際の登録成功を証明しない。観測できない条件は未確認のまま残す。

## 本番変更の凍結条件

画面定義の読み取り成功後、本番変更前に以下を凍結した。

1. 本番のmanaged対象選択をリンクした `dotnet run --project tests/Recruitment/Recruitment.Tests.csproj` が終了コード0。希望IDの行だけを選び、Agentとボタンの順序を独立に入れ替えても同じ結果となる。重複ID/名前/該当ボタン、未知ID、状態違い、空の名前/ハンドル、未対応ラベルは操作しない。参加希望と戦闘突入のラベルを前方一致で混同しない。
2. 同試験で本番runnerをホスト置換して動かし、既登録時の二重申請禁止、クリック後の登録ID確認、一致しない登録IDの中止、対象行だけの抽選失効判定、戦闘突入先の一致、ボタン消失だけを成功としないこと、既存の補給待ちと連打防止を検証する。
3. 実行時アダプターはAgentの公開ID/名前/状態と、画面定義で確認した行部品の名前欄・操作欄の親子関係を照合する。非表示の親を持つ行/ボタン、無効ボタンは候補にしない。実行時の構造が異なる場合は別の行へ代替しない。このunsafe境界の実機確認は未実施として残す。
4. `python -B tools/validate_v110_contract.py`、`python -B tools/audit_visible_japanese.py`、既存の選択/記録試験、packet全適用2回の差分不変、Debug/Release、`git diff --check` はすべて終了コード0。既存検査・入力は変更しない。
5. 意図的に誤った行を返す不具合、二重申請許可、ボタン消失だけで成功する不具合を同じ試験が終了コード1で拒否する。正常ソースへ復元後は終了コード0。検査器の外側で各試行の時間を制限する。
6. 南方ボズヤ戦線とザトゥノル高原で、複数受付から選択IDと実際の登録IDが一致し、抽選中の通常行動と当選後の転送が成立することを試験票で確認する。これは機械試験の成功では代替しない。

## 証跡

以下は機械試験とディスク上の画面定義に限定した証跡。条件6の実機受入は未実施。

| コマンド | 終了コード | 観測 |
| --- | --- | --- |
| `dotnet run --project tests/UiLayoutProbe/UiLayoutProbe.csproj -- <sqpack-directory>` | 0 | 行component 1007内のtitle 5・操作欄9・button 10/11/12の親子関係を取得 |
| `dotnet run --no-build --project tests/UiLayoutProbe/UiLayoutProbe.csproj` | 1（期待通り） | 引数不足を捕捉し、ダイアログではなくエラー出力 |
| `dotnet run --no-restore --project tests/Recruitment/Recruitment.Tests.csproj` | 0 | 167件成功。本番runner/collector/targetingをリンク、ゲームサービスだけ置換 |
| 同コマンド、先頭行へ誤って代替する不具合 | 1（期待通り） | check 2: event and UI orders are independent |
| 同コマンド、既登録時にもRegisterを許す不具合 | 1（期待通り） | check 156: matching existing registration does not click Register |
| 同コマンド、ボタン消失だけでDoneにする不具合 | 1（期待通り） | check 151: button disappearance alone is not success |
| 同コマンド、正常ソース復元後 | 0 | 167件再成功。下記5ファイルのハッシュ一致 |
| `python -B tools/packets/run_all.py` 2回 | 各0 | 下記の厳格な比較で170ファイルのSHA-256が各適用前後で一致 |
| `python -B tools/validate_v110_contract.py` | 0 | 既存契約を変更せず成功 |
| `python -B tools/audit_visible_japanese.py` | 0 | 既存の日本語表示検査成功 |
| `dotnet run --no-restore --project tests/CeSelection/CeSelection.Tests.csproj` | 0 | 144件成功、既存入力変更なし |
| `dotnet run --no-restore --project tests/LiveReview/LiveReview.Tests.csproj` | 0 | 94件成功、既存入力変更なし |
| `dotnet build BozjaBuddyReborn.csproj -c Debug --no-restore --nologo` | 0 | 警告NU1900のみ |
| `dotnet build BozjaBuddyReborn.csproj -c Release --no-restore --nologo` | 0 | 警告NU1900のみ |
| `git diff --check` | 0 | 空白エラーなし |

使用SDKは10.0.400、Pythonは3.13。NU1900はネットワーク制約下の脆弱性情報取得失敗であり、監査は無効化していない。最初のホスト置換のビルドでは診断サービスstubが不足しCS0103/CS0117となった。本番に合わせてstubを補い、検査本体を弱めず解消した。

### 検査入力の凍結と復元

不具合を入れる直前と、3件を拒否して本番ソースを戻した後で、以下のSHA-256が一致した。

- `tests/Recruitment/Program.cs`: `964d4641f21b6cf3ab3444c8235f9643031642b207417a13dcabfb15200ce2f5`
- `tests/Recruitment/HostStubs.cs`: `181d7f1c5e34fa843d2cf0bbf3b1df68afe0c6c2f0db33450eb6981258ff7cfd`
- `tests/Recruitment/Recruitment.Tests.csproj`: `02938e47f12ea42a6904cc7dd9616bb9740caaecc632d8675e3bfd1248b4d377`
- `Automation/RecruitmentTargeting.cs`: `94214348d4692d22bad2f0d756b58222ad3599fa551c6aba28cef97808842e1b`
- `Automation/SignUpRunner.cs`: `027d19e14d581e1da40b17ec23adb5cbfe5fd67057c80a3ba542cac4a8779e80`

通常実行に加え、同じ3不具合と復元後の試験を次の外側の30秒制限で再実行した。すべて約3秒以内で終了し、コードは順に1/1/1/0。`$dotnetPath` は検証環境の既存SDK実行ファイルを指定する。終了コードは子プロセスの値をそのまま返し、意図的な失敗を成功へ変換しない。

```powershell
$testStart = [System.Diagnostics.ProcessStartInfo]::new($dotnetPath)
$testStart.UseShellExecute = $false
$testStart.CreateNoWindow = $true
$testStart.RedirectStandardOutput = $true
$testStart.RedirectStandardError = $true
foreach ($testArgument in @('run', '--no-restore', '--project', 'tests/Recruitment/Recruitment.Tests.csproj')) {
    $testStart.ArgumentList.Add($testArgument)
}
$testProcess = [System.Diagnostics.Process]::Start($testStart)
$testOutput = $testProcess.StandardOutput.ReadToEndAsync()
$testError = $testProcess.StandardError.ReadToEndAsync()
if (-not $testProcess.WaitForExit(30000)) {
    $testProcess.Kill($true)
    $testProcess.WaitForExit()
    Write-Output 'FAIL: 30-second check timeout'
    exit 124
}
Write-Output $testOutput.GetAwaiter().GetResult()
Write-Output $testError.GetAwaiter().GetResult()
exit $testProcess.ExitCode
```

### packet比較の注意

最初の補助比較は、日本語パスがGitのquotePathでエスケープされ、PowerShellの非終了エラーを見落とした。この比較の表示は合格根拠に採用していない。`$ErrorActionPreference='Stop'` と `git -c core.quotePath=false ls-files` に直し、追跡ファイルと新規本番/試験4ファイル、計170件を各適用前後で比較し直した。存在しないパスを与える負の確認は終了コード1で停止した。

### レビューと未実施

- 直接差分レビュー: 既存スカーミッシュ戦闘・Controller・転送処理は変更なし。新規依存なし。新規UI文字列は日本語。Agentと画面の配列順を結び付けず、実行時の型/親子関係/名前を確認する。ゲーム資産本体は含めない。
- 独立レビューは未実施。利用者が停止したオーケストレーションを再開していない。直接レビューを独立レビューとは記録しない。
- 未実施: 実際のクライアント構造、描画中の文字列書式、複製行、サーバー側の登録確認と転送、南方/ザトゥノルの受入。
- テストfeedは既存の公開版のまま。CI候補生成だけで自動更新しない。安定版昇格・main mergeの合格ではない。
