from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/BozjaController.cs"
text = P.read_text(encoding="utf-8-sig")

marker = "RunRoutineSupplyRecovery(supply);"
if marker not in text:
    old = """        if (_supplyRecovery.Active)\n        {\n            Svc.Log.Information(\"[BozjaBuddyReborn] Critical survival supply recovered; returning to normal objective selection.\");\n            _supplyRecovery.Reset();\n        }\n\n        // --- decide what to do next ----------------------------------------\n"""
    new = """        // Routine low-watermark refill never abandons a skirmish that we have already\n        // reached and committed to. Once that skirmish ends, or if we had only been travelling\n        // toward a fresh one, supply wins before another objective is selected. CE registration\n        // continues above this block and a selected CE is still free to Commence immediately\n        // because SignUpRunner only holds Commence for CriticalNoRecovery.\n        if (supply.InventoryAvailable && supply.NeedsRoutineRefill)\n        {\n            var finishingCurrentSkirmish = _lastObjective.Kind == ObjectiveKind.Fate\n                                           && _committed\n                                           && IsObjectiveStillWorthDoing(_lastObjective);\n            if (!finishingCurrentSkirmish)\n            {\n                RunRoutineSupplyRecovery(supply);\n                return;\n            }\n        }\n\n        if (_supplyRecovery.Active && !supply.NeedsRoutineRefill)\n        {\n            Svc.Log.Information(\"[BozjaBuddyReborn] Survival supply recovered above low-water marks; returning to normal objective selection.\");\n            _supplyRecovery.Reset();\n        }\n\n        // --- decide what to do next ----------------------------------------\n"""
    if old not in text:
        raise RuntimeError("BozjaController routine-supply arbitration anchor missing")
    text = text.replace(old, new, 1)

    anchor = "    private void TickAutomaticCeRegistration()\n"
    if anchor not in text:
        raise RuntimeError("BozjaController routine-supply method insertion anchor missing")
    method = r'''    private void RunRoutineSupplyRecovery(SupplyStatus supply)
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

        if (supply.Reasons.Count > 0)
            Svc.Log.Debug($"[BozjaBuddyReborn] Routine supply recovery: {string.Join("; ", supply.Reasons)}.");
    }

'''
    text = text.replace(anchor, method + anchor, 1)
    P.write_text(text, encoding="utf-8")
    print("Automation/BozjaController.cs: routine low-watermark recovery wired")
else:
    print("Automation/BozjaController.cs: routine low-watermark recovery already applied")
