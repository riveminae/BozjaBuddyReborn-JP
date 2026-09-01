using System;
using System.Collections.Generic;
using System.Linq;

namespace BozjaBuddyReborn.Game;

public readonly record struct SurvivalLoadoutTarget(
    byte RowId,
    string Name,
    byte Weight,
    int Available,
    int Desired,
    int Planned,
    string Purpose);

public readonly record struct SurvivalLoadoutPlan(
    SurvivalRole Role,
    byte EssenceRow,
    byte DutySlot0Row,
    byte DutySlot1Row,
    int TotalWeight,
    IReadOnlyList<SurvivalLoadoutTarget> Targets,
    IReadOnlyList<string> Warnings)
{
    public bool HasSelfRecovery => Targets.Any(t =>
        t.Planned > 0 &&
        (t.Name is "Resistance Potion Kit" or "Lost Cure IV" or "Lost Cure II" or "Lost Cure III" or "Lost Cure" or "Lost Full Cure"));
}

/// <summary>
/// Pure target-state planner for Initialize. It consumes a read-only Cache/Holster snapshot and
/// produces the exact target rows/counts that the transfer transaction should build. It never
/// touches the MYCItemBox UI itself, so the still-unresolved server-backed transfer callback is
/// isolated from all policy decisions.
/// </summary>
public sealed class SurvivalLoadoutPlanner(Configuration config, LostActionCatalog catalog)
{
    public const int HolsterCapacity = 99;

    private readonly Configuration _config = config;
    private readonly LostActionCatalog _catalog = catalog;
    private readonly SurvivalPolicy _policy = new(config, catalog);

    private sealed record Candidate(LostActionCatalog.Entry Entry, int Low, int Target, string Purpose, int Rank);

    public SurvivalLoadoutPlan Build(LostItemBoxSnapshot snapshot)
    {
        List<string> warnings = [];
        if (!snapshot.Available)
            return new SurvivalLoadoutPlan(_policy.Role, 0, 0, 0, 0, [], ["Lost Finds Cache/Holster state unavailable"]);

        var candidates = BuildCandidates(snapshot, warnings);
        var planned = new Dictionary<byte, int>();
        var weight = 0;

        // Pass 1: build the minimum survivability floor before spending capacity on reserves.
        foreach (var candidate in candidates.OrderBy(c => c.Rank))
        {
            var available = Available(snapshot, candidate.Entry);
            var floor = Math.Min(candidate.Low, available);
            AddUpTo(candidate.Entry, floor, available, planned, ref weight);
        }

        // Pass 2: grow each category to the agreed target while preserving the same safety order.
        foreach (var candidate in candidates.OrderBy(c => c.Rank))
        {
            var available = Available(snapshot, candidate.Entry);
            var desired = Math.Min(candidate.Target, available);
            AddUpTo(candidate.Entry, desired, available, planned, ref weight);
        }

        // Pass 3: use remaining capacity for additional survival reserves, round-robin. Main heal
        // gets first chance each round, then Potion Kit, Reraiser, Manawall and the chosen Essence.
        // This avoids a single cheap item monopolising the 99-weight holster.
        var expandable = candidates.OrderBy(c => c.Rank).ToArray();
        var changed = true;
        while (changed && weight < HolsterCapacity)
        {
            changed = false;
            foreach (var candidate in expandable)
            {
                var entry = candidate.Entry;
                var available = Available(snapshot, entry);
                var current = planned.GetValueOrDefault(entry.RowId);
                var max = entry.MaxHeld > 0 ? Math.Min(available, entry.MaxHeld) : available;
                if (current >= max || entry.Weight == 0 || weight + entry.Weight > HolsterCapacity)
                    continue;
                planned[entry.RowId] = current + 1;
                weight += entry.Weight;
                changed = true;
            }
        }

        var targets = new List<SurvivalLoadoutTarget>();
        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;
            var available = Available(snapshot, entry);
            var amount = planned.GetValueOrDefault(entry.RowId);
            targets.Add(new SurvivalLoadoutTarget(
                entry.RowId,
                EnglishName(entry),
                entry.Weight,
                available,
                candidate.Target,
                amount,
                candidate.Purpose));
        }

        var essence = SelectEssence(snapshot);
        var (slot0, slot1) = SelectDutySlots(snapshot, planned);

        if (slot0 == 0)
            warnings.Add("No survivability-first duty action is available for slot 1.");
        if (slot1 == 0)
            warnings.Add("No second survivability duty action/utility fallback is available for slot 2.");

        if (!targets.Any(t => t.Planned > 0 && t.Name == "Resistance Potion Kit")
            && !targets.Any(t => t.Planned > 0 && IsHealName(t.Name)))
            warnings.Add("No Potion Kit or self-heal can be placed in the holster.");

