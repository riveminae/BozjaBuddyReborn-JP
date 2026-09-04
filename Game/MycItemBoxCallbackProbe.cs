using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
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
    private const int StableSnapshotTicks = 2;
    private const int MaxSnapshotTicks = 30;
    private const int MaxPendingObservations = 64;
    private readonly Configuration _config;
    private readonly LostItemBoxInventory _inventory = new(new LostActionCatalog());
    private readonly List<PendingObservation> _pending = [];
    private long _nextObservationId;

    public MycItemBoxCallbackProbe(Configuration config)
    {
        _config = config;
        foreach (var addon in Addons)
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, addon, OnReceiveEvent);
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        foreach (var addon in Addons)
            Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, addon, OnReceiveEvent);
        Svc.Framework.Update -= OnFrameworkUpdate;
        _pending.Clear();
    }

    private void OnReceiveEvent(AddonEvent _, AddonArgs args)
    {
        if (!_config.LogMycItemBoxCallbacks || args is not AddonReceiveEventArgs evt)
            return;

        try
        {
            var before = _inventory.Read();
            var observationId = ++_nextObservationId;
            var atkEventParam = evt.AtkEvent == 0 ? 0 : ((AtkEvent*)evt.AtkEvent)->Param;
            var data = evt.AtkEventData == 0
                ? "(null)"
                : Convert.ToHexString(MemoryHelper.ReadRaw(evt.AtkEventData, 40));

            Svc.Log.Information(
                $"[BozjaBuddyReborn] MYC transfer probe id={observationId} addon={args.AddonName} " +
                $"type={evt.AtkEventType} eventParam=0x{evt.EventParam:X} atkParam={atkEventParam} data40={data} " +
                $"before={Describe(before)}");

            // The UI event is observed, never replayed. Its server result can only be sampled on
            // later normal framework updates; a callback return value is not an acknowledgement.
            var ambiguous = _pending.Count != 0;
            if (ambiguous)
            {
                foreach (var pending in _pending)
                    pending.Ambiguous = true;
            }

            if (_pending.Count >= MaxPendingObservations)
            {
                var dropped = _pending[0];
                _pending.RemoveAt(0);
                Svc.Log.Warning(
                    $"[BozjaBuddyReborn] MYC transfer probe pending queue full; dropping id={dropped.Id} before correlation.");
            }
            _pending.Add(new PendingObservation(observationId, before, ambiguous));
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[BozjaBuddyReborn] MYC transfer probe could not decode event: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!_config.LogMycItemBoxCallbacks)
        {
            _pending.Clear();
            return;
        }

        if (_pending.Count == 0)
            return;

        var after = _inventory.Read();
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            pending.UpdateTicks++;
            if (pending.LastAfter is { } previous && SameSnapshot(previous, after))
                pending.StableTicks++;
            else
                pending.StableTicks = 0;

            pending.LastAfter = after;
            var settled = after.Available && pending.StableTicks >= StableSnapshotTicks;
            var expired = pending.UpdateTicks >= MaxSnapshotTicks;
            if (!settled && !expired)
                continue;

            Svc.Log.Information(
                $"[BozjaBuddyReborn] MYC transfer correlation id={pending.Id} " +
                $"snapshotStable={settled} ambiguous={pending.Ambiguous} updates={pending.UpdateTicks} " +
                $"before={Describe(pending.Before)} after={Describe(after)} " +
                $"delta={DescribeDelta(pending.Before, after)} deltaMatchesExpected=unknown " +
                "acknowledgement=unconfirmed");
            _pending.RemoveAt(i);
        }
    }

    private static bool SameSnapshot(LostItemBoxSnapshot left, LostItemBoxSnapshot right) =>
        left.Available == right.Available &&
        left.HolsterWeight == right.HolsterWeight &&
        left.Cache.OrderBy(pair => pair.Key).SequenceEqual(right.Cache.OrderBy(pair => pair.Key)) &&
        left.Holster.OrderBy(pair => pair.Key).SequenceEqual(right.Holster.OrderBy(pair => pair.Key));

    private static string Describe(LostItemBoxSnapshot snapshot) =>
        $"available={snapshot.Available} cache=[{DescribeCounts(snapshot.Cache)}] " +
        $"holster=[{DescribeCounts(snapshot.Holster)}] weight={snapshot.HolsterWeight}";

    private static string DescribeCounts(IReadOnlyDictionary<byte, int> counts) =>
        string.Join(',', counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));

    private static string DescribeDelta(LostItemBoxSnapshot before, LostItemBoxSnapshot after)
    {
        var rows = before.Cache.Keys.Concat(before.Holster.Keys)
            .Concat(after.Cache.Keys).Concat(after.Holster.Keys).Distinct().OrderBy(row => row);
        var changes = rows.Select(row =>
        {
            var cache = after.CacheCount(row) - before.CacheCount(row);
            var holster = after.HolsterCount(row) - before.HolsterCount(row);
            return (row, cache, holster);
        }).Where(change => change.cache != 0 || change.holster != 0);

        return string.Join(',', changes.Select(change =>
            $"row={change.row}:cache={change.cache:+#;-#;0},holster={change.holster:+#;-#;0}")) is { Length: > 0 } text
            ? text
            : "none";
    }

    private sealed class PendingObservation(long id, LostItemBoxSnapshot before, bool ambiguous)
    {
        public long Id { get; } = id;
        public LostItemBoxSnapshot Before { get; } = before;
        public bool Ambiguous { get; set; } = ambiguous;
        public LostItemBoxSnapshot? LastAfter { get; set; }
        public int StableTicks { get; set; }
        public int UpdateTicks { get; set; }
    }
}
