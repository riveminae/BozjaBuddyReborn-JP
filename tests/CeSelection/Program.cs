using System.Numerics;
using BozjaBuddyReborn;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Relic;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

var checkedCases = 0;
var failures = 0;

void Check(bool condition, string label)
{
    checkedCases++;
    if (condition) return;
    failures++;
    Console.Error.WriteLine($"FAIL: {label}");
}

static CeSnapshot Event(ushort id = 10, uint seconds = 60, bool position = true,
    DynamicEventState state = DynamicEventState.Register, bool duel = false)
    => new(0, id, state, 0, 0, 48, seconds, 180, position ? Vector3.One : Vector3.Zero,
        20, $"CE {id}", duel);

static TargetSelector Selector(Configuration config)
    => new(new CeCatalog(), config, new RegionResolver(config), new Movement());

foreach (var territory in new uint[] { 920, 975 })
{
    Svc.ClientState.TerritoryType = territory;
    foreach (var legacySeconds in new[] { -1, 15, 60, int.MaxValue })
    foreach (var seconds in new uint[] { 0, 1, 9, 10, 14, 15, 60 })
    foreach (var position in new[] { false, true })
    {
        var config = new Configuration { MinRegisterSecondsLeft = legacySeconds };
        Check(Selector(config).SelectRegistration([Event(seconds: seconds, position: position)], false)?.EventId == 10,
            $"open remote CE: territory={territory}, legacy={legacySeconds}, seconds={seconds}, position={position}");
    }

    foreach (var state in new[] { DynamicEventState.Inactive, DynamicEventState.Warmup, DynamicEventState.Battle })
        Check(Selector(new()).SelectRegistration([Event(state: state)], false) == null, $"closed phase {state}");

    var blocked = new Configuration();
    blocked.BlockedEngagements.Add(10);
    Check(Selector(blocked).SelectRegistration([Event()], false) == null, "explicit CE block retained");
    Check(Selector(new()).SelectRegistration([Event(duel: true)], false) == null, "duel remains opt-in");
    Check(Selector(new() { EngageDuels = true }).SelectRegistration([Event(duel: true)], false)?.EventId == 10,
        "enabled duel allowed");
    Check(Selector(new()).SelectRegistration([Event(id: 16)], false) == null, "large-scale remains opt-in");

    foreach (var deterministic in new[] { false, true })
    {
        var priority = new Configuration { EngageLargeScale = true };
        priority.PriorityEngagements.Add(11);
        Check(Selector(priority).SelectRegistration([Event(id: 11), Event(id: 16, position: false)], deterministic)?.EventId == 16,
            "enabled large-scale outranks explicit priority without map position");
        Check(Selector(priority).SelectRegistration([Event(id: 10), Event(id: 11)], deterministic)?.EventId == 11,
            "explicit priority retained");
        Check(Selector(new()).SelectRegistration([Event(id: 12), Event(id: 10)], deterministic)?.EventId == 10,
            "tie uses event id, not input order");
    }
}

Svc.ClientState.TerritoryType = 975;
var farm = new Configuration { FarmMaterialItemId = ResistanceRelic.CompactSpring };
farm.LearnedRegions["975:c:10"] = (byte)FieldRegionId.Zone1;
Check(Selector(farm).SelectRegistration([Event(position: false)], false)?.EventId == 10,
    "learned matching Relic region works without map position");
Check(Selector(farm).SelectRegistration([Event(id: 11, position: false)], false) == null,
    "unknown ordinary CE cannot bypass active Relic target");
farm.LearnedRegions["975:c:10"] = (byte)FieldRegionId.Zone2;
Check(Selector(farm).SelectRegistration([Event()], false) == null, "nonmatching Relic region excluded");
farm.LearnedRegions["975:c:10"] = (byte)FieldRegionId.Zone1;
farm.FarmMaterialItemId = ResistanceRelic.CompactAxle;
Check(Selector(farm).SelectRegistration([Event()], false) == null, "skirmish-only material excludes ordinary CE");
farm.FarmMaterialItemId = ResistanceRelic.CompactSpring;
Svc.ClientState.TerritoryType = 920;
farm.LearnedRegions["920:c:10"] = (byte)FieldRegionId.Zone1;
Check(Selector(farm).SelectRegistration([Event()], false) == null,
    "same region number in wrong territory cannot satisfy Relic target");
farm.EngageLargeScale = true;
Check(Selector(farm).SelectRegistration([Event(id: 16, position: false)], false)?.EventId == 16,
    "enabled large-scale retains absolute priority over Relic restriction");

Console.WriteLine($"CE selector cases: {checkedCases - failures}/{checkedCases} passed");
return failures == 0 ? 0 : 1;