        return new SurvivalLoadoutPlan(_policy.Role, essence, slot0, slot1, weight, targets, warnings);
    }

    private List<Candidate> BuildCandidates(LostItemBoxSnapshot snapshot, List<string> warnings)
    {
        List<Candidate> result = [];

        void Add(string name, int low, int target, string purpose, int rank)
        {
            var found = _policy.Find(name);
            if (found is not { } entry || !_policy.BringAllowed(entry))
                return;
            if (Available(snapshot, entry) <= 0)
                return;
            if (result.Exists(c => c.Entry.RowId == entry.RowId))
                return;
            result.Add(new Candidate(entry, Math.Max(0, low), Math.Max(low, target), purpose, rank));
        }

        // Main heal is deliberately first: the requirement's hard-stop condition is no Potion Kit
        // AND no usable self heal, so the holster floor must guarantee a heal before luxury reserve.
        if (_policy.Role == SurvivalRole.Healer)
            Add("Lost Full Cure", _config.SupplyMainHealLow, _config.SupplyMainHealTarget, "主回復", 0);
        else
        {
            Add("Lost Cure IV", _config.SupplyMainHealLow, _config.SupplyMainHealTarget, "主回復", 0);
            Add("Lost Cure II", 1, 3, "回復フォールバック", 5);
            Add("Lost Cure III", 1, 2, "回復フォールバック", 6);
            Add("Lost Cure", 1, 2, "回復フォールバック", 7);
        }

        Add("Resistance Potion Kit", _config.SupplyPotionKitLow, _config.SupplyPotionKitTarget, "自動回復保険", 1);
        Add("Resistance Reraiser", _config.SupplyReraiserLow, _config.SupplyReraiserTarget, "蘇生保険", 2);
        Add("Lost Manawall", _config.SupplyEmergencyDefenseLow, _config.SupplyEmergencyDefenseTarget, "緊急防御", 3);

        var essence = SelectEssenceEntry(snapshot);
        if (essence is { } e)
            result.Add(new Candidate(e, 1, 1, "生存Essence", 4));
        else
            warnings.Add("No permitted survivability Essence is available.");

        return result;
    }

    private byte SelectEssence(LostItemBoxSnapshot snapshot) => SelectEssenceEntry(snapshot)?.RowId ?? 0;

    private LostActionCatalog.Entry? SelectEssenceEntry(LostItemBoxSnapshot snapshot)
    {
        foreach (var name in _policy.EssencePriority)
        {
            var found = _policy.Find(name);
            if (found is { } entry && _policy.BringAllowed(entry) && Available(snapshot, entry) > 0)
                return entry;
        }
        return null;
    }

    private (byte Slot0, byte Slot1) SelectDutySlots(LostItemBoxSnapshot snapshot, IReadOnlyDictionary<byte, int> planned)
    {
        var names = _policy.Role == SurvivalRole.Healer
            ? new[] { "Lost Full Cure", "Lost Manawall" }
            : new[] { "Lost Cure IV", "Lost Manawall", "Lost Cure II", "Lost Cure III", "Lost Cure" };

        byte a = 0, b = 0;
        foreach (var name in names)
        {
            var found = _policy.Find(name);
            if (found is not { } entry || !entry.IsAction || !_policy.BringAllowed(entry))
                continue;
            if (planned.GetValueOrDefault(entry.RowId) <= 0 && snapshot.HolsterCount(entry.RowId) <= 0)
                continue;
            if (a == 0) a = entry.RowId;
            else if (b == 0 && entry.RowId != a) { b = entry.RowId; break; }
        }
        return (a, b);
    }

    private int Available(LostItemBoxSnapshot snapshot, LostActionCatalog.Entry entry)
    {
        var total = snapshot.CacheCount(entry.RowId) + snapshot.HolsterCount(entry.RowId);
        return entry.MaxHeld > 0 ? Math.Min(total, entry.MaxHeld) : total;
    }

    private static void AddUpTo(
        LostActionCatalog.Entry entry,
        int desired,
        int available,
        IDictionary<byte, int> planned,
        ref int totalWeight)
    {
        var current = planned.GetValueOrDefault(entry.RowId);
        var max = entry.MaxHeld > 0 ? Math.Min(available, entry.MaxHeld) : available;
        var target = Math.Min(desired, max);
        while (current < target && entry.Weight > 0 && totalWeight + entry.Weight <= HolsterCapacity)
        {
            current++;
            totalWeight += entry.Weight;
        }
        if (current > 0)
            planned[entry.RowId] = current;
    }

    private string EnglishName(LostActionCatalog.Entry entry)
    {
        // SurvivalPolicy's index is deliberately English-name based. Find the stable policy name
        // for known candidates; catalog.Name remains localized and is only a display fallback.
        string[] known =
        [
            "Resistance Potion Kit", "Resistance Reraiser", "Lost Manawall", "Lost Full Cure",
            "Lost Cure IV", "Lost Cure III", "Lost Cure II", "Lost Cure",
            .. _policy.EssencePriority,
        ];
        foreach (var name in known)
            if (_policy.Find(name) is { } found && found.RowId == entry.RowId)
                return name;
        return entry.Name;
    }

    private static bool IsHealName(string name) =>
        name is "Lost Full Cure" or "Lost Cure IV" or "Lost Cure III" or "Lost Cure II" or "Lost Cure";
}
