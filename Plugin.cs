using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using BozjaBuddyReborn.Relic;
using BozjaBuddyReborn.Windows;
using Dalamud.Game.Command;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;

namespace BozjaBuddyReborn;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Bozja Buddy Reborn";

    private const string CommandMain = "/bbr";
    private const string CommandRelic = "/bbrelic";

    /// <summary>
    /// Controller cadence. The engagement state machine has nothing useful to do at 60 Hz, and
    /// every tick reads live game memory and pokes IPC, so it runs at ~5 Hz instead.
    /// </summary>
    private const int TickIntervalMs = 200;

    private readonly Configuration _config;
    private readonly WindowSystem _windows = new("BozjaBuddyReborn");

    private readonly CeCatalog _catalog = new();
    private readonly LostActionCatalog _lostActions = new();
    private readonly RelicTracker _relicTracker = new();
    private readonly CharacterRelicStateStore _characterRelicState;

    private readonly NavmeshIpc _navmesh;
    private readonly CombatDirector _director;
    private readonly DependencySupervisor _dependencies;
    private readonly TextAdvanceIpc _textAdvance;
    private readonly DeathRecoveryDriver _deathRecovery;
    private readonly MultiboxLink _link = new();
    private readonly AggroAvoidance _aggroAvoidance;
    private readonly Movement _movement;
    private readonly MycItemBoxCallbackProbe _mycItemBoxProbe;
    private readonly CombatApproach _approach;
    private readonly RegionResolver _regions;
    private readonly TargetSelector _selector;
    private readonly HolsterDriver _holster;
    private readonly LostItemBoxInventory _lostItemInventory;
    private readonly SupplyManager _supplies;
    private readonly ErrandRunner _errands;
    private readonly LoadoutDriver _loadoutDriver;
    private readonly SignUpRunner _signUps;
    private readonly PartySupportDriver _partySupport;
    private readonly BozjaController _controller;
    private readonly SocialRequestGuard _socialRequests;

    private readonly DutyActionSync _dutySync;

    private readonly MainWindow _mainWindow;
    private readonly ConfigWindow _configWindow;
    private readonly RelicWindow _relicWindow;
    private readonly DutyActionWindow _dutyWindow;
    private readonly MultiboxerWindow _multiboxerWindow;

    private bool _multiboxStarted;
    private long _debugOverlayScanMs;
    private List<DangerZone> _debugOverlayDangerZones = [];

    /// <summary>Last known character name, announced over the multibox pipe. See SyncMultiboxLink.</summary>
    private string _selfName = "unknown";

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        _config = ConfigRecovery.Load(pluginInterface);
        _characterRelicState = new CharacterRelicStateStore(_config);

        _navmesh = new NavmeshIpc(pluginInterface);
        _director = new CombatDirector(pluginInterface, _config);
        _dependencies = new DependencySupervisor(_navmesh, _director);
        _textAdvance = new TextAdvanceIpc(pluginInterface);
        _deathRecovery = new DeathRecoveryDriver(_textAdvance);
        _aggroAvoidance = new AggroAvoidance(_config);
        _movement = new Movement(_navmesh, _config, _aggroAvoidance, pluginInterface);
        _mycItemBoxProbe = new MycItemBoxCallbackProbe(_config);
        _approach = new CombatApproach(_navmesh, _config);
        _regions = new RegionResolver(_config);
        _selector = new TargetSelector(_catalog, _config, _regions, _movement);
        _holster = new HolsterDriver(_config, _lostActions);
        _lostItemInventory = new LostItemBoxInventory(_lostActions);
        _supplies = new SupplyManager(_config, _lostActions, _lostItemInventory);
        _errands = new ErrandRunner(_movement, _navmesh);
        _loadoutDriver = new LoadoutDriver(_lostActions);

        // Registration itself is always immediate and remote. Only the second-phase Commence is
        // gated, and only for the one Q109C exception: confirmed complete loss of Potion Kit
        // protection AND usable self-healing. An unavailable inventory read is not critical in
        // SupplyManager, so this fails open rather than sacrificing a CE to uncertain telemetry.
        _signUps = new SignUpRunner(() => !_supplies.Evaluate().CriticalNoRecovery);
        _partySupport = new PartySupportDriver(_config, _lostActions);

        _controller = new BozjaController(
            _config, _catalog, _selector, _movement, _director, _approach, _holster, _supplies, _link, _navmesh, _regions,
            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery, _dependencies);
        _socialRequests = new SocialRequestGuard(_config, () => _controller.Running);

        _mainWindow = new MainWindow(_config, _controller, _director, _navmesh, _link, _catalog) { IsOpen = false };
        _configWindow = new ConfigWindow(_config, _lostActions, _regions, _aggroAvoidance)
        {
            IsOpen = false,
            OnIdleSpotsChanged = () => _controller.InvalidateIdleSpots(),
        };
        _relicWindow = new RelicWindow(_relicTracker, _config) { IsOpen = false };

        _dutySync = new DutyActionSync(_config, _link);
        _dutyWindow = new DutyActionWindow(_config, _dutySync, _link) { IsOpen = false };
        _mainWindow.OnOpenDutyActions = () => _dutyWindow.IsOpen = true;

        _multiboxerWindow = new MultiboxerWindow(
            _config, _link, _dutySync, _controller, _lostActions, _errands, _signUps) { IsOpen = false };
        _mainWindow.OnOpenMultiboxer = () => _multiboxerWindow.IsOpen = true;

        _windows.AddWindow(_mainWindow);
        _windows.AddWindow(_configWindow);
        _windows.AddWindow(_relicWindow);
        _windows.AddWindow(_dutyWindow);
        _windows.AddWindow(_multiboxerWindow);

        pluginInterface.UiBuilder.Draw += _windows.Draw;
        pluginInterface.UiBuilder.Draw += DrawDebugWorldOverlay;
        pluginInterface.UiBuilder.OpenMainUi += OpenMain;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;

        Svc.Commands.AddHandler(CommandMain, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Bozja Buddy Reborn. /bbr start | stop | config | relic | duty | boxes",
        });
        Svc.Commands.AddHandler(CommandRelic, new CommandInfo(OnRelicCommand)
        {
            HelpMessage = "Open the Resistance relic progress window.",
        });

        Svc.Framework.Update += OnUpdate;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;

        // NOT SyncMultiboxLink() here. Dalamud constructs plugins on a threadpool thread, and
        // that method reads the object table for the character name - which API 15 refuses off
        // the framework thread, throwing "Not on main thread!" out of the constructor and failing
        // the whole plugin load. OnUpdate already calls it every frame, so the constructor call
        // only ever bought one frame, and only for users with multibox enabled (the disabled path
        // returns before the read, which is why this stayed hidden).
    }

    private void OpenMain() => _mainWindow.IsOpen = true;
    private void OpenConfig() => _configWindow.IsOpen = true;

    /// <summary>TEST-only world-space diagnostics; never affects movement or selection.</summary>
    private void DrawDebugWorldOverlay()
    {
        if (!_config.DebugWorldOverlay || !FieldState.InFieldZone)
            return;

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return;

        var now = Environment.TickCount64;
        if (now - _debugOverlayScanMs >= 500)
        {
            _debugOverlayScanMs = now;
            _debugOverlayDangerZones = _aggroAvoidance.Scan(140f);
        }

        var draw = ImGui.GetForegroundDrawList();
        var routeColour = ImGui.GetColorU32(new Vector4(0.25f, 0.80f, 1.00f, 0.90f));
        var teleportColour = ImGui.GetColorU32(new Vector4(0.75f, 0.45f, 1.00f, 0.90f));
        var goalColour = ImGui.GetColorU32(new Vector4(0.30f, 1.00f, 0.45f, 0.95f));
        var dangerColour = ImGui.GetColorU32(new Vector4(1.00f, 0.25f, 0.20f, 0.80f));
        var marginColour = ImGui.GetColorU32(new Vector4(1.00f, 0.75f, 0.20f, 0.55f));

        var goal = _movement.DebugRouteGoal;
        if (goal == Vector3.Zero && _controller.CurrentObjective.IsSet)
            goal = _controller.CurrentObjective.Position;

        if (_movement.DebugRouteDeparture is { } departure)
        {
            DrawWorldLine(me.Position, departure, routeColour, 2.5f);
            DrawWorldLabel(departure, $"出発Aethernet #{_movement.DebugRouteDeparturePlaceNameId}", routeColour);

            if (_movement.DebugRouteInbound is { } inbound)
            {
                DrawWorldLine(departure, inbound, teleportColour, 2.0f);
                DrawWorldLabel(inbound, $"到着Aethernet #{_movement.DebugRouteInboundPlaceNameId}", teleportColour);
                if (goal != Vector3.Zero)
                    DrawWorldLine(inbound, goal, routeColour, 2.5f);
            }
        }
        else if (goal != Vector3.Zero)
        {
            DrawWorldLine(me.Position, goal, routeColour, 2.5f);
        }

        if (goal != Vector3.Zero)
            DrawWorldLabel(goal, $"目的地 / {_movement.TravelMode}", goalColour);

        foreach (var zone in _debugOverlayDangerZones)
            DrawDangerZone(zone, dangerColour, marginColour);

        return;

        void DrawDangerZone(DangerZone zone, uint danger, uint margin)
        {
            DrawWorldCircle(zone.Position, zone.ProximityRadius, danger, 2.0f);

            // Sight cone: radius arc plus the two radial edges.
            const int ArcSegments = 20;
            var previous = Vector3.Zero;
            for (var i = 0; i <= ArcSegments; i++)
            {
                var t = i / (float)ArcSegments;
                var angle = zone.Rotation - zone.ConeHalfAngleRad + t * zone.ConeHalfAngleRad * 2f;
                var point = zone.Position + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * zone.SightRadius;
                if (i > 0)
                    DrawWorldLine(previous, point, danger, 1.8f);
                previous = point;
            }
            var left = zone.Position + new Vector3(
                MathF.Sin(zone.Rotation - zone.ConeHalfAngleRad), 0f,
                MathF.Cos(zone.Rotation - zone.ConeHalfAngleRad)) * zone.SightRadius;
            var right = zone.Position + new Vector3(
                MathF.Sin(zone.Rotation + zone.ConeHalfAngleRad), 0f,
                MathF.Cos(zone.Rotation + zone.ConeHalfAngleRad)) * zone.SightRadius;
            DrawWorldLine(zone.Position, left, danger, 1.8f);
            DrawWorldLine(zone.Position, right, danger, 1.8f);

            var extra = _config.DangerClearance
                        + (zone.Strength == FieldEnemyStrength.Star ? _config.DangerStarExtraClearance : 0f);
            DrawWorldCircle(zone.Position, zone.OuterRadius + extra, margin, 1.2f);

            var rank = zone.Strength switch
            {
                FieldEnemyStrength.Star => "★",
                FieldEnemyStrength.Unknown => "?",
                _ => ((byte)zone.Strength).ToString(),
            };
            DrawWorldLabel(zone.Position, $"[{rank}] {zone.Name}", danger);
        }

        void DrawWorldCircle(Vector3 center, float radius, uint colour, float thickness)
        {
            const int Segments = 32;
            var previous = center + new Vector3(radius, 0f, 0f);
            for (var i = 1; i <= Segments; i++)
            {
                var angle = MathF.Tau * i / Segments;
                var current = center + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
                DrawWorldLine(previous, current, colour, thickness);
                previous = current;
            }
        }

        void DrawWorldLine(Vector3 a, Vector3 b, uint colour, float thickness)
        {
            if (Svc.GameGui.WorldToScreen(a, out var sa) && Svc.GameGui.WorldToScreen(b, out var sb))
                draw.AddLine(sa, sb, colour, thickness);
        }

        void DrawWorldLabel(Vector3 world, string text, uint colour)
        {
            var raised = world + new Vector3(0f, 2.5f, 0f);
            if (Svc.GameGui.WorldToScreen(raised, out var screen))
                draw.AddText(screen, colour, text);
        }
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "start":
                _controller.Start();
                break;
            case "stop":
                _controller.Stop();
                break;
            case "config":
            case "cfg":
                _configWindow.IsOpen = !_configWindow.IsOpen;
                break;
            case "relic":
                _relicWindow.IsOpen = !_relicWindow.IsOpen;
                break;
            case "duty":
            case "actions":
                _dutyWindow.IsOpen = !_dutyWindow.IsOpen;
                break;
            case "box":
            case "boxes":
            case "mb":
                _multiboxerWindow.IsOpen = !_multiboxerWindow.IsOpen;
                break;
            default:
                _mainWindow.IsOpen = !_mainWindow.IsOpen;
                break;
        }
    }

    private void OnRelicCommand(string command, string args)
        => _relicWindow.IsOpen = !_relicWindow.IsOpen;

    private void OnTerritoryChanged(uint territory)
    {
        // A zone change invalidates every cached path and both plugins' latched state.
        _approach.Release();
        _movement.Stop();
        _director.Resync();
        _holster.Reset();
        Mount.Reset();
        _controller.InvalidateIdleSpots();

        // Object ids do not survive a zone change, so a suppression list kept from the old zone
        // would silently ignore whichever new enemies happen to reuse those ids.
        _aggroAvoidance.ClearSuppressions();

        // Neither does an objective. The link latched the last one it was told about and nothing
        // cleared it on a zone change, so a box that followed the group into Zadnor was still
        // holding a Bozjan engagement - and since the two zones' coordinates overlap, it pathed
        // to the stale position rather than reporting that it had nothing to do.
        _link.ResetObjective();

        // Party support is stopped rather than reset, and for the same id reason: it holds a table
        // of members the game recently refused, keyed by object id, and a stale entry would
        // silently pass over a legitimate new target. Its party is gone too - a zone change is the
        // end of the job it was doing, not an interruption to it.
        _partySupport.Stop($"Left the zone (now in {BozjaZones.Name(territory)}).");

        // Leaving Bozja entirely is a stop, not a pause - there is nothing to orchestrate
        // outside the field zones and leaving the rotation armed would be a nasty surprise.
        if (_controller.Running && !BozjaZones.IsFieldZone(territory))
            _controller.Stop($"Left the field zone (now in {BozjaZones.Name(territory)}).");
    }

    private void OnUpdate(object _)
    {
        _characterRelicState.Tick();
        SyncCallbackLogging();
        SyncMultiboxLink();

        // Drive any outstanding vnavmesh stop to completion, every frame and regardless of
        // whether the controller is running.
        //
        // vnavmesh's Path.Stop clears the waypoint list but CANNOT cancel a pathfind that is
        // already computing (FollowPath.Stop never touches AsyncMoveRequest, and that request
        // hands its result to FollowPath.Move whenever it lands, stop or no stop). So one Stop
        // is a request, not a guarantee. This lives here rather than in the controller tick
        // because the paths that matter most - stopping the run, leaving the field zone,
        // unloading the plugin - are exactly the ones where the controller is no longer
        // ticking, and a pathfind landing after those would walk the character off on its own.
        _movement.PumpStop();
        _approach.PumpStop();

        // Own duty slots are read every frame (two pointer dereferences) so your own row stays
        // smooth; the wire send inside is throttled.
        _dutySync.Tick();

        // Operator instructions are drained at frame rate, ABOVE the throttle. A duty-action press
        // from the hotbar window rides this queue - an ImGui click happens during the draw
        // callback, which is not the framework thread, so the queue is also what gets the press
        // onto a legal thread. Leaving it inside the 200ms tick made the button feel unreliable.
        try
        {
            _controller.PumpCommands();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[BozjaBuddyReborn] Instruction failed.");
        }

        if (!EzThrottler.Throttle("BozjaBuddyReborn.Tick", TickIntervalMs))
            return;

        try
        {
            _controller.Tick();
            DiagnosticsRecorder.Observe(_controller.State, _controller.Status);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[BozjaBuddyReborn] Controller tick failed; stopping for safety.");
            _controller.Stop("内部エラーのため停止しました。/xllog を確認してください。");
            DiagnosticsRecorder.Warning("内部エラーのためコントローラーを停止しました。");
            DiagnosticsRecorder.Observe(_controller.State, _controller.Status);
        }
    }

    /// <summary>
    /// Install or remove the UI-callback log to match the setting, so it can be turned on
    /// mid-session without a reload - which matters, because the thing it is for is a two-minute
    /// window that has already started by the time you think to enable it.
    ///
    /// See Configuration.LogUiCallbacks. The hook is ECommons', it logs at Debug level, and it
    /// covers every addon in the client rather than only ours.
    /// </summary>
    private void SyncCallbackLogging()
    {
        if (_config.LogUiCallbacks == _callbackLogInstalled)
            return;

        try
        {
            if (_config.LogUiCallbacks)
                ECommons.Automation.Callback.InstallHook();
            else
                ECommons.Automation.Callback.UninstallHook();

            _callbackLogInstalled = _config.LogUiCallbacks;
        }
        catch (Exception ex)
        {
            // Never let a diagnostic take the plugin down; just stop trying.
            _callbackLogInstalled = _config.LogUiCallbacks;
            Svc.Log.Warning($"[BozjaBuddyReborn] Could not toggle the UI callback log: {ex.Message}");
        }
    }

    private bool _callbackLogInstalled;

    /// <summary>
    /// Bring the pipe link in line with configuration. Restarting it on a role change is what
    /// lets the user flip host/client without reloading the plugin.
    /// </summary>
    private void SyncMultiboxLink()
    {
        var wanted = _config.MultiboxEnabled;

        if (!wanted)
        {
            if (_multiboxStarted)
            {
                _link.Stop();
                _multiboxStarted = false;
            }
            return;
        }

        // Guarded rather than assumed: the object table throws outright if it is touched off the
        // framework thread. Degrading to "unknown" is always better than taking the plugin down.
        //
        // Cached, because this runs on EVERY framework frame (deliberately - it is what notices
        // a role change) and marshalling a name string out of the object table 60+ times a
        // second to hand it to a method that discards it unchanged is pure waste.
        if (Svc.Framework.IsInFrameworkUpdateThread)
        {
            var name = Svc.Objects.LocalPlayer?.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(name) && name != _selfName)
                _selfName = name;
        }

        var roleMatches = _multiboxStarted && _link.IsHost == _config.MultiboxIsHost;
        if (roleMatches && _link.Running)
        {
            // The character is not loaded when the plugin starts, so the first HELLO goes out as
            // "unknown". UpdateSelfName now PUSHES a corrected name down the live link rather
            // than only assigning a field - nothing else ever corrected it, because the pipe has
            // no keepalive and so never drops on its own.
            _link.UpdateSelfName(_selfName);
            return;
        }

        _link.Start(_config.MultiboxIsHost, _selfName);
        _multiboxStarted = true;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;

        // A hook that outlives the plugin crashes the client on the next callback.
        if (_callbackLogInstalled)
        {
            try { ECommons.Automation.Callback.UninstallHook(); }
            catch { /* best effort during teardown */ }
        }

        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;

        // Never leave the user's combat plugins latched into our configuration.
        try { _controller.Stop("Plugin unloaded."); }
        catch { /* best effort during teardown */ }

        try { _director.ReleaseControl(); }
        catch { /* best effort */ }

        try { _socialRequests.Dispose(); }
        catch { /* best effort */ }

        _link.Dispose();
        try { _mycItemBoxProbe.Dispose(); }
        catch { /* best effort */ }

        Svc.Commands.RemoveHandler(CommandMain);
        Svc.Commands.RemoveHandler(CommandRelic);

        Svc.PluginInterface.UiBuilder.Draw -= _windows.Draw;
        Svc.PluginInterface.UiBuilder.Draw -= DrawDebugWorldOverlay;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        _windows.RemoveAllWindows();

        // Through ConfigSaver, never straight to Dalamud. This exact line was the unload error:
        // several game clients share one XIVLauncher config database, another box held the SQLite
        // write lock, the exception escaped Dispose, and Dalamud reported the plugin as having
        // failed to unload - which is far worse than losing one config write.
        ConfigSaver.Save(_config);

        ECommonsMain.Dispose();
    }
}
