using System;
using System.Numerics;
using System.Reflection;
using System.Text;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using BozjaBuddyReborn.Relic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

using BozjaBuddyReborn;

namespace BozjaBuddyReborn.Windows;

public sealed class MainWindow : Window
{
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Blue = new(0.50f, 0.75f, 1.00f, 1f);

    private readonly Configuration _config;
    private readonly BozjaController _controller;
    private readonly CombatDirector _director;
    private readonly NavmeshIpc _navmesh;
    private readonly MultiboxLink _link;
    private readonly CeCatalog _catalog;
    private static readonly Version AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    private static bool IsTestBuild => AssemblyVersion.Major == 1 && AssemblyVersion.Minor == 0 && AssemblyVersion.Build == 90;

    public MainWindow(
        Configuration config,
        BozjaController controller,
        CombatDirector director,
        NavmeshIpc navmesh,
        MultiboxLink link,
        CeCatalog catalog)
        : base("Bozja Buddy Reborn###BozjaBuddyRebornMain")
    {
        _config = config;
        _controller = controller;
        _director = director;
        _navmesh = navmesh;
        _link = link;
        _catalog = catalog;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    public override void Draw()
    {
        DrawControls();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##bbr_tabs"))
        {
            if (ImGui.BeginTabItem(Loc.T("Engagements", "CE / スカーミッシュ")))
            {
                DrawFieldState();
                ImGui.Separator();
                DrawEngagementTable();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Loc.T("Multibox", "マルチボックス")))
            {
                DrawMultibox();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Loc.T("Dependencies", "依存関係")))
            {
                DrawDependencies();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawControls()
    {
        var running = _controller.Running;

        if (IsTestBuild)
        {
            ImGui.TextColored(Yellow, $"テスト版 v{AssemblyVersion} を使用中です。");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("不具合時は Dalamud のカスタムプラグインリポジトリから Test repo を無効化/削除し、Stable repo を有効化して Bozja Buddy Reborn JP を更新または再インストールしてください。");
        }

        if (ImGui.Button(running ? Loc.T("Stop", "停止") : Loc.T("Start", "開始"), new Vector2(90, 0)))
            _controller.Toggle();

        ImGui.SameLine();
        var stateColour = _controller.State switch
        {
            ControllerState.Engaged => Green,
            ControllerState.Travelling => Blue,
            ControllerState.Holding => Yellow,
            ControllerState.Blocked => Red,
            _ => Grey,
        };
        ImGui.TextColored(stateColour, Loc.Controller(_controller.State));

        ImGui.SameLine();
        ImGui.TextUnformatted("-");
        ImGui.SameLine();
        ImGui.TextWrapped(Loc.Runtime(_controller.Status));

        if (_controller.Running)
        {
            ImGui.TextColored(Grey, $"経路: {_controller.TravelRoute} / Lifestream: {(_controller.LifestreamAvailable ? "接続" : "未接続")}");
            var me = Svc.Objects.LocalPlayer;
            if (me != null && me.MaxHp > 0)
                ImGui.TextColored(Grey, $"HP: {me.CurrentHp * 100f / me.MaxHp:F0}% / ロール: {SurvivalPolicy.CurrentRole()}");
        }

        // The Lost Action driver gets its own line only while it has something to say. Its presses
        // also appear under the duty-action bar, but a load that never lands is reported nowhere
        // else - and "the driver quietly did nothing" is precisely the failure this line exists
        // to make visible.
        if (_config.AutoUseLostActions && _controller.LastLostAction.Length > 0)
            ImGui.TextColored(Grey, $"ロストアクション: {Loc.Runtime(_controller.LastLostAction)}");

        var latestWarning = DiagnosticsRecorder.LatestWarning;
        if (!string.IsNullOrWhiteSpace(latestWarning))
            ImGui.TextColored(Yellow, $"直近の警告: {latestWarning}");

        if (ImGui.SmallButton("診断情報をコピー"))
            ImGui.SetClipboardText(BuildDiagnostics());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("現在の状態・依存関係・経路・CE状態を個人情報なしでコピーします。");

        DrawPartySupport();
        DrawZonePicker();
    }

    /// <summary>
    /// Start and stop the party-support task, and say what it is doing.
    ///
    /// It gets a control on the main window rather than living in settings because it is a TASK,
    /// not a preference: it spends farmed charges on other people for as long as it runs, and the
    /// request for it asked for a stop in the same breath as the feature. A stop you have to go
    /// and find is not a stop.
    /// </summary>
    private void DrawPartySupport()
    {
        var task = _controller.PartySupport;

        // Nothing configured and not running: stay out of the way entirely rather than showing a
        // button that can only refuse.
        if (!task.Active && _config.PartySupportActions.Count == 0)
            return;

        if (ImGui.Button(task.Active ? "パーティ支援を停止" : "パーティ支援を開始", new Vector2(170, 0)))
            task.Toggle();

        ImGui.SameLine();
        ImGui.TextColored(task.Active ? Green : Grey,
            task.Status.Length > 0 ? Loc.Runtime(task.Status) : "パーティ支援は待機中です。");

        if (task.Active && task.Applied > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"（{task.Applied}回使用）");
        }
    }

    /// <summary>
    /// Restrict work to one third of the zone. Sits next to the Start button because it is the
    /// setting most likely to be changed between runs.
    /// </summary>
    private void DrawZonePicker()
    {
        var territory = Svc.ClientState.TerritoryType;
        var farm = _config.FarmMaterialItemId == 0 ? null : ZoneDrops.For(_config.FarmMaterialItemId);

        ImGui.TextUnformatted(Loc.T("Work zone:", "周回エリア:"));
        ImGui.SameLine();

        if (farm is { } locked)
        {
            // A farm material already pins the region AND the activity, so leaving the picker
            // live here would just let the two contradict each other.
            ImGui.TextColored(Blue, FieldRegions.Label(locked.Territory, locked.Region));
            ImGui.SameLine();
            ImGui.TextColored(Grey, "（Relicの周回対象から自動設定）");
            return;
        }

        var labelTerritory = BozjaZones.IsFieldZone(territory) ? territory : BozjaZones.Zadnor;
        var current = (int)_config.PreferredRegion;

        string[] options =
        [
            Loc.T("Anywhere", "指定なし"),
            FieldRegions.Label(labelTerritory, FieldRegionId.Zone1),
            FieldRegions.Label(labelTerritory, FieldRegionId.Zone2),
            FieldRegions.Label(labelTerritory, FieldRegionId.Zone3),
        ];

        ImGui.SetNextItemWidth(240);
        if (ImGui.Combo("##bbr_zone", ref current, options, options.Length))
        {
            _config.PreferredRegion = (byte)current;
            ConfigSaver.Save(_config);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "このエリア内のCEとスカーミッシュだけを対象にします。\n" +
                "表示名は現在のフィールドに合わせますが、保存されるのはZ1/Z2/Z3の区分です。\n" +
                "同じ区分設定が南方ボズヤ戦線とザトゥノル高原の両方に適用されます。");
    }

    private void DrawFieldState()
    {
        var territory = Svc.ClientState.TerritoryType;
        if (!BozjaZones.IsFieldZone(territory))
        {
            ImGui.TextColored(Yellow, $"南方ボズヤ戦線/ザトゥノル高原の外にいます（現在: {BozjaZones.Name(territory)}）。");
            return;
        }

        ImGui.TextColored(Green, BozjaZones.Name(territory));
        ImGui.SameLine();

        // The zone third is the thing that decides which relic material drops here, so it gets
        // equal billing with the zone name.
        var region = _controller.CurrentRegion;
        ImGui.TextColored(region == FieldRegionId.Unknown ? Grey : Blue,
            $"- {FieldRegions.Label(territory, region)}");

        if (_config.FarmMaterialItemId != 0 && ZoneDrops.For(_config.FarmMaterialItemId) is { } farm)
        {
            var right = farm.Territory == territory && farm.Region == region;
            ImGui.TextColored(right ? Green : Yellow,
                right
                    ? $"現在の周回対象: {farm.Describe()}"
                    : $"周回対象は {farm.Describe()} です。現在地が対象外です。");
        }

        if (!FieldState.Available)
        {
            ImGui.TextColored(Yellow, "ボズヤのフィールド状態を初期化待ちです。");
            return;
        }

        var rank = FieldState.ResistanceRank;
        var mettle = FieldState.Mettle;
        var needed = FieldState.MettleNeeded;

        ImGui.TextUnformatted(Loc.Ja ? $"レジスタンスランク {rank}" : $"Resistance Rank {rank}");
        ImGui.SameLine();
        if (needed > 0)
        {
            var fraction = Math.Clamp((float)mettle / needed, 0f, 1f);
            ImGui.ProgressBar(fraction, new Vector2(220, 0), $"戦果 {mettle:N0} / {needed:N0}");
        }
        else
        {
            ImGui.TextUnformatted($"戦果 {mettle:N0}");
        }
    }

    private void DrawEngagementTable()
    {
        var engagements = _controller.Engagements;
        if (engagements.Count == 0)
        {
            ImGui.TextColored(Grey, "現在参加可能なクリティカルエンゲージメントはありません。");
            return;
        }

        if (!ImGui.BeginTable("##bbr_ce", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn(Loc.T("Engagement", "クリティカルエンゲージメント"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("State", "状態"), ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn(Loc.T("Time", "残り時間"), ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn(Loc.T("Players", "人数"), ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn(Loc.T("Progress", "進行度"), ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn(Loc.T("Skip", "除外"), ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableHeadersRow();

        foreach (var ce in engagements)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var label = ce.Name;
            if (ce.IsDuel)
                label += "  [一騎打ち]";
            else if (_catalog.IsLargeScale(ce.EventId))
                label += "  [大規模戦闘]";

            var nameColour = ce.State switch
            {
                DynamicEventState.Battle => Green,
                DynamicEventState.Warmup => Yellow,
                DynamicEventState.Register => Blue,
                _ => Grey,
            };
            ImGui.TextColored(nameColour, label);
            if (ImGui.IsItemHovered() && _catalog.TryGet(ce.EventId, out var entry) && entry.Description.Length > 0)
                ImGui.SetTooltip(entry.Description);

            ImGui.TableNextColumn();
            ImGui.TextColored(nameColour, Loc.CeState(ce.State));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ce.IsLive ? FormatSeconds(ce.SecondsLeft) : "-");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ce.IsLive ? $"{ce.Participants}/{ce.MaxParticipants}" : "-");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ce.State == DynamicEventState.Battle ? $"{ce.Progress}%" : "-");

            ImGui.TableNextColumn();
            var blocked = _config.BlockedEngagements.Contains(ce.EventId);
            if (ImGui.Checkbox($"##skip{ce.EventId}", ref blocked))
            {
                if (blocked)
                    _config.BlockedEngagements.Add(ce.EventId);
                else
                    _config.BlockedEngagements.Remove(ce.EventId);
                ConfigSaver.Save(_config);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("この対象には参加・移動しません。");
        }

        ImGui.EndTable();
    }

    private void DrawMultibox()
    {
        var enabled = _config.MultiboxEnabled;
        if (ImGui.Checkbox(Loc.T("Coordinate with other game clients on this PC", "このPC上の複数クライアントを連携する"), ref enabled))
        {
            _config.MultiboxEnabled = enabled;
            ConfigSaver.Save(_config);
        }
        ImGui.TextColored(Grey,
            "AutoDutyと同様に、このPC内のローカルNamed Pipeで連携します。1クライアントをホストにし、\n" +
            "ホストが目的地を決め、他クライアントが追従することで分散を防ぎます。");

        if (!enabled)
            return;

        var isHost = _config.MultiboxIsHost;
        if (ImGui.Checkbox(Loc.T("This client is the host", "このクライアントをホストにする"), ref isHost))
        {
            _config.MultiboxIsHost = isHost;
            ConfigSaver.Save(_config);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("グループ内でホストにするクライアントは1つだけにしてください。");

        var barrier = _config.MultiboxArrivalBarrier;
        if (ImGui.Checkbox("全クライアント到着後に戦闘開始する", ref barrier))
        {
            _config.MultiboxArrivalBarrier = barrier;
            ConfigSaver.Save(_config);
        }

        if (barrier)
        {
            var timeout = _config.MultiboxBarrierTimeoutSeconds;
            ImGui.SetNextItemWidth(160);
            if (ImGui.SliderInt("到着待ちタイムアウト（秒）", ref timeout, 10, 180))
            {
                _config.MultiboxBarrierTimeoutSeconds = timeout;
                ConfigSaver.Save(_config);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("この時間を超えると、1クライアントが詰まっていてもホストが待機を解除します。");
        }

        ImGui.Separator();

        // Three states, not two. "Link down" covering both "still looking" and "there is no host
        // on this PC" is why the only available diagnostic was to toggle the checkbox and hope.
        switch (_link.State)
        {
            case LinkState.Connected:
                ImGui.TextColored(Green, "接続済み");
                break;

            case LinkState.Connecting when _link.SecondsSearching < 8f:
                ImGui.TextColored(Yellow,
                    $"ホストへ接続中…（試行 {_link.ConsecutiveFailures + 1}）");
                break;

            case LinkState.Connecting:
                ImGui.TextColored(Red, "このPC上でホストが見つかりません。");
                ImGui.TextColored(Grey,
                    $"検索中 {_link.SecondsSearching:F0}秒（{_link.ConsecutiveFailures}回試行" +
                    (_link.LastLinkError is not null ? ", 直近エラーあり（診断ログ参照）" : "") + ").\n" +
                    "どれか1つのクライアントだけで「このクライアントをホストにする」をONにしてください。\n" +
                    "初期状態は全クライアントが子機なので、そのままではホストが存在しません。単独動作中も\n" +
                    "決定論的な対象選択により、各クライアントは同じ目的地へ収束します。");
                break;

            default:
                ImGui.TextColored(_link.IsHost ? Green : Grey,
                    _link.IsHost ? "ホスト中 - 他クライアントの接続待ち" : "リンク待機中");
                break;
        }

        ImGui.TextUnformatted($"役割: {(_link.IsHost ? "ホスト" : "クライアント")}   接続数: {_link.PeerCount}");

        if (_link.IsHost && _link.PeerCount == 0)
        {
            ImGui.TextColored(Grey,
                "まだ他クライアントは接続していません。他クライアント側でもマルチボックスを有効にし、\n" +
                "ホスト設定はOFFにしてください。");
        }

        if (_link.IsHost && _config.MultiboxArrivalBarrier)
            ImGui.TextUnformatted($"到着: {_link.ArrivedCount}/{_link.PeerCount}");

        var objective = _link.Objective;
        ImGui.TextUnformatted(objective.IsSet
            ? $"共有目的地: #{objective.Id}"
            : "共有目的地: なし");

        if (_link.IsHost)
        {
            if (ImGui.Button(Loc.T("Start all", "全クライアント開始")))
                _link.BroadcastRunState(true);
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Stop all", "全クライアント停止")))
                _link.BroadcastRunState(false);
        }

        ImGui.Separator();
        if (ImGui.Button(Loc.T("Open the multiboxer panel", "マルチボックス操作画面を開く")))
            OnOpenMultiboxer?.Invoke();
        ImGui.TextColored(Grey,
            "1画面から全クライアントの開始/停止、ロストアクション構成の送信、" +
            "最寄りエーテライトやロストボックスへの移動を操作できます。/bbr boxes でも開けます。");

        ImGui.Spacing();
        if (ImGui.Button(Loc.T("Open the group duty-action hotbar", "グループDuty Actionバーを開く")))
            OnOpenDutyActions?.Invoke();
        ImGui.TextColored(Grey,
            "接続中の全クライアントのDuty Action 2枠を、アイコン・残数・リキャスト付きで1画面に表示します。\n" +
            "自分の枠と同じ情報を確認できます。/bbr duty でも開けます。");

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "リンクが切れても各クライアントは同じ決定論的選択を行うため、\n" +
            "別々の対象へ散らばりにくい設計です。");
    }

    /// <summary>Raised by the "open the hotbar" button; the plugin owns that window.</summary>
    public Action? OnOpenDutyActions { get; set; }

    /// <summary>Raised by the "open the multiboxer panel" button.</summary>
    public Action? OnOpenMultiboxer { get; set; }

    private void DrawDependencies()
    {
        Row("vnavmesh", _navmesh.Available, "移動・経路探索（必須）");

        var avoidance = _director.Avoidance;
        var fork = avoidance.Fork;

        Row(avoidance.ForkName, _director.AvoidanceAvailable, fork switch
        {
            BossModFork.Reborn => "AoE回避専用。AIはForbidActions、AIプリセットなし、自動ローテーション無効",
            BossModFork.Original => "AoE回避専用。自動ターゲットOFF、ローテーションプリセットなし",
            _ => "AoE回避。どちらのforkでも動作しますがRebornを推奨",
        });

        if (avoidance.UnavailableReason is { } reason)
        {
            ImGui.Indent();
            ImGui.TextColored(Red, "BossModとの接続に失敗しています。詳細は /xllog を確認してください。");
            ImGui.TextColored(Grey,
                "BossMod Rebornは回避中かどうかを取得できるため、最も完全な連携ができます。\n" +
                "回避中は移動制御をBossModへ譲ります。オリジナル版BossModでも動作しますが、\n" +
                "下記の制約があります。");
            ImGui.Unindent();
        }

        if (avoidance.BothForksLoaded)
        {
            ImGui.Indent();
            ImGui.TextColored(Yellow,
                "BossModの2つのforkが同時に読み込まれています。同じBossMod.* IPCを共有するため、\n" +
                "後から読み込まれた側が競合します。どちらか一方をアンロードしてください。現在はRebornを使用します。");
            ImGui.Unindent();
        }

        Row("Rotation Solver Reborn", _director.RotationAvailable, "戦闘ローテーション（必須）");

        ImGui.Separator();
        ImGui.TextColored(Grey, "役割分担");
        ImGui.BulletText($"{avoidance.ForkName}: 予兆AoEから移動して回避します。戦闘アクションは押しません。");
        ImGui.BulletText("RSR: 戦闘ローテーションを実行し、戦闘アクションの入力を担当します。");

        if (fork == BossModFork.Original)
        {
            ImGui.BulletText("オリジナル版はFollowSlotで対象へ接近するため、BBR側の接近制御を停止します。");
            ImGui.BulletText("オリジナル版は回避中か判定できないため、経路移動中に回避へ制御を譲れません。");
            ImGui.Indent();
            ImGui.TextColored(Grey,
                "経路移動していない待機中・到着後のみ回避します。Rebornは移動中も回避できます。");
            ImGui.Unindent();
        }

        if (_director.AvoidanceAvailable)
        {
            ImGui.Separator();
            ImGui.TextColored(Grey, "回避テレメトリ");
            if (avoidance.SteeringKnown)
            {
                var (zones, navigating) = _director.AvoidanceSignals;

                // Both halves shown separately, because they mean different things and confusing
                // them is what stalled the runner: "wants to move" is true almost always once
                // Reborn's AI is on, and only "danger zones > 0" means there is a mechanic.
                ImGui.TextUnformatted($"危険領域: {zones}");
                ImGui.TextUnformatted($"BossMod移動要求: {(navigating ? "あり" : "なし")}");
                ImGui.TextColored(_director.AvoidanceIsSteering ? Yellow : Grey,
                    $"=> 移動制御を譲渡: {(_director.AvoidanceIsSteering ? "はい" : "いいえ")}" +
                    (_controller.SecondsYielding > 0f ? $" ({_controller.SecondsYielding:F0}s)" : ""));

                if (navigating && zones == 0)
                {
                    ImGui.TextColored(Grey,
                        "BossModは移動を要求していますが危険予兆はありません。これは\n" +
                        "位置調整であり回避ではないため、BBRの経路移動を継続します。正常動作です。");
                }
            }
            else
            {
                ImGui.TextColored(Grey, "危険領域/移動制御: このforkでは取得できません（Hints.* / AI.* IPCなし）。");
            }

            // Reborn cannot be asked what state it is in, so what we can honestly show is what we
            // last SENT and how long ago. A last-sent age that keeps climbing past the re-assert
            // interval means the heartbeat is off or is being withheld. (The original's AI on/off
            // is a config value and IS read back before each heartbeat write.)
            ImGui.TextUnformatted(
                $"最終送信 - AI: {Age(avoidance.SecondsSinceSent)}, " +
                $"rotation: {_director.Rotation.CurrentMode?.ToString() ?? "未設定"} " +
                $"{Age(_director.Rotation.SecondsSinceSent)}");
            if (ImGui.Button("回避専用設定を再適用"))
                avoidance.ApplyAvoidanceOnlyConfig(force: true);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(fork == BossModFork.Original
                    ? "現在値を確認してから AIConfig.ForbidActions=true / ForbidMovement=false を再適用し、\n" +
                      "ユーザープリセットやforce-disableが有効ならPresets.ClearActiveを実行します。\n" +
                      "オリジナル版ではSetForceDisabledが回避AI自体を外すため、意図的に使用しません。"
                    : "/bmrai forbidactions on、/bmrai forbidmovement off を再送し、\n" +
                      "AI.SetPreset(\"\") と Presets.SetForceDisabled() を再適用します。");

            ImGui.SameLine();
            if (ImGui.Button($"{avoidance.ForkName} の設定を復元"))
                avoidance.ReleaseControl();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("AIを停止し、BBRが変更した設定・プリセットを元に戻します。");
        }
        return;

        static void Row(string name, bool ok, string note)
        {
            ImGui.TextColored(ok ? Green : Red, ok ? "OK  " : "未接続  ");
            ImGui.SameLine();
            ImGui.TextUnformatted(name);
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"- {note}");
        }

        static string Age(float seconds) => seconds < 0f ? "（未送信）" : $"{seconds:F0}秒前";
    }

    private string BuildDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("BozjaBuddyReborn-JP diagnostics");
        sb.AppendLine($"version={AssemblyVersion}");
        sb.AppendLine($"territory={Svc.ClientState.TerritoryType}");
        sb.AppendLine($"running={_controller.Running}");
        sb.AppendLine($"state={_controller.State}");
        sb.AppendLine($"status={_controller.Status}");
        sb.AppendLine($"routeMode={_controller.TravelMode}");
        sb.AppendLine($"route={_controller.TravelRoute}");
        sb.AppendLine($"routeSpawnBlacklist={_controller.RouteBlacklistCount}");
        sb.AppendLine($"vnavmesh={_navmesh.Available}");
        sb.AppendLine($"lifestream={_controller.LifestreamAvailable}");
        sb.AppendLine($"rotationSolver={_director.RotationAvailable}");
        sb.AppendLine($"bossMod={_director.AvoidanceAvailable}");
        sb.AppendLine($"bossModFork={_director.Avoidance.Fork}");

        var me = Svc.Objects.LocalPlayer;
        if (me != null && me.MaxHp > 0)
        {
            sb.AppendLine($"hpPercent={me.CurrentHp * 100f / me.MaxHp:F1}");
            sb.AppendLine($"role={SurvivalPolicy.CurrentRole()}");
        }

        sb.AppendLine($"ceCount={_controller.Engagements.Count}");
        foreach (var ce in _controller.Engagements)
            sb.AppendLine($"ce={ce.EventId},state={ce.State},left={ce.SecondsLeft},progress={ce.Progress}");

        sb.AppendLine("stateTransitions:");
        foreach (var entry in DiagnosticsRecorder.StateTransitions)
            sb.AppendLine($"  {entry.Timestamp:O} state={entry.State} status={entry.Message}");

        sb.AppendLine("warnings:");
        foreach (var entry in DiagnosticsRecorder.WarningHistory)
            sb.AppendLine($"  {entry.Timestamp:O} state={entry.State} warning={entry.Message}");

        // Intentionally excluded: character name, world, chat, party member names and any free-form user text.
        return sb.ToString();
    }

    private static string FormatSeconds(uint seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }
}
