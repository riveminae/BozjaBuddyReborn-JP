using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using BozjaBuddyReborn.Relic;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn.Automation;

public enum ControllerState
{
    Idle,
    Blocked,
    Selecting,
    Travelling,
    Holding,
    Engaged,
}

/// <summary>
/// The orchestration loop.
///
/// Runs once per framework tick (throttled) and drives one decision at a time:
///   already in an engagement  -> fight it
///   an engagement is open     -> travel to it and walk in (which is what registers you)
///   nothing open              -> farm a skirmish FATE
///
/// TWO MOVEMENT SOURCES, ONE WINNER. BossMod's avoidance and vnavmesh both want to steer the
/// character. Whenever BossMod is actively dodging, this loop stops issuing vnavmesh paths and
/// yields - a dodge that gets overridden by a travel path is a death. Travel resumes as soon
/// as the mechanic resolves.
///
/// MULTIBOX. When enabled, the host decides the objective and broadcasts it; clients follow.
/// The selection rule is deterministic, so even with the pipe down every box independently
/// picks the same engagement rather than scattering.
/// </summary>
public sealed class BozjaController
{
    private readonly Configuration _config;
    private readonly CeCatalog _catalog;
    private readonly TargetSelector _selector;
    private readonly Movement _movement;
    private readonly CombatDirector _director;
    private readonly CombatApproach _approach;
    private readonly HolsterDriver _holster;
    private readonly SupplyManager _supplies;
    private readonly SupplyRecoveryDriver _supplyRecovery;
    private readonly MultiboxLink _link;
    private readonly NavmeshIpc _navmesh;
    private readonly RegionResolver _regions;
    private readonly ErrandRunner _errands;
    private readonly LoadoutDriver _loadouts;
    private readonly SignUpRunner _signUps;
    private readonly PartySupportDriver _partySupport;
    private readonly RelicFarmCoordinator _relicFarm;
    private readonly DeathRecoveryDriver _deathRecovery;
    private readonly DependencySupervisor _dependencies;
    private readonly SafeStopCoordinator _safeStop = new();

    private long _arrivedAtMs;
    private bool _reportedArrival;
    private bool _committed;

    /// <summary>
    /// True once we have been dragged genuinely clear of a committed objective and are heading
    /// back. While set, the widened <see cref="CommittedLeash"/> no longer counts as arrival, so
    /// the return trip does not terminate 30y short of where it is going.
    /// </summary>
    private bool _returning;
    private SharedObjective _lastObjective = SharedObjective.None;

    /// <summary>
    /// How far the character may be dragged from an objective it has already reached before
    /// travel takes over again.
    ///
    /// Once a fight starts, chasing a mob out of the ring must not read as "no longer arrived":
    /// that hands movement back to travel, which walks away from the mob being hit AND switches
    /// the rotation off on the way, so the two keep swapping and nothing ever dies. Past this
    /// distance we really have been pulled somewhere else and going back is right.
    /// </summary>
    private const float CommittedLeash = 30f;

    public BozjaController(
        Configuration config,
        CeCatalog catalog,
        TargetSelector selector,
        Movement movement,
        CombatDirector director,
        CombatApproach approach,
        HolsterDriver holster,
        SupplyManager supplies,
        MultiboxLink link,
        NavmeshIpc navmesh,
        RegionResolver regions,
        ErrandRunner errands,
        LoadoutDriver loadouts,
        SignUpRunner signUps,
        PartySupportDriver partySupport,
        DeathRecoveryDriver deathRecovery,
        DependencySupervisor dependencies)
    {
        _config = config;
        _catalog = catalog;
        _selector = selector;
        _movement = movement;
        _director = director;
        _approach = approach;
        _holster = holster;
        _supplies = supplies;
        _supplyRecovery = new SupplyRecoveryDriver(movement);
        _link = link;
        _navmesh = navmesh;
        _regions = regions;
        _errands = errands;
        _loadouts = loadouts;
        _signUps = signUps;
        _partySupport = partySupport;
        _deathRecovery = deathRecovery;
        _dependencies = dependencies;
        _relicFarm = new RelicFarmCoordinator(config, new RelicTracker());
    }

    /// <summary>The zone third the character is standing in right now.</summary>
    public FieldRegionId CurrentRegion { get; private set; } = FieldRegionId.Unknown;

    public bool Running { get; private set; }
    public ControllerState State { get; private set; } = ControllerState.Idle;
    public string Status { get; private set; } = "Stopped.";
    public SharedObjective CurrentObjective => _lastObjective;
    public string TravelRoute => _movement.RouteDescription;
    public FieldTravelMode TravelMode => _movement.TravelMode;
    public bool LifestreamAvailable => _movement.LifestreamAvailable;
    public int RouteBlacklistCount => _selector.RouteBlacklistedFateCount;

    /// <summary>Last framework-tick survival inventory evaluation; UI reads this cache only.</summary>
    public SupplyStatus SupplyStatus { get; private set; } = new(false, false, false, 0, 0, 0, 0, []);

    /// <summary>Live engagement snapshot from the last tick, for the UI.</summary>
    public IReadOnlyList<CeSnapshot> Engagements { get; private set; } = [];

    public void Start()
    {
        if (Svc.Objects.LocalPlayer == null)
        {
            Running = false;
            State = ControllerState.Blocked;
            Status = "ログイン後に開始してください。";
            return;
        }

        if (!FieldState.InFieldZone)
        {
            Running = false;
            State = ControllerState.Blocked;
            Status = "南方ボズヤ戦線またはザトゥノル高原の中で開始してください。";
            return;
        }

        Running = true;
        _reportedArrival = false;
        _committed = false;
        _returning = false;
        _arrivedAtMs = 0;
        _lastAttackerMs = 0;
        _director.Resync();
        _dependencies.Reset();
        _safeStop.Reset();
        _selector.ClearRouteBlacklist();
        _supplyRecovery.Reset();
        _deathRecovery.CancelAndRestore();
        _holster.Reset();
        ResetYieldState();

        // A run starts from nothing. The link's objective and release latch used to survive a
        // Stop/Start, so restarting a group sent every client back to the objective it had
        // before - and with the GO still latched, straight past the arrival barrier.
        _link.ResetObjective();

        Status = "開始処理中です。";

        if (_config.MultiboxEnabled && _config.MultiboxIsHost)
            _link.BroadcastRunState(true);
    }

    /// <summary>Drop everything the dodge-yield state machine has accumulated. See IsDodging.</summary>
    private void ResetYieldState()
    {
        _yieldSinceMs = 0;
        _yieldCapWarned = false;
        _yieldBlockedUntilMs = 0;
        _lastDodgeQueryMs = 0;
        _lastDodgeMs = 0;
        _dodgeTicks = 0;
        _dodgeAnsweredForTick = 0;
        _dodgeAnswer = false;
    }

    public void Stop(string reason = "停止しました。")
    {
        Running = false;
        State = ControllerState.Idle;
        Status = reason;

        _approach.Release();
        _movement.Stop();
        _director.ReleaseControl();
        _supplyRecovery.Reset();
        _deathRecovery.CancelAndRestore();

        // A sign-up outlived Stop, so a stopped box carried on driving the recruitment window.
        _signUps.Cancel("停止しました。");

        _lastObjective = SharedObjective.None;
        _reportedArrival = false;
        _committed = false;
        _returning = false;
        _arrivedAtMs = 0;
        _lastAttackerMs = 0;
        ResetYieldState();
        _link.ResetObjective();

        if (_config.MultiboxEnabled && _config.MultiboxIsHost)
            _link.BroadcastRunState(false);
    }

    public void Toggle()
    {
        if (Running)
            Stop();
        else
            Start();
    }

