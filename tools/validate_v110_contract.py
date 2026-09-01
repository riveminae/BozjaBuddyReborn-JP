from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def require(path: str, needle: str, why: str) -> None:
    text = read(path)
    if needle not in text:
        raise SystemExit(f"CONTRACT FAIL [{path}]: {why}\nmissing: {needle!r}")
    print(f"ok: {path}: {why}")


def forbid(path: str, needle: str, why: str) -> None:
    text = read(path)
    if needle in text:
        raise SystemExit(f"CONTRACT FAIL [{path}]: {why}\nforbidden: {needle!r}")
    print(f"ok: {path}: {why}")


# JP fork visible-language invariant.
require("Localization.cs", "public static bool Ja => true;", "visible UI is Japanese-fixed")

# Q27A removed the user-facing flight option and v1.1 field travel must always be ground routing.
# Configuration intentionally keeps a dead compatibility field so old 1.0.x JSON can deserialize
# without losing shape during migration; the contract is that nothing in UI or Movement uses it.
forbid("Windows/ConfigWindow.cs", "AllowFlight", "flight setting is not exposed in v1.1 UI")
require(
    "Automation/Movement.cs",
    "MoveCloseTo(legTarget, legRange, false, NavClient.Travel)",
    "field travel always requests a ground path",
)

# Mounted travel must never be dismounted by survival automation.
require("Automation/HolsterDriver.cs", "if (Mount.IsMounted)", "mounted Lost Action guard exists")
require("Automation/HolsterDriver.cs", "TickTravelSurvival", "on-foot travel survival path exists")

# CE recruitment safety. These markers protect against the API15 regression that replaced the
# proven button event path with EventList/ReceiveEvent guesses and against Register->Withdraw
# double-clicks while the label is settling.
require(
    "Automation/SignUpRunner.cs",
    "using var eventData = EventData.ForNormalTarget(ownerNode, addon);",
    "CE click uses concrete event data",
)
require(
    "Automation/SignUpRunner.cs",
    "using var inputData = InputData.Empty();",
    "CE click uses concrete input data",
)
require(
    "Automation/SignUpRunner.cs",
    "_clickSettleUntilMs = Environment.TickCount64 + ClickSettleMs;",
    "Register/Withdraw/Commence settle interlock exists",
)
require(
    "Automation/SignUpRunner.cs",
    "HoldCommenceForCriticalSupply()",
    "critical-supply Commence gate exists",
)
forbid(
    "Automation/SignUpRunner.cs",
    "AtkEventManager.EventList",
    "removed API15-incompatible event-list click must not return",
)
forbid(
    "Automation/SignUpRunner.cs",
    "ButtonTextNode?.",
    "native pointer null-conditional regression must not return",
)

# Q109C: registration remains immediate, but Commence is held only when recovery supply is
# confirmed completely empty. Unknown inventory is deliberately fail-open inside SupplyManager.
require(
    "Plugin.cs",
    "new SignUpRunner(() => !_supplies.Evaluate().CriticalNoRecovery)",
    "CE Commence is wired to critical supply evaluation",
)
require(
    "Automation/SupplyManager.cs",
    "var critical = noPotionProtection && noRecoverableHeal;",
    "critical supply means Potion Kit protection and self-heal are both absent",
)
require(
    "Automation/SupplyManager.cs",
    "return new SupplyStatus(false, false, false",
    "unknown inventory does not falsely block CE Commence",
)

# Required dependency recovery must retain the safe-stop path and survival automation while
# waiting. Exact timers are implementation details; these are the architectural invariants.
require(
    "Automation/BozjaController.cs",
    "_holster.Tick(inCombat: Svc.Condition[ConditionFlag.InCombat]);",
    "survival automation remains active during required-dependency wait",
)
require(
    "Automation/BozjaController.cs",
    "_safeStop.Tick(Svc.Condition[ConditionFlag.InCombat])",
    "required-dependency timeout enters safe stop",
)

# Dangerous-enemy routing is strength-based, with unknowns failing safe.
require("Automation/AggroAvoidance.cs", "strength.Dangerous", "field-rank danger classification is used")
require("Game/EnemyStrengthResolver.cs", "FieldEnemyStrength.Unknown", "unknown field rank remains representable")

# AGPL/provenance invariants introduced by direct BOCCHI reuse.
license_text = read("LICENSE").lstrip()
if not license_text.startswith("GNU AFFERO GENERAL PUBLIC LICENSE"):
    raise SystemExit("CONTRACT FAIL [LICENSE]: repository root must remain AGPL-3.0")
print("ok: LICENSE: root remains AGPL-3.0")
require(
    "Vendor/BOCCHI/GeneralActions.cs",
    "SPDX-License-Identifier: AGPL-3.0-only",
    "vendored Return helper carries AGPL SPDX",
)
require(
    "THIRD-PARTY-NOTICES.md",
    "KanoNoUta/BOCCHI",
    "maintenance-fork provenance is retained",
)

# Test repo update semantics. A fresh test version on every non-bot run prevents silent ZIP
# replacement under the same AssemblyVersion.
require(
    ".github/workflows/test-build.yml",
    '$version = "1.0.90.${{ github.run_number }}"',
    "test builds auto-increment 1.0.90.x",
)

print("v1.1 static contract: PASS")
