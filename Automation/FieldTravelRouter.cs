using System;
using System.Numerics;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Vendor.BOCCHI;
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
}

/// <summary>
/// One instruction from the high-level route planner to Movement.  Walking remains
/// owned by the existing Movement class so its stall recovery and hostile detours are
/// preserved; this planner only decides when a walk should be split by an aethernet hop.
/// </summary>
public readonly record struct FieldTravelDirective(
    Vector3 Destination,
    float Range,
    bool HoldMovement,
    FieldTravelMode Mode,
    string Detail);

/// <summary>
/// BOCCHI-style Walk -> Aethernet -> Walk planner for Bozja and Zadnor.
///
/// The route-cost model deliberately uses BOCCHI's yalm-equivalent constants.  v1.1's
/// first implementation uses horizontal distance for the two walk legs rather than
/// importing BOCCHI's entire graph/pathfinder service; the state machine and cost
/// semantics are otherwise the same shape.  Existing BBR Movement still executes each
/// walk leg, retaining its vnavmesh snapping, stall recovery and enemy avoidance.
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
    private bool _fallbackForGoal;

    private const float GoalIdentityRadius = 10f;
    private const float AethernetReadyRadius = 15f;
    private const float AethernetWalkArrivalRadius = 7f;
    private const float TeleportArrivalRadius = 55f;
    private const long TeleportRetryDelayMs = 1_200;
    private const long TeleportTimeoutMs = 20_000;

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

        if (!_config.UseBocchiNavigation || !_config.UseAethernetTravel || !FieldState.InFieldZone)
            return Direct(finalDestination, finalRange, FieldTravelMode.Direct, "直接移動");

        if (_fallbackForGoal || _departure is null || _inbound is null)
            return Direct(finalDestination, finalRange,
                _fallbackForGoal ? FieldTravelMode.FallbackDirect : FieldTravelMode.Direct,
                _fallbackForGoal ? "簡易テレポ失敗のため直接移動" : "直接移動");

        var departure = _departure.Value;
        var inbound = _inbound.Value;

        switch (_mode)
        {
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

                // Arrival is established by either Lifestream's active custom node or proximity.
                // The latter is important because the active-node gate can briefly read zero while
                // the destination area's UI is settling.
                if (_lifestream.ActiveCustomAetheryte == inbound.CustomAetheryteId
                    || Movement.HorizontalDistance(me.Position, inbound.Position) <= TeleportArrivalRadius)
                {
                    _mode = FieldTravelMode.WalkFromAetheryte;
                    RouteDescription = "簡易テレポ完了 → 目的地へ移動";
                    return new FieldTravelDirective(
                        finalDestination, finalRange, false, _mode, RouteDescription);
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

                // One initial call plus one retry, then fail open to the proven legacy walk.
                if (_teleportAttempts >= 2)
                {
                    FallBack("Lifestream rejected aethernet teleport twice");
                    return Direct(finalDestination, finalRange, FieldTravelMode.FallbackDirect, "簡易テレポ失敗 → 直接移動");
                }

                // Teleport interaction must not race a mounted state.  Unlike survival actions,
                // this dismount is intentional: the next operation is the aethernet hop itself.
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
                RouteDescription = "簡易テレポ完了 → 目的地へ移動";
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

        var territory = Svc.ClientState.TerritoryType;
        var nodes = FieldAethernet.ForTerritory(territory);
        var direct = Movement.HorizontalDistance(start, finalDestination);

        var maxDirect = _config.NavigationMaxDirectWalkDistance > 0
            ? _config.NavigationMaxDirectWalkDistance
            : NavigationConstants.MaxDirectWalkDistance;
        var hopCost = _config.NavigationAethernetHopCost > 0
            ? _config.NavigationAethernetHopCost
            : NavigationConstants.AethernetHopCost;

        if (!_config.UseBocchiNavigation || !_config.UseAethernetTravel || !_lifestream.Available
            || nodes.Count < 2 || direct <= maxDirect)
        {
            _mode = FieldTravelMode.Direct;
            RouteDescription = "直接移動";
            return;
        }

        var best = direct;
        FieldAethernet.Node? bestDeparture = null;
        FieldAethernet.Node? bestInbound = null;

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
                bestDeparture = departure;
                bestInbound = inbound;
            }
        }

        if (bestDeparture is null || bestInbound is null)
        {
            _mode = FieldTravelMode.Direct;
            RouteDescription = "直接移動";
            return;
        }

        _departure = bestDeparture;
        _inbound = bestInbound;
        _mode = FieldTravelMode.WalkToAetheryte;
        RouteDescription = $"簡易テレポ経路 ({bestDeparture.Value.PlaceNameId}→{bestInbound.Value.PlaceNameId})";

        Svc.Log.Information(
            $"[BozjaBuddyReborn] BOCCHI-style route selected: direct={direct:F0}y, planned={best:F0}y, " +
            $"departure={bestDeparture.Value.PlaceNameId}, inbound={bestInbound.Value.PlaceNameId}.");
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
