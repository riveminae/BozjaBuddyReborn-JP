using System.Numerics;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Relic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

using BozjaBuddyReborn;

namespace BozjaBuddyReborn.Windows;

/// <summary>
/// Resistance-relic progress: which stage you are on, whether its quest is done, and how many
/// of each material you are holding.
///
/// Scoped to relic progression only - no fragments, no Lost Action inventory, no field notes.
/// </summary>
public sealed class RelicWindow : Window
{
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Blue = new(0.50f, 0.75f, 1.00f, 1f);

    private readonly RelicTracker _tracker;
    private readonly Configuration _config;
    private bool _showAllStages;

    public RelicWindow(RelicTracker tracker, Configuration config)
        : base("レジスタンス・ウェポン###BozjaBuddyRebornRelic")
    {
        _tracker = tracker;
        _config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(1400, 1200),
        };
    }

    public override void Draw()
    {
        DrawUnlocks();
        ImGui.Separator();

        ImGui.Checkbox(Loc.T("Show every stage", "全段階を表示"), ref _showAllStages);
        ImGui.SameLine();
        ImGui.TextColored(Grey, "OFFの場合は現在段階と未完了素材だけを表示します");
        ImGui.Separator();

        if (_showAllStages)
            DrawAllStages();
        else
            DrawCurrent();
    }

    private void DrawUnlocks()
    {
        var bozja = RelicTracker.BozjaUnlocked;
        var zadnor = RelicTracker.ZadnorUnlocked;

        ImGui.TextColored(bozja ? Green : Yellow,
            bozja ? "南方ボズヤ戦線: 解放済み" : "南方ボズヤ戦線: 未解放（荒鷲の巣作戦）");
        ImGui.TextColored(zadnor ? Green : Yellow,
            zadnor ? "ザトゥノル高原: 解放済み" : "ザトゥノル高原: 未解放");
    }

    private void DrawCurrent()
    {
        var current = _tracker.CurrentStage();
        if (current is not { } stage)
        {
            ImGui.TextColored(Green, "追跡対象の全段階が完了しています。");
            return;
        }

        DrawStage(stage, expanded: true);

        ImGui.Separator();
        ImGui.TextColored(Blue, "全段階の未完了素材");
        ImGui.TextColored(Grey, "複数段階で必要になる素材をまとめて確認できます。");

        var outstanding = _tracker.OutstandingMaterials();
        if (outstanding.Count == 0)
        {
            ImGui.TextColored(Green, "不足素材はありません。");
            return;
        }

        if (!ImGui.BeginTable("##bbr_outstanding", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn(Loc.T("Material", "素材"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("Held", "所持数"), ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn(Loc.T("Farm", "周回"), ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn(Loc.T("Where", "入手場所"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var m in outstanding)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Name);

            ImGui.TableNextColumn();
            ImGui.TextColored(Yellow, $"{m.Held} / {m.Required}");

            // The zone-targeted farm button. Only offered for materials this plugin can
            // actually route to - alliance-raid and deep-dungeon drops get no button, because
            // pretending we could farm them would be a lie.
            ImGui.TableNextColumn();
            var drop = ZoneDrops.For(m.ItemId);
            if (drop is { } d)
            {
                var active = _config.FarmMaterialItemId == m.ItemId;
                if (active)
                {
                    if (ImGui.Button($"停止##farm{m.ItemId}"))
                    {
                        _config.FarmMaterialItemId = 0;
                        ConfigSaver.Save(_config);
                    }
                }
                else if (ImGui.Button($"周回開始##farm{m.ItemId}"))
                {
                    _config.FarmMaterialItemId = m.ItemId;
                    ConfigSaver.Save(_config);
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        $"周回対象を {d.Describe()} に限定します。\n" +
                        "対象外のイベントはスキップします。");
            }
            else
            {
                ImGui.TextColored(Grey, "-");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("フィールド内で取得する素材ではないため、このプラグインでは自動周回できません。");
            }

            ImGui.TableNextColumn();
            // Prefer the precise region/activity line over the stage's prose blurb.
            ImGui.TextColored(Grey, drop is { } dd ? dd.Describe() : m.Source);
        }

        ImGui.EndTable();

        if (_config.FarmMaterialItemId != 0 && ZoneDrops.For(_config.FarmMaterialItemId) is { } active2)
        {
            ImGui.Separator();
            ImGui.TextColored(Blue, $"周回中: {active2.Describe()}");
            ImGui.SameLine();
            if (ImGui.SmallButton("解除##farmclear"))
            {
                _config.FarmMaterialItemId = 0;
                ConfigSaver.Save(_config);
            }
        }
    }

    private void DrawAllStages()
    {
        foreach (var stage in _tracker.ReadAll())
            DrawStage(stage, expanded: false);
    }

    private static void DrawStage(StageProgress p, bool expanded)
    {
        var stage = p.Stage;

        var statusColour = p.QuestComplete ? Green : p.QuestAccepted ? Yellow : Grey;
        var statusText = p.QuestComplete
            ? (stage.OneTime ? "完了" : "段階解放済み")
            : p.QuestAccepted ? $"進行中（クエスト進行度 {p.QuestSequence}）" : "未開始";

        var header = $"{stage.Order}. {stage.Name}";
        if (stage.ItemLevel.Length > 0)
            header += $"  (i{stage.ItemLevel})";
        if (stage.OneTime)
            header += "  [一度のみ]";

        ImGui.SetNextItemOpen(expanded, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader($"{header}###stage{stage.Order}"))
        {
            ImGui.SameLine();
            ImGui.TextColored(statusColour, statusText);
            return;
        }

        ImGui.TextColored(statusColour, $"{stage.QuestName} - {statusText}");
        ImGui.TextColored(Grey, stage.Note);

        if (stage.Materials.Count > 0)
        {
            if (ImGui.BeginTable($"##mats{stage.Order}", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn(Loc.T("Material", "素材"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(Loc.T("Held", "所持数"), ImGuiTableColumnFlags.WidthFixed, 130);
                ImGui.TableSetupColumn(Loc.T("Where", "入手場所"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var m in p.Materials)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(m.Name);

                    ImGui.TableNextColumn();
                    ImGui.TextColored(m.Satisfied ? Green : Yellow, $"{m.Held} / {m.Required}");
                    ImGui.SameLine();
                    ImGui.ProgressBar(m.Fraction, new Vector2(60, 0), "");

                    ImGui.TableNextColumn();
                    ImGui.TextColored(Grey, m.Source);
                }

                ImGui.EndTable();
            }

            ImGui.TextColored(p.MaterialsReady ? Green : Grey,
                p.MaterialsReady ? "必要素材が揃っています。次の工程へ進めます。" : "必要素材が不足しています。");
        }

        ImGui.Spacing();
    }
}