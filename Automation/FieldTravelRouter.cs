using System;
using System.Collections.Generic;
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
    WaitingForLifestream = 6,
    Planning = 7,
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
/// The candidate costs deliberately use BOCCHI's yalm-equivalent constants. For the departure
/// walk, BBR now measures the actual vnavmesh ground path with the same Nav.Pathfind primitive
/// BOCCHI/Ocelot uses. Inbound-to-goal costs remain horizontal estimates because BBR does not vendor
/// the entire zone graph. Movement still executes every walk leg, preserving stall recovery and
/// IV/V/★ avoidance.
/// </summary>
public sealed class FieldTravelRouter(LifestreamIpc lifestream, Configuration config, NavmeshIpc navmesh)
{
    private readonly LifestreamIpc _lifestream = lifestream;
    private readonly Configuration _config = config;
    private readonly NavmeshIpc _navmesh = navmesh;
    private readonly NavPathCostCache _pathCosts = new(navmesh);

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
    private long _optionalLifestreamWaitStartedMs;
    private bool _fallbackForGoal;

    // Short-lived cost-probe state. The character is held still only for a new long-distance
    // route and only while this plugin's cancelable Nav.Pathfind query is alive.
    private Vector3 _planningStart;
    private Vector3 _planningDeparturePoint;
    private uint _planningTerritory;
    private long _planningStartedMs;
    private bool _planningCancelSent;
    private bool _planningDiscardResult;

    private const float GoalIdentityRadius = 10f;
    private const float AethernetReadyRadius = 15f;
    private const float AethernetWalkArrivalRadius = 7f;
    private const float TeleportArrivalRadius = 55f;
    private const long TeleportRetryDelayMs = 1_200;
    private const long TeleportTimeoutMs = 20_000;
    private const long ReturnTimeoutMs = 25_000;
    private const long OptionalLifestreamWaitMs = 30_000;
    private const long PathCostPlanningWaitMs = 750;

    public FieldTravelMode Mode => _mode;
    public bool LifestreamAvailable => _lifestream.Available;
    public bool OnFinalLeg => _mode is FieldTravelMode.Direct or FieldTravelMode.WalkFromAetheryte or FieldTravelMode.FallbackDirect;
    public string RouteDescription { get; private set; } = "直接移動";

    // Read-only diagnostic snapshot. These are world coordinates only; exposing them cannot
    // mutate route state, and keeps the overlay out of the planner's decision logic.
    public Vector3 DebugGoal => _goal;
    public Vector3? DebugDeparture => _departure?.Position;
    public Vector3? DebugInbound => _inbound?.Position;
    public uint DebugDeparturePlaceNameId => _departure?.PlaceNameId ?? 0;
    public uint DebugInboundPlaceNameId => _inbound?.PlaceNameId ?? 0;

    public bool IsRoutingTo(Vector3 destination) =>
        _goal != Vector3.Zero && Movement.HorizontalDistance(_goal, destination) <= GoalIdentityRadius;

    /// <summary>Estimate the cheapest currently usable BOCCHI-style route without mutating route state.</summary>
    public float EstimateCost(Vector3 start, Vector3 destination)
    {
        var direct = Movement.HorizontalDistance(start, destination);
        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)
            return direct;

        var nodes = FieldAethernet.ForTerritory(Svc.ClientState.TerritoryType);
        if (nodes.Count == 0)
            return direct;

        var maxDirect = _config.NavigationMaxDirectWalkDistance > 0
            ? _config.NavigationMaxDirectWalkDistance
            : NavigationConstants.MaxDirectWalkDistance;
        if (direct <= maxDirect)
            return direct;

        var hopCost = _config.NavigationAethernetHopCost > 0
            ? _config.NavigationAethernetHopCost
            : NavigationConstants.AethernetHopCost;
        var returnCost = _config.NavigationReturnCost > 0
            ? _config.NavigationReturnCost
            : NavigationConstants.ReturnCost;

