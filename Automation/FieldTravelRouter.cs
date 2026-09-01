using System;
using System.Numerics;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Vendor.BOCCHI;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>Current high-level field travel mode, surfaced for diagnostics.</summary>
public enum FieldTravelMode : byte
{
    Direct = 0,
    WalkToAetheryte = 1,
    Teleporting = 2,
    WalkFromAetheryte = 3,
    FallbackDirect = 4,
    Returning = 5,
}

/// <summary>
/// One instruction from the high-level route planner to Movement. Walking remains owned by the
/// existing Movement class so its stall recovery and hostile detours are preserved; this planner
/// decides whether a walk is split by an aethernet hop or by Return + optional aethernet.
/// </summary>
public readonly record struct FieldTravelDirective(
    Vector3 Destination,
    float Range,
    bool HoldMovement,
    FieldTravelMode Mode,
    string Detail);

/// <summary>
/// BOCCHI-style Direct / Walk-Teleport-Walk / Return-Teleport-Walk planner for Bozja and Zadnor.
///
/// The candidate costs deliberately use BOCCHI's yalm-equivalent constants. The BBR adapter still
/// uses horizontal-distance estimates for walk legs instead of importing BOCCHI's entire graph
/// service; Movement executes every walk leg and therefore retains vnavmesh snapping, stall
/// recovery and IV/V/★ avoidance.
/// </summary>
public sealed class FieldTravelRouter(LifestreamIpc lifestream, Configuration config)
{
    private readonly LifestreamIpc _lifestream = lifestream;
    private readonly Configuration _config = config;

    private Vector3 _goal;
    private float _goalRange;
    private FieldAethernet.Node? _departure;
    private FieldAethernet.Node? _inbound;
    private FieldTravelMode _mode = FieldTravelMode.Direct;
    private long _teleportStartedMs;
    private long _lastTeleportAttemptMs;
    private int _teleportAttempts;
    private long _returnStartedMs;
    private bool _returnConfirmationSent;
    private bool _fallbackForGoal;

    private const float GoalIdentityRadius = 10f;
    private const float AethernetReadyRadius = 15f;
    private const float AethernetWalkArrivalRadius = 7f;
    private const float TeleportArrivalRadius = 55f;
    private const long TeleportRetryDelayMs = 1_200;
    private const long TeleportTimeoutMs = 20_000;
    private const long ReturnTimeoutMs = 25_000;

    public FieldTravelMode Mode => _mode;
    public bool LifestreamAvailable => _lifestream.Available;
    public bool OnFinalLeg => _mode is FieldTravelMode.Direct or FieldTravelMode.WalkFromAetheryte or FieldTravelMode.FallbackDirect;
    public string RouteDescription { get; private set; } = "直接移動";

    public bool IsRoutingTo(Vector3 destination) =>
        _goal != Vector3.Zero && Movement.HorizontalDistance(_goal, destination) <= GoalIdentityRadius;

    public void Reset()
    {
        _goal = Vector3.Zero;
        _goalRange = 0;
        _departure = null;
        _inbound = null;
        _mode = FieldTravelMode.Direct;
        _teleportStartedMs = 0;
        _lastTeleportAttemptMs = 0;
        _teleportAttempts = 0;
        _returnStartedMs = 0;
        _returnConfirmationSent = false;
        _fallbackForGoal = false;
        RouteDescription = "直接移動";
    }

