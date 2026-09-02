from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SUPPLY = ROOT / "Automation/SupplyManager.cs"
RECOVERY = ROOT / "Automation/SupplyRecoveryDriver.cs"
CTRL = ROOT / "Automation/BozjaController.cs"
PLUGIN = ROOT / "Plugin.cs"


def replace_once(path: Path, old: str, new: str, marker: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if marker in text:
        print(f"{path.relative_to(ROOT)}: already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"{path.relative_to(ROOT)} anchor missing: {old[:180]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path.relative_to(ROOT)}: patched ({marker})")


replace_once(
    SUPPLY,
    """public readonly record struct SupplyStatus(\n    bool InventoryAvailable,\n    bool NeedsRoutineRefill,\n    bool CriticalNoRecovery,\n    int PotionKits,\n    int Reraisers,\n    int MainHealUnits,\n    int EmergencyDefenseUnits,\n    IReadOnlyList<string> Reasons)\n{\n    public bool SafeToContinue => InventoryAvailable && !CriticalNoRecovery;\n}\n\n""",
    """public readonly record struct SupplyStatus(\n    bool InventoryAvailable,\n    bool NeedsRoutineRefill,\n    bool CriticalNoRecovery,\n    int PotionKits,\n    int Reraisers,\n    int MainHealUnits,\n    int EmergencyDefenseUnits,\n    IReadOnlyList<string> Reasons)\n{\n    public bool SafeToContinue => InventoryAvailable && !CriticalNoRecovery;\n}\n\n/// <summary>Read-only result of inspecting the actual Lost Finds Cache after reaching it.</summary>\npublic readonly record struct CacheSupplyInspection(\n    bool InventoryAvailable,\n    bool CanImproveRoutine,\n    bool CanRecoverCritical,\n    IReadOnlyList<string> Reasons);\n\n""",
    "public readonly record struct CacheSupplyInspection(",
)

replace_once(
    SUPPLY,
    """    private readonly LostItemBoxInventory _inventory = inventory;\n    private readonly SurvivalPolicy _survival = new(config, catalog);\n\n    public SupplyStatus Evaluate()\n""",
    """    private readonly LostItemBoxInventory _inventory = inventory;\n    private readonly SurvivalPolicy _survival = new(config, catalog);\n\n    // Q57A: a row observed absent from the real Cache stays unavailable for this field instance.\n    // Do not oscillate back to base every few seconds hoping it changed. Territory change and a\n    // future Force Initialize/manual re-initialize explicitly clear this latch.\n    private readonly HashSet<byte> _cacheUnavailableForInstance = [];\n\n    public int CacheUnavailableForInstanceCount => _cacheUnavailableForInstance.Count;\n\n    public void ResetInstanceCacheAvailability() => _cacheUnavailableForInstance.Clear();\n\n    /// <summary>Whether any currently-low category still has an untested/unavailable-not-latched Cache candidate.</summary>\n    public bool CanAttemptRoutineRefill(SupplyStatus supply)\n    {\n        if (!supply.InventoryAvailable || !supply.NeedsRoutineRefill)\n            return false;\n\n        if (supply.PotionKits < Math.Max(0, _config.SupplyPotionKitLow)\n            && HasUnlatchedCandidate(Candidates(\"Resistance Potion Kit\")))\n            return true;\n        if (supply.Reraisers < Math.Max(0, _config.SupplyReraiserLow)\n            && HasUnlatchedCandidate(Candidates(\"Resistance Reraiser\")))\n            return true;\n        if (supply.MainHealUnits < Math.Max(0, _config.SupplyMainHealLow)\n            && HasUnlatchedCandidate(MainHealCandidates()))\n            return true;\n        if (supply.EmergencyDefenseUnits < Math.Max(0, _config.SupplyEmergencyDefenseLow)\n            && HasUnlatchedCandidate(Candidates(\"Lost Manawall\")))\n            return true;\n\n        return false;\n    }\n\n    /// <summary>Whether a critical no-recovery state still has an untested Cache recovery candidate.</summary>\n    public bool CanAttemptCriticalRecovery(SupplyStatus supply)\n        => !supply.CriticalNoRecovery\n           || HasUnlatchedCandidate(Candidates(\"Resistance Potion Kit\"))\n           || HasUnlatchedCandidate(MainHealCandidates());\n\n    /// <summary>\n    /// Inspect the Cache only after the real MYCItemBox was opened. Zero-count rows are latched\n    /// unavailable for this field instance; positive stock never clears an earlier latch.\n    /// </summary>\n    public CacheSupplyInspection InspectCacheAndLatch(SupplyStatus supply)\n    {\n        var box = _inventory.Read();\n        if (!box.Available)\n            return new CacheSupplyInspection(false, true, true, [\"Lost Finds Cache state unavailable while open\"]);\n\n        List<string> reasons = [];\n        var needPotion = supply.CriticalNoRecovery\n                         || supply.PotionKits < Math.Max(0, _config.SupplyPotionKitLow);\n        var needHeal = supply.CriticalNoRecovery\n                       || supply.MainHealUnits < Math.Max(0, _config.SupplyMainHealLow);\n        var needReraiser = supply.Reraisers < Math.Max(0, _config.SupplyReraiserLow);\n        var needDefense = supply.EmergencyDefenseUnits < Math.Max(0, _config.SupplyEmergencyDefenseLow);\n\n        var potionAvailable = needPotion\n            && InspectCandidates(box, Candidates(\"Resistance Potion Kit\"), \"Resistance Potion Kit\", reasons);\n        var healAvailable = needHeal\n            && InspectCandidates(box, MainHealCandidates(), \"main self-heal\", reasons);\n        var reraiserAvailable = needReraiser\n            && InspectCandidates(box, Candidates(\"Resistance Reraiser\"), \"Resistance Reraiser\", reasons);\n        var defenseAvailable = needDefense\n            && InspectCandidates(box, Candidates(\"Lost Manawall\"), \"Lost Manawall\", reasons);\n\n        var canImproveRoutine =\n            (needPotion && potionAvailable)\n            || (needHeal && healAvailable)\n            || (needReraiser && reraiserAvailable)\n            || (needDefense && defenseAvailable);\n        var canRecoverCritical = !supply.CriticalNoRecovery || potionAvailable || healAvailable;\n\n        return new CacheSupplyInspection(true, canImproveRoutine, canRecoverCritical, reasons);\n    }\n\n    public SupplyStatus Evaluate()\n""",
    "public CacheSupplyInspection InspectCacheAndLatch(SupplyStatus supply)",
)

# Refactor the heal candidate enumeration once so evaluation and Cache inspection cannot drift by role.
replace_once(
    SUPPLY,
    """    private int MainHealUnits(LostItemBoxSnapshot box)\n    {\n        // \"Units\" are deliberately conservative: ready charges in a currently loaded action are\n        // exact; each holster reserve counts as one future unit because MYCTemporaryItem does not\n        // expose how many duty charges that future load will materialise as. This may request a\n        // refill early, but can never falsely report five heals that do not exist.\n        string[] priority = _survival.Role == SurvivalRole.Healer\n            ? [\"Lost Full Cure\"]\n            : [\"Lost Cure IV\", \"Lost Cure II\", \"Lost Cure III\", \"Lost Cure\"];\n\n        var total = 0;\n        foreach (var name in priority)\n        {\n            var entry = _survival.Find(name);\n            if (entry is not { } e || !_survival.BringAllowed(e))\n                continue;\n            total += box.HolsterCount(e.RowId);\n            total += LoadedCharges(e);\n        }\n        return total;\n    }\n\n""",
    """    private int MainHealUnits(LostItemBoxSnapshot box)\n    {\n        // \"Units\" are deliberately conservative: ready charges in a currently loaded action are\n        // exact; each holster reserve counts as one future unit because MYCTemporaryItem does not\n        // expose how many duty charges that future load will materialise as. This may request a\n        // refill early, but can never falsely report five heals that do not exist.\n        var total = 0;\n        foreach (var entry in MainHealCandidates())\n        {\n            total += box.HolsterCount(entry.RowId);\n            total += LoadedCharges(entry);\n        }\n        return total;\n    }\n\n    private IEnumerable<LostActionCatalog.Entry> MainHealCandidates()\n    {\n        string[] priority = _survival.Role == SurvivalRole.Healer\n            ? [\"Lost Full Cure\"]\n            : [\"Lost Cure IV\", \"Lost Cure II\", \"Lost Cure III\", \"Lost Cure\"];\n\n        foreach (var name in priority)\n        {\n            var entry = _survival.Find(name);\n            if (entry is { } e && _survival.BringAllowed(e))\n                yield return e;\n        }\n    }\n\n    private IEnumerable<LostActionCatalog.Entry> Candidates(params string[] names)\n    {\n        foreach (var name in names)\n        {\n            var entry = _survival.Find(name);\n            if (entry is { } e && _survival.BringAllowed(e))\n                yield return e;\n        }\n    }\n\n    private bool HasUnlatchedCandidate(IEnumerable<LostActionCatalog.Entry> candidates)\n    {\n        foreach (var entry in candidates)\n            if (!_cacheUnavailableForInstance.Contains(entry.RowId))\n                return true;\n        return false;\n    }\n\n    private bool InspectCandidates(\n        LostItemBoxSnapshot box,\n        IEnumerable<LostActionCatalog.Entry> candidates,\n        string label,\n        ICollection<string> reasons)\n    {\n        var available = false;\n        var sawCandidate = false;\n        foreach (var entry in candidates)\n        {\n            sawCandidate = true;\n            if (_cacheUnavailableForInstance.Contains(entry.RowId))\n                continue;\n\n            if (box.CacheCount(entry.RowId) > 0)\n            {\n                available = true;\n                continue;\n            }\n\n            _cacheUnavailableForInstance.Add(entry.RowId);\n        }\n\n        if (!available)\n            reasons.Add(sawCandidate\n                ? $\"{label}: no usable Cache stock in this instance\"\n                : $\"{label}: no bring-enabled candidate for current role\");\n        return available;\n    }\n\n""",
    "private bool InspectCandidates(",
)

replace_once(
    RECOVERY,
    """    public bool Active { get; private set; }\n    public string Status { get; private set; } = string.Empty;\n""",
    """    public bool Active { get; private set; }\n    public bool CacheOpened => _cacheOpened;\n    public string Status { get; private set; } = string.Empty;\n""",
    "public bool CacheOpened => _cacheOpened;",
)

# Critical stock: first visit if untested, but once the real Cache proves both recovery paths absent,
# stop instead of shuttling back forever.
replace_once(
    CTRL,
    """        if (supply.InventoryAvailable && supply.CriticalNoRecovery)\n        {\n            RunCriticalSupplyRecovery(supply);\n            return;\n        }\n""",
    """        if (supply.InventoryAvailable && supply.CriticalNoRecovery)\n        {\n            if (!_supplies.CanAttemptCriticalRecovery(supply))\n            {\n                Stop(\"回復手段が完全に枯渇し、Lost Finds Cacheにも補充候補がないため停止しました。\");\n                DiagnosticsRecorder.Warning(\"回復手段が完全に枯渇し、Cache在庫もないため自動周回を停止しました。\");\n                return;\n            }\n\n            RunCriticalSupplyRecovery(supply);\n            return;\n        }\n""",
    "_supplies.CanAttemptCriticalRecovery(supply)",
)

replace_once(
    CTRL,
    """        if (supply.InventoryAvailable && supply.NeedsRoutineRefill)\n        {\n            var finishingCurrentSkirmish = _lastObjective.Kind == ObjectiveKind.Fate\n""",
    """        if (supply.InventoryAvailable\n            && supply.NeedsRoutineRefill\n            && _supplies.CanAttemptRoutineRefill(supply))\n        {\n            var finishingCurrentSkirmish = _lastObjective.Kind == ObjectiveKind.Fate\n""",
    "&& _supplies.CanAttemptRoutineRefill(supply)",
)

replace_once(
    CTRL,
    """        _supplyRecovery.Tick();\n        Status = _supplyRecovery.Status;\n\n        if (supply.Reasons.Count > 0)\n            Svc.Log.Debug($\"[BozjaBuddyReborn] Critical supply recovery: {string.Join(\"; \", supply.Reasons)}.\");\n""",
    """        _supplyRecovery.Tick();\n        Status = _supplyRecovery.Status;\n\n        if (_supplyRecovery.CacheOpened)\n        {\n            var cache = _supplies.InspectCacheAndLatch(supply);\n            if (cache.InventoryAvailable && !cache.CanRecoverCritical)\n            {\n                Svc.Log.Warning(\"[BozjaBuddyReborn] Critical recovery stock is absent from Lost Finds Cache for this field instance; stopping instead of retrying base trips.\");\n                Stop(\"回復手段が完全に枯渇し、Lost Finds Cacheにも補充候補がないため停止しました。\");\n                DiagnosticsRecorder.Warning(\"回復手段が完全に枯渇し、Cache在庫もないため自動周回を停止しました。\");\n                return;\n            }\n        }\n\n        if (supply.Reasons.Count > 0)\n            Svc.Log.Debug($\"[BozjaBuddyReborn] Critical supply recovery: {string.Join(\"; \", supply.Reasons)}.\");\n""",
    "cache.InventoryAvailable && !cache.CanRecoverCritical",
)

replace_once(
    CTRL,
    """        _supplyRecovery.Tick(critical: false);\n        Status = _supplyRecovery.Status;\n\n        if (supply.Reasons.Count > 0)\n            Svc.Log.Debug($\"[BozjaBuddyReborn] Routine supply recovery: {string.Join(\"; \", supply.Reasons)}.\");\n""",
    """        _supplyRecovery.Tick(critical: false);\n        Status = _supplyRecovery.Status;\n\n        if (_supplyRecovery.CacheOpened)\n        {\n            var cache = _supplies.InspectCacheAndLatch(supply);\n            if (cache.InventoryAvailable && !cache.CanImproveRoutine)\n            {\n                Svc.Log.Warning(\"[BozjaBuddyReborn] Requested routine survival stock is absent from Lost Finds Cache for this field instance; suppressing repeated base trips.\");\n                _supplyRecovery.Reset();\n                Status = \"Lost Finds Cacheにも現在不足している補給候補がないため、このインスタンスでは再補給を繰り返さず周回を続けます。\";\n                DiagnosticsRecorder.Warning(\"Cache在庫がない補給候補をこのインスタンスでは再試行しません。\");\n                return;\n            }\n        }\n\n        if (supply.Reasons.Count > 0)\n            Svc.Log.Debug($\"[BozjaBuddyReborn] Routine supply recovery: {string.Join(\"; \", supply.Reasons)}.\");\n""",
    "cache.InventoryAvailable && !cache.CanImproveRoutine",
)

replace_once(
    PLUGIN,
    """        _holster.Reset();\n        Mount.Reset();\n        _controller.InvalidateIdleSpots();\n""",
    """        _holster.Reset();\n        _supplies.ResetInstanceCacheAvailability();\n        Mount.Reset();\n        _controller.InvalidateIdleSpots();\n""",
    "_supplies.ResetInstanceCacheAvailability();",
)
