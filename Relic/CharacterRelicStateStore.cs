using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace BozjaBuddyReborn.Relic;

/// <summary>
/// Keeps the legacy Configuration.FarmMaterialItemId field as the runtime-facing value while
/// persisting that selection separately for each character. This avoids rewriting every existing
/// selector/window call site and prevents one character's farm target leaking into another.
/// </summary>
public sealed unsafe class CharacterRelicStateStore(Configuration config)
{
    private readonly Configuration _config = config;
    private ulong _activeContentId;
    private uint _lastObservedFarmTarget;

    public void Tick()
    {
        var playerState = PlayerState.Instance();
        var cid = playerState != null && playerState->IsLoaded ? playerState->ContentId : 0UL;
        if (cid == 0)
            return;

        if (_activeContentId != cid)
        {
            if (_activeContentId != 0)
                _config.CharacterFarmMaterialItemIds[_activeContentId] = _config.FarmMaterialItemId;

            _activeContentId = cid;
            _config.FarmMaterialItemId = _config.CharacterFarmMaterialItemIds.TryGetValue(cid, out var stored)
                ? stored
                : 0;
            _lastObservedFarmTarget = _config.FarmMaterialItemId;
            ConfigSaver.Save(_config);
            Svc.Log.Information("[BozjaBuddyReborn] Loaded character-specific Resistance Relic farm state.");
            return;
        }

        if (_config.FarmMaterialItemId == _lastObservedFarmTarget)
            return;

        _lastObservedFarmTarget = _config.FarmMaterialItemId;
        _config.CharacterFarmMaterialItemIds[cid] = _lastObservedFarmTarget;
        ConfigSaver.Save(_config);
        Svc.Log.Information("[BozjaBuddyReborn] Saved character-specific Resistance Relic farm target.");
    }
}
