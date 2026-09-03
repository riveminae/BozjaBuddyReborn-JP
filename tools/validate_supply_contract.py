from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def require(path: str, needle: str, why: str) -> None:
    text = read(path)
    if needle not in text:
        raise SystemExit(f"SUPPLY CONTRACT FAIL [{path}]: {why}\nmissing: {needle!r}")
    print(f"ok: {path}: {why}")


def forbid(path: str, needle: str, why: str) -> None:
    text = read(path)
    if needle in text:
        raise SystemExit(f"SUPPLY CONTRACT FAIL [{path}]: {why}\nforbidden: {needle!r}")
    print(f"ok: {path}: {why}")


# Read-only cache inspection. Counts may be read, but no server-backed count may be assigned here.
require(
    "Automation/SupplyManager.cs",
    "public CacheSupplyInspection InspectCacheAndLatch(SupplyStatus supply)",
    "real Cache stock is inspected before declaring a refill impossible",
)
require(
    "Automation/SupplyManager.cs",
    "box.CacheCount(entry.RowId) > 0",
    "cache availability is based on server-backed read-only counts",
)
require(
    "Automation/SupplyManager.cs",
    "private readonly HashSet<byte> _cacheUnavailableForInstance = [];",
    "zero-stock rows are latched for the current field instance",
)
require(
    "Automation/SupplyManager.cs",
    "public void ResetInstanceCacheAvailability() => _cacheUnavailableForInstance.Clear();",
    "instance stock latch has an explicit reset operation",
)
forbid(
    "Automation/SupplyManager.cs",
    "ItemBoxData->",
    "supply policy does not directly mutate MYC ItemBoxData",
)

# Routine shortage: visit once while candidates are untested, then suppress repeated base trips if
# the opened Cache proves every currently-needed candidate absent.
require(
    "Automation/SupplyManager.cs",
    "public bool CanAttemptRoutineRefill(SupplyStatus supply)",
    "routine refill arbitration consults the instance absence latch",
)
require(
    "Automation/BozjaController.cs",
    "&& _supplies.CanAttemptRoutineRefill(supply)",
    "controller does not repeat routine cache trips for latched-out stock",
)
require(
    "Automation/BozjaController.cs",
    "cache.InventoryAvailable && !cache.CanImproveRoutine",
    "opened Cache can suppress an impossible routine refill",
)
require(
    "Automation/BozjaController.cs",
    "このインスタンスでは再補給を繰り返さず周回を続けます。",
    "routine out-of-stock behavior continues farming instead of looping",
)

# Critical no-recovery: one Cache inspection is allowed, but if neither Potion Kit nor a usable
# self-heal can be supplied, stop rather than continue a zero-recovery unattended run.
require(
    "Automation/SupplyManager.cs",
    "public bool CanAttemptCriticalRecovery(SupplyStatus supply)",
    "critical recovery respects latched Cache absence",
)
require(
    "Automation/BozjaController.cs",
    "if (!_supplies.CanAttemptCriticalRecovery(supply))",
    "controller stops before repeating an impossible critical cache trip",
)
require(
    "Automation/BozjaController.cs",
    "cache.InventoryAvailable && !cache.CanRecoverCritical",
    "opened Cache can prove critical recovery impossible",
)
require(
    "Automation/BozjaController.cs",
    "回復手段が完全に枯渇し、Lost Finds Cacheにも補充候補がないため停止しました。",
    "critical out-of-stock behavior is fail-closed",
)

# Latch lifetime: changing field instance resets it, and the settings UI provides an explicit manual
# retry control for the same instance after the user replenishes Cache stock by hand.
require(
    "Plugin.cs",
    "_supplies.ResetInstanceCacheAvailability();",
    "territory change clears instance-specific Cache absence",
)
require(
    "Windows/ConfigWindow.cs",
    "Cache在庫なし記録をクリアして再確認",
    "manual same-instance Cache recheck is available",
)
require(
    "Windows/ConfigWindow.cs",
    "_supplies.ResetInstanceCacheAvailability();",
    "manual recheck actually clears the latch",
)

# Q55/Q56: each survival category exposes independently configurable low and target counts.
for marker, why in [
    ("SupplyPotionKitLow", "Potion Kit low watermark is configurable"),
    ("SupplyPotionKitTarget", "Potion Kit refill target is configurable"),
    ("SupplyReraiserLow", "Reraiser low watermark is configurable"),
    ("SupplyReraiserTarget", "Reraiser refill target is configurable"),
    ("SupplyMainHealLow", "main-heal low watermark is configurable"),
    ("SupplyMainHealTarget", "main-heal refill target is configurable"),
    ("SupplyEmergencyDefenseLow", "Manawall low watermark is configurable"),
    ("SupplyEmergencyDefenseTarget", "Manawall refill target is configurable"),
]:
    require("Windows/ConfigWindow.cs", marker, why)

require(
    "Windows/ConfigWindow.cs",
    "target = Math.Max(target, low);",
    "raising a low watermark cannot leave its target below the low value",
)
require(
    "Windows/ConfigWindow.cs",
    "target = Math.Clamp(nextTarget, low, 99);",
    "refill target cannot be configured below its low watermark",
)

print("survival supply static contract: PASS")