    public void Tick()
    {
        _tickSeq++;

        // PARTY SUPPORT RUNS ON A PARKED BOX TOO, and above everything else, for the same reason
        // the errands do: a box sitting in the staging area buffing the group is a completely
        // ordinary thing to want, and gating it on the orchestrator would mean starting a farm run
        // to get a Bravery. It takes no movement and issues no path, so unlike an errand it has
        // nothing to seize and nothing below needs to yield to it.
        _partySupport.Tick();

        // Sign-up is a UI errand rather than a movement one, so it runs alongside everything else
        // and needs no MOVEMENT guards - and like the others it must work on a parked box. It
        // does need the ordinary "is the client in a state where poking agents is legal" guards
        // though, and those live inside the runner now: it used to sit above every check in this
        // method, including the LocalPlayer null test, so it drove UI agents during loading
        // screens and from outside Bozja entirely.
        if (_signUps.Active)
        {
            _signUps.Tick();
            LastCommandResult = $"参加申請: {_signUps.Status}";
        }

        // OPERATOR ERRANDS RUN WHETHER OR NOT THE ORCHESTRATOR IS. That is the entire point of
        // the control panel: acting on a box you have not focused, and a box you want to send to
        // the cache or an aetheryte is usually one that is parked rather than farming. So this
        // sits above the Running guard, with only the guards an errand genuinely needs, and takes
        // movement for its duration - it is short and explicit, so it outranks the orchestrator's
        // own goal rather than negotiating with it.
        if (_errands.Active)
        {
            if (Svc.Objects.LocalPlayer == null ||
                Svc.Condition[ConditionFlag.BetweenAreas] ||
                Svc.Condition[ConditionFlag.BetweenAreas51] ||
                Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
                Svc.Condition[ConditionFlag.WatchingCutscene78])
                return;

            if (Svc.Condition[ConditionFlag.Unconscious] || Svc.Objects.LocalPlayer?.CurrentHp == 0)
            {
                _errands.Cancel("キャラクターが戦闘不能になったため移動指示を中止しました。");
                return;
            }

            State = ControllerState.Travelling;
            _errands.Tick();
            Status = _errands.Status;

            // Keep dodging live during the walk, but never the rotation: an errand is not a fight.
            _director.Travel(_config.UseBossModAvoidance);

            if (_errands.Active)
                return;
        }

        if (!Running)
            return;

        if (Svc.Objects.LocalPlayer == null)
        {
            Status = "ログイン状態を確認できません。";
            State = ControllerState.Blocked;
            return;
        }

        // Never drive during a loading screen or a cutscene.
        if (Svc.Condition[ConditionFlag.BetweenAreas] ||
            Svc.Condition[ConditionFlag.BetweenAreas51] ||
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Svc.Condition[ConditionFlag.WatchingCutscene78])
        {
            State = ControllerState.Blocked;
            Status = "エリア移動またはカットシーン終了を待っています。";
            return;
        }

        // Timed unattended death recovery. TextAdvance is enabled only for the corpse window and
        // restored after revival. CE deaths never cast Return while the CE remains live; a committed
        // skirmish gets a 30s raise window and travel/idle gets 10s.
        var dead = Svc.Condition[ConditionFlag.Unconscious] || Svc.Objects.LocalPlayer?.CurrentHp == 0;
        if (dead)
        {
            var currentWhileDead = CriticalEngagements.Current(_catalog);
            var inLiveCe = currentWhileDead is { } deadCe && deadCe.IsLive;
            var diedDuringSkirmish = _lastObjective.Kind == ObjectiveKind.Fate
                                     && (_committed || State is ControllerState.Engaged or ControllerState.Holding);
            var recovery = _deathRecovery.Tick(true, inLiveCe, diedDuringSkirmish);

            State = ControllerState.Blocked;
            Status = recovery.JapaneseStatus;
            _committed = false;
            _returning = false;
            _reportedArrival = false;
            _arrivedAtMs = 0;
            _approach.Release();
            _movement.Stop();
            _director.Travel(_config.UseBossModAvoidance);

            if (recovery.Fatal)
                Stop(recovery.JapaneseStatus);
            return;
        }

        // A live-again tick is what restores TextAdvance to the exact state it had before death.
        _deathRecovery.Tick(false, false, false);

        if (!FieldState.InFieldZone)
        {
            Stop("対応エリア外へ移動したため自動周回を停止しました。");
            return;
        }

        var dependency = _dependencies.Snapshot();
        if (!dependency.Ready)
        {
            State = ControllerState.Blocked;
            _movement.Stop();

            // Waiting for a required plugin must not turn into a free death. The survivability
            // driver remains allowed while unmounted; its own mounted invariant prevents a heal
            // from dismounting the character during travel.
            _holster.Tick(inCombat: Svc.Condition[ConditionFlag.InCombat]);

            if (dependency.Health == DependencyHealth.WaitingRequired)
            {
                Status = $"必須プラグインとの接続が失われました: {dependency.MissingText}。" +
                         $"復帰を待っています（残り{Math.Ceiling(dependency.Remaining.TotalSeconds):F0}秒）。";
                return;
            }

            var safeStop = _safeStop.Tick(Svc.Condition[ConditionFlag.InCombat]);
            Status = safeStop.JapaneseStatus + $" ({dependency.MissingText})";
            if (safeStop.StopNow)
                Stop(Status);
            return;
        }

        // A recovered dependency cancels any pending pre-stop Return state.
        _safeStop.Reset();

        if (!_navmesh.MeshReady)
        {
            State = ControllerState.Blocked;
            var progress = _navmesh.BuildProgress;
            Status = progress >= 0
                ? $"このエリアのnavmeshを構築中です（{progress * 100f:F0}%）。"
                : "このエリアのnavmeshを待っています。";
            return;
        }

        Engagements = CriticalEngagements.Read(_catalog);
        CurrentRegion = FieldRegions.Current();

        // The first Relic target is always manual. Once that explicit target becomes satisfied,
        // advance inside the current territory before CE/FATE selection. Existing sticky-objective
        // permission checks decide whether the current skirmish remains valid for the new target.
        var relicUpdate = _relicFarm.Tick(Svc.ClientState.TerritoryType);
        if (relicUpdate.Stop)
        {
            Stop(relicUpdate.JapaneseStatus);
            return;
        }
        if (relicUpdate.ChangedTarget)
        {
            Svc.Log.Information(
                $"[BozjaBuddyReborn] Resistance Relic farm target advanced from item {relicUpdate.PreviousItemId} to {relicUpdate.CurrentItemId}.");
        }

        // Critical Engagements are a remote UI workflow, not a travel objective. Register while
        // continuing the current skirmish; SignUpRunner will press Commence immediately if this
        // box wins the draw. This is intentionally before objective selection.
        TickAutomaticCeRegistration();

        // Cache supply on the framework tick for both arbitration and UI. This deliberately
        // happens before live-CE dispatch so the main window remains informative during a CE; the
        // CE branch below still returns before any refill decision can run.
        SupplyStatus = _supplies.Evaluate();

        // --- already registered and fighting -------------------------------
        var current = CriticalEngagements.Current(_catalog);
        if (current is { } ce && ce.IsLive)
        {
            _supplyRecovery.Reset();
            RunEngagement(ce);
            return;
        }

        // Q54C/Q63A/Q109C: complete loss of both Potion Kit protection and a usable
        // self-heal is the one supply state severe enough to abandon the current skirmish
        // immediately. Registration keeps running above this block; if the lottery selects this
        // box, SignUpRunner independently holds Commence until the same supply predicate clears.
        var supply = SupplyStatus;
        if (supply.InventoryAvailable && supply.CriticalNoRecovery)
        {
            if (!_supplies.CanAttemptCriticalRecovery(supply))
            {
                Stop("回復手段が完全に枯渇し、Lost Finds Cacheにも補充候補がないため停止しました。");
                DiagnosticsRecorder.Warning("回復手段が完全に枯渇し、Cache在庫もないため自動周回を停止しました。");
                return;
            }

            RunCriticalSupplyRecovery(supply);
            return;
        }

        // Routine low-watermark refill never abandons a skirmish that we have already
        // reached and committed to. Once that skirmish ends, or if we had only been travelling
        // toward a fresh one, supply wins before another objective is selected. CE registration
        // continues above this block and a selected CE is still free to Commence immediately
        // because SignUpRunner only holds Commence for CriticalNoRecovery.
        if (supply.InventoryAvailable
            && supply.NeedsRoutineRefill
            && _supplies.CanAttemptRoutineRefill(supply))
        {
            var finishingCurrentSkirmish = _lastObjective.Kind == ObjectiveKind.Fate
                                           && _committed
                                           && IsObjectiveStillWorthDoing(_lastObjective);
            if (!finishingCurrentSkirmish)
            {
                RunRoutineSupplyRecovery(supply);
                return;
            }
        }

        if (_supplyRecovery.Active && !supply.NeedsRoutineRefill)
        {
            Svc.Log.Information("[BozjaBuddyReborn] Survival supply recovered above low-water marks; returning to normal objective selection.");
            _supplyRecovery.Reset();
        }

        // --- decide what to do next ----------------------------------------
        var objective = ResolveObjective();
        if (!objective.IsSet)
        {
            State = ControllerState.Selecting;

            // The farm filter's own explanation beats a generic "nothing available" - being in
            // the wrong zone for the material you are chasing is the failure worth naming.
            var reason = _config.MultiboxEnabled && !_config.MultiboxIsHost
                ? "ホストが次の目的地を選択するのを待っています。"
                : _selector.FarmFilterNote
                  ?? "現在参加可能なCEまたはスカーミッシュがありません。";

            _director.Travel(_config.UseBossModAvoidance);
            RunIdle(reason);
            return;
        }

        // IDENTITY, NOT VALUE - see SharedObjective.SameTarget. SharedObjective is a record
        // struct, so the old inequality compared Position too, and a skirmish ring drifts as the
        // FATE progresses. Every drifted millimetre therefore read as a brand-new objective and
        // cleared the whole arrival state five times a second: _committed never survived to let
        // the leash work, _arrivedAtMs never accumulated so the host's barrier timeout never
        // elapsed, and _reportedArrival was re-sent on every tick. The position is still taken
        // live for travel (see LivePosition); it is simply not part of naming the objective.
        if (!objective.SameTarget(_lastObjective))
        {
            _reportedArrival = false;
            _committed = false;
            _returning = false;
            _arrivedAtMs = 0;
        }

        _lastObjective = objective;

        RunTravel(objective);
    }