        var best = new TraversalCandidate(direct);
        var resolvedDeparture = ResolveDepartureNode(nodes, start);
        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2
            && resolvedDeparture is { } departure)
        {
            var walkToDeparture = Movement.HorizontalDistance(start, departure.Position);
            foreach (var inbound in nodes)
            {
                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)
                    continue;

                // BOCCHI leaves field -> base-camp travel to ReturnTeleportWalk rather than
                // paying an aethernet hop back to the base shard.
                if (inbound.IsBaseCamp && !departure.IsBaseCamp)
                    continue;

                var candidate = new TraversalCandidate(
                    walkToDeparture
                    + hopCost
                    + Movement.HorizontalDistance(inbound.Position, destination));
                if (candidate.TotalCost < best.TotalCost)
                    best = candidate;
            }
        }

        var camp = FieldAethernet.BaseCamp(Svc.ClientState.TerritoryType);
        if (_config.UseReturnRouting
            && camp is { } baseCamp
            && Movement.HorizontalDistance(start, baseCamp.Position) > NavigationConstants.CampRadius
            && !Svc.Condition[ConditionFlag.InCombat]
            && GeneralActions.ReturnReady())
        {
            var directReturn = new TraversalCandidate(
                returnCost + Movement.HorizontalDistance(baseCamp.Position, destination));
            if (directReturn.TotalCost < best.TotalCost)
                best = directReturn;
            if (_config.UseAethernetTravel && _lifestream.Available)
            {
                foreach (var inbound in nodes)
                {
                    if (inbound.IsBaseCamp)
                        continue;
                    var candidate = new TraversalCandidate(returnCost + hopCost
                        + Movement.HorizontalDistance(inbound.Position, destination));
                    if (candidate.TotalCost < best.TotalCost)
                        best = candidate;
                }
            }
        }

        return best.TotalCost;
    }

    public void Reset()
    {
        _pathCosts.CancelAllPending();

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
        _optionalLifestreamWaitStartedMs = 0;
        _fallbackForGoal = false;
        _planningStart = Vector3.Zero;
        _planningDeparturePoint = Vector3.Zero;
        _planningTerritory = 0;
        _planningStartedMs = 0;
        _planningCancelSent = false;
        _planningDiscardResult = false;
        RouteDescription = "直接移動";
    }

    public FieldTravelDirective Resolve(
        Vector3 finalDestination,
        float finalRange,
        bool waitForOptionalLifestream = false)
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "プレイヤー情報待ち");

        if (!IsRoutingTo(finalDestination))
            Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream);

        if (!_config.UseBocchiNavigation || !FieldState.InFieldZone)
            return Direct(finalDestination, finalRange, FieldTravelMode.Direct, "直接移動");

        if (_mode == FieldTravelMode.Planning)
        {
            var now = Environment.TickCount64;

            if (_planningDiscardResult)
            {
                _pathCosts.CancelAllPending();
                if (_pathCosts.HasPending)
                {
                    RouteDescription = "旧実経路コスト計算の終了待ち";
                    return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
                }

                _planningDiscardResult = false;
                Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream, allowPathCostWait: true);
                return Resolve(finalDestination, finalRange, waitForOptionalLifestream);
            }

            var territory = _planningTerritory;
            if (territory == 0)
                territory = Svc.ClientState.TerritoryType;

            if (_pathCosts.TryGet(territory, _planningStart, _planningDeparturePoint, out var measured))
            {
                Svc.Log.Debug($"[BozjaBuddyReborn] vnavmesh measured departure walk at {measured:F1}y; finalizing BOCCHI route.");
                var planStart = _planningStart;
                Plan(planStart, finalDestination, finalRange, waitForOptionalLifestream, allowPathCostWait: false);
                return Resolve(finalDestination, finalRange, waitForOptionalLifestream);
            }

            if (now - _planningStartedMs < PathCostPlanningWaitMs)
            {
                RouteDescription = "vnavmeshで実経路コストを計算中";
                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
            }

            if (!_planningCancelSent)
            {
                _pathCosts.Cancel(territory, _planningStart, _planningDeparturePoint);
                _planningCancelSent = true;
                RouteDescription = "実経路コスト計算を打ち切り中";
                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
            }

            // Cancellation is cooperative inside vnavmesh. Never overlap the real movement path
            // with a telemetry Pathfind that has not actually stopped yet.
            if (_pathCosts.IsPending(territory, _planningStart, _planningDeparturePoint))
            {
                RouteDescription = "実経路コスト計算の終了待ち";
                return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
            }

            Svc.Log.Debug("[BozjaBuddyReborn] Departure path-cost probe exceeded 750ms; using horizontal fallback for this route.");
            var fallbackStart = _planningStart;
            Plan(fallbackStart, finalDestination, finalRange, waitForOptionalLifestream, allowPathCostWait: false);
            return Resolve(finalDestination, finalRange, waitForOptionalLifestream);
        }

        if (_mode == FieldTravelMode.WaitingForLifestream)
        {
            var now = Environment.TickCount64;
            RouteDescription = "Lifestream復帰待ち（最大30秒）";

            if (_lifestream.Available)
            {
                Svc.Log.Information("[BozjaBuddyReborn] Optional Lifestream recovered during nonurgent wait; replanning route.");
                Plan(me.Position, finalDestination, finalRange, waitForOptionalLifestream: false);
                return Resolve(finalDestination, finalRange, waitForOptionalLifestream: false);
            }

            if (_optionalLifestreamWaitStartedMs == 0)
                _optionalLifestreamWaitStartedMs = now;

            if (now - _optionalLifestreamWaitStartedMs >= OptionalLifestreamWaitMs)
            {
                FallBack("optional Lifestream did not recover within the 30-second nonurgent window");
                return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect,
                    "Lifestreamが30秒以内に復帰しないため直接移動");
            }

            return new FieldTravelDirective(Vector3.Zero, 0, true, _mode, RouteDescription);
        }

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

    private void Plan(
        Vector3 start,
        Vector3 finalDestination,
        float finalRange,
        bool waitForOptionalLifestream = false,
        bool allowPathCostWait = true)
    {
        if (_pathCosts.HasPending)
        {
            _pathCosts.CancelAllPending();
            _goal = finalDestination;
            _goalRange = finalRange;
            _departure = null;
            _inbound = null;
            _mode = FieldTravelMode.Planning;
            _planningStart = Vector3.Zero;
            _planningDeparturePoint = Vector3.Zero;
            _planningTerritory = 0;
            _planningStartedMs = Environment.TickCount64;
            _planningCancelSent = true;
            _planningDiscardResult = true;
            RouteDescription = "旧実経路コスト計算の終了待ち";
            return;
        }

        _goal = finalDestination;
        _goalRange = finalRange;
        _departure = null;
        _inbound = null;
        _fallbackForGoal = false;
        _teleportStartedMs = 0;
        _lastTeleportAttemptMs = 0;
        _teleportAttempts = 0;
        _planningStart = Vector3.Zero;
        _planningDeparturePoint = Vector3.Zero;
        _planningTerritory = 0;
        _planningStartedMs = 0;
        _planningCancelSent = false;
        _planningDiscardResult = false;
        _returnStartedMs = 0;
        _returnConfirmationSent = false;
        _optionalLifestreamWaitStartedMs = 0;

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

        var best = new TraversalCandidate(direct);
        var bestMode = FieldTravelMode.Direct;
        FieldAethernet.Node? bestDeparture = null;
        FieldAethernet.Node? bestInbound = null;

        var resolvedDeparture = ResolveDepartureNode(nodes, start);
        if (allowPathCostWait
            && _config.UseAethernetTravel
            && _lifestream.Available
            && nodes.Count >= 2
            && resolvedDeparture is { } probeDeparture)
        {
            var probePoint = ResolveNodePathPoint(probeDeparture, start.Y);
            if (!_pathCosts.TryGet(territory, start, probePoint, out _))
            {
                var fallback = Movement.HorizontalDistance(start, probeDeparture.Position);
                _pathCosts.Estimate(territory, start, probePoint, fallback, request: true);
                if (_pathCosts.IsPending(territory, start, probePoint))
                {
                    _mode = FieldTravelMode.Planning;
                    _departure = probeDeparture;
                    _planningStart = start;
                    _planningDeparturePoint = probePoint;
                    _planningTerritory = territory;
                    _planningStartedMs = Environment.TickCount64;
                    _planningCancelSent = false;
                    _planningDiscardResult = false;
                    RouteDescription = "vnavmeshで実経路コストを計算中";
                    return;
                }
            }
        }

        // Walk -> aethernet -> walk candidate. This is offered only when Lifestream is currently
        // answering; optional-dependency failure must never strand the route planner.
        if (_config.UseAethernetTravel && _lifestream.Available && nodes.Count >= 2
            && resolvedDeparture is { } departure)
        {
            var departurePoint = ResolveNodePathPoint(departure, start.Y);
            var departureFallback = Movement.HorizontalDistance(start, departure.Position);
            var walkToDeparture = _pathCosts.Estimate(
                territory, start, departurePoint, departureFallback, request: false);
            foreach (var inbound in nodes)
            {
                if (departure.CustomAetheryteId == inbound.CustomAetheryteId)
                    continue;
                if (inbound.IsBaseCamp && !departure.IsBaseCamp)
                    continue;

                var walkFromInbound = Movement.HorizontalDistance(inbound.Position, finalDestination);
                var candidate = new TraversalCandidate(walkToDeparture + hopCost + walkFromInbound);
                if (candidate.TotalCost >= best.TotalCost)
                    continue;

                best = candidate;
                bestMode = FieldTravelMode.WalkToAetheryte;
                bestDeparture = departure;
                bestInbound = inbound;
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
            var returnCandidate = new TraversalCandidate(returnCost + baseWalk);
            if (returnCandidate.TotalCost < best.TotalCost)
            {
                best = returnCandidate;
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
                    var returnHopCandidate = new TraversalCandidate(returnCost + hopCost
                        + Movement.HorizontalDistance(inbound.Position, finalDestination));
                    if (returnHopCandidate.TotalCost >= best.TotalCost)
                        continue;
                    best = returnHopCandidate;
                    bestMode = FieldTravelMode.Returning;
                    bestDeparture = camp;
                    bestInbound = inbound;
                }
            }
        }

        // In a nonurgent context (idle staging, cache errands and future supply runs), wait
        // briefly for optional Lifestream only when an aethernet route WOULD actually beat the
        // best route that is usable right now. Activity travel never passes this flag, so a CE or
        // skirmish cannot lose 30 seconds to an optional plugin outage.
        if (waitForOptionalLifestream
            && _config.UseAethernetTravel
            && !_lifestream.Available
            && nodes.Count >= 2)
        {
            var hypothetical = best;
            if (resolvedDeparture is { } waitDeparture)
            {
                var walkToDeparture = Movement.HorizontalDistance(start, waitDeparture.Position);
                foreach (var inbound in nodes)
                {
                    if (waitDeparture.CustomAetheryteId == inbound.CustomAetheryteId)
                        continue;
                    if (inbound.IsBaseCamp && !waitDeparture.IsBaseCamp)
                        continue;
                    var candidate = new TraversalCandidate(walkToDeparture
                        + hopCost
                        + Movement.HorizontalDistance(inbound.Position, finalDestination));
                    if (candidate.TotalCost < hypothetical.TotalCost)
                        hypothetical = candidate;
                }
            }

            if (_config.UseReturnRouting
                && baseCamp is { } waitCamp
                && Movement.HorizontalDistance(start, waitCamp.Position) > NavigationConstants.CampRadius
                && !Svc.Condition[ConditionFlag.InCombat]
                && GeneralActions.ReturnReady())
            {
                foreach (var inbound in nodes)
                {
                    if (inbound.IsBaseCamp)
                        continue;
                    var candidate = new TraversalCandidate(returnCost + hopCost
                        + Movement.HorizontalDistance(inbound.Position, finalDestination));
                    if (candidate.TotalCost < hypothetical.TotalCost)
                        hypothetical = candidate;
                }
            }

            if (hypothetical.TotalCost < best.TotalCost)
            {
                _mode = FieldTravelMode.WaitingForLifestream;
                _optionalLifestreamWaitStartedMs = Environment.TickCount64;
                RouteDescription = "Lifestream復帰待ち（最大30秒）";
                Svc.Log.Information(
                    $"[BozjaBuddyReborn] Nonurgent route can benefit from Lifestream; waiting up to 30 seconds " +
                    $"before direct fallback (current={best.TotalCost:F0}y, hypothetical={hypothetical.TotalCost:F0}y).");
                return;
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
            $"planned={best.TotalCost:F0}y, departure={bestDeparture.Value.PlaceNameId}, inbound={bestInbound.Value.PlaceNameId}.");
    }

    private Vector3 ResolveNodePathPoint(FieldAethernet.Node node, float fallbackY)
    {
        var resolved = _navmesh.ResolveGroundPoint(node.Position.X, node.Position.Z);
        if (!float.IsFinite(resolved.X) || !float.IsFinite(resolved.Y) || !float.IsFinite(resolved.Z)
            || resolved.Y > 900f)
            return new Vector3(node.Position.X, fallbackY, node.Position.Z);
        return resolved;
    }

    private static FieldAethernet.Node? ResolveDepartureNode(
        IReadOnlyList<FieldAethernet.Node> nodes, Vector3 start)
    {
        FieldAethernet.Node? baseCamp = null;
        FieldAethernet.Node? nearest = null;
        FieldAethernet.Node? snapped = null;
        var nearestDistance = float.MaxValue;
        var snappedDistance = float.MaxValue;

        foreach (var node in nodes)
        {
            var distance = Movement.HorizontalDistance(start, node.Position);
            if (node.IsBaseCamp)
                baseCamp = node;

            if (distance < nearestDistance)
            {
                nearest = node;
                nearestDistance = distance;
            }

            if (distance <= NavigationConstants.GraphSnapRadius && distance < snappedDistance)
            {
                snapped = node;
                snappedDistance = distance;
            }
        }

        if (baseCamp is { } camp
            && Movement.HorizontalDistance(start, camp.Position) <= NavigationConstants.CampRadius)
            return camp;

        return snapped ?? nearest;
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
