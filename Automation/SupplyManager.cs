using System;
using System.Collections.Generic;
using System.Linq;
using BozjaBuddyReborn.Game;

namespace BozjaBuddyReborn.Automation;

public readonly record struct SupplyStatus(
    bool InventoryAvailable,
    bool NeedsRoutineRefill,
    bool CriticalNoRecovery,
    int PotionKits,
    int Reraisers,
    int MainHealUnits,
    int EmergencyDefenseUnits,
    IReadOnlyList<string> Reasons)
{
    public bool SafeToContinue => InventoryAvailable && !CriticalNoRecovery;
}

/// <summary>Read-only result of inspecting the actual Lost Finds Cache after reaching it.</summary>
public readonly record struct CacheSupplyInspection(
    bool InventoryAvailable,
    bool CanImproveRoutine,
    bool CanRecoverCritical,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Pure supply-state evaluator.  It does not move the character and does not transfer inventory;
/// those effects belong to the controller/refill transaction.  Separating evaluation from transfer
/// lets v1.1 make correct CE-vs-supply decisions before the MYCItemBox callback is implemented.
/// </summary>
public sealed class SupplyManager(Configuration config, LostActionCatalog catalog, LostItemBoxInventory inventory)
{
    private readonly Configuration _config = config;
    private readonly LostActionCatalog _catalog = catalog;
    private readonly LostItemBoxInventory _inventory = inventory;
    private readonly SurvivalPolicy _survival = new(config, catalog);

    // Q57A: a row observed absent from the real Cache stays unavailable for this field instance.
    // Do not oscillate back to base every few seconds hoping it changed. Territory change and a
    // future Force Initialize/manual re-initialize explicitly clear this latch.
    private readonly HashSet<byte> _cacheUnavailableForInstance = [];

    public int CacheUnavailableForInstanceCount => _cacheUnavailableForInstance.Count;

    public void ResetInstanceCacheAvailability() => _cacheUnavailableForInstance.Clear();

    /// <summary>Whether any currently-low category still has an untested/unavailable-not-latched Cache candidate.</summary>
    public bool CanAttemptRoutineRefill(SupplyStatus supply)
    {
        if (!supply.InventoryAvailable || !supply.NeedsRoutineRefill)
            return false;

        var box = _inventory.Read();
        if (!box.Available) return false;
        if (NeedsRefill(box, Candidates("Resistance Potion Kit"), _config.SupplyPotionKitLow)
            && HasUnlatchedCandidate(Candidates("Resistance Potion Kit")))
            return true;
        if (NeedsRefill(box, Candidates("Resistance Reraiser"), _config.SupplyReraiserLow)
            && HasUnlatchedCandidate(Candidates("Resistance Reraiser")))
            return true;
        if (NeedsRefill(box, MainHealCandidates(), _config.SupplyMainHealLow)
            && HasUnlatchedCandidate(MainHealCandidates()))
            return true;
        if (NeedsRefill(box, Candidates("Lost Manawall"), _config.SupplyEmergencyDefenseLow)
            && HasUnlatchedCandidate(Candidates("Lost Manawall")))
            return true;

        return false;
    }

    /// <summary>Whether a critical no-recovery state still has an untested Cache recovery candidate.</summary>
    public bool CanAttemptCriticalRecovery(SupplyStatus supply)
        => !supply.CriticalNoRecovery
           || HasUnlatchedCandidate(Candidates("Resistance Potion Kit").Where(_survival.AutoUseAllowed))
           || HasUnlatchedCandidate(MainHealCandidates().Where(_survival.AutoUseAllowed));

    /// <summary>
    /// Inspect the Cache only after the real MYCItemBox was opened. Zero-count rows are latched
    /// unavailable for this field instance; positive stock never clears an earlier latch.
    /// </summary>
    public CacheSupplyInspection InspectCacheAndLatch(SupplyStatus supply)
    {
        var box = _inventory.Read();
        if (!box.Available)
            return new CacheSupplyInspection(false, true, true, ["Lost Finds Cache state unavailable while open"]);

        List<string> reasons = [];
        var needPotion = supply.CriticalNoRecovery
                         || NeedsRefill(box, Candidates("Resistance Potion Kit"), _config.SupplyPotionKitLow);
        var needHeal = supply.CriticalNoRecovery
                       || NeedsRefill(box, MainHealCandidates(), _config.SupplyMainHealLow);
        var needReraiser = NeedsRefill(box, Candidates("Resistance Reraiser"), _config.SupplyReraiserLow);
        var needDefense = NeedsRefill(box, Candidates("Lost Manawall"), _config.SupplyEmergencyDefenseLow);

        var potionAvailable = needPotion
            && InspectCandidates(box, Candidates("Resistance Potion Kit"), "Resistance Potion Kit", reasons);
        var healAvailable = needHeal
            && InspectCandidates(box, MainHealCandidates(), "main self-heal", reasons);
        var reraiserAvailable = needReraiser
            && InspectCandidates(box, Candidates("Resistance Reraiser"), "Resistance Reraiser", reasons);
        var defenseAvailable = needDefense
            && InspectCandidates(box, Candidates("Lost Manawall"), "Lost Manawall", reasons);

        var canImproveRoutine =
            (needPotion && potionAvailable)
            || (needHeal && healAvailable)
            || (needReraiser && reraiserAvailable)
            || (needDefense && defenseAvailable);
        var canRecoverCritical = !supply.CriticalNoRecovery
            || HasUsableCacheStock(box, Candidates("Resistance Potion Kit"))
            || HasUsableCacheStock(box, MainHealCandidates());

        return new CacheSupplyInspection(true, canImproveRoutine, canRecoverCritical, reasons);
    }

    public SupplyStatus Evaluate()
    {
        var box = _inventory.Read();
        if (!box.Available)
            return new SupplyStatus(false, false, false, 0, 0, 0, 0, ["Lost Finds Cache/Holster state unavailable"]);

        var potion = Count(box, "Resistance Potion Kit");
        var reraiser = Count(box, "Resistance Reraiser");
        var manawall = Count(box, "Lost Manawall") + LoadedCharges("Lost Manawall");
        var heals = MainHealUnits(box);

        List<string> reasons = [];
        if (NeedsRefill(box, Candidates("Resistance Potion Kit"), _config.SupplyPotionKitLow))
            reasons.Add($"Potion Kit bring-enabled reserve is below {_config.SupplyPotionKitLow}");
        if (NeedsRefill(box, Candidates("Resistance Reraiser"), _config.SupplyReraiserLow))
            reasons.Add($"Reraiser bring-enabled reserve is below {_config.SupplyReraiserLow}");
        if (NeedsRefill(box, MainHealCandidates(), _config.SupplyMainHealLow))
            reasons.Add($"main heal bring-enabled reserve is below {_config.SupplyMainHealLow}");
        if (NeedsRefill(box, Candidates("Lost Manawall"), _config.SupplyEmergencyDefenseLow))
            reasons.Add($"emergency defense bring-enabled reserve is below {_config.SupplyEmergencyDefenseLow}");

        // User requirement: only the complete absence of both Potion Kit reserve/effect and a
        // usable self-heal justifies abandoning the current skirmish immediately for supply.
        var noPotionProtection = potion <= 0 && !_survival.HasAutoPotion();
        var noHeal = heals <= 0;
        var critical = noPotionProtection && noHeal;
        if (critical)
            reasons.Insert(0, "no Potion Kit protection and no usable self-heal remain");

        return new SupplyStatus(
            true,
            reasons.Count > 0,
            critical,
            potion,
            reraiser,
            heals,
            manawall,
            reasons);
    }

    private int MainHealUnits(LostItemBoxSnapshot box)
    {
        // "Units" are deliberately conservative: ready charges in a currently loaded action are
        // exact; each holster reserve counts as one future unit because MYCTemporaryItem does not
        // expose how many duty charges that future load will materialise as. This may request a
        // refill early, but can never falsely report five heals that do not exist.
        var total = 0;
        foreach (var entry in MainHealCandidates(forRefill: false))
        {
            total += box.HolsterCount(entry.RowId);
            total += LoadedCharges(entry);
        }
        return total;
    }

    private IEnumerable<LostActionCatalog.Entry> MainHealCandidates(bool forRefill = true)
    {
        string[] priority = _survival.Role == SurvivalRole.Healer
            ? ["Lost Full Cure"]
            : ["Lost Cure IV", "Lost Cure II", "Lost Cure III", "Lost Cure"];

        foreach (var name in priority)
        {
            var entry = _survival.Find(name);
            if (entry is { } e && (forRefill ? _survival.BringAllowed(e) : _survival.AutoUseAllowed(e)))
                yield return e;
        }
    }

    private IEnumerable<LostActionCatalog.Entry> Candidates(params string[] names)
    {
        foreach (var name in names)
        {
            var entry = _survival.Find(name);
            if (entry is { } e && _survival.BringAllowed(e))
                yield return e;
        }
    }

    private bool HasUnlatchedCandidate(IEnumerable<LostActionCatalog.Entry> candidates)
    {
        foreach (var entry in candidates)
            if (!_cacheUnavailableForInstance.Contains(entry.RowId))
                return true;
        return false;
    }

    private bool HasUsableCacheStock(LostItemBoxSnapshot box, IEnumerable<LostActionCatalog.Entry> candidates)
        => candidates.Any(entry => _survival.AutoUseAllowed(entry)
            && !_cacheUnavailableForInstance.Contains(entry.RowId) && box.CacheCount(entry.RowId) > 0);

    private static bool NeedsRefill(LostItemBoxSnapshot box, IEnumerable<LostActionCatalog.Entry> candidates, int low)
    {
        var any = false;
        var reserve = 0;
        foreach (var entry in candidates)
        {
            any = true;
            // Refilling counts what is held, even if automatic use is currently disabled.
            // Critical recovery instead counts only usable stock; these are different questions.
            reserve += box.HolsterCount(entry.RowId);
            if (entry.IsAction) reserve += LoadedCharges(entry);
        }
        return any && reserve < Math.Max(0, low);
    }

    private bool InspectCandidates(
        LostItemBoxSnapshot box,
        IEnumerable<LostActionCatalog.Entry> candidates,
        string label,
        ICollection<string> reasons)
    {
        var available = false;
        var sawCandidate = false;
        foreach (var entry in candidates)
        {
            sawCandidate = true;
            if (_cacheUnavailableForInstance.Contains(entry.RowId))
                continue;

            if (box.CacheCount(entry.RowId) > 0)
            {
                available = true;
                continue;
            }

            _cacheUnavailableForInstance.Add(entry.RowId);
        }

        if (!available)
            reasons.Add(sawCandidate
                ? $"{label}: no usable Cache stock in this instance"
                : $"{label}: no bring-enabled candidate for current role");
        return available;
    }

    private int Count(LostItemBoxSnapshot box, string englishName)
    {
        var entry = _survival.Find(englishName);
        return entry is { } e && _survival.AutoUseAllowed(e) ? box.HolsterCount(e.RowId) : 0;
    }

    private int LoadedCharges(string englishName)
    {
        var entry = _survival.Find(englishName);
        return entry is { } e && _survival.AutoUseAllowed(e) ? LoadedCharges(e) : 0;
    }

    private static int LoadedCharges(LostActionCatalog.Entry entry)
    {
        var total = 0;
        for (var slot = 0; slot < DutyActions.SlotCount; slot++)
        {
            var duty = DutyActions.Read(slot);
            if (duty.ActionId == entry.ActionId)
                total += duty.CurCharges;
        }
        return total;
    }
}
