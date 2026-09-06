using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BozjaBuddyReborn.Automation;

public enum ReviewOutcome { Undecided, Pass, Fail, Unavailable, Aborted }

public readonly record struct ReviewEngagement(ushort Id, int State, byte Progress, uint SecondsLeft);

// Deliberately no names, raw status/log strings, character identifiers or user text.
// Enum values are copied from the host by the framework adapter; unknown values render as unknown.
public sealed record ReviewSample(
    uint Territory, bool Running, int State, int Route, int Role, int? HpPercent, bool Mounted,
    int ObjectiveKind, uint ObjectiveId, bool CeAvailable, ushort RegisteredEventId,
    bool InventoryAvailable, int PotionKits, int Reraisers, int MainHealUnits, int DefenseUnits,
    bool NavigationAvailable, bool RotationAvailable, bool AvoidanceAvailable,
    bool TransferAvailable, bool RecoveryAvailable, IReadOnlyList<ReviewEngagement> Engagements);

/// <summary>
/// Opt-in sampled evidence, never an automated test verdict. Framework capture and UI export
/// share a lock. Samples are bounded and copied; capture failures never affect gameplay.
/// No persistence, log scraping, clipboard access or external transmission occurs here.
/// </summary>
public sealed class LiveReviewRecorder(Version version)
{
    private const int Capacity = 512;
    private readonly object _gate = new();
    private readonly Queue<(DateTimeOffset Time, ReviewSample Sample)> _samples = new();
    private bool _hasSession, _recording, _copied;
    private int _test, _variant, _dropped, _failures, _gaps;
    private DateTimeOffset _start, _end;
    private long? _lastTick;

    public bool HasSession { get { lock (_gate) return _hasSession; } }
    public bool IsRecording { get { lock (_gate) return _recording; } }
    public bool CanStart { get { lock (_gate) return !_recording && (!_hasSession || _copied); } }

