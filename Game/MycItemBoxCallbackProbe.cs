using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Narrow runtime probe for the only unresolved Initialize primitive: server-backed transfer
/// between Lost Finds Cache and Lost Holster. It observes only MYCItemBox / MYCItemBagTrade receive
/// events and is disabled by default. It never mutates an addon and never replays a callback.
///
/// The event fields follow the same AddonLifecycle diagnostic pattern used by AutoRetainer's
/// EventLogger. Once a real manual transfer yields a stable event signature, that signature can be
/// documented under docs/research and implemented by a separately guarded transfer executor.
/// </summary>
public sealed unsafe class MycItemBoxCallbackProbe : IDisposable
{
    private static readonly string[] Addons = ["MYCItemBox", "MYCItemBagTrade"];
    private readonly Configuration _config;

    public MycItemBoxCallbackProbe(Configuration config)
    {
        _config = config;
        foreach (var addon in Addons)
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, addon, OnReceiveEvent);
    }

    public void Dispose()
    {
        foreach (var addon in Addons)
            Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, addon, OnReceiveEvent);
    }

    private void OnReceiveEvent(AddonEvent _, AddonArgs args)
    {
        if (!_config.LogMycItemBoxCallbacks || args is not AddonReceiveEventArgs evt)
            return;

        try
        {
            var atkEventParam = evt.AtkEvent == 0 ? 0 : ((AtkEvent*)evt.AtkEvent)->Param;
            var data = evt.AtkEventData == 0
                ? "(null)"
                : Convert.ToHexString(MemoryHelper.ReadRaw(evt.AtkEventData, 40));

            Svc.Log.Information(
                $"[BozjaBuddyReborn] MYC transfer probe addon={args.AddonName} " +
                $"type={evt.AtkEventType} eventParam=0x{evt.EventParam:X} atkParam={atkEventParam} data40={data}");
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] MYC transfer probe could not decode event: {ex.Message}");
        }
    }
}
