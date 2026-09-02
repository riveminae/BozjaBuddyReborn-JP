using System;
using System.Numerics;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Relic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

using BozjaBuddyReborn;

namespace BozjaBuddyReborn.Windows;

public sealed class ConfigWindow : Window
{
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);

    private readonly Configuration _config;
    private readonly LostActionCatalog _lostActions;
    private readonly RegionResolver _regions;
    private readonly AggroAvoidance _avoidance;

    public ConfigWindow(
        Configuration config,
        LostActionCatalog lostActions,
        RegionResolver regions,
        AggroAvoidance avoidance)
        : base("Bozja Buddy Reborn - 設定###BozjaBuddyRebornConfig")
    {
        _config = config;
        _lostActions = lostActions;
        _regions = regions;
        _avoidance = avoidance;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 420),
            MaximumSize = new Vector2(1200, 1200),
        };
    }

    private void Save() => ConfigSaver.Save(_config);

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##bbr_cfg"))
            return;

        if (ImGui.BeginTabItem(Loc.T("Combat", "戦闘")))
        {
            DrawCombat();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Engagements", "CE / スカーミッシュ")))
        {
            DrawEngagements();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("生存"))
        {
            DrawSurvival();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Movement", "移動")))
        {
            DrawMovement();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Zones", "エリア")))
        {
            DrawZones();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Lost Actions", "ロストアクション")))
        {
            DrawLostActions();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawCombat()
    {
        ImGui.TextColored(Grey,
            "2つのプラグインは役割を分離して競合を防ぎます。\n" +
            "BossModが回避、RSRが戦闘アクションを担当します。");
        ImGui.Separator();

        var avoid = _config.UseBossModAvoidance;
        if (ImGui.Checkbox(Loc.T("BossMod: AoE avoidance", "BossMod: AoE回避"), ref avoid))
        {
            _config.UseBossModAvoidance = avoid;
            Save();
        }
        ImGui.TextColored(Grey,
            "BossMod Reborn（推奨）またはオリジナル版BossModの、読み込まれている方を使用します。\n" +
            "Reborn: ForbidActionsでAIを有効にし、AIプリセットを空にして、グローバルの\n" +
            "自動ローテーションを強制無効化するため、回避だけ行い戦闘アクションは入力しません。\n" +
            "オリジナル版: 自動ターゲットOFFでAIを有効化し、アクティブなローテーションプリセットを解除します。\n" +
            "AIプリセットはターゲット・追従・移動のみになるため、戦闘アクションは入力しません。\n" +
            "この分離を行わないとBossModとRSRのアクションキューが競合します。\n" +
            "停止時にはBBRが変更した設定を復元します。");
        ImGui.TextColored(Grey,
            "オリジナル版は回避中か取得できず、BBRが経路移動していない時だけ移動できるため、\n" +
            "待機中と到着後に回避します。Rebornは経路移動中も回避できます。");

        ImGui.Spacing();

        var rsr = _config.UseRotationSolver;
        if (ImGui.Checkbox(Loc.T("Rotation Solver Reborn: rotation", "Rotation Solver Reborn: 戦闘ローテーション"), ref rsr))
        {
            _config.UseRotationSolver = rsr;
            Save();
        }
        ImGui.TextColored(Grey, "RSRはAutoモードで動作し、対象選択と攻撃を自動で行います。");

        if (!rsr)
            ImGui.TextColored(Yellow, "RSRをOFFにすると自動攻撃されません。戦闘は手動操作が必要です。");

        ImGui.Spacing();

        var reapply = _config.ReapplyAvoidanceConfigEachFight;
        if (ImGui.Checkbox("戦闘ごとに回避専用設定を再適用する", ref reapply))
        {
            _config.ReapplyAvoidanceConfigEachFight = reapply;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("若干処理は増えますが、途中でBossMod設定を変更しても回避専用状態へ戻せます。");

        ImGui.Separator();

        var close = _config.CloseToTarget;
        if (ImGui.Checkbox("戦闘中は対象の攻撃射程まで接近する", ref close))
        {
            _config.CloseToTarget = close;
            Save();
        }
        ImGui.TextColored(Grey,
            "BossMod Rebornはパーティマスター追従時以外は対象へ自動接近しません。ソロでは\n" +
            "自分自身がマスターになるため、BBRの回避専用設定では回避だけを行い、\n" +
            "接近は行いません。この設定がOFFだと近接ジョブが移動終了地点に立ち続け、\n" +
            "遠隔代替技（侍の燕飛など）だけを使う場合があります。\n" +
            "近接・タンクはヒットボックス内側2y、その他は15yまで接近します。\n" +
            "オリジナル版BossModではFollowSlotが自力で対象へ接近するため、この処理を使いません。\n" +
            "AoEを避けながら接近するため、BBR側の接近経路を重ねる必要がありません。");

        ImGui.Spacing();

        var reassert = _config.CombatStateReassertSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("戦闘状態の再適用間隔（秒）", ref reassert, 0f, 30f, "%.0f"))
        {
            _config.CombatStateReassertSeconds = reassert;
            Save();
        }
        ImGui.TextColored(Grey,
            "状態が変化していなくても、この間隔で両プラグインへON/OFF状態を再送します。\n" +
            "RSRとRebornは現在状態を問い合わせられず、内部状態が解除される場合があるため、\n" +
            "定期的に再適用します。Rebornは追従中のパーティ枠が無効になるとAIを待機させます。\n" +
            "ボズヤのアライアンスではこれが頻繁に起きるため、再適用しないと\n" +
            "自動戦闘が解除されたまま周回を続ける可能性があります。オリジナル版はAI状態を読めるため、\n" +
            "状態がずれた場合だけ書き戻します（死亡時など）。\n" +
            "0で再適用を無効化します。");
        if (reassert > 0f)
        {
            ImGui.TextColored(Yellow,
                "RSR側の設定変更をチャット表示する機能がONだと、再適用ごとにメッセージが出ます。\n" +
                "気になる場合はRSR側の表示をOFFにするか、この間隔を長くしてください。");
        }
    }

    private void DrawEngagements()
    {
        var ces = _config.DoCriticalEngagements;
        if (ImGui.Checkbox(Loc.T("Join Critical Engagements", "クリティカルエンゲージメントに参加する"), ref ces))
        {
            _config.DoCriticalEngagements = ces;
            Save();
        }

        var fates = _config.DoFates;
        if (ImGui.Checkbox(Loc.T("Farm skirmish FATEs when no engagement is open", "CEがない間はスカーミッシュを周回する"), ref fates))
        {
            _config.DoFates = fates;
            Save();
        }

        ImGui.Separator();

        ImGui.TextUnformatted("移動中に敵から感知された場合:");

        var keepRunning = _config.AggroResponse == TravelAggroResponse.KeepRunning;

        if (ImGui.RadioButton(Loc.T("Keep running (never attack)", "そのまま走る（反撃しない）"), keepRunning))
        {
            _config.AggroResponse = TravelAggroResponse.KeepRunning;
            Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.T("Stop and fight back", "停止して反撃する"), !keepRunning))
        {
            _config.AggroResponse = TravelAggroResponse.FightBack;
            Save();
        }

        ImGui.TextColored(Grey,
            "「そのまま走る」では移動中のローテーションをOFFにし、道中の敵へ反撃しません。\n" +
            "十分に離れればフィールド敵は追跡をやめます。感知のたびに停止して戦うと、\n" +
            "報酬のない戦闘が連続し、\n" +
            "CEの参加受付時間を消費します。\n" +
            "目的地へ到着した後は、追ってきた敵にも反撃します。\n" +
            "CEの抽選待機中など、その場を離れられない状況では戦闘を処理します。\n" +
            "判定には戦闘フラグではなく、実際に自分をターゲットしている敵を使用します。\n" +
            "戦闘フラグは敵撃破後もしばらく残るためです。");
        ImGui.TextColored(Yellow,
            "戦闘中はマウントできないため、敵に追跡されている間は徒歩で逃走し、\n" +
            "追跡が切れた後に再度マウントします。");

        var sticky = _config.StickyObjective;
        if (ImGui.Checkbox("現在の対象が終わるまで継続する", ref sticky))
        {
            _config.StickyObjective = sticky;
            Save();
        }
        ImGui.TextColored(Grey,
            "対象候補は毎tick再評価されます。この設定がOFFだと、途中でより高順位の対象が発生した際に\n" +
            "現在戦闘中のスカーミッシュから離脱する可能性があります。");

        ImGui.Separator();

        var duels = _config.EngageDuels;
        if (ImGui.Checkbox(Loc.T("Enter duels (1v1)", "一騎打ちに参加する"), ref duels))
        {
            _config.EngageDuels = duels;
            Save();
        }
        ImGui.TextColored(Grey,
            "ボズヤ/ザトゥノルの一騎打ちを対象にします。\n" +
            "参加者は1名のみ選出され、参加条件・悪名度の影響があります。");

        var large = _config.EngageLargeScale;
        if (ImGui.Checkbox(Loc.T("Enter large-scale battles", "大規模戦闘に参加する"), ref large))
        {
            _config.EngageLargeScale = large;
            Save();
        }
        ImGui.TextColored(Grey, "カストルム・ラクスリトレおよび旗艦ダル・リアータを対象にします。通常CEより優先されます。");

        ImGui.Separator();

        var minSeconds = _config.MinRegisterSecondsLeft;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("参加申請する最低残り時間（秒）", ref minSeconds, 10, 60))
        {
            _config.MinRegisterSecondsLeft = minSeconds;
            Save();
        }
        ImGui.TextColored(Grey,
            "残り10秒未満では申請できないため、UI処理分の余裕を確保します。");
    }

    private void DrawSurvival()
    {
        var enabled = _config.AutoSurvivalLostActions;
        if (ImGui.Checkbox("生存優先のロストアクション自動使用", ref enabled))
        {
            _config.AutoSurvivalLostActions = enabled;
            Save();
        }
        ImGui.TextColored(Grey,
            "マウント中はロストアクションを一切使用しません。徒歩/戦闘中のみ、HPとロールを見て\n" +
            "ポーションキット・リレイザー・緊急防御・回復を使用します。");

        DrawRole("タンク", ref _config.TankSurvivalHealFraction, ref _config.TankSurvivalEmergencyFraction);
        DrawRole("ヒーラー", ref _config.HealerSurvivalHealFraction, ref _config.HealerSurvivalEmergencyFraction);
        DrawRole("DPS", ref _config.DpsSurvivalHealFraction, ref _config.DpsSurvivalEmergencyFraction);

        return;

        void DrawRole(string role, ref float heal, ref float emergency)
        {
            var h = heal * 100f;
            var e = emergency * 100f;
            ImGui.SetNextItemWidth(180);
            if (ImGui.SliderFloat($"{role} 通常回復 (%)", ref h, 20f, 95f, "%.0f%%"))
            {
                heal = Math.Clamp(h / 100f, 0.2f, 0.95f);
                Save();
            }
            ImGui.SetNextItemWidth(180);
            if (ImGui.SliderFloat($"{role} 緊急 (%)", ref e, 10f, 80f, "%.0f%%"))
            {
                emergency = Math.Clamp(e / 100f, 0.1f, heal);
                Save();
            }
        }
    }

    private void DrawMovement()
    {
        ImGui.TextColored(Grey,
            "南方ボズヤ戦線・ザトゥノル高原のフィールド内エーテライトを利用できます。\n" +
            "BOCCHI方式で徒歩/マウント直行と簡易テレポ経路を比較し、速い方を選択します。");
        ImGui.Separator();

        var bocchi = _config.UseBocchiNavigation;
        if (ImGui.Checkbox("BOCCHI方式の移動経路を使用する", ref bocchi))
        {
            _config.UseBocchiNavigation = bocchi;
            Save();
        }

        var aethernet = _config.UseAethernetTravel;
        if (ImGui.Checkbox("フィールド内の簡易テレポを使用する（Lifestream）", ref aethernet))
        {
            _config.UseAethernetTravel = aethernet;
            Save();
        }

        var legacy = _config.LegacyMovement;
        if (ImGui.Checkbox("非常用: 従来の直接移動を使用する", ref legacy))
        {
            _config.LegacyMovement = legacy;
            Save();
        }

        var mount = _config.UseMount;
        if (ImGui.Checkbox(Loc.T("Summon a mount for long travel", "長距離移動ではマウントを使用する"), ref mount))
        {
            _config.UseMount = mount;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("30yを超える長距離ではマウントルーレットを使用し、到着時に戦闘可能な状態へ降ります。");

        ImGui.TextColored(Grey, "この2エリアではマウント飛行は使用しません。常に地上経路です。");

        var direct = _config.NavigationMaxDirectWalkDistance;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("直接移動を優先する距離 (y)", ref direct, 20f, 200f, "%.0f"))
        {
            _config.NavigationMaxDirectWalkDistance = direct;
            Save();
        }

        var hop = _config.NavigationAethernetHopCost;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("簡易テレポの時間換算コスト", ref hop, 10f, 150f, "%.0f"))
        {
            _config.NavigationAethernetHopCost = hop;
            Save();
        }

        var arrive = _config.ArriveRange;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("到着判定距離 (y)", ref arrive, 3f, 40f, "%.0f"))
        {
            _config.ArriveRange = arrive;
            Save();
        }

        var stall = _config.StallTimeoutSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("移動スタック判定（秒）", ref stall, 3f, 30f, "%.0f"))
        {
            _config.StallTimeoutSeconds = stall;
            Save();
        }
        ImGui.TextColored(Grey,
            "ザトゥノルの高低差などで地上経路が詰まった場合、実移動がこの時間発生しなければ\n" +
            "現在経路を破棄して目的地をnavmeshへ再スナップし、\n" +
            "新しい経路を作成します。");

        ImGui.Spacing();
        var debugOverlay = _config.DebugWorldOverlay;
        if (ImGui.Checkbox("テスト用: 経路・危険敵をworld上に表示する", ref debugOverlay))
        {
            _config.DebugWorldOverlay = debugOverlay;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("目的地、選択Aethernet経路、IV/V/★/判定不能敵の感知範囲を描画します。通常はOFFにしてください。");

        ImGui.Separator();
        DrawAggroAvoidance();

        ImGui.Separator();
        DrawIdleSpots();
    }

    /// <summary>Routing around enemy aggro while travelling.</summary>
    private void DrawAggroAvoidance()
    {
        var avoid = _config.AvoidDangerousEnemies;
        if (ImGui.Checkbox(Loc.T("Route around enemy aggro while travelling", "移動中に敵の感知範囲を迂回する"), ref avoid))
        {
            _config.AvoidDangerousEnemies = avoid;
            Save();
        }
        ImGui.TextColored(Grey,
            "敵感知を前方の視覚コーンと、全方向の近接感知リングとして扱います。\n" +
            "正面では感知される距離でも、背後なら安全な場合は通過できます。\n" +
            "本機能は地上移動用です。");

        if (!avoid)
            return;

        ImGui.TextColored(Grey,
            "ボズヤ内の敵は通常Lv80のため、レベルではなく固有の強さ I～V / ★ を判定します。\n" +
            "I～IIIは無視し、IV・V・★・判定不能だけを迂回します。");

        var star = _config.DangerStarExtraClearance;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("★敵の追加安全距離 (y)", ref star, 0f, 20f, "%.0f"))
        {
            _config.DangerStarExtraClearance = star;
            Save();
        }

        var sight = _config.DangerSightRadius;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("視覚感知距離 (y)", ref sight, 5f, 50f, "%.0f"))
        {
            _config.DangerSightRadius = sight;
            Save();
        }

        var cone = _config.DangerConeDegrees;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("視覚感知角度 (度)", ref cone, 30f, 360f, "%.0f"))
        {
            _config.DangerConeDegrees = cone;
            Save();
        }

        var proximity = _config.DangerProximityRadius;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("近接感知距離 (y)", ref proximity, 2f, 30f, "%.0f"))
        {
            _config.DangerProximityRadius = proximity;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("向きに関係なく感知される距離です。真後ろでも適用されます。");

        var clearance = _config.DangerClearance;
        ImGui.SetNextItemWidth(200);
        // Floor at 2, not 0. The detour offset is (sight radius + this), and the sidestep has to
        // clear the enemy by enough for the route to it to be accepted - at 0 there is no offset
        // to speak of and no detour can ever be used, which silently switches the whole feature
        // off while every other setting still says it is on.
        if (ImGui.SliderFloat("迂回時の余裕距離 (y)", ref clearance, 2f, 25f, "%.0f"))
        {
            _config.DangerClearance = clearance;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "敵を迂回する際に横へどれだけ余裕を取るか指定します。\n" +
                "大きいほど安全側です。視覚感知距離を増やすと迂回経路の成立条件が厳しくなり、\n" +
                "この値を増やすとより外側を通る経路を選びやすくなります。");

        var ignore = _config.DangerIgnoreNearObjective;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("目的地付近では迂回しない距離 (y)", ref ignore, 0f, 80f, "%.0f"))
        {
            _config.DangerIgnoreNearObjective = ignore;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "目的地付近の敵はスカーミッシュ等の対象敵である可能性が高いため、\n" +
                "ここで迂回すると目的地へ到着できなくなるため無視します。");

        ImGui.Spacing();
        ImGui.TextColored(Grey, "周辺の敵（危険度判定の診断）:");

        var zones = _avoidance.Scan();
        var census = _avoidance.LastCensus;

        // WHY THE LIST IS THE SHAPE IT IS. "Avoidance does nothing" has several causes that look
        // identical from outside, and one of them - the game not setting the hostile flag on an
        // idle field mob - would empty this list completely while every setting still reads as
        // correct. Showing what was dropped and why turns that into a glance.
        ImGui.TextColored(Grey,
            $"  検出 {census.Combatants}体: 追跡 {census.Accepted}, " +
            $"非敵対扱い {census.NotHostile}, 低危険度 {census.BelowLevel}, " +
            $"既に自分を対象 {census.AlreadyOnUs}, 別階層 {census.OtherFloor}, " +
            $"一時除外 {census.Suppressed}, 範囲外 {census.OutOfRange}.");

        if (census.Combatants > 0 && census.Accepted == 0 && census.NotHostile == census.Combatants)
            ImGui.TextColored(Yellow,
                "  周辺combatantがすべて非敵対フラグとして除外されています。\n" +
                "  通常のフィールド敵でもこの表示になる場合、敵判定が機能していません。\n" +
                "  診断情報をコピーして確認してください。");

        if (zones.Count == 0)
        {
            ImGui.TextColored(Grey, "  追跡対象なし");
            return;
        }

        if (!ImGui.BeginChild("##bbr_danger", new Vector2(0, 140), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var z in zones)
            ImGui.TextUnformatted($"[{(z.Strength == FieldEnemyStrength.Star ? "★" : z.Strength == FieldEnemyStrength.Unknown ? "?" : ((byte)z.Strength).ToString())}] {z.Name}   {Movement.DistanceToPlayer(z.Position):F0}y   icon={z.NamePlateIconId}/{z.CharacterDataIcon}");

        ImGui.EndChild();
    }

    /// <summary>Staging points to wait at when the working zone has nothing up.</summary>
    private void DrawIdleSpots()
    {
        var idle = _config.UseIdleSpot;
        if (ImGui.Checkbox(Loc.T("Wait at a staging point when nothing is up", "対象がないときは待機地点へ移動する"), ref idle))
        {
            _config.UseIdleSpot = idle;
            Save();
        }
        ImGui.TextColored(Grey,
            "対象エリア内の待機地点へ移動し、次のspawnへの初動距離を短くします。ザトゥノルは\n" +
            "各台地が大きく離れているため、別エリアで待つと\n" +
            "移動時間が大きくなります。");

        if (!idle)
            return;

        var territory = Svc.ClientState.TerritoryType;
        var editable = BozjaZones.IsFieldZone(territory) ? territory : BozjaZones.Zadnor;

        ImGui.Spacing();
        ImGui.TextColored(Grey, $"{BozjaZones.Name(editable)} の待機地点（マップ座標）:");

        foreach (var region in FieldRegions.All)
        {
            var key = $"{editable}:{(byte)region}";
            var has = _config.IdleSpots.TryGetValue(key, out var value) && value.Length >= 2;
            var coords = has ? new Vector2(value![0], value[1]) : Vector2.Zero;

            ImGui.SetNextItemWidth(160);
            if (ImGui.InputFloat2($"{FieldRegions.Label(editable, region)}##idle{key}", ref coords))
            {
                if (coords is { X: > 0, Y: > 0 })
                    _config.IdleSpots[key] = [coords.X, coords.Y];
                else
                    _config.IdleSpots.Remove(key);
                Save();
                OnIdleSpotsChanged?.Invoke();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"現在地##idle{key}"))
            {
                if (MapCoords.PlayerMapPosition() is { } here && BozjaZones.IsFieldZone(territory))
                {
                    _config.IdleSpots[key] = [here.X, here.Y];
                    Save();
                    OnIdleSpotsChanged?.Invoke();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("この待機地点を現在立っている位置に設定します。");

            if (has)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"削除##idle{key}"))
                {
                    _config.IdleSpots.Remove(key);
                    Save();
                    OnIdleSpotsChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Raised when a staging point is edited, so the controller drops its resolved-position
    /// cache and re-snaps the new coordinates to the navmesh.
    /// </summary>
    public Action? OnIdleSpotsChanged { get; set; }

    private void DrawZones()
    {
        ImGui.TextColored(Grey,
            "両フィールドは3エリアに分かれ、Relic素材はエリアごとに入手場所が異なります。\n" +
            "ザトゥノルでは同じ台地でもスカーミッシュとCEで\n" +
            "入手素材が異なります。対象エリアまたは活動種別を間違えると\n" +
            "目的素材を取得できません。");
        ImGui.Separator();

        DrawDropTable("南方ボズヤ戦線", BozjaZones.BozjanSouthernFront);
        ImGui.Spacing();
        DrawDropTable("ザトゥノル高原", BozjaZones.Zadnor);

        ImGui.Separator();
        ImGui.TextColored(Grey, "学習済みのイベントエリア");
        ImGui.TextColored(Grey,
            "CEごとの所属エリアを示す固定テーブルがないため、\n" +
            "初回到着時に実位置から記録します。それまではマップ上の推定位置を使用し、\n" +
            "学習後は記録値を優先します。");
        ImGui.TextUnformatted($"学習済み: {_regions.LearnedCount}");
        if (ImGui.Button("学習済みエリアを削除"))
            _regions.Forget();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("パッチ等でイベント位置が変わった場合に使用します。");

        ImGui.Spacing();
        var skipUnknown = _config.SkipUnknownRegions;
        if (ImGui.Checkbox("所属エリアが未判定の対象を除外する", ref skipUnknown))
        {
            _config.SkipUnknownRegions = skipUnknown;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "初期値はOFFです。未判定の対象へ実際に到着することで所属エリアを学習します。\n" +
                "ONにすると新規環境では対象が見つからなくなる可能性があります。");

        if (skipUnknown && _regions.LearnedCount == 0)
            ImGui.TextColored(Yellow,
                "まだエリア学習データがないため、Farm対象設定中はすべて除外される可能性があります。");

        ImGui.Separator();
        ImGui.TextColored(Grey, Loc.T("Diagnostics", "診断"));

        var logCallbacks = _config.LogUiCallbacks;
        if (ImGui.Checkbox("全UI callbackを /xllog へ記録する（デバッグ）", ref logCallbacks))
        {
            _config.LogUiCallbacks = logCallbacks;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "ボズヤ関連UIの実callbackを調査するための診断機能です。\n" +
                "参加希望/戦闘突入やLost Finds Cacheなど、公開仕様がないUI操作を調べる場合に使用します。\n" +
                "ONの状態でゲームUIを手動操作すると、/xllog に\n" +
                "addon名とゲームが使用したcallback引数を記録します。\n" +
                "非常に大量のログが出ます。BBR以外のaddonも対象です。");

        if (logCallbacks)
            ImGui.TextColored(Yellow, "  全UI callbackを記録中です。調査終了後はOFFにしてください。");

        return;

        static void DrawDropTable(string title, uint territory)
        {
            ImGui.TextColored(Grey, title);
            if (!ImGui.BeginTable($"##drops{territory}", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                return;

            ImGui.TableSetupColumn("エリア", ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableSetupColumn("活動", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("素材", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var region in FieldRegions.All)
            {
                foreach (var drop in ZoneDrops.ForTerritory(territory))
                {
                    if (drop.Region != region)
                        continue;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FieldRegions.Label(territory, region));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(drop.Activity switch
                    {
                        DropActivity.Skirmish => "スカーミッシュ",
                        DropActivity.CriticalEngagement => "クリティカルエンゲージメント",
                        _ => "どちらでも可",
                    });
                    ImGui.TableNextColumn();
                    var name = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>()?
                        .GetRowOrDefault(drop.ItemId)?.Name.ExtractText();
                    ImGui.TextUnformatted(string.IsNullOrEmpty(name) ? $"アイテムID {drop.ItemId}" : name);
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawLostActions()
    {
        // These are independent features. In particular, party support must stay configurable
        // even when ordinary combat auto-use is disabled; the old single long page returned early
        // on AutoUseLostActions=false and accidentally hid the party-support controls as collateral.
        if (!ImGui.BeginTabBar("##bbr_lostaction_tabs"))
            return;

        if (ImGui.BeginTabItem("Duty Actionバー"))
        {
            DrawDutyActionBarSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("自動使用"))
        {
            DrawLostActionAutoUseSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("パーティ支援"))
        {
            DrawPartySupport();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawDutyActionBarSettings()
    {
        var click = _config.DutyActionClickToUse;
        if (ImGui.Checkbox("Duty Actionバーのクリックでアクションを使用する", ref click))
        {
            _config.DutyActionClickToUse = click;
            Save();
        }
        ImGui.TextColored(Grey,
            "初期値はONです。自分の2枠は常に操作でき、他クライアントの枠は\n" +
            "ホストからのみ操作できます。OFFにすると\n" +
            "Duty Actionウィンドウは表示専用になります。");

        ImGui.Spacing();

        var clear = _config.DutyActionTransparent;
        if (ImGui.Checkbox("Duty Actionウィンドウの背景を透明にする", ref clear))
        {
            _config.DutyActionTransparent = clear;
            Save();
        }
        ImGui.TextColored(Grey,
            "初期値はONです。ゲーム画面上のオーバーレイとして表示します。\n" +
            "タイトルバーは残るため、背景を透明にしても\n" +
            "そこをドラッグして移動できます。");
    }

    private void DrawLostActionAutoUseSettings()
    {
        var auto = _config.AutoUseLostActions;
        if (ImGui.Checkbox(Loc.T("Automatically use Lost Actions in combat", "戦闘中にロストアクションを自動使用する"), ref auto))
        {
            _config.AutoUseLostActions = auto;
            Save();
        }
        ImGui.TextColored(Grey,
            "通常の自動使用設定です。ロストアクションは有限資源なので、\n" +
            "下記の優先順と使用間隔に従って1つずつ使用します。\n" +
            "既に同じバフが有効なものはスキップし、次候補へ進みます。\n" +
            "Essenceを無駄に上書きしません。");

        if (!auto)
        {
            ImGui.TextColored(Grey, "自動使用はOFFです。パーティ支援とDuty Actionバー設定には影響しません。");
            return;
        }

        ImGui.Spacing();

        var fire = _config.AutoFireLostActions;
        if (ImGui.Checkbox("…Duty Actionも実際に発動してチャージを消費する", ref fire))
        {
            _config.AutoFireLostActions = fire;
            Save();
        }
        ImGui.TextColored(Yellow,
            "初期値はOFFです。有効にする前に以下を確認してください。\n" +
            "\n" +
            "Holsterには消費方法が異なる2種類があります。アイテム型（Essence、各種Potion/Ether/\n" +
            "Medi Kit、Dynamis Dice、Reraiser、Lodestone、Light Curtain、Resistance Elixir等）は、\n" +
            "Holsterから使用した時点でそのまま消費されます。アクション型（Lost Cure、\n" +
            "Lost Font of Power、各種Banner等）は、HolsterからDuty Action枠へロードしただけでは\n" +
            "チャージを消費しません。\n" +
            "\n" +
            "このスイッチをONにすると、下で選択したアクション型もDuty Actionとして実際に発動し、\n" +
            "戦闘中は設定した使用間隔ごとにチャージを消費します。OFFのままなら従来どおり、\n" +
            "アイテム型だけを使用し、アクション型は発動しません。\n" +
            "\n" +
            "ロードが必要な場合はDuty Action 1を使用するため、そこに置いていたロードアウトが\n" +
            "置き換わることがあります。既にどちらかの枠にあるアクションはその枠から発動します。");

        ImGui.Spacing();

        var cooldown = _config.LostActionCooldownMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("最低使用間隔 (ms)", ref cooldown, 2000, 60000))
        {
            _config.LostActionCooldownMs = cooldown;
            Save();
        }

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "自動使用を許可する項目を選択します。「アイテム」はHolsterから直接消費される種類です。\n" +
            "Duty Action発動設定とは独立して消費されます。");

        if (!ImGui.BeginChild("##bbr_lostactions", new Vector2(0, 300), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var entry in _lostActions.All)
        {
            var selected = _config.AutoLostActions.Contains(entry.RowId);
            var kind = entry.IsItem ? ", アイテム" : string.Empty;
            var label = $"{entry.Name}  (重量 {entry.Weight}{kind})##la{entry.RowId}";
            if (ImGui.Checkbox(label, ref selected))
            {
                if (selected)
                    _config.AutoLostActions.Add(entry.RowId);
                else
                    _config.AutoLostActions.Remove(entry.RowId);
                Save();
            }

            if (selected && !entry.IsItem && !fire)
            {
                ImGui.SameLine();
                ImGui.TextColored(Yellow, "（発動しません）");
            }
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// The party-support task's settings. The task itself is started and stopped from the main
    /// window - this is only what it does once it is running.
    /// </summary>
    private void DrawPartySupport()
    {
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Party support", "パーティ支援"));
        ImGui.TextColored(Grey,
            "パーティメンバーへのロストアクションバフ維持と、HPが低いメンバーへの回復を行う独立タスクです。\n" +
            "開始/停止はメイン画面から行います。対象は自分のパーティのみで、\n" +
            "アライアンスメンバーや周囲のプレイヤーには使用しません。在庫切れ時は停止します。");

        ImGui.Spacing();

        var refresh = _config.PartyBuffRefreshFraction * 100f;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("残り効果時間が以下なら更新 (%)", ref refresh, 5f, 50f, "%.0f%%"))
        {
            _config.PartyBuffRefreshFraction = Math.Clamp(refresh / 100f, 0.05f, 0.5f);
            Save();
        }
        ImGui.TextColored(Grey,
            "バフが付いていないメンバーを最優先します。その後、残り効果時間がこの割合を下回った\n" +
            "メンバーを、残り時間が短い順に更新します。Lost Braveryは600秒なので、20%なら\n" +
            "残り2分未満が更新対象です。総効果時間はゲーム内Tooltipから取得します。\n" +
            "効果時間を取得できないアクションは自動更新せず、未付与のメンバーにだけ使用します。");

        ImGui.Spacing();

        var heal = _config.PartyHealBelowFraction * 100f;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("このHP未満を回復 (%)", ref heal, 20f, 95f, "%.0f%%"))
        {
            _config.PartyHealBelowFraction = Math.Clamp(heal / 100f, 0.2f, 0.95f);
            Save();
        }
        ImGui.TextColored(Grey,
            "回復は毎回の使用直前に判定し、HP割合が最も低いメンバーを優先します。\n" +
            "この閾値を設けることで、全員がほぼ満タンのときに不要な回復を消費しません。");

        ImGui.Spacing();

        var gap = _config.PartySupportGapMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("使用間隔 (ms)", ref gap, 500, 10000))
        {
            _config.PartySupportGapMs = gap;
            Save();
        }

        var slot = _config.PartySupportSlot + 1;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("ロード先Duty Action枠", ref slot, 1, 2))
        {
            _config.PartySupportSlot = Math.Clamp(slot - 1, 0, 1);
            Save();
        }
        ImGui.TextColored(Grey,
            "初期値はDuty Action 2です。通常の自動使用がDuty Action 1を使うため、同じ枠を\n" +
            "互いにロードし直して競合するのを避けます。既にどちらかの枠にあるアクションは\n" +
            "その枠から使用するため、この設定は新しくロードする場合だけ影響します。");

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "維持するアクションを優先順に選択します。パーティメンバーを対象にできるものだけを表示します。\n" +
            "蘇生系と、状態判定できないアクションは除外します。\n" +
            "既に有効なバフを重複使用しないことを優先します。");

        if (!ImGui.BeginChild("##bbr_partysupport", new Vector2(0, 200), true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var entry in _lostActions.PartySupport)
        {
            var selected = _config.PartySupportActions.Contains(entry.RowId);
            var kind = entry.IsPartyHeal
                ? "回復"
                : entry.HasDuration ? $"{Describe(entry.DurationSeconds)} バフ" : "バフ";

            if (ImGui.Checkbox($"{entry.Name}  ({kind})##ps{entry.RowId}", ref selected))
            {
                if (selected)
                    _config.PartySupportActions.Add(entry.RowId);
                else
                    _config.PartySupportActions.Remove(entry.RowId);
                Save();
            }

            // A buff with no duration can only ever be given to someone who has nothing - saying so
            // here is cheaper than the operator wondering why nobody is being topped up.
            if (selected && !entry.IsPartyHeal && !entry.HasDuration)
            {
                ImGui.SameLine();
                ImGui.TextColored(Yellow, "（効果時間データなし - 自動更新しません）");
            }
        }

        ImGui.EndChild();
    }

    private static string Describe(float seconds)
    {
        var total = (int)seconds;
        if (total >= 3600) return $"{total / 3600}h";
        return total >= 60 ? $"{total / 60}m" : $"{total}s";
    }
}