    public FieldTravelDirective Resolve(Vector3 finalDestination, float finalRange)
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "プレイヤー情報待ち");

        if (!IsRoutingTo(finalDestination))
            Plan(me.Position, finalDestination, finalRange);

        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)
            return Direct(finalDestination, finalRange, FieldTravelMode.Direct, "直接移動");

        if (_fallbackForGoal || _departure is null || _inbound is null)
            return Direct(finalDestination, finalRange,
                _fallbackForGoal ? FieldTravelMode.FallbackDirect : FieldTravelMode.Direct,
                _fallbackForGoal ? "高速移動失敗のため直接移動" : "直接移動");

        var departure = _departure.Value;
        var inbound = _inbound.Value;

        switch (_mode)
        {
            case FieldTravelMode.Returning:
            {
                RouteDescription = inbound.IsBaseCamp
                    ? "デジョン → 目的地へ移動"
                    : $"デジョン → 簡易テレポ ({departure.PlaceNameId}→{inbound.PlaceNameId}) → 目的地";

                // Once Return has landed, continue either directly from base camp or with the
                // aethernet hop selected by the candidate calculator.
                if (Movement.HorizontalDistance(me.Position, departure.Position) <= TeleportArrivalRadius)
                {
                    _returnStartedMs = 0;
                    if (inbound.IsBaseCamp || inbound.CustomAetheryteId == departure.CustomAetheryteId)
                    {
                        _mode = FieldTravelMode.WalkFromAetheryte;
                        RouteDescription = "デジョン完了 → 目的地へ移動";
                        return new FieldTravelDirective(finalDestination, finalRange, false, _mode, RouteDescription);
                    }

                    _mode = FieldTravelMode.Teleporting;
                    _teleportStartedMs = Environment.TickCount64;
                    _lastTeleportAttemptMs = 0;
                    _teleportAttempts = 0;
                    goto case FieldTravelMode.Teleporting;
                }

                // Return cannot be cast while fighting. Do not stand waiting to die: abandon this
                // optimization and let the proven direct travel keep running/leashing the pull.
                if (Svc.Condition[ConditionFlag.InCombat])
                {
                    FallBack("Return route became unavailable because combat started");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "戦闘中のためデジョンを中止 → 直接移動");
                }

                var now = Environment.TickCount64;
                if (_returnStartedMs != 0)
                {
                    // BOCCHI treats Return as cast + owned SelectYesno confirmation. Confirm only
                    // while this router has a live Return pending flag; GeneralActions never clicks
                    // a generic dialog on its own.
                    if (!_returnConfirmationSent && GeneralActions.TryConfirmPendingReturn())
                    {
                        _returnConfirmationSent = true;
                        Svc.Log.Information("[BozjaBuddyReborn] Confirmed pending Return traversal dialog.");
                    }

                    if (now - _returnStartedMs > ReturnTimeoutMs)
                    {
                        FallBack("Return did not reach base camp before timeout");
                        return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "デジョンがタイムアウト → 直接移動");
                    }

                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
                }

                if (!GeneralActions.ReturnReady())
                {
                    FallBack("Return selected by planner but general action is not ready");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "デジョン使用不可 → 直接移動");
                }

                // Intentional dismount: Return is the selected route, unlike survival automation
                // which must never cause a travel dismount.
                if (!Mount.EnsureDismounted())
                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, "デジョンのため降車中");

                if (!GeneralActions.CastReturn())
                {
                    FallBack("Return general action was refused");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "デジョン失敗 → 直接移動");
                }

                _returnStartedMs = now;
                _returnConfirmationSent = false;
                Svc.Log.Information("[BozjaBuddyReborn] BOCCHI-style Return traversal started.");
                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
            }

            case FieldTravelMode.WalkToAetheryte:
            {
                var distance = Movement.HorizontalDistance(me.Position, departure.Position);
                if (distance > AethernetReadyRadius)
                {
                    RouteDescription = $"エーテライトへ移動 → 簡易テレポ ({departure.PlaceNameId}→{inbound.PlaceNameId})";
                    // Map data has no trusted Y here; seed the current floor and let Movement's
                    // reachable-point snap resolve the exact pad height.
                    var walk = new Vector3(departure.Position.X, me.Position.Y, departure.Position.Z);
                    return new FieldTravelDirective(
                        walk,
                        AethernetWalkArrivalRadius,
                        false,
                        FieldTravelMode.WalkToAetheryte,
                        RouteDescription);
                }

                _mode = FieldTravelMode.Teleporting;
                _teleportStartedMs = Environment.TickCount64;
                _lastTeleportAttemptMs = 0;
                _teleportAttempts = 0;
                goto case FieldTravelMode.Teleporting;
            }

            case FieldTravelMode.Teleporting:
            {
                RouteDescription = $"簡易テレポ中 ({departure.PlaceNameId}→{inbound.PlaceNameId})";

                if (_lifestream.ActiveCustomAetheryte == inbound.CustomAetheryteId
                    || Movement.HorizontalDistance(me.Position, inbound.Position) <= TeleportArrivalRadius)
                {
                    _mode = FieldTravelMode.WalkFromAetheryte;
                    RouteDescription = "簡易テレポ完了 → 目的地へ移動";
                    return new FieldTravelDirective(finalDestination, finalRange, false, _mode, RouteDescription);
                }

                var now = Environment.TickCount64;
                if (!_lifestream.Available)
                {
                    FallBack("Lifestream unavailable during aethernet route");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "Lifestream未接続 → 直接移動");
                }

                if (now - _teleportStartedMs > TeleportTimeoutMs)
                {
                    FallBack("Lifestream aethernet teleport timed out");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "簡易テレポがタイムアウト → 直接移動");
                }

                if (_lifestream.IsBusy)
                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);

                if (_lastTeleportAttemptMs != 0 && now - _lastTeleportAttemptMs < TeleportRetryDelayMs)
                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);

                if (_teleportAttempts >= 2)
                {
                    FallBack("Lifestream rejected aethernet teleport twice");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "簡易テレポ失敗 → 直接移動");
                }

                if (!Mount.EnsureDismounted())
                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, "簡易テレポのため降車中");

                _lastTeleportAttemptMs = now;
                _teleportAttempts++;
                var accepted = _lifestream.AethernetTeleportByPlaceNameId(inbound.PlaceNameId);
                if (!accepted)
                {
                    Svc.Log.Warning(
                        $"[BozjaBuddyReborn] Lifestream rejected aethernet teleport to PlaceName {inbound.PlaceNameId} " +
                        $"(attempt {_teleportAttempts}/2).");
                }
                else
                {
                    Svc.Log.Information(
                        $"[BozjaBuddyReborn] Lifestream accepted aethernet teleport to PlaceName {inbound.PlaceNameId}.");
                }

                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
            }

            case FieldTravelMode.WalkFromAetheryte:
                return new FieldTravelDirective(finalDestination, finalRange, false, _mode, RouteDescription);

            default:
                return Direct(finalDestination, finalRange, _mode, RouteDescription);
        }
    }

    private void Plan(Vector3 start, Vector3 finalDestination, float finalRange)
    {
        _goal = finalDestination;
        _goalRange = finalRange;
        _departure = null;
        _inbound = null;
        _fallbackForGoal = false;
        _teleportStartedMs = 0;
        _lastTeleportAttemptMs = 0;
        _teleportAttempts = 0;
        _returnStartedMs = 0;

        var territory = Svc.ClientState.TerritoryType;
        var nodes = FieldAethernet.ForTerritory(territory);
        var direct = Movement.HorizontalDistance(start, finalDestination);

        var maxDirect = _config.NavigationMaxDirectWalkDistance > 0
            ? _config.NavigationMaxDirectWalkDistance
            : NavigationConstants.MaxDirectWalkDistance;
        var hopCost = _config.NavigationAethernetHopCost > 0
            ? _config.NavigationAethernetHopCost
            : NavigationConstants.AethernetHopCost;
        var returnCost = _config.NavigationReturnCost > 0
            ? _config.NavigationReturnCost
            : NavigationConstants.ReturnCost;

        if (!_config.UseBocchiNavigation || nodes.Count == 0 || direct <= maxDirect)
        {
            _mode = FieldTravelMode.Direct;
            RouteDescription = "直接移動";
            return;
        }

        var best = direct;
        var bestMode = FieldTravelMode.Direct;
        FieldAethernet.Node? bestDeparture = null;
        FieldAethernet.Node? bestInbound = null;

        // Walk -> aethernet -> walk candidate. This is offered only when Lifestream is currently
        // answering; optional-dependency failure must never strand the route planner.
        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2)
        {
            foreach (var departure in nodes)
            {
                var walkToDeparture = Movement.HorizontalDistance(start, departure.Position);
                foreach (var inbound in nodes)
                {
                    if (departure.CustomAetheryteId == inbound.CustomAetheryteId)
                        continue;

                    var walkFromInbound = Movement.HorizontalDistance(inbound.Position, finalDestination);
                    var cost = walkToDeparture + hopCost + walkFromInbound;
                    if (cost >= best)
                        continue;

                    best = cost;
                    bestMode = FieldTravelMode.WalkToAetheryte;
                    bestDeparture = departure;
                    bestInbound = inbound;
                }
            }
        }

        // Return -> base camp -> optional aethernet -> walk, matching BOCCHI's candidate shape.
        // Do not offer it while already in camp, in combat, or while Return is on cooldown.
        var baseCamp = FieldAethernet.BaseCamp(territory);
        if (_config.UseReturnRouting
            && baseCamp is { } camp
            && Movement.HorizontalDistance(start, camp.Position) > NavigationConstants.CampRadius
            && !Svc.Condition[ConditionFlag.InCombat]
            && GeneralActions.ReturnReady())
        {
            var baseWalk = Movement.HorizontalDistance(camp.Position, finalDestination);
            var cost = returnCost + baseWalk;
            if (cost < best)
            {
                best = cost;
                bestMode = FieldTravelMode.Returning;
                bestDeparture = camp;
                bestInbound = camp;
            }

            if (_config.UseAethernetTravel && _lifestream.Available)
            {
                foreach (var inbound in nodes)
                {
                    if (inbound.IsBaseCamp)
                        continue;
                    var candidate = returnCost + hopCost
                                    + Movement.HorizontalDistance(inbound.Position, finalDestination);
                    if (candidate >= best)
                        continue;
                    best = candidate;
                    bestMode = FieldTravelMode.Returning;
                    bestDeparture = camp;
                    bestInbound = inbound;
                }
            }
        }

        if (bestDeparture is null || bestInbound is null || bestMode == FieldTravelMode.Direct)
        {
            _mode = FieldTravelMode.Direct;
            RouteDescription = "直接移動";
            return;
        }

        _departure = bestDeparture;
        _inbound = bestInbound;
        _mode = bestMode;
        RouteDescription = bestMode == FieldTravelMode.Returning
            ? bestInbound.Value.IsBaseCamp
                ? "デジョン経路"
                : $"デジョン → 簡易テレポ経路 ({bestInbound.Value.PlaceNameId})"
            : $"簡易テレポ経路 ({bestDeparture.Value.PlaceNameId}→{bestInbound.Value.PlaceNameId})";

        Svc.Log.Information(
            $"[BozjaBuddyReborn] BOCCHI-style route selected: mode={bestMode}, direct={direct:F0}y, " +
            $"planned={best:F0}y, departure={bestDeparture.Value.PlaceNameId}, inbound={bestInbound.Value.PlaceNameId}.");
    }

    private void FallBack(string reason)
    {
        _fallbackForGoal = true;
        _mode = FieldTravelMode.FallbackDirect;
        Svc.Log.Warning($"[BozjaBuddyReborn] {reason}; falling back to direct vnavmesh travel.");
    }

    private static FieldTravelDirective Direct(Vector3 destination, float range, FieldTravelMode mode, string detail)
        => new(destination, range, false, mode, detail);
}
