using BozjaBuddyReborn;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using ECommons.DalamudServices;

try
{
    var checks = 0;
    void Check(bool ok, string label) { checks++; if (!ok) throw new Exception($"check {checks}: {label}"); }
    LostActionCatalog MakeCatalog()
    {
        var catalog = new LostActionCatalog { Entries = [
            new(1, 101, "ポーションキット", true), new(2, 102, "回復", false, true),
            new(3, 103, "深度の高い秘薬", true), new(4, 104, "支援", false, false, 104),
            new(5, 105, "リレイザー", true), new(6, 106, "防御"), new(7, 107, "全回復") ] };
        string[] names = ["Resistance Potion Kit", "Lost Cure IV", "Deep Essence of the Bloodsucker", "Lost Protect", "Resistance Reraiser", "Lost Manawall", "Lost Full Cure"];
        Svc.Data.Actions.Clear();
        for (var i = 0; i < names.Length; i++) Svc.Data.Actions[(uint)(101 + i)] = new((uint)(101 + i), new(names[i]));
        return catalog;
    }
    void Reset()
    {
        Svc.Objects.LocalPlayer = new(); Svc.Data.Role = 1; Svc.Data.ActionsAvailable = true;
        Mount.IsMounted = false; FieldState.Held = []; FieldState.Uses = 0; FieldState.LastRow = 0;
        DutyActions.Presses = 0; Array.Clear(DutyActions.Slots); LostActionStatuses.Active.Clear();
        PartyView.Members = [new(1, "試験用", Svc.Objects.LocalPlayer, true)];
    }
    Reset();
    var catalog = MakeCatalog();
    var config = new Configuration();
    var policy = new SurvivalPolicy(config, catalog);
    var potion = catalog.Entries[0]; var heal = catalog.Entries[1]; var deep = catalog.Entries[2]; var support = catalog.Entries[3];
    Check(policy.BringAllowed(potion) && policy.AutoUseAllowed(potion), "ordinary survival defaults on");
    Check(!policy.BringAllowed(deep) && !policy.AutoUseAllowed(deep), "Deep defaults off");
    foreach (var bring in new[] { false, true })
    foreach (var use in new[] { false, true })
    {
        config.LostActionBringPermissions[deep.RowId] = bring;
        config.LostActionAutoUsePermissions[deep.RowId] = use;
        Check(policy.BringAllowed(deep) == bring && policy.AutoUseAllowed(deep) == use, "four independent explicit permission combinations");
    }
    config = new(); Svc.Data.ActionsAvailable = false; policy = new(config, catalog);
    Check(!policy.BringAllowed(deep) && !policy.AutoUseAllowed(deep), "missing classification never enables a rare item");
    Svc.Data.ActionsAvailable = true;
    Check(policy.BringAllowed(potion) && !policy.BringAllowed(deep), "classification retries when data returns");
    var emptyCatalog = new LostActionCatalog(); policy = new(config, emptyCatalog);
    Check(policy.Find("Resistance Potion Kit") == null, "empty catalog is unavailable");
    emptyCatalog.Entries.Add(potion);
    Check(policy.Find("Resistance Potion Kit")?.RowId == potion.RowId, "empty catalog must not permanently seal the index");
    policy = new(config, catalog);
    Svc.Data.Actions.Remove(deep.ActionId);
    Check(!policy.AutoUseAllowed(deep), "missing row classification fails closed");
    MakeCatalog();
    Check(!policy.AutoUseAllowed(deep), "late Deep row remains off after reclassification");

    foreach (var bring in new[] { false, true })
    foreach (var use in new[] { false, true })
    {
        Reset(); config = new() { AutoSurvivalLostActions = false, AutoUseLostActions = true, AutoFireLostActions = true };
        config.AutoLostActions.Add(potion.RowId); config.LostActionBringPermissions[potion.RowId] = bring;
        config.LostActionAutoUsePermissions[potion.RowId] = use; FieldState.Held = [potion.RowId];
        new HolsterDriver(config, catalog).Tick(true);
        Check(FieldState.Uses == (use ? 1 : 0), "ordinary item use obeys only auto-use permission");
        Reset(); config.AutoSurvivalLostActions = true; config.AutoUseLostActions = false; FieldState.Held = [potion.RowId];
        new HolsterDriver(config, catalog).TickTravelSurvival();
        Check(FieldState.Uses == (use ? 1 : 0), "survival item use obeys only auto-use permission");
    }
    Reset(); config = new() { AutoSurvivalLostActions = false, AutoUseLostActions = true, AutoFireLostActions = true };
    config.AutoLostActions.Add(heal.RowId); FieldState.Held = [heal.RowId]; var driver = new HolsterDriver(config, catalog);
    driver.Tick(true); Check(FieldState.Uses == 1 && DutyActions.Presses == 0, "ordinary action loads before pressing");
    config.LostActionAutoUsePermissions[heal.RowId] = false; DutyActions.Slots[0] = new(heal.ActionId, 3);
    driver.Tick(true); Check(DutyActions.Presses == 0, "ordinary pending permission revocation stops the press");
    Reset(); config = new(); config.LostActionAutoUsePermissions[potion.RowId] = false;
    FieldState.Held = [heal.RowId]; Svc.Objects.LocalPlayer!.CurrentHp = 40; driver = new(config, catalog);
    driver.Tick(false); Check(FieldState.Uses == 1, "survival heal starts loading");
    config.LostActionAutoUsePermissions[heal.RowId] = false; DutyActions.Slots[0] = new(heal.ActionId, 3);
    driver.Tick(false); Check(DutyActions.Presses == 0, "survival pending permission revocation stops the press");

    Reset(); config = new(); config.PartySupportActions.Add(support.RowId);
    config.LostActionAutoUsePermissions[support.RowId] = false; FieldState.Held = [support.RowId];
    var party = new PartySupportDriver(config, catalog); party.Begin(); party.Tick();
    Check(FieldState.Uses == 0 && DutyActions.Presses == 0, "support respects explicit denial");
    Reset(); config.LostActionAutoUsePermissions[support.RowId] = true; FieldState.Held = [support.RowId];
    party = new(config, catalog); party.Begin(); party.Tick(); Check(FieldState.Uses == 1, "support permitted loading");
    config.LostActionAutoUsePermissions[support.RowId] = false; DutyActions.Slots[1] = new(support.ActionId, 3);
    party.Tick(); Check(DutyActions.Presses == 0, "support pending permission revocation stops the press");

    Reset(); config = new() { AutoUseLostActions = true, AutoFireLostActions = true };
    config.AutoLostActions.Add(potion.RowId); FieldState.Held = [potion.RowId]; Mount.IsMounted = true;
    driver = new(config, catalog); driver.Tick(true); driver.TickTravelSurvival();
    Check(FieldState.Uses == 0 && DutyActions.Presses == 0, "mounted ordinary and survival actions never run");
    config.PartySupportActions.Add(support.RowId); FieldState.Held = [support.RowId];
    party = new(config, catalog); party.Begin(); party.Tick();
    Check(FieldState.Uses == 0 && DutyActions.Presses == 0, "mounted support never loads or fires");

    foreach (var bring in new[] { false, true })
    foreach (var use in new[] { false, true })
    {
        Reset(); config = new(); config.LostActionBringPermissions[heal.RowId] = bring; config.LostActionAutoUsePermissions[heal.RowId] = use;
        var inventory = new LostItemBoxInventory { Snapshot = new(new Dictionary<byte,int>(), new Dictionary<byte,int> { [heal.RowId] = 2 }, 0, true) };
        var supplies = new SupplyManager(config, catalog, inventory); var status = supplies.Evaluate();
        Check(status.CriticalNoRecovery == !use && status.MainHealUnits == (use ? 2 : 0), "critical recovery counts usable held heals independently of bring");
        inventory.Snapshot = inventory.Snapshot with { Holster = new Dictionary<byte,int>() }; DutyActions.Slots[0] = new(heal.ActionId, 3);
        status = supplies.Evaluate();
        Check(status.CriticalNoRecovery == !use && status.MainHealUnits == (use ? 3 : 0), "loaded usable charges do not require bring permission");
    }
    Reset(); config = new(); var box = new LostItemBoxInventory(); var manager = new SupplyManager(config, catalog, box);
    LostActionStatuses.Active.Add(99);
    Check(!manager.Evaluate().CriticalNoRecovery, "existing auto-potion protection prevents critical depletion");
    LostActionStatuses.Active.Clear();
    config.LostActionAutoUsePermissions[potion.RowId] = false; config.LostActionAutoUsePermissions[heal.RowId] = false;
    box.Snapshot = new(new Dictionary<byte,int> { [potion.RowId] = 5, [heal.RowId] = 5 }, new Dictionary<byte,int>(), 0, true);
    var critical = manager.Evaluate();
    Check(critical.CriticalNoRecovery && !manager.CanAttemptCriticalRecovery(critical), "unusable cached items cannot recover an autonomous critical state");
    Check(!manager.InspectCacheAndLatch(critical).CanRecoverCritical, "cache inspection also rejects recovery through disabled auto-use");
    config.LostActionAutoUsePermissions[heal.RowId] = true; config.LostActionBringPermissions[heal.RowId] = false;
    Check(!manager.CanAttemptCriticalRecovery(manager.Evaluate()), "critical cache recovery also requires bring permission");

    foreach (byte role in new byte[] { 1, 2, 4 })
    foreach (var bring in new[] { false, true })
    foreach (var use in new[] { false, true })
    {
        Reset(); Svc.Data.Role = role;
        var roleHeal = role == 4 ? catalog.Entries[6] : heal;
        config = new(); config.LostActionBringPermissions[roleHeal.RowId] = bring;
        config.LostActionAutoUsePermissions[roleHeal.RowId] = use;
        Svc.Objects.LocalPlayer!.CurrentHp = 20; DutyActions.Slots[0] = new(roleHeal.ActionId, 3);
        driver = new(config, catalog); driver.Tick(false);
        Check(DutyActions.Presses == (use ? 1 : 0) && FieldState.Uses == 0,
            "loaded-only survival recovery uses charges with an empty holster and independent permissions");
        box = new(); manager = new(config, catalog, box);
        Check(manager.Evaluate().CriticalNoRecovery == !use, "loaded-only supply safety agrees with each role's actual firing policy");
        Reset(); config.AutoSurvivalLostActions = false; config.AutoUseLostActions = true; config.AutoFireLostActions = true;
        config.AutoLostActions.Add(roleHeal.RowId); DutyActions.Slots[0] = new(roleHeal.ActionId, 3);
        new HolsterDriver(config, catalog).Tick(true);
        Check(DutyActions.Presses == (use ? 1 : 0) && FieldState.Uses == 0, "ordinary loaded-only action also respects permission");
    }
    foreach (var change in new[] { "permission", "selection", "master", "fire", "mount" })
    {
        Reset(); config = new() { AutoSurvivalLostActions = false, AutoUseLostActions = true, AutoFireLostActions = true };
        config.AutoLostActions.Add(heal.RowId); FieldState.Held = [heal.RowId]; driver = new(config, catalog);
        driver.Tick(true); Check(FieldState.Uses == 1 && DutyActions.Presses == 0, "pending ordinary setup before revocation");
        DutyActions.Slots[0] = new(heal.ActionId, 3);
        switch (change)
        {
            case "permission": config.LostActionAutoUsePermissions[heal.RowId] = false; break;
            case "selection": config.AutoLostActions.Clear(); break;
            case "master": config.AutoUseLostActions = false; break;
            case "fire": config.AutoFireLostActions = false; break;
            case "mount": Mount.IsMounted = true; break;
        }
        driver.Tick(true); Check(DutyActions.Presses == 0, "each pending ordinary revocation path stops the charge");
    }
    Reset(); config = new() { AutoUseLostActions = true, AutoFireLostActions = true };
    config.LostActionAutoUsePermissions[potion.RowId] = false;
    config.AutoLostActions.Add(support.RowId); FieldState.Held = [heal.RowId]; Svc.Objects.LocalPlayer!.CurrentHp = 40;
    driver = new(config, catalog); driver.Tick(false); Check(FieldState.Uses == 1, "pending survival setup");
    config.AutoSurvivalLostActions = false; DutyActions.Slots[0] = new(heal.ActionId, 3);
    driver.Tick(true); Check(DutyActions.Presses == 0, "survival master off cannot fall through to ordinary pending fire");

    foreach (var change in new[] { "permission", "selection", "mount" })
    {
        Reset(); config = new(); config.PartySupportActions.Add(support.RowId); FieldState.Held = [support.RowId];
        party = new(config, catalog); party.Begin(); party.Tick(); Check(FieldState.Uses == 1, "pending party setup");
        DutyActions.Slots[1] = new(support.ActionId, 3);
        if (change == "permission") config.LostActionAutoUsePermissions[support.RowId] = false;
        else if (change == "selection") config.PartySupportActions.Clear();
        else Mount.IsMounted = true;
        party.Tick(); Check(DutyActions.Presses == 0, "pending party cancellation or mounting prevents firing");
    }
    Reset(); config = new(); config.PartySupportActions.Add(support.RowId); config.LostActionBringPermissions[support.RowId] = false;
    config.LostActionAutoUsePermissions[support.RowId] = true; DutyActions.Slots[1] = new(support.ActionId, 3);
    party = new(config, catalog); party.Begin(); party.Tick();
    Check(DutyActions.Presses == 1, "loaded party action does not require bring permission or a holster reserve");

    Reset(); config = new(); config.LostActionAutoUsePermissions[potion.RowId] = false;
    box = new() { Snapshot = new(new Dictionary<byte,int> { [1] = 10, [2] = 10, [5] = 10, [6] = 10 },
        new Dictionary<byte,int> { [1] = 5, [2] = 5, [5] = 5, [6] = 5 }, 0, true) };
    manager = new(config, catalog, box); var enough = manager.Evaluate();
    Check(!enough.NeedsRoutineRefill && !enough.CriticalNoRecovery, "held but auto-disabled stock must not cause repeated routine refill");
    Check(!manager.CanAttemptRoutineRefill(enough), "no refill attempt for full raw reserves");
    box.Snapshot = box.Snapshot with { Holster = new Dictionary<byte,int> { [2] = 5, [5] = 5, [6] = 5 } };
    var shortPotion = manager.Evaluate();
    Check(shortPotion.NeedsRoutineRefill && manager.CanAttemptRoutineRefill(shortPotion), "bring-only stock can still request routine refill");
    box.Snapshot = box.Snapshot with { Cache = new Dictionary<byte,int>() };
    Check(!manager.InspectCacheAndLatch(shortPotion).CanImproveRoutine, "missing Cache stock is latched");
    Check(!manager.CanAttemptRoutineRefill(shortPotion), "latched shortage does not cause another trip");
    manager.ResetInstanceCacheAvailability();
    Check(manager.CanAttemptRoutineRefill(shortPotion), "explicit instance reset permits recheck");
    box.Snapshot = box.Snapshot with { Available = false };
    Check(!manager.Evaluate().CriticalNoRecovery && !manager.Evaluate().InventoryAvailable, "unknown inventory is not confirmed zero");
    Console.WriteLine($"PASS: {checks} survival permission checks (production policy, drivers, supply; synthetic host).");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    Environment.ExitCode = 1;
}