    public bool Start(int test, int variant, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_recording || (_hasSession && !_copied) || test is < 1 or > 9999 || variant is < 1 or > 9999)
                return false;
            _test = test;
            _variant = variant;
            _start = now;
            _end = default;
            _hasSession = _recording = true;
            _copied = false;
            _dropped = _failures = _gaps = 0;
            _lastTick = null;
            _samples.Clear();
            return true;
        }
    }

    public void Capture(DateTimeOffset now, long monotonicMs, Func<ReviewSample> read)
    {
        lock (_gate)
        {
            if (!_recording || (_lastTick is { } previous && monotonicMs - previous < 1000))
                return;
            if (_lastTick is { } last && monotonicMs - last > 2000)
                _gaps++;
            _lastTick = monotonicMs;
            try
            {
                var sample = read();
                var events = new ReviewEngagement[Math.Min(sample.Engagements.Count, 16)];
                for (var i = 0; i < events.Length; i++) events[i] = sample.Engagements[i];
                if (sample.Engagements.Count > events.Length) _failures++;
                _samples.Enqueue((now, sample with { Engagements = events }));
                if (_samples.Count > Capacity)
                {
                    _samples.Dequeue();
                    _dropped++;
                }
            }
            catch (Exception)
            {
                // Exception messages may contain personal data. Count only, never copy them.
                _failures++;
            }
        }
    }

    public void End(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_recording) return;
            _end = now;
            _recording = false;
            _copied = false;
        }
    }

    // The UI calls this only AFTER a successful clipboard write. An active copy is provisional.
    public void MarkCopied() { lock (_gate) { if (!_recording) _copied = true; } }
    public void MarkOutcomeChanged() { lock (_gate) _copied = false; }

    public static string OutcomeName(ReviewOutcome outcome) => outcome switch
    {
        ReviewOutcome.Pass => "合格",
        ReviewOutcome.Fail => "不合格",
        ReviewOutcome.Unavailable => "実施不可",
        ReviewOutcome.Aborted => "中止",
        _ => "未判定",
    };

    public string BuildReport(ReviewOutcome outcome)
    {
        lock (_gate)
        {
            if (!_hasSession) return "実機試験結果\n判定: 未実施\n試験記録はまだ開始されていません。\n";
            var sb = new StringBuilder();
            sb.AppendLine("実機試験結果");
            sb.AppendLine(FormattableString.Invariant($"試験番号: {_test} / 条件番号: {_variant}"));
            sb.AppendLine(FormattableString.Invariant($"版番号: {version.Major}.{version.Minor}.{version.Build}.{version.Revision}"));
            sb.AppendLine($"開始日時: {Time(_start)}");
            sb.AppendLine($"終了日時: {(_recording ? "記録中" : Time(_end))}");
            var verdict = _recording ? "未判定（記録中）"
                : _samples.Count == 0 && outcome == ReviewOutcome.Pass ? "未判定（観測なし）"
                : OutcomeName(outcome) + "（実施者の判断）";
            sb.AppendLine($"判定: {verdict}");
            sb.AppendLine(FormattableString.Invariant($"保存件数: {_samples.Count} / 上限超過で除外: {_dropped} / 取得失敗: {_failures} / 観測間隔の空白: {_gaps}"));
            if (_dropped > 0 || _failures > 0 || _gaps > 0)
                sb.AppendLine("注意: 記録に欠落あり。この記録だけでは試験全体の合格を確認できません。");
            sb.AppendLine("約1秒間隔の状態記録です。短い操作、在庫移動の確定、全ての警告を記録するものではありません。");
            sb.AppendLine("目的地・在庫・戦闘一覧は直前の周回処理の評価値です。停止中や待機中は更新されていない場合があります。");
            sb.AppendLine("在庫の数値は自動使用を許可した回復・防御手段の評価値です。使用を禁止した品の実所持数は含めず、主回復と防御には装填済みの残り使用回数も含みます。");
            sb.AppendLine("名前・会話・自由文の動作説明・開発者向け記録は含みません。版の再読み込みで記録は消えます。");
            sb.AppendLine("条件の説明・期待した動作・実際の動作・画像番号・後片付け結果は、貼り付け先に追記してください。");
            sb.AppendLine("動作記録:");
            foreach (var (time, s) in _samples)
            {
                sb.AppendLine($"【{Time(time)}】");
                sb.AppendLine(FormattableString.Invariant($"場所: {Territory(s.Territory)} / 自動周回: {On(s.Running)} / 状態: {State(s.State)} / 移動: {Route(s.Route)}"));
                sb.AppendLine(FormattableString.Invariant($"役割: {Role(s.Role)} / 残り体力: {(s.HpPercent is { } hp ? hp.ToString(CultureInfo.InvariantCulture) + "%" : "読み取り待ち")} / マウント騎乗: {On(s.Mounted)}"));
                sb.AppendLine(FormattableString.Invariant($"目的地: {Objective(s.ObjectiveKind)} 対象番号{s.ObjectiveId}"));
                sb.AppendLine("参加登録先: " + (!s.CeAvailable ? "読み取り待ち" : s.RegisteredEventId == 0 ? "なし" : "対象番号" + s.RegisteredEventId.ToString(CultureInfo.InvariantCulture)));
                if (s.InventoryAvailable)
                    sb.AppendLine(FormattableString.Invariant($"レジスタンスポーションキット: {s.PotionKits} / レジスタンスリレイザー: {s.Reraisers} / 主回復残数: {s.MainHealUnits} / ロスト・ウォール: {s.DefenseUnits}"));
                else sb.AppendLine("在庫: 読み取り待ち");
                sb.AppendLine($"補助機能: 経路探索={On(s.NavigationAvailable)} / 戦闘操作={On(s.RotationAvailable)} / 攻撃予兆回避={On(s.AvoidanceAvailable)} / 転送={On(s.TransferAvailable)} / 蘇生・帰還確認={On(s.RecoveryAvailable)}");
                foreach (var ce in s.Engagements)
                    sb.AppendLine(FormattableString.Invariant($"クリティカルエンゲージメント: 対象番号{ce.Id} / {EngagementState(ce.State)} / 進行度{ce.Progress}% / 残り{ce.SecondsLeft}秒"));
            }
            return sb.ToString();
        }
    }

    private static string Time(DateTimeOffset time) => time.ToString("yyyy年MM月dd日 HH時mm分ss秒 zzz", CultureInfo.InvariantCulture);
    private static string On(bool value) => value ? "有効" : "無効";
    private static string Territory(uint value) => value switch { 920 => "南方ボズヤ戦線", 975 => "ザトゥノル高原", _ => "対応エリア外" };
    private static string State(int value) => value switch { 0 => "停止中", 1 => "実行待ち・停止理由あり", 2 => "目的地選択中", 3 => "移動中", 4 => "待機中", 5 => "戦闘中", _ => "不明" };
    private static string Route(int value) => value switch { 0 => "直行", 1 => "エーテライトへ移動", 2 => "転送中", 3 => "転送先から移動", 4 => "直行へ切り替え", 5 => "デジョン", 6 => "転送用の補助機能を待機", 7 => "経路を計算中", _ => "不明" };
    private static string Role(int value) => value switch { 1 => "タンク", 2 => "ヒーラー", 3 => "攻撃を主な役割とするジョブ", _ => "不明" };
    private static string Objective(int value) => value switch { 0 => "なし", 1 => "クリティカルエンゲージメント", 2 => "スカーミッシュ", _ => "不明" };
    private static string EngagementState(int value) => value switch { 0 => "未開催", 1 => "参加募集中", 2 => "開戦準備中", 3 => "戦闘中", _ => "不明" };
}
