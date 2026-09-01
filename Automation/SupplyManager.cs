using System;
using System.Collections.Generic;
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
        if (potion < Math.Max(0, _config.SupplyPotionKitLow))
            reasons.Add($"Potion Kit reserve {potion} < {_config.SupplyPotionKitLow}");
        if (reraiser < Math.Max(0, _config.SupplyReraiserLow))
            reasons.Add($"Reraiser reserve {reraiser} < {_config.SupplyReraiserLow}");
        if (heals < Math.Max(0, _config.SupplyMainHealLow))
            reasons.Add($"main heal reserve {heals} < {_config.SupplyMainHealLow}");
        if (manawall < Math.Max(0, _config.SupplyEmergencyDefenseLow))
            reasons.Add($"emergency defense reserve {manawall} < {_config.SupplyEmergencyDefenseLow}");

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
        string[] priority = _survival.Role == SurvivalRole.Healer
            ? ["Lost Full Cure"]
            : ["Lost Cure IV", "Lost Cure II", "Lost Cure III", "Lost Cure"];

        var total = 0;
        foreach (var name in priority)
        {
            var entry = _survival.Find(name);
            if (entry is not { } e || !_survival.BringAllowed(e))
                continue;
            total += box.HolsterCount(e.RowId);
            total += LoadedCharges(e);
        }
        return total;
    }

    private int Count(LostItemBoxSnapshot box, string englishName)
    {
        var entry = _survival.Find(englishName);
        return entry is { } e && _survival.BringAllowed(e) ? box.HolsterCount(e.RowId) : 0;
    }

    private int LoadedCharges(string englishName)
    {
        var entry = _survival.Find(englishName);
        return entry is { } e ? LoadedCharges(e) : 0;
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
