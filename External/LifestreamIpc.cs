using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace BozjaBuddyReborn.External;

/// <summary>
/// Optional Lifestream IPC used for the Bozja/Zadnor custom aethernet.
///
/// The gate names are the same surface used by Ocelot/BOCCHI.  Unlike Ocelot's
/// thin wrapper, this fork treats Lifestream as optional: every call is guarded
/// by HasFunction and no IPC exception is allowed to escape into the controller.
///
/// Source/API reference:
///   OhKannaDuh/Ocelot, Ocelot/Ipc/Lifestream/LifestreamIpc.cs
///   commit 28fba75f12ac66b46a18a5440aa0828c20360f71 (MIT).
/// </summary>
public sealed class LifestreamIpc
{
    private readonly ICallGateSubscriber<bool>? _isBusy;
    private readonly ICallGateSubscriber<uint>? _getActiveCustomAetheryte;
    private readonly ICallGateSubscriber<uint, bool>? _aethernetTeleportByPlaceNameId;

    public LifestreamIpc(IDalamudPluginInterface plugin)
    {
        _isBusy = Bind(() => plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy"));
        _getActiveCustomAetheryte = Bind(() => plugin.GetIpcSubscriber<uint>("Lifestream.GetActiveCustomAetheryte"));
        _aethernetTeleportByPlaceNameId = Bind(
            () => plugin.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportByPlaceNameId"));
    }

    private static T? Bind<T>(Func<T> resolve) where T : class
    {
        try { return resolve(); }
        catch { return null; }
    }

    public bool Available
    {
        get
        {
            try
            {
                return _isBusy?.HasFunction == true
                    && _getActiveCustomAetheryte?.HasFunction == true
                    && _aethernetTeleportByPlaceNameId?.HasFunction == true;
            }
            catch { return false; }
        }
    }

    public bool IsBusy
    {
        get
        {
            try { return _isBusy?.HasFunction == true && _isBusy.InvokeFunc(); }
            catch { return false; }
        }
    }

    public uint ActiveCustomAetheryte
    {
        get
        {
            try { return _getActiveCustomAetheryte?.HasFunction == true ? _getActiveCustomAetheryte.InvokeFunc() : 0; }
            catch { return 0; }
        }
    }

    public bool AethernetTeleportByPlaceNameId(uint placeNameRowId)
    {
        try
        {
            return _aethernetTeleportByPlaceNameId?.HasFunction == true
                   && _aethernetTeleportByPlaceNameId.InvokeFunc(placeNameRowId);
        }
        catch
        {
            return false;
        }
    }
}