    private void RunCriticalSupplyRecovery(SupplyStatus supply)
    {
        State = ControllerState.Travelling;

        // The skirmish is intentionally abandoned, not merely paused. After recovery the selector
        // performs a fresh ranking, so we do not run back across the zone to a stale nearly-finished
        // objective just because it was the one being fought when stock hit zero.
        _lastObjective = SharedObjective.None;
        _reportedArrival = false;
        _committed = false;
        _returning = false;
        _arrivedAtMs = 0;

        _approach.Release();
        _director.Travel(_config.UseBossModAvoidance);

        // On foot, any surviving defensive option may still save the trip. HolsterDriver has an
        // absolute mounted guard, so this can never dismiss a travel mount or start combat.
        _holster.TickTravelSurvival();

        _supplyRecovery.Tick();
        Status = _supplyRecovery.Status;

        if (_supplyRecovery.CacheOpened)
        {
            var cache = _supplies.InspectCacheAndLatch(supply);
            if (cache.InventoryAvailable && !cache.CanRecoverCritical)
            {
                Svc.Log.Warning("[BozjaBuddyReborn] Critical recovery stock is absent from Lost Finds Cache for this field instance; stopping instead of retrying base trips.");
                Stop("回復手段が完全に枯渇し、Lost Finds Cacheにも補充候補がないため停止しました。");
                DiagnosticsRecorder.Warning("回復手段が完全に枯渇し、Cache在庫もないため自動周回を停止しました。");
                return;
            }
        }

        if (supply.Reasons.Count > 0)
            Svc.Log.Debug($"[BozjaBuddyReborn] Critical supply recovery: {string.Join("; ", supply.Reasons)}.");
    }

    private void RunRoutineSupplyRecovery(SupplyStatus supply)
    {
        State = ControllerState.Travelling;

        // Routine recovery is entered only when no committed live skirmish remains, so it is safe
        // to forget any stale/travel-only objective and let selection start fresh after refill.
        _lastObjective = SharedObjective.None;
        _reportedArrival = false;
        _committed = false;
        _returning = false;
        _arrivedAtMs = 0;

        _approach.Release();
        _director.Travel(_config.UseBossModAvoidance);
        _holster.TickTravelSurvival();

        _supplyRecovery.Tick(critical: false);
        Status = _supplyRecovery.Status;

        if (_supplyRecovery.CacheOpened)
        {
            var cache = _supplies.InspectCacheAndLatch(supply);
            if (cache.InventoryAvailable && !cache.CanImproveRoutine)
            {
                Svc.Log.Warning("[BozjaBuddyReborn] Requested routine survival stock is absent from Lost Finds Cache for this field instance; suppressing repeated base trips.");
                _supplyRecovery.Reset();
                Status = "Lost Finds Cacheにも現在不足している補給候補がないため、このインスタンスでは再補給を繰り返さず周回を続けます。";
                DiagnosticsRecorder.Warning("Cache在庫がない補給候補をこのインスタンスでは再試行しません。");
                return;
            }
        }

        if (supply.Reasons.Count > 0)
            Svc.Log.Debug($"[BozjaBuddyReborn] Routine supply recovery: {string.Join("; ", supply.Reasons)}.");
    }

    private void TickAutomaticCeRegistration()
    {
        if (!_config.DoCriticalEngagements || _signUps.Active)
            return;

        // Once registered, the existing SignUpRunner owns the lottery/Commence state. Starting
        // a second attempt here would reopen the window and risk withdrawing the first one.
        if (CriticalEngagements.RegisteredEventId is { } registered && registered != 0)
            return;

        var selected = _selector.SelectRegistration(Engagements, deterministic: _config.MultiboxEnabled);
        if (selected is not { } ce)
            return;

        _signUps.Begin(ce.EventId);
        Svc.Log.Information(
            $"[BozjaBuddyReborn] Auto-registering remotely for CE #{ce.EventId} \"{ce.Name}\"; no travel to CE marker required.");
    }

    // ------------------------------------------------------------- engagement

    private void RunEngagement(CeSnapshot ce)
    {
        State = ControllerState.Engaged;

        // Being inside an engagement is the most reliable possible sighting of its region.
        LearnRegionHere(new SharedObjective(
            ObjectiveKind.CriticalEngagement, ce.EventId, ce.Position, Svc.ClientState.TerritoryType));

        var region = FieldRegions.Label(Svc.ClientState.TerritoryType, CurrentRegion);
        Status = $"「{ce.Name}」で戦闘中（{region}）- {Loc.CeState(ce.State)} / 進行度 {ce.Progress}% " +
                 $"（{ce.Participants}/{ce.MaxParticipants}人）。";

        // You cannot attack from a mount, so the rotation must not be armed until we are
        // grounded - otherwise RSR has nothing it can press and looks like it is doing nothing.
        if (!Mount.EnsureDismounted())
        {
            Status = $"「{ce.Name}」で戦闘するためマウントから降りています。";
            _approach.Release();
            _director.Travel(_config.UseBossModAvoidance);
            return;
        }

        _director.Engage(_config.UseBossModAvoidance);

        // Travel is finished with; from here the only path we issue is the approach.
        _movement.Stop();

        // Walk into range of whatever we are fighting. BossMod Reborn dodges but will not close
        // the gap in the avoidance-only configuration we run it in, so a melee job would
        // otherwise stand wherever travel left it - see CombatApproach. (The original fork closes
        // by itself, in which case the approach stands down and BossMod owns combat movement.)
        if (_approach.Tick(IsDodging(), _director.AvoidanceOwnsApproach) && _approach.ClosingOn is { } closing)
        {
            Status = $"「{ce.Name}」で戦闘中（{region}）- {closing}へ接近中 " +
                     $"（射程まであと{_approach.ShortfallYalms:F0}y）。";
        }

        _holster.Tick(Svc.Condition[ConditionFlag.InCombat]);
    }

    // ------------------------------------------------------------------- idle

