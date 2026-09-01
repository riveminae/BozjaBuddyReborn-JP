from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CTRL = ROOT / "Automation/BozjaController.cs"
PLUGIN = ROOT / "Plugin.cs"


def patch(path: Path, old: str, new: str, marker: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if marker in text:
        print(f"{path.relative_to(ROOT)}: already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"{path.relative_to(ROOT)} anchor missing: {old[:180]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path.relative_to(ROOT)}: patched ({marker})")


patch(
    CTRL,
    """    private readonly HolsterDriver _holster;\n    private readonly MultiboxLink _link;\n""",
    """    private readonly HolsterDriver _holster;\n    private readonly SupplyManager _supplies;\n    private readonly SupplyRecoveryDriver _supplyRecovery;\n    private readonly MultiboxLink _link;\n""",
    "private readonly SupplyRecoveryDriver _supplyRecovery;",
)

patch(
    CTRL,
    """        CombatApproach approach,\n        HolsterDriver holster,\n        MultiboxLink link,\n""",
    """        CombatApproach approach,\n        HolsterDriver holster,\n        SupplyManager supplies,\n        MultiboxLink link,\n""",
    "SupplyManager supplies,",
)

patch(
    CTRL,
    """        _approach = approach;\n        _holster = holster;\n        _link = link;\n""",
    """        _approach = approach;\n        _holster = holster;\n        _supplies = supplies;\n        _supplyRecovery = new SupplyRecoveryDriver(movement);\n        _link = link;\n""",
    "_supplyRecovery = new SupplyRecoveryDriver(movement);",
)

patch(
    CTRL,
    """        _selector.ClearRouteBlacklist();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n""",
    """        _selector.ClearRouteBlacklist();\n        _supplyRecovery.Reset();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n""",
    "_selector.ClearRouteBlacklist();\n        _supplyRecovery.Reset();",
)

patch(
    CTRL,
    """        _movement.Stop();\n        _director.ReleaseControl();\n        _deathRecovery.CancelAndRestore();\n""",
    """        _movement.Stop();\n        _director.ReleaseControl();\n        _supplyRecovery.Reset();\n        _deathRecovery.CancelAndRestore();\n""",
    "_director.ReleaseControl();\n        _supplyRecovery.Reset();",
)

# CE battle always wins: a character already deployed to a live CE must never leave it to refill.
# Immediately after that guard, critical supply preempts ordinary skirmish selection/travel.
patch(
    CTRL,
    """        if (current is { } ce && ce.IsLive)\n        {\n            RunEngagement(ce);\n            return;\n        }\n\n        // --- decide what to do next ----------------------------------------\n""",
    """        if (current is { } ce && ce.IsLive)\n        {\n            _supplyRecovery.Reset();\n            RunEngagement(ce);\n            return;\n        }\n\n        // Q54C/Q63A/Q109C: complete loss of both Potion Kit protection and a usable\n        // self-heal is the one supply state severe enough to abandon the current skirmish\n        // immediately. Registration keeps running above this block; if the lottery selects this\n        // box, SignUpRunner independently holds Commence until the same supply predicate clears.\n        var supply = _supplies.Evaluate();\n        if (supply.InventoryAvailable && supply.CriticalNoRecovery)\n        {\n            RunCriticalSupplyRecovery(supply);\n            return;\n        }\n\n        if (_supplyRecovery.Active)\n        {\n            Svc.Log.Information(\"[BozjaBuddyReborn] Critical survival supply recovered; returning to normal objective selection.\");\n            _supplyRecovery.Reset();\n        }\n\n        // --- decide what to do next ----------------------------------------\n""",
    "RunCriticalSupplyRecovery(supply);",
)

# Keep the recovery effect local and explicit. It deliberately does not claim to refill: the only
# unresolved primitive is still Cache<->Holster transfer, and SupplyRecoveryDriver stops at the
# cache window until that is implemented safely.
method_marker = "private void RunCriticalSupplyRecovery(SupplyStatus supply)"
text = CTRL.read_text(encoding="utf-8-sig")
if method_marker not in text:
    anchor = "    private void TickAutomaticCeRegistration()\n"
    if anchor not in text:
        raise RuntimeError("BozjaController insertion anchor missing for supply recovery method")
    method = r'''    private void RunCriticalSupplyRecovery(SupplyStatus supply)
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

        if (supply.Reasons.Count > 0)
            Svc.Log.Debug($"[BozjaBuddyReborn] Critical supply recovery: {string.Join("; ", supply.Reasons)}.");
    }

'''
    CTRL.write_text(text.replace(anchor, method + anchor, 1), encoding="utf-8")
    print("Automation/BozjaController.cs: critical supply recovery method added")
else:
    print("Automation/BozjaController.cs: critical supply recovery method already applied")

patch(
    PLUGIN,
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery, _dependencies);\n""",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _supplies, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery, _dependencies);\n""",
    "_holster, _supplies, _link",
)
