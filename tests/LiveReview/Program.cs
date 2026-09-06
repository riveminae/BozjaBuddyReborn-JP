using System.Globalization;
using System.Text;
using BozjaBuddyReborn.Automation;

try
{
var checks = 0;
void Check(bool ok, string label)
{
    checks++;
    if (!ok) throw new Exception(label);
}
bool JapaneseOnly(string value) => !value.Normalize(NormalizationForm.FormKD).Any(c =>
    c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

var start = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.FromHours(9));
var ce = new[] { new ReviewEngagement(12, 1, 79, 15) };
var sample = new ReviewSample(920, true, 3, 1, 1, 55, true, 2, 42, true, 12,
    true, 2, 1, 5, 1, true, true, true, false, false, ce);
var recorder = new LiveReviewRecorder(new Version(1, 0, 90, 168));
Check(!recorder.HasSession && !recorder.IsRecording, "initially inactive");
Check(recorder.BuildReport(ReviewOutcome.Pass).Contains("未実施"), "no automatic pass before start");
Check(!recorder.Start(0, 1, start), "reject invalid test number");
Check(!recorder.Start(1, 0, start), "reject invalid variant number");
Check(recorder.Start(27, 2, start), "start");
Check(!recorder.Start(28, 1, start), "cannot replace active session");
var calls = 0;
recorder.Capture(start, 1000, () => { calls++; return sample; });
recorder.Capture(start.AddMilliseconds(200), 1200, () => { calls++; return sample; });
Check(calls == 1, "one-second cadence");
ce[0] = new ReviewEngagement(99, 3, 100, 0);
var active = recorder.BuildReport(ReviewOutcome.Pass);
Check(active.Contains("未判定（記録中）") && !active.Contains("判定: 合格"), "active cannot be final pass");
Check(active.Contains("対象番号12") && !active.Contains("対象番号99"), "immutable copied event array");
Check(active.Contains("試験番号: 27") && active.Contains("条件番号: 2"), "trial identifiers");
Check(active.Contains("1.0.90.168") && active.Contains("南方ボズヤ戦線"), "version and territory");
Check(active.Contains("参加登録先: 対象番号12"), "actual registration separate from selected objective");
Check(active.Contains("目的地: スカーミッシュ 対象番号42"), "selected objective");
Check(active.Contains("レジスタンスポーションキット: 2"), "official inventory name");
Check(active.Contains("ロスト・ウォール: 1"), "official defense name");
Check(JapaneseOnly(active), "Japanese report");

recorder.Capture(start.AddSeconds(1), 2000, () => throw new Exception("PRIVATE_NAME secret/path"));
recorder.Capture(start.AddSeconds(5), 6000, () => sample with { Territory = 975, Mounted = false });
recorder.End(start.AddSeconds(6));
var report = recorder.BuildReport(ReviewOutcome.Fail);
Check(report.Contains("判定: 不合格"), "human selected outcome");
Check(report.Contains("取得失敗: 1") && report.Contains("観測間隔の空白: 1"), "capture gaps explicit");
Check(!report.Contains("PRIVATE") && !report.Contains("secret"), "exception text never exported");
Check(report.Contains("ザトゥノル高原"), "both fields");
recorder.Capture(start.AddSeconds(7), 8000, () => throw new Exception("should not execute"));
Check(recorder.BuildReport(ReviewOutcome.Fail) == report, "end freezes samples");
Check(!recorder.Start(28, 1, start), "uncopied completed session protected");
Check(recorder.BuildReport(ReviewOutcome.Undecided).Contains("判定: 未判定"), "default undecided");
Check(recorder.BuildReport(ReviewOutcome.Unavailable).Contains("判定: 実施不可"), "unavailable outcome");
Check(recorder.BuildReport(ReviewOutcome.Aborted).Contains("判定: 中止"), "aborted outcome");
recorder.MarkCopied();
Check(recorder.Start(28, 1, start.AddMinutes(1)), "copied session may be replaced");
Check(!recorder.BuildReport(ReviewOutcome.Undecided).Contains("対象番号12"), "no previous-session leakage");
recorder.End(start.AddMinutes(1));
Check(recorder.BuildReport(ReviewOutcome.Pass).Contains("未判定（観測なし）"), "empty session cannot pass");
recorder.MarkCopied();
Check(recorder.Start(37, 1, start), "restart after empty copy");
for (var i = 0; i < 514; i++)
    recorder.Capture(start.AddSeconds(i), i * 1000L, () => sample with { ObjectiveId = (uint)i });
recorder.End(start.AddSeconds(514));
report = recorder.BuildReport(ReviewOutcome.Pass);
Check(report.Contains("保存件数: 512") && report.Contains("上限超過で除外: 2"), "bounded buffer and truncation");
Check(report.Contains("記録に欠落あり"), "partial evidence explicitly marked even with human pass");
Check(report.Contains("判定: 合格（実施者の判断）"), "manual verdict not machine acceptance");
Check(report.Contains("対象番号513") && !report.Contains("目的地: スカーミッシュ 対象番号0\n"), "latest samples retained");
Check(JapaneseOnly(report), "Japanese truncated report");

// Every enum-like input, culture and unknown value must stay Japanese and not throw.
foreach (var culture in new[] { "ja-JP", "en-US", "ar-SA" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    for (var state = -1; state <= 8; state++)
    {
        var r = new LiveReviewRecorder(new Version(1, 1, 0, 0));
        r.Start(1, 1, start);
        r.Capture(start, 0, () => sample with {
            State = state, Route = state, Role = state, ObjectiveKind = state,
            Territory = 999, HpPercent = null, InventoryAvailable = false,
            CeAvailable = false, Engagements = new[] { new ReviewEngagement(1, state, 0, 0) }
        });
        r.End(start.AddSeconds(1));
        var text = r.BuildReport((ReviewOutcome)999);
        Check(JapaneseOnly(text) && text.Contains("未判定"), "culture/unknown safely rendered");
        Check(text.Contains("在庫: 読み取り待ち") && text.Contains("参加登録先: 読み取り待ち"), "unknown not zero");
    }
}
Console.WriteLine($"PASS: {checks} live-review recorder checks");
}
catch (Exception failure)
{
    // Expected assertion failures must be a nonzero CLI exit, never a desktop crash dialog.
    Console.Error.WriteLine($"FAIL: {failure.Message}");
    Environment.ExitCode = 1;
}