    /// <summary>
    /// Nothing to do: go stand at the staging point for the zone we are working, rather than
    /// idling wherever the last objective happened to end.
    ///
    /// Staging inside the working region matters because the next thing to spawn will be there,
    /// and Zadnor's plateaus are far enough apart that starting the run from the wrong one costs
    /// most of the registration window.
    /// </summary>
    private void RunIdle(string reason)
    {
        _approach.Release();

        if (!_config.UseIdleSpot)
        {
            Status = $"{reason} その場で待機します。";
            _movement.Stop();
            return;
        }

        var territory = Svc.ClientState.TerritoryType;

        // Stage in the region we are actually working: the restriction if one is set, else
        // wherever we already are.
        //
        // THE RESTRICTION'S ZONE ORDINAL ONLY MEANS SOMETHING IN ITS OWN TERRITORY. Restriction
        // hands back a bare Z1/Z2/Z3, and resolving a Zadnor material's Z3 against the Bozjan
        // Southern Front produces a perfectly real Z3 that is a different place entirely - so the
        // plugin printed "Zadnor is where that material drops, you are in the Bozjan Southern
        // Front" and then walked the character to a Bozjan staging point in the same breath.
        var (restricted, _) = _selector.Restriction;
        var restrictedTerritory = _selector.RestrictedTerritory;

        if (restrictedTerritory != 0 && restrictedTerritory != territory)
            restricted = FieldRegionId.Unknown;

        var region = restricted != FieldRegionId.Unknown ? restricted : CurrentRegion;

        // v1.1 stages at the field aethernet whenever possible. A Relic restriction picks
        // a node classified inside that exact region; ordinary idle uses the nearest node. This
        // lets the same BOCCHI router exploit the aethernet instantly when the next activity pops.
        Vector3 spot;
        string label;
        if (TryGetAethernetIdleSpot(territory, region, out spot, out var aethernetLabel))
        {
            label = aethernetLabel;
        }
        else if (region != FieldRegionId.Unknown && TryGetIdleSpot(territory, region, out spot))
        {
            label = FieldRegions.Label(territory, region);
        }
        else
        {
            Status = $"{reason} その場で待機します。";
            _movement.Stop();
            return;
        }

        if (_movement.HasArrived(spot, _config.IdleArriveRange))
        {
            Status = $"{reason} {label}で待機しています。";
            _movement.Stop();
            return;
        }

        // Dodging still wins over repositioning. And the path has to actually STOP, not merely
        // not be re-issued: BossMod (either fork) refuses to steer while vnavmesh's
        // "vnav.PathIsRunning" flag is set, so a path left running underneath the dodge is what
        // keeps the character walking through the mechanic.
        if (IsDodging())
        {
            Status = $"{reason} BossModへ移動制御を渡してギミックを回避しています。";
            _movement.Suspend();
            return;
        }

        _movement.TravelTo(spot, _config.IdleArriveRange, waitForOptionalDependencies: true);
        if (_movement.TravelMode == FieldTravelMode.WaitingForLifestream)
        {
            Status = $"{reason} Lifestreamの復帰を最大30秒待っています。";
            return;
        }

        Status = $"{reason} 待機地点 {label} へ移動中 " +
                 $"（残り {Movement.DistanceToPlayer(spot):F0}y）。";
    }

