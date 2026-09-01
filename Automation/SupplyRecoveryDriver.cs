using System;
using BozjaBuddyReborn.Game;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Gets a critically depleted character back to the Lost Finds Cache without pretending the
/// unresolved Cache <-> Holster transfer primitive is solved.
///
/// This class owns only the safe, already-proven effects: BOCCHI-style field travel, dismount at
/// the destination, and an ordinary world-object interaction. Once the cache is open it waits.
/// A future transfer executor can take over there without changing the arbitration that got us
/// out of the skirmish in the first place.
/// </summary>
public sealed class SupplyRecoveryDriver(Movement movement)
{
    private readonly Movement _movement = movement;

    private const float CacheInteractRange = 3.5f;
    private const float BaseCampArriveRange = 18f;
    private const long InteractGapMs = 1500;

    private long _lastInteractMs;
    private bool _cacheOpened;

    public bool Active { get; private set; }
    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Run one recovery tick. Returns true while this driver owns the controller's objective.
    /// The caller is responsible for disabling combat/approach before calling.
    /// </summary>
    public bool Tick()
    {
        Active = true;

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
        {
            Status = "プレイヤー状態を取得できないため補給地点へ移動できません。";
            return true;
        }

        // Once the interaction succeeds, do not hammer the object every controller tick. The
        // cache window is now the correct place to be; SupplyManager will release this state as
        // soon as inventory actually contains a recovery path again.
        if (_cacheOpened)
        {
            _movement.Stop();
            Status =
                "Lost Finds Cacheを開いて補給待機中です。Cache↔Holsterの安全な自動転送手段が確定するまで、この位置を維持します。";
            return true;
        }

        // Prefer the real streamed cache object whenever possible. That gives us its exact floor
        // and lets the same code work in both Bozja and Zadnor without hard-coded cache coords.
        if (Interactables.Nearest(Interactables.LostFindsCache, 250f) is { } cache)
        {
            var distance = Movement.HorizontalDistance(me.Position, cache.Position);
            if (distance > CacheInteractRange)
            {
                _movement.TravelTo(cache.Position, CacheInteractRange - 0.5f, waitForOptionalDependencies: true);
                Status = _movement.TravelMode == FieldTravelMode.WaitingForLifestream
                    ? "緊急補給のためLost Finds Cacheへ戻ります。Lifestreamの復帰を最大30秒待っています。"
                    : $"緊急補給のためLost Finds Cacheへ移動中です（残り {distance:F0}y / {_movement.RouteDescription}）。";
                return true;
            }

            _movement.Stop();

            // Interacting with field objects from a mount is unreliable and we are no longer
            // travelling, so the mounted-no-actions invariant does not require us to stay mounted.
            if (!Mount.EnsureDismounted())
            {
                Status = "Lost Finds Cacheを操作するためマウントから降りています。";
                return true;
            }

            var now = Environment.TickCount64;
            if (now - _lastInteractMs < InteractGapMs)
            {
                Status = "Lost Finds Cacheを開いています。";
                return true;
            }

            _lastInteractMs = now;
            if (Interactables.Interact(cache))
            {
                _cacheOpened = true;
                Status = "Lost Finds Cacheを開きました。補給処理を待っています。";
                Svc.Log.Information("[BozjaBuddyReborn] Critical supply recovery reached and opened Lost Finds Cache.");
            }
            else
            {
                Status = "Lost Finds Cacheの操作をゲーム側に拒否されました。再試行します。";
            }
            return true;
        }

        // The cache is not streamed yet. Route to the known base-camp aethernet position; once we
        // get close enough the real cache object appears and the branch above takes over. This is
        // deliberately a non-urgent optional-dependency route: the skirmish was already abandoned,
        // so waiting up to 30s for Lifestream can save a long cross-zone walk.
        var camp = FieldAethernet.BaseCamp(Svc.ClientState.TerritoryType);
        if (camp is not { } baseCamp)
        {
            _movement.Stop();
            Status = "このエリアの補給拠点位置を取得できないため、その場で補給待機します。";
            return true;
        }

        var campDistance = Movement.HorizontalDistance(me.Position, baseCamp.Position);
        if (campDistance <= BaseCampArriveRange)
        {
            _movement.Stop();
            Status = "補給拠点へ到着しました。Lost Finds Cacheが表示されるのを待っています。";
            return true;
        }

        _movement.TravelTo(baseCamp.Position, BaseCampArriveRange, waitForOptionalDependencies: true);
        Status = _movement.TravelMode == FieldTravelMode.WaitingForLifestream
            ? "緊急補給のため拠点へ戻ります。Lifestreamの復帰を最大30秒待っています。"
            : $"緊急補給のため拠点へ移動中です（残り {campDistance:F0}y / {_movement.RouteDescription}）。";
        return true;
    }

    public void Reset()
    {
        Active = false;
        Status = string.Empty;
        _lastInteractMs = 0;
        _cacheOpened = false;
    }
}
