using System;
using System.Collections.Generic;
using BozjaBuddyReborn.Relic;

namespace BozjaBuddyReborn.Relic;

public readonly record struct RelicFarmUpdate(
    bool ChangedTarget,
    bool Stop,
    uint PreviousItemId,
    uint CurrentItemId,
    string JapaneseStatus)
{
    public static readonly RelicFarmUpdate None = new(false, false, 0, 0, string.Empty);
}

/// <summary>
/// Advances an explicitly-selected Resistance Relic farm target after it becomes satisfied.
/// A target is NEVER invented when FarmMaterialItemId is zero: the first target remains a user
/// decision. Once farming has begun, continuation stays inside the current field territory and
/// prefers remaining materials from the same relic stage before later stages.
/// </summary>
public sealed class RelicFarmCoordinator(Configuration config, RelicTracker tracker)
{
    private readonly Configuration _config = config;
    private readonly RelicTracker _tracker = tracker;

    public RelicFarmUpdate Tick(uint territory)
    {
        var currentId = _config.FarmMaterialItemId;
        if (currentId == 0)
            return RelicFarmUpdate.None;

        if (!TryFindMaterial(currentId, out var stage, out var material))
            return RelicFarmUpdate.None;

        if (RelicTracker.ItemCount(currentId) < material.Required)
            return RelicFarmUpdate.None;

        if (_config.RelicFarmStopMode == RelicFarmStopMode.SelectedMaterialComplete)
        {
            return new RelicFarmUpdate(false, true, currentId, currentId,
                "指定したResistance Relic素材が必要数に達したため停止します。");
        }

        if (_config.RelicFarmStopMode == RelicFarmStopMode.CurrentStageComplete
            && StageMaterialsReady(stage))
        {
            return new RelicFarmUpdate(false, true, currentId, currentId,
                "現在のResistance Relic段階に必要な素材が揃ったため停止します。");
        }

        if (!_config.RelicAutoContinue)
        {
            return new RelicFarmUpdate(false, true, currentId, currentId,
                "指定したResistance Relic素材が必要数に達しました。自動継続が無効なため停止します。");
        }

        var next = FindNextInTerritory(territory, stage, currentId);
        if (next == 0)
        {
            return new RelicFarmUpdate(false, true, currentId, currentId,
                "このエリアで続けて取得できるResistance Relic素材がありません。エリア移動は自動化しないため停止します。");
        }

        _config.FarmMaterialItemId = next;
        ConfigSaver.Save(_config);
        return new RelicFarmUpdate(true, false, currentId, next,
            "Resistance Relicの次の不足素材へ自動的に切り替えました。");
    }

    private uint FindNextInTerritory(uint territory, RelicStage completedStage, uint completedItem)
    {
        // Same stage first: this keeps a one-time multi-material grind grouped together.
        foreach (var material in completedStage.Materials)
        {
            if (material.ItemId == completedItem || RelicTracker.ItemCount(material.ItemId) >= material.Required)
                continue;
            var source = ZoneDrops.For(material.ItemId);
            if (source is { } drop && drop.Territory == territory)
                return material.ItemId;
        }

        // Then later/other outstanding field materials, in canonical relic-stage order.
        foreach (var progress in _tracker.OutstandingMaterials())
        {
            if (progress.ItemId == completedItem)
                continue;
            var source = ZoneDrops.For(progress.ItemId);
            if (source is { } drop && drop.Territory == territory)
                return progress.ItemId;
        }

        return 0;
    }

    private static bool StageMaterialsReady(RelicStage stage)
    {
        foreach (var material in stage.Materials)
            if (RelicTracker.ItemCount(material.ItemId) < material.Required)
                return false;
        return true;
    }

    private static bool TryFindMaterial(uint itemId, out RelicStage stage, out RelicMaterial material)
    {
        foreach (var candidateStage in ResistanceRelic.Stages)
        foreach (var candidateMaterial in candidateStage.Materials)
        {
            if (candidateMaterial.ItemId != itemId)
                continue;
            stage = candidateStage;
            material = candidateMaterial;
            return true;
        }

        stage = default;
        material = default;
        return false;
    }
}