    /// <summary>Choose an aethernet node for idle staging, resolving its actual navmesh floor.</summary>
    private bool TryGetAethernetIdleSpot(
        uint territory,
        FieldRegionId preferredRegion,
        out Vector3 spot,
        out string label)
    {
        spot = Vector3.Zero;
        label = string.Empty;
        var nodes = FieldAethernet.ForTerritory(territory);
        if (nodes.Count == 0)
            return false;

        FieldAethernet.Node? best = null;
        var bestDistance = float.MaxValue;
        foreach (var node in nodes)
        {
            // For a Relic/region restriction, never choose a node confidently belonging to a
            // different region. The base camp is allowed only if its own position classifies into
            // the requested region. Unknown classification is not trusted for a restricted farm.
            if (preferredRegion != FieldRegionId.Unknown
                && FieldRegions.ClassifyByPosition(territory, node.Position) != preferredRegion)
                continue;

            var distance = Movement.DistanceToPlayer(node.Position);
            if (best == null || distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }

        if (best is not { } selected)
            return false;

        var grounded = _navmesh.ResolveGroundPoint(selected.Position.X, selected.Position.Z);
        if (grounded == Vector3.Zero)
            grounded = selected.Position;
        spot = grounded;

        try
        {
            var place = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>()?
                .GetRowOrDefault(selected.PlaceNameId)?.Name.ExtractText();
            label = string.IsNullOrWhiteSpace(place) ? "エーテライト" : place;
        }
        catch
        {
            label = "エーテライト";
        }

        return true;
    }

    /// <summary>Resolve a configured staging point to a ground position, cached per key.</summary>
    private bool TryGetIdleSpot(uint territory, FieldRegionId region, out Vector3 spot)
    {
        spot = Vector3.Zero;

        var key = $"{territory}:{(byte)region}";
        if (_idleSpotCache.TryGetValue(key, out var cached))
        {
            spot = cached;
            return cached != Vector3.Zero;
        }

        if (!_config.IdleSpots.TryGetValue(key, out var map) || map.Length < 2)
        {
            _idleSpotCache[key] = Vector3.Zero;
            return false;
        }

        var world = MapCoords.ToWorldXZ(territory, map[0], map[1]);
        if (world is not { } w)
            return false; // map sheet not ready - retry next tick rather than caching a miss

        // Map coordinates carry no altitude, so drop the point onto the navmesh from above.
        spot = _navmesh.ResolveGroundPoint(w.X, w.Z);
        _idleSpotCache[key] = spot;
        return true;
    }

    private readonly Dictionary<string, Vector3> _idleSpotCache = [];

    /// <summary>Drop resolved staging points, e.g. on a zone change.</summary>
    public void InvalidateIdleSpots() => _idleSpotCache.Clear();

    /// <summary>
    /// Is BossMod steering the character out of a mechanic right now?
    ///
    /// Held for a short tail after the signal drops. BossMod Reborn computes AI.IsNavigating as
    /// "the AI currently has a navigation target", recomputed every execute and going null the
    /// instant the character's own cell reads safe - so around the edge of a telegraph it flaps
    /// at high frequency. Without the tail, each flap tore the travel path down and re-issued it,
    /// which is both a burst of pathfinds and a character that visibly stutters instead of
    /// dodging cleanly. The original fork has no such gate, so this is permanently false there.
    /// </summary>
    private bool IsDodging()
    {
        // MEMOISED PER TICK, because this is no longer a pure read: it counts consecutive
        // steering ticks to decide when to yield, and several branches consult it more than once
        // in the same pass (the travel gate, then the approach inside Commit). Counting the same
        // tick twice would halve the entry threshold on exactly those paths.
        if (_dodgeAnsweredForTick == _tickSeq)
            return _dodgeAnswer;

        _dodgeAnsweredForTick = _tickSeq;
        _dodgeAnswer = EvaluateDodging();
        return _dodgeAnswer;
    }

    private bool EvaluateDodging()
    {
        var now = Environment.TickCount64;

        // THE YIELD CLOCK ONLY ADVANCES WHILE SOMEBODY IS ASKING IT TO. This is called from the
        // travel and idle branches only, so an engagement - or an errand, or a death - leaves
        // _yieldSinceMs frozen at whatever it was when travel last ran. Minutes later the first
        // dodge of the next route would find the cap already exceeded and refuse to yield at all,
        // permanently, for a signal that had been true for 200ms. A gap in the questioning is the
        // end of the episode, not part of it.
        if (_lastDodgeQueryMs != 0 && now - _lastDodgeQueryMs > YieldEpisodeGapMs)
        {
            _yieldSinceMs = 0;
            _yieldCapWarned = false;
            _yieldBlockedUntilMs = 0;
            _dodgeTicks = 0;
            _lastDodgeMs = 0;
        }

        _lastDodgeQueryMs = now;

        // Standing down after an overlong yield; see the cap below.
        if (_yieldBlockedUntilMs != 0)
        {
            if (now < _yieldBlockedUntilMs)
                return false;

            _yieldBlockedUntilMs = 0;
        }

        if (_director.AvoidanceIsSteering)
        {
            // HYSTERESIS ON ENTERING TOO, not only on leaving. This was a hair trigger: one true
            // tick cost a full path teardown, and resuming forced a fresh pathfind. With the
            // resume tail that is a ~600ms cycle against a repath interval of 750ms - the
            // character was being asked to restart a path more often than vnavmesh can deliver
            // one, so it never actually walked anywhere. And the signal genuinely does flap: this
            // method's own comment says so, and AvoidanceIsSteering is satisfied by any
            // telegraphed zone existing at all, which in an open field zone full of other players
            // is most of the time.
            //
            // Requiring the signal to hold for two consecutive ticks costs 200ms of reaction time
            // on a real mechanic - well inside any cast bar - and discards the single-tick spikes
            // that were doing all the damage.
            if (++_dodgeTicks < DodgeTicksToYield)
                return _lastDodgeMs != 0 && now - _lastDodgeMs < DodgeResumeDelayMs;

            if (_yieldSinceMs == 0)
                _yieldSinceMs = now;

            // A DODGE IS SHORT. Yielding is us handing our movement to another plugin on trust,
            // and there is no mechanic in this game that justifies giving it up for this long -
            // so past the cap we take the path back and keep travelling regardless. Without a cap
            // a stuck signal is indistinguishable from a permanent stall, which is exactly the
            // failure this guard was added for: the runner sat reporting "dodging a mechanic"
            // forever with nothing near it, because the gate it trusted meant something else.
            if (now - _yieldSinceMs > MaxYieldMs)
            {
                if (!_yieldCapWarned)
                {
                    _yieldCapWarned = true;
                    Svc.Log.Warning(
                        "[BozjaBuddyReborn] BossMod has been asking to steer for over " +
                        $"{MaxYieldMs / 1000}s with danger reported - taking movement back so the " +
                        "run continues. Check Dependencies for the live avoidance signals.");
                }

                // RE-BASE, DO NOT LATCH. The cap used to hold for as long as the signal stayed
                // continuously true, because the only thing that cleared it was the signal
                // dropping - so one overlong yield turned avoidance off for the whole remaining
                // danger period, which in a Critical Engagement is the entire fight. Standing
                // down for a cooldown and then trusting BossMod again keeps the guard's purpose
                // (a stuck signal cannot stall the run) without the collateral.
                _yieldSinceMs = 0;
                _yieldBlockedUntilMs = now + YieldCooldownMs;
                return false;
            }

            _lastDodgeMs = now;
            return true;
        }

        // Signal genuinely dropped: the next dodge gets a fresh budget and a fresh entry count.
        _yieldSinceMs = 0;
        _yieldCapWarned = false;
        _dodgeTicks = 0;

        return _lastDodgeMs != 0 && now - _lastDodgeMs < DodgeResumeDelayMs;
    }

    private long _lastDodgeMs;
    private long _yieldSinceMs;
    private bool _yieldCapWarned;
    private int _dodgeTicks;
    private long _lastDodgeQueryMs;
    private long _yieldBlockedUntilMs;

    /// <summary>Monotonic tick counter, so per-tick answers can be memoised. See IsDodging.</summary>
    private ulong _tickSeq;
    private ulong _dodgeAnsweredForTick;
    private bool _dodgeAnswer;

    /// <summary>Consecutive ticks the steering signal must hold before movement is handed over.</summary>
    private const int DodgeTicksToYield = 2;

    /// <summary>How long travel keeps the path after the yield cap trips, before trying again.</summary>
    private const long YieldCooldownMs = 3000;

    /// <summary>A gap this long without anyone asking ends the current yield episode.</summary>
    private const long YieldEpisodeGapMs = 2000;

    private long _lastAttackerMs;

    /// <summary>
    /// How long the field has to stay clear before "under attack" is released.
    ///
    /// See the call site: the raw count flickers, and every flicker used to swap the whole
    /// movement and rotation configuration.
    /// </summary>
    private const long AttackerReleaseMs = 2500;

    /// <summary>
    /// Latched "something is hitting us". Enters on the first sighting, leaves only after the
    /// field has been quiet for <see cref="AttackerReleaseMs"/> and the game agrees combat is
    /// over.
    /// </summary>
    private bool UnderAttack(int attackers)
    {
        var now = Environment.TickCount64;

        if (attackers > 0)
        {
            _lastAttackerMs = now;
            return true;
        }

        if (_lastAttackerMs == 0)
            return false;

        // InCombat lingers after the last mob dies, which is why it is not the primary signal -
        // but as a release GUARD that is the right behaviour: it keeps us from turning the
        // rotation off between two hits of the same fight.
        if (now - _lastAttackerMs < AttackerReleaseMs || Svc.Condition[ConditionFlag.InCombat])
            return true;

        _lastAttackerMs = 0;
        return false;
    }

    /// <summary>Longest we will hand movement to BossMod continuously before taking it back.</summary>
    private const long MaxYieldMs = 6000;

    /// <summary>Seconds we have continuously been yielding, for the UI.</summary>
    public float SecondsYielding => _yieldSinceMs == 0 ? 0f : (Environment.TickCount64 - _yieldSinceMs) / 1000f;

    /// <summary>Appended to the status when we arrived somewhere other than the marker centre.</summary>
    private string? _arrivalNote;

    private long _lastArrivalPingMs;

    /// <summary>How often a waiting client re-announces its arrival, so a reconnect self-heals.</summary>
    private const long ArrivalRepingMs = 2000;

    /// <summary>How long after a dodge signal drops before travel takes the path back.</summary>
    private const long DodgeResumeDelayMs = 400;

    // ----------------------------------------------------------------- travel

    private void RunTravel(SharedObjective objective)
    {
        // Positions drift: a FATE's ring moves as it progresses, and an engagement's marker can
        // be republished. Always prefer the live position over the one we were handed.
        var destination = LivePosition(objective) ?? objective.Position;
        var range = ArriveRangeFor(objective);
        var arrived = _movement.HasArrived(destination, range);

        // Read before the leash decision: an active fight must not be mistaken for a return trip.
        // Keyed on hostiles actually targeting us rather than ConditionFlag.InCombat, which
        // lingers after the last mob dies and would make the runner stop for phantom fights.
        var attackers = Threat.CountAttackers();

        // Hysteresis on LEAVING the arrived state - see CommittedLeash. The approach deliberately
        // walks the character off the objective's exact centre to reach what it is fighting.
        //
        // ONE-WAY. Applied symmetrically (as it was), the widened radius also satisfies the
        // ENTRY test, so a box that had ever arrived and was then dragged well clear - a chased
        // fight, or far more commonly a death and the corpse run back from base camp - would
        // stop travelling the instant it came back within range + 30y and report "At <objective>"
        // while standing 40y+ outside the arena. Once genuinely dragged past the leash, only the
        // strict test gets us back in.
        if (arrived)
        {
            _committed = true;
            _returning = false;
        }
        else if (_committed && !_returning)
        {
            // ONE THRESHOLD IS NOT HYSTERESIS. Entry and exit used to be the same expression -
            // `dist <= range + CommittedLeash` decided both whether to keep "arrived" and, by its
            // negation, whether to give it up - so a character sitting on that boundary flipped
            // branches every tick. And the only one-way latch, _returning, was armed behind
            // `attackers == 0`, i.e. disarmed in exactly the situation the leash exists for: a
            // stand-off attacker dragging the character back and forth across the line put it
            // into RunDefend and travel alternately, dashing outward and inward with the rotation
            // toggled each way and the approach path torn down each time.
            //
            // So the widened radius is now purely an EXIT threshold for a box that has genuinely
            // arrived, the latch is on distance alone, and combat no longer decides it. Getting
            // back in still requires the strict test above, which is what stops a box 40y outside
            // an arena reporting that it is standing in one.
            if (Movement.DistanceToPlayer(destination) <= range + CommittedLeash)
                arrived = true;
            else
                _returning = true;
        }

        // AGGRO EN ROUTE. Bozja and Zadnor pull things onto the route constantly, and stopping to
        // kill each one turns a single run into a string of fights that earn nothing and burn the
        // registration window. So by default we run straight through it: the rotation stays OFF
        // for the whole route, nothing is attacked, and field mobs leash off once outrun.
        //
        // Standing AT the objective is not "en route". There is nowhere further to run, and a
        // Critical Engagement's registration window has to be waited out where we stand, so
        // attackers are answered there regardless of the setting.
        // LATCHED, NOT INSTANTANEOUS. CountAttackers is an unlatched object-table scan recomputed
        // every 200ms, and its two branches disagree about everything that matters: RunDefend
        // stops travel, arms the rotation and drives an approach path; the travel branch
        // disarms the rotation, releases the approach and issues a travel path. A mob whose
        // target flickers - losing us for a tick as it retargets, or dying and being replaced -
        // therefore toggled all of that several times a second. Every flip is a real IPC send in
        // both directions, a torn-down path, and a fresh 700ms approach lockout, which is exactly
        // what "stuttering" looks like from the outside.
        //
        // Entering is immediate: being hit is not something to deliberate over. Leaving waits for
        // the field to be genuinely quiet.
        if (UnderAttack(attackers) && (arrived || _config.AggroResponse == TravelAggroResponse.FightBack))
        {
            RunDefend(objective, attackers);
            return;
        }

        // Dodging beats travelling. Always - and beats an approach path just as hard, so hand
        // vnavmesh back rather than leaving one running underneath the dodge. That means the
        // TRAVEL path too, not just the approach: both BossMod forks refuse to steer while
        // vnavmesh's "vnav.PathIsRunning" flag is set (MovementOverride.FollowPathActive), so
        // returning here without stopping the path leaves the character being walked through the
        // mechanic by vnavmesh while BossMod waits politely for the path to end.
        //
        // SUSPEND, not Stop: the destination and the snap are kept, so resuming does not re-snap,
        // does not reset the stall clock, and does not zero the repath counter. Using Stop here
        // meant every mechanic silently defeated the repath throttle and made "stuck" unreportable.
        if (IsDodging())
        {
            State = ControllerState.Travelling;
            Status = "BossModに移動制御を渡してギミックを回避しています。";
            _approach.Release();
            _movement.Suspend();
            _director.Travel(_config.UseBossModAvoidance);
            return;
        }

        if (!arrived)
        {
            State = ControllerState.Travelling;

            // Travel(), not Engage(): this is what holds the rotation off, and it is the whole
            // reason a chase does not turn into a fight.
            _director.Travel(_config.UseBossModAvoidance);

            // Approach and travel both drive vnavmesh, so exactly one of them may hold it. Hand
            // it back BEFORE issuing the travel path, never after.
            _approach.Release();

            // On-foot survival may use instant Lost Actions. The driver has an absolute mounted
            // guard, so this can never be the reason a travelling mount is dismissed.
            _holster.TickTravelSurvival();

            if (!_movement.TravelTo(destination, range))
            {
                Status = "vnavmeshで経路を開始できませんでした。";
                return;
            }

            var distance = Movement.DistanceToPlayer(destination);

            // Repeated re-snaps and re-paths have all failed to shift the character. Say so:
            // this used to present as a silent hang while the repath counter climbed.
            if (_movement.Stuck)
            {
                var failedRepaths = _movement.RepathCount;
                var failedSeconds = _movement.SecondsWithoutProgress;

                if (objective.Kind == ObjectiveKind.Fate)
                {
                    _selector.BlacklistFateForCurrentSpawn(objective.Id);
                    Svc.Log.Warning(
                        $"[BozjaBuddyReborn] Route to skirmish FateId {objective.Id} remained stuck after " +
                        $"{failedRepaths} repaths / {failedSeconds:F0}s without progress; blacklisting this live spawn.");
                    DiagnosticsRecorder.Warning(
                        $"スカーミッシュ #{objective.Id} への経路を確立できなかったため、この出現中は除外します。",
                        ControllerState.Travelling);
                    Status = $"スカーミッシュ #{objective.Id} へ到達できないため、この出現中は除外して次の対象を探します。";
                }
                else
                {
                    Svc.Log.Warning(
                        $"[BozjaBuddyReborn] Route to objective {objective.Kind} #{objective.Id} remained stuck after " +
                        $"{failedRepaths} repaths / {failedSeconds:F0}s without progress; abandoning the objective.");
                    DiagnosticsRecorder.Warning(
                        "現在の目的地へ到達できないため、対象を解除して再選択します。",
                        ControllerState.Travelling);
                    Status = "現在の目的地へ到達できないため、対象を解除して再選択します。";
                }

                _movement.Stop();
                _approach.Release();
                _director.Travel(_config.UseBossModAvoidance);
                _lastObjective = SharedObjective.None;
                _committed = false;
                _returning = false;
                _reportedArrival = false;
                _arrivedAtMs = 0;
                _arrivalNote = null;
                return;
            }

            // Being chased. Worth naming, because the character is visibly in combat and
            // deliberately not fighting back - that reads as a broken rotation otherwise.
            if (attackers > 0)
            {
                Status = $"{Describe(objective)}へ移動中（残り{distance:F0}y）- " +
                         $"{attackers}体に追跡されていますが、停止せず逃走を継続します。";
                return;
            }

            if (_movement.AvoidingEnemy is { } enemy)
            {
                Status = $"{Describe(objective)}へ移動中 ({distance:F0}y) - " +
                         $"危険な敵 {enemy.Name} [{enemy.Strength switch { Game.FieldEnemyStrength.IV => "IV", Game.FieldEnemyStrength.V => "V", Game.FieldEnemyStrength.Star => "★", _ => "?" }}] を迂回中。";
                return;
            }

            Status = $"{Describe(objective)}へ移動中 ({distance:F0}y / {_movement.RouteDescription}" +
                     (_movement.RepathCount > 0 ? $", 再経路 {_movement.RepathCount}" : "") +
                     (_movement.RejectedIssues > 0 ? $", 経路要求拒否 {_movement.RejectedIssues}回" : "") +
                     // Detours that were needed and could not be used. Worth naming: this is the
                     // difference between "the route was clear" and "the route was not clear and
                     // we walked it anyway", which used to read identically.
                     (_movement.RefusedDetours > 0 ? $", 迂回失敗 {_movement.RefusedDetours}回" : "") +
                     ").";
            return;
        }

        // --- arrived ---------------------------------------------------------
        _movement.Stop();

        // Standing at the objective is the one moment TerritoryInfo describes the OBJECTIVE
        // rather than wherever we happened to be, so this is where the region map is learned.
        // Once recorded it is exact and permanent, superseding the positional estimate.
        LearnRegionHere(objective);

        if (_arrivedAtMs == 0)
            _arrivedAtMs = Environment.TickCount64;

        // Arrival now means "as close to the marker as the navmesh can actually get us", which
        // is the only definition that always terminates. When the snap had to move the target a
        // long way, that distinction matters - the character may be standing outside the arena
        // it was aiming for - so say so rather than reporting a clean arrival.
        _arrivalNote = _movement.SnapDrift > range
            ? $" navmeshで到達可能な最寄り地点に到着しました（マーカー中心から{_movement.SnapDrift:F0}y）。"
            : null;

        if (!IsReleasedToCommit())
        {
            State = ControllerState.Holding;

            // HOLDING IS STILL A STATE THAT HAS TO BE DRIVEN. This used to return bare, which
            // left two things running that should not be: an approach path issued on an earlier
            // tick (Movement.Stop deliberately declines to tear down a path the approach owns,
            // so it survives and walks the box off the marker while the status says it is
            // waiting for the group), and the combat director's heartbeat, which stops being
            // re-asserted for as long as the hold lasts - up to the full barrier timeout.
            _approach.Release();
            _director.Travel(_config.UseBossModAvoidance);
            return;
        }

        Commit(objective);
    }

    /// <summary>
    /// Stand and fight whatever is on us, then let the next tick resume travel once it is dead.
    ///
    /// Reached once we have arrived at the objective (nowhere left to run), or anywhere on the
    /// route when the user has asked for <see cref="TravelAggroResponse.FightBack"/>.
    /// </summary>
    private void RunDefend(SharedObjective objective, int attackers)
    {
        State = ControllerState.Engaged;
        _movement.Stop();

        // Cannot fight from a mount, so this has to land before the rotation is armed.
        if (!Mount.EnsureDismounted())
        {
            Status = "攻撃を受けています。反撃のためマウントから降りています。";
            _approach.Release();
            _director.Travel(_config.UseBossModAvoidance);
            return;
        }

        Status = $"{attackers}体から攻撃されています。{Describe(objective)}への移動再開前に排除します。";

        _director.Engage(_config.UseBossModAvoidance);

        // Most things that aggro are already on top of us, but a ranged puller is not, and
        // standing still swinging at nothing is the same failure as everywhere else.
        if (_approach.Tick(IsDodging(), _director.AvoidanceOwnsApproach) && _approach.ClosingOn is { } closing)
            Status = $"{attackers}体から攻撃されています。{closing}へ接近中です。";

        _holster.Tick(inCombat: true);
    }

    private void Commit(SharedObjective objective)
    {
        State = ControllerState.Engaged;

        switch (objective.Kind)
        {
            case ObjectiveKind.CriticalEngagement:
                // Hold position, keep avoidance live, and wait for the engagement to flip to
                // Warmup/Battle - the next tick picks it up as the current engagement and
                // switches to RunEngagement.
                //
                // THIS USED TO SAY "there is no button to press", AND THAT IS PROBABLY WRONG.
                // The Patch 5.35 notes say critical engagements "do not require you to be present
                // in the field to participate. Instead, players must request deployment via the
                // Resistance Recruitment window" - Register, then Commence once selected, which
                // is what SignUpRunner now drives. If that is right, arriving here and waiting
                // enrols nobody and this branch should be starting a sign-up instead of holding.
                // Not changed yet: it needs one live check (see CeSnapshot.IsJoinable), and
                // guessing wrong in this direction would break the one CE path that people may
                // currently be relying on.
                Status = $"{Describe(objective)}付近で参加処理を待っています。{_arrivalNote}";
                _approach.Release();
                _director.Travel(_config.UseBossModAvoidance);
                break;

            case ObjectiveKind.Fate:
                if (!TargetSelector.FateIsActive(objective.Id))
                {
                    Status = "スカーミッシュが終了しました。次の対象を選択します。";
                    _lastObjective = SharedObjective.None;
                    _approach.Release();
                    _director.Travel(_config.UseBossModAvoidance);
                    return;
                }

                // Grounded first - a mounted character cannot fight.
                if (!Mount.EnsureDismounted())
                {
                    Status = $"{Describe(objective)}で戦闘するためマウントから降りています。";
                    _approach.Release();
                    _director.Travel(_config.UseBossModAvoidance);
                    return;
                }

                Status = $"{Describe(objective)}で戦闘中です。";
                _director.Engage(_config.UseBossModAvoidance);

                // Arriving inside a skirmish ring is not the same as being on top of its mobs -
                // the ring is tens of yalms across - so the approach matters most here.
                if (_approach.Tick(IsDodging(), _director.AvoidanceOwnsApproach) && _approach.ClosingOn is { } closing)
                {
                    Status = $"{Describe(objective)}で戦闘中 - {closing}へ接近しています " +
                             $"（射程まであと{_approach.ShortfallYalms:F0}y）。";
                }

                _holster.Tick(Svc.Condition[ConditionFlag.InCombat]);
                break;

            default:
                _approach.Release();
                _director.Travel(_config.UseBossModAvoidance);
                break;
        }
    }

    // --------------------------------------------------------------- multibox

    private SharedObjective ResolveObjective()
    {
        var multibox = _config.MultiboxEnabled;

        // A client follows the host, but only while the link is actually up. If the pipe is
        // down it falls through to the deterministic selection, which by construction produces
        // the same answer the host would have produced.
        if (multibox && !_config.MultiboxIsHost && _link.Connected)
        {
            var fromHost = _link.Objective;

            // THE HOST'S ZONE IS NOT NECESSARILY OURS. SharedObjective carries the territory
            // precisely so this can be asked: without it a client in Zadnor took the host's
            // Bozjan coordinates and pathed to them interpreted in its own territory, which
            // lands somewhere plausible because the two zones use overlapping coordinate ranges
            // - so it looked like travel rather than like a bug.
            if (fromHost.IsSet && fromHost.Territory != 0 &&
                fromHost.Territory != Svc.ClientState.TerritoryType)
                return SharedObjective.None;

            return fromHost;
        }

        // STICKINESS. The selector re-ranks from scratch every tick, so without this a newly
        // spawned skirmish that ranks higher (a lower id in deterministic mode, or simply a
        // nearer one) yanks the character off the fight it is already in and sends it running.
        // Stay on a committed objective while it is still worth doing.
        //
        // STILL WORTH DOING IS NOT THE ONLY QUESTION. This used to test liveness alone, so a
        // committed objective was never re-checked against the region/activity restriction:
        // changing the farm material took effect only once the current objective ended - which
        // for a Bozja skirmish is many minutes - and on a host the out-of-region objective was
        // re-broadcast to the whole group every tick in the meantime. Stickiness exists to stop
        // the runner being yanked off a fight it is already in, not to outrank the filter.
        if (_config.StickyObjective
            && _lastObjective.Kind != ObjectiveKind.CriticalEngagement
            && IsObjectiveStillWorthDoing(_lastObjective)
            && _selector.StillPermitted(_lastObjective.Kind, _lastObjective.Id, _lastObjective.Position))
        {
            if (multibox && _config.MultiboxIsHost)
                _link.BroadcastObjective(_lastObjective);
            return _lastObjective;
        }

        var choice = _selector.Select(Engagements, deterministic: multibox);

        // BROADCAST "NOTHING" TOO. Guarding this on IsSet meant the link latched the last
        // objective forever: a host that ran out of work, or changed zone, left every client
        // still holding a dead engagement and running at it, because nothing on the wire ever
        // said to stop. Clients treat an unset objective as "go idle".
        if (multibox && _config.MultiboxIsHost)
            _link.BroadcastObjective(choice.Objective);

        return choice.Objective;
    }

    /// <summary>
    /// Is the objective we already committed to still worth finishing?
    ///
    /// A skirmish counts while it is still running and incomplete. An engagement counts only
    /// while registration is still open with enough margin to arrive - once it starts without
    /// us, continuing to run at it is wasted travel.
    /// </summary>
    private bool IsObjectiveStillWorthDoing(SharedObjective objective)
    {
        if (!objective.IsSet)
            return false;

        if (objective.Kind == ObjectiveKind.Fate)
            return TargetSelector.FateIsActive(objective.Id);

        foreach (var ce in Engagements)
        {
            if (ce.EventId != objective.Id)
                continue;

            return ce.State == DynamicEventState.Register
                   && ce.SecondsLeft >= (uint)_config.MinRegisterSecondsLeft;
        }

        return false;
    }

    /// <summary>
    /// Whether this box may commit to the objective now.
    ///
    /// Solo, or with the barrier off: immediately. With the barrier on, a client reports its
    /// arrival and waits for the host's GO; the host waits for every peer to report in, and
    /// releases anyway once the timeout expires so one stuck box cannot stall the group.
    /// </summary>
    private bool IsReleasedToCommit()
    {
        if (!_config.MultiboxEnabled || !_config.MultiboxArrivalBarrier)
            return true;

        if (!_link.Connected)
            return true; // link down - do not stall, act alone

        if (!_config.MultiboxIsHost)
        {
            // ONCE RELEASED, STOP TALKING. The re-ping used to sit above this check, so a client
            // that had already been given the GO went on announcing its arrival every two
            // seconds for as long as it stood there. Those pings land on the host with no
            // objective attached, so one arriving just after the host cleared the barrier for
            // the NEXT objective re-filled it with a box that is tens of seconds away - and the
            // host then satisfied arrived >= peers and released the group early, which is the
            // barrier silently doing the opposite of its job.
            if (_link.Released)
                return true;

            // Latch only on a send that actually LANDED. It used to latch unconditionally, so an
            // ARRIVED issued while the pipe happened to be down was never retried and this box
            // silently held the whole group until the barrier timed out.
            if (!_reportedArrival)
            {
                _reportedArrival = _link.ReportArrived();
            }
            else if (Environment.TickCount64 - _lastArrivalPingMs > ArrivalRepingMs)
            {
                // Idempotent on the host (it just sets a flag against our connection id), and
                // necessary: a reconnect gives this box a NEW connection id, so its previous
                // arrival record is legitimately gone and would otherwise never be replaced.
                _lastArrivalPingMs = Environment.TickCount64;
                _link.ReportArrived();
            }

            Status = $"{Describe(_lastObjective)}に到着済み - グループを待っています。";
            return false;
        }

        // Host: hold until everyone has checked in, or the timeout elapses.
        var peers = _link.PeerCount;
        var arrived = _link.ArrivedCount;
        var waitedMs = Environment.TickCount64 - _arrivedAtMs;
        var timedOut = waitedMs > _config.MultiboxBarrierTimeoutSeconds * 1000L;

        if (arrived >= peers || timedOut)
        {
            _link.BroadcastGo();
            return true;
        }

        Status = $"{Describe(_lastObjective)}に到着済み - グループ待機中（{arrived}/{peers}到着）。";
        return false;
    }

    /// <summary>
    /// Carry out anything the operator has told this box to do.
    ///
    /// EVERY FRAME, NOT ON THE THROTTLED TICK. This used to sit at the top of <see cref="Tick"/>,
    /// which the plugin runs at 200ms, so every instruction was delivered somewhere between
    /// instantly and a fifth of a second late. That was invisible for "go tap the cache" and is
    /// not for a hotbar press - a button that answers on a coin flip between 0 and 200ms reads as
    /// a button that sometimes does not work. The drain itself is a dequeue on an empty queue in
    /// the overwhelming majority of frames, so running it at frame rate costs nothing.
    /// </summary>
    public void PumpCommands()
    {
        while (_link.InboundCommands.TryDequeue(out var command))
        {
            switch (command)
            {
                case "START" when !Running:
                    Start();
                    break;
                case "STOP" when Running:
                    Stop("マルチボックスのホストから停止されました。");
                    break;
            }
        }

        // Operator instructions. Deliberately drained even when the orchestrator is NOT running -
        // the whole point of the control panel is acting on an idle box without focusing its
        // window, and "apply this loadout" or "go tap the cache" are exactly the things you want
        // to do to a box that is parked.
        while (_link.InboundCommandQueue.TryDequeue(out var box))
            Execute(box);
    }

    /// <summary>Carry out one instruction from the operator's box.</summary>
    private void Execute(BoxCommand command)
    {
        switch (command.Verb)
        {
            case BoxVerb.Start:
                if (!Running)
                    Start();
                break;

            case BoxVerb.Stop:
                if (Running)
                    Stop("マルチボックス操作画面から停止されました。");
                break;

            case BoxVerb.Loadout:
                if (Loadout.TryDecode(command.Arg, out var a0, out var a1, out var ess))
                {
                    _loadouts.Apply(a0, a1, ess);
                    LastCommandResult = $"ロストアクション構成: {Loc.Runtime(_loadouts.LastResult)}";
                }
                else
                {
                    LastCommandResult = "ロストアクション構成: 指定内容を読み取れませんでした。";
                }
                break;

            case BoxVerb.Interact:
                if (uint.TryParse(command.Arg, out var dataId))
                {
                    // An errand takes movement off the orchestrator for its duration rather than
                    // racing it - see the yield in Tick.
                    _approach.Release();
                    _movement.Stop();
                    _errands.Begin(dataId);
                    LastCommandResult = $"移動指示: {_errands.Status}";
                }
                break;

            case BoxVerb.SignUp:
                // Refuse rather than start an attempt that cannot succeed. The runner used to be
                // started with no preconditions at all, so pressing "sign up all" told every box
                // - including ones in Gangos, ones already in the engagement, and ones on a
                // loading screen - to go and poke UI agents.
                if (CriticalEngagements.RegisteredEventId is { } joined)
                    LastCommandResult = $"参加申請: 既にイベント #{joined} へ参加中です。";
                else if (!FieldState.InFieldZone)
                    LastCommandResult =
                        $"参加申請: 対応フィールド外です（現在地: {BozjaZones.Name(Svc.ClientState.TerritoryType)}）。";
                else
                {
                    _signUps.Begin();
                    LastCommandResult = $"参加申請: {_signUps.Status}";
                }
                break;

            case BoxVerb.Cancel:
                _errands.Cancel("マルチボックス操作画面から中止されました。");
                _signUps.Cancel("マルチボックス操作画面から中止されました。");
                LastCommandResult = "移動指示を中止しました。";
                break;

            case BoxVerb.PartySupport:
                if (command.Arg == "1")
                    _partySupport.Begin();
                else
                    _partySupport.Stop("マルチボックス操作画面からパーティ支援を停止しました。");
                LastCommandResult = $"パーティ支援: {_partySupport.Status}";
                break;

            case BoxVerb.DutyAction:
                // Note this deliberately does NOT touch the orchestrator, the errand runner or
                // the approach. Pressing a duty action is an instant - it takes nothing away from
                // whatever the box is doing, so unlike an errand it has nothing to seize.
                // The expected action id is what protects this against HolsterDriver, which reloads
                // slot 0 out of the holster every few seconds while AutoUseLostActions is on: if
                // the slot has been swapped between the frame the operator looked and the frame
                // this runs, the press is refused by name rather than firing the replacement.
                if (BoxCommand.TryDecodeDutyAction(command.Arg, out var dutySlot, out var expectedAction))
                {
                    var press = DutyActions.Press(dutySlot, expectedAction);
                    LastCommandResult = $"Duty Action {dutySlot + 1}: {press.Message}";
                }
                else
                {
                    LastCommandResult = "Duty Action: 使用するスロットを読み取れませんでした。";
                }
                break;
        }
    }

    /// <summary>What the last operator instruction did, reported back for the panel.</summary>
    public string LastCommandResult { get; private set; } = string.Empty;

    /// <summary>
    /// What the Lost Action driver last did, or last refused to do.
    ///
    /// Worth surfacing rather than only logging, because the driver's whole failure mode through
    /// 1.0.20.0 was that it did nothing and said nothing while looking busy. Its presses also show
    /// under the duty-action bar (they go through DutyActions.Press), but a load that never lands
    /// only appears here.
    /// </summary>
    public string LastLostAction => _holster.LastResult;

    /// <summary>The party-support task on this box, for the panel that starts and stops it.</summary>
    public PartySupportDriver PartySupport => _partySupport;

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Record which zone third the objective we are standing at belongs to.
    /// </summary>
    private void LearnRegionHere(SharedObjective objective)
    {
        if (CurrentRegion == FieldRegionId.Unknown || !objective.IsSet)
            return;

        // ONLY FROM WHERE THE OBJECTIVE ACTUALLY IS. This records the region the CHARACTER is
        // standing in against the OBJECTIVE's id, and persists it permanently - so it is only
        // honest while the two are the same place. "Arrived" is not that guarantee: the committed
        // leash grants it from up to 30y beyond the arrival range, and the snap can legitimately
        // leave us further out still (the caller prints "as close as the navmesh allows" for
        // exactly that case, right after this used to run).
        //
        // Getting it wrong is not self-correcting, despite what RegionResolver claims: a wrong
        // value that disagrees with the restriction filters the objective out forever, so it is
        // never revisited to be corrected. Requiring genuine proximity is cheap; being wrong is
        // permanent.
        var live = LivePosition(objective) ?? objective.Position;
        if (Movement.DistanceToPlayer(live) > LearnRegionRange)
            return;

        var territory = Svc.ClientState.TerritoryType;
        if (_regions.Learn(territory, objective.Kind, objective.Id, CurrentRegion))
        {
            Svc.Log.Information(
                $"[BozjaBuddyReborn] Learned {Describe(objective)} is in " +
                $"{FieldRegions.Label(territory, CurrentRegion)}.");
        }
    }

    /// <summary>
    /// How close to an objective the character has to be for its sub-region reading to describe
    /// the OBJECTIVE rather than wherever we happen to be standing. Comfortably inside the
    /// narrowest gap between two regions.
    /// </summary>
    private const float LearnRegionRange = 20f;

    private Vector3? LivePosition(SharedObjective objective)
    {
        if (objective.Kind == ObjectiveKind.Fate)
            return TargetSelector.FatePosition(objective.Id);

        foreach (var ce in Engagements)
            if (ce.EventId == objective.Id && ce.HasPosition)
                return ce.Position;

        return null;
    }

    private float ArriveRangeFor(SharedObjective objective)
    {
        if (objective.Kind != ObjectiveKind.Fate)
            return _config.ArriveRange;

        // Being inside the FATE ring is what level-syncs the character and makes its mobs
        // count, so aim comfortably inside rather than at the edge.
        //
        // CAPPED, and that cap is load-bearing. A skirmish ring is tens of yalms across, so half
        // its radius could be 30y+ from the centre - "arrived" would fire that far out, the
        // rotation would arm against mobs it cannot reach, and a melee job would sit there using
        // its ranged filler. The approach closes the rest, but there is no reason to declare
        // arrival from the far edge of the ring in the first place.
        var radius = TargetSelector.FateRadius(objective.Id);
        return radius > 0 ? Math.Clamp(radius * 0.5f, 5f, 20f) : _config.ArriveRange;
    }

    private string Describe(SharedObjective objective)
    {
        if (!objective.IsSet)
            return "目的地なし";

        if (objective.Kind == ObjectiveKind.CriticalEngagement)
            return $"CE「{_catalog.Name((ushort)objective.Id)}」";

        foreach (var fate in Svc.Fates)
            if (fate != null && fate.FateId == objective.Id)
                return $"スカーミッシュ「{fate.Name.TextValue}」";

        return $"スカーミッシュ #{objective.Id}";
    }
}
