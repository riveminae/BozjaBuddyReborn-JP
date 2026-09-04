from __future__ import annotations

import re
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


def require_order(path: str, first: str, second: str, why: str) -> None:
    text = read(path)
    a = text.find(first)
    b = text.find(second)
    if a < 0 or b < 0 or a >= b:
        raise SystemExit(
            f"CONTRACT FAIL [{path}]: {why}\nexpected order: {first!r} before {second!r}"
        )
    print(f"ok: {path}: {why}")


def require_count(path: str, needle: str, expected: int, why: str) -> None:
    text = read(path)
    actual = text.count(needle)
    if actual != expected:
        raise SystemExit(
            f"CONTRACT FAIL [{path}]: {why}\n"
            f"expected {expected} occurrence(s), found {actual}: {needle!r}"
        )
    print(f"ok: {path}: {why}")


def method_body(path: str, signature: str) -> str:
    """Return one C# method body using balanced braces, ignoring braces inside strings/comments."""
    text = read(path)
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f"CONTRACT FAIL [{path}]: method signature not found: {signature!r}")
    brace = text.find("{", start + len(signature))
    if brace < 0:
        raise SystemExit(f"CONTRACT FAIL [{path}]: method body not found: {signature!r}")

    depth = 0
    i = brace
    state = "code"
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if state == "line":
            if ch == "\n":
                state = "code"
        elif state == "block":
            if ch == "*" and nxt == "/":
                state = "code"
                i += 1
        elif state == "string":
            if ch == "\\":
                i += 1
            elif ch == '"':
                state = "code"
        elif state == "char":
            if ch == "\\":
                i += 1
            elif ch == "'":
                state = "code"
        elif ch == "/" and nxt == "/":
            state = "line"
            i += 1
        elif ch == "/" and nxt == "*":
            state = "block"
            i += 1
        elif ch == '"':
            state = "string"
        elif ch == "'":
            state = "char"
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace + 1 : i]
        i += 1

    raise SystemExit(f"CONTRACT FAIL [{path}]: unbalanced method body: {signature!r}")


def compact_code(text: str) -> str:
    """Collapse layout so contract checks describe control flow rather than line/token placement."""
    return re.sub(r"\s+", " ", text).strip()


def require_activity_relic_behavior() -> None:
    """Evaluate selector/planner control-flow facts against the frozen v1.1 scenarios."""
    registration = compact_code(method_body(
        "Automation/TargetSelector.cs",
        "public CeSnapshot? SelectRegistration(IReadOnlyList<CeSnapshot> engagements, bool deterministic)",
    ))
    eligibility = compact_code(method_body(
        "Automation/TargetSelector.cs", "private bool IsEligible(CeSnapshot ce)"
    ))
    filter_body = compact_code(method_body(
        "Automation/TargetSelector.cs",
        "private bool PassesFarmFilter(ObjectiveKind kind, uint id, Vector3 position, DropActivity activity)",
    ))
    permission = compact_code(method_body(
        "Automation/TargetSelector.cs",
        "public bool StillPermitted(ObjectiveKind kind, uint id, Vector3 position)",
    ))
    select_body = compact_code(method_body(
        "Automation/TargetSelector.cs",
        "public Choice Select(IReadOnlyList<CeSnapshot> engagements, bool deterministic)",
    ))
    select_fate = compact_code(method_body(
        "Automation/TargetSelector.cs", "private Choice SelectFate(bool deterministic)"
    ))
    relic_tick = compact_code(method_body(
        "Relic/RelicFarmCoordinator.cs", "public RelicFarmUpdate Tick(uint territory)"
    ))
    resolve = compact_code(method_body(
        "Automation/BozjaController.cs", "private SharedObjective ResolveObjective()"
    ))
    idle = compact_code(method_body(
        "Automation/BozjaController.cs", "private void RunIdle(string reason)"
    ))

    facts = {
        "large_enabled_gate": "if (largeScale && !_config.EngageLargeScale) return false;" in eligibility,
        "large_bypasses_relic": "if (!largeScale && !PassesFarmFilter(" in eligibility,
        "large_absolute_rank": "var rank = largeScale ? int.MinValue : PriorityRank(ce.EventId);" in registration,
        "zero_target_noop": "if (currentId == 0) return RelicFarmUpdate.None;" in relic_tick,
        "incomplete_target_noop": (
            "if (RelicTracker.ItemCount(currentId) < material.Required) return RelicFarmUpdate.None;"
            in relic_tick
        ),
        "continuation_after_guard": (
            relic_tick.find("RelicTracker.ItemCount(currentId) < material.Required")
            < relic_tick.find("_config.FarmMaterialItemId = next;")
        ),
        "ce_only_blocks_fates": (
            "var wantFate = _config.DoFates && requiredActivity != DropActivity.CriticalEngagement;"
            in select_body
        ),
        "fates_apply_relic_filter": (
            "if (!PassesFarmFilter(ObjectiveKind.Fate, fate.FateId, fate.Position, DropActivity.Skirmish))"
            in select_fate
            and "continue;" in select_fate[
                select_fate.find("if (!PassesFarmFilter(ObjectiveKind.Fate") :
                select_fate.find("if (!PassesFarmFilter(ObjectiveKind.Fate") + 180
            ]
        ),
        "unknown_relic_fails_closed": (
            "if (region == FieldRegionId.Unknown) return FarmTarget == null && !_config.SkipUnknownRegions;"
            in filter_body
        ),
        "shared_objective_rechecks_filter": (
            "return fromHost.IsSet && _selector.StillPermitted(" in resolve
        ),
        "large_shared_objective_bypasses_relic": (
            "if (kind == ObjectiveKind.CriticalEngagement && _catalog.IsLargeScale((ushort)id))"
            in permission
            and "return _config.EngageLargeScale;" in permission
        ),
        "idle_prefers_aethernet": (
            "if (TryGetAethernetIdleSpot(territory, region" in idle
            and idle.find("TryGetAethernetIdleSpot(territory, region")
            < idle.find("TryGetIdleSpot(territory, region")
        ),
    }

    scenarios = {
        "enabled Castrum/Dalriada outranks Relic and configured CE priority": (
            facts["large_enabled_gate"]
            and facts["large_bypasses_relic"]
            and facts["large_absolute_rank"]
        ),
        "no first Relic target is invented without explicit selection": facts["zero_target_noop"],
        "automatic continuation occurs only after the selected material completes": (
            facts["incomplete_target_noop"] and facts["continuation_after_guard"]
        ),
        "an unmatched active Relic target cannot run an unrelated skirmish": (
            facts["ce_only_blocks_fates"]
            and facts["fates_apply_relic_filter"]
            and facts["unknown_relic_fails_closed"]
            and facts["shared_objective_rechecks_filter"]
            and facts["large_shared_objective_bypasses_relic"]
        ),
        "no matching objective attempts aethernet staging before generic idle fallback": (
            facts["idle_prefers_aethernet"]
        ),
    }

    failed = [name for name, passed in scenarios.items() if not passed]
    if failed:
        raise SystemExit("ACTIVITY/RELIC CONTRACT FAIL: " + "; ".join(failed))
    for name in scenarios:
        print(f"ok: activity/relic behavior: {name}")


# JP fork visible-language invariant.
require("Localization.cs", "public static bool Ja => true;", "visible UI is Japanese-fixed")

# Lost Action settings are independent concerns. Party support must remain reachable even when
# ordinary combat auto-use is disabled; the previous single page returned early and hid it.
require(
    "Windows/ConfigWindow.cs",
    'ImGui.BeginTabBar("##bbr_lostaction_tabs")',
    "Lost Action settings use nested category tabs",
)
require(
    "Windows/ConfigWindow.cs",
    'if (ImGui.BeginTabItem("パーティ支援"))',
    "party-support settings have an independent subtab",
)
require(
    "Windows/ConfigWindow.cs",
    "DrawLostActionAutoUseSettings();",
    "ordinary Lost Action auto-use is isolated in its own section",
)

# Q27A removed the user-facing flight option and v1.1 field travel must always be ground routing.
# Configuration intentionally keeps a dead compatibility field so old 1.0.x JSON can deserialize
# without losing shape during migration; the contract is that nothing in UI or Movement uses it.
forbid("Windows/ConfigWindow.cs", "AllowFlight", "flight setting is not exposed in v1.1 UI")
require(
    "Automation/Movement.cs",
    "MoveCloseTo(legTarget, legRange, false, NavClient.Travel)",
    "field travel always requests a ground path",
)

# BOCCHI-derived field traversal invariants. Departure identity follows BOCCHI's camp -> 45y graph
# snap -> nearest-node rule. The departure walk may be measured with vnavmesh.Nav.PathfindCancelable,
# but telemetry must stay bounded/non-blocking and must drain before a real movement path can replace it.
require(
    "Vendor/BOCCHI/NavigationConstants.cs",
    "public const float GraphSnapRadius = 45f;",
    "BOCCHI graph snap radius is retained",
)
require(
    "Vendor/BOCCHI/TraversalCandidate.cs",
    "public readonly record struct TraversalCandidate(float TotalCost);",
    "direct, aethernet, and Return routes share BOCCHI candidate costs",
)
require(
    "Automation/FieldTravelRouter.cs",
    "new TraversalCandidate(direct)",
    "direct travel enters the same candidate comparison as traversal routes",
)
require(
    "Automation/FieldTravelRouter.cs",
    "ResolveDepartureNode(nodes, start)",
    "aethernet planning resolves one BOCCHI-style departure node",
)
require(
    "External/NavmeshIpc.cs",
    '"vnavmesh.Nav.PathfindCancelable"',
    "route-cost telemetry uses the cancelable vnavmesh query",
)
require(
    "Automation/NavPathCostCache.cs",
    "private const int MaxPending = 1;",
    "only one route-cost path query may be pending",
)
require(
    "Automation/NavPathCostCache.cs",
    "public bool HasPending",
    "router can observe telemetry drain completion",
)
require(
    "Automation/NavPathCostCache.cs",
    "public bool CancelAllPending()",
    "router can cancel telemetry without touching movement",
)
forbid(
    "Automation/NavPathCostCache.cs",
    "SimpleMove",
    "route-cost cache must never own movement",
)
forbid(
    "Automation/NavPathCostCache.cs",
    "GetAwaiter().GetResult",
    "route-cost telemetry must never synchronously block the framework tick",
)
require(
    "Automation/FieldTravelRouter.cs",
    "private const long PathCostPlanningWaitMs = 750;",
    "new routes wait at most 750ms before cancelling slow cost telemetry",
)
require(
    "Automation/FieldTravelRouter.cs",
    "_pathCosts.Estimate(",
    "aethernet candidate can consume measured departure walk cost",
)
require(
    "Automation/FieldTravelRouter.cs",
    "private bool _planningDiscardResult;",
    "stale planning results are explicitly discardable",
)
require(
    "Automation/FieldTravelRouter.cs",
    "_planningTerritory = territory;",
    "path-cost query keeps the territory that owns its cache key",
)
require(
    "Automation/FieldTravelRouter.cs",
    "_pathCosts.CancelAllPending();",
    "Stop/replan requests telemetry cancellation",
)
require(
    "Automation/FieldTravelRouter.cs",
    "if (_pathCosts.HasPending)",
    "replanning drains old telemetry before new movement",
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
    "var noPotionProtection = potion <= 0 && !_survival.HasAutoPotion();",
    "critical supply checks Potion Kit reserve/effect",
)
require(
    "Automation/SupplyManager.cs",
    "var noHeal = heals <= 0;",
    "critical supply checks usable self-heal absence",
)
require(
    "Automation/SupplyManager.cs",
    "var critical = noPotionProtection && noHeal;",
    "critical supply requires both recovery paths absent",
)
require(
    "Automation/SupplyManager.cs",
    "return new SupplyStatus(false, false, false",
    "unknown inventory does not falsely block CE Commence",
)

# Supply diagnostics are evaluated on the framework tick and cached on the controller. ImGui must
# never start its own MYC inventory read while drawing; the UI consumes only that cached snapshot.
require(
    "Automation/BozjaController.cs",
    "public SupplyStatus SupplyStatus { get; private set; }",
    "controller exposes cached survival supply status",
)
require(
    "Automation/BozjaController.cs",
    "SupplyStatus = _supplies.Evaluate();",
    "supply status is refreshed on the framework tick",
)
require(
    "Automation/BozjaController.cs",
    "var supply = SupplyStatus;",
    "supply arbitration reuses the cached framework-tick evaluation",
)
require(
    "Windows/MainWindow.cs",
    "var supply = _controller.SupplyStatus;",
    "main UI reads controller-cached supply status",
)
require(
    "Windows/MainWindow.cs",
    "生存在庫: Potion Kit",
    "main UI exposes survival stock at a glance",
)
forbid(
    "Windows/MainWindow.cs",
    "new LostItemBoxInventory",
    "ImGui draw path must not read MYC inventory directly",
)

# Critical depletion interrupts ordinary skirmishes, but not an already-deployed CE. Remote CE
# registration must also continue before recovery takes movement ownership. SupplyRecoveryDriver
# is explicitly navigation+interaction only: no direct AgentMycItemBox memory manipulation.
require("Automation/BozjaController.cs", "RunCriticalSupplyRecovery(supply);", "critical depletion enters cache recovery")
require("Automation/SupplyRecoveryDriver.cs", "Interactables.LostFindsCache", "recovery targets the real Lost Finds Cache object")
require("Automation/SupplyRecoveryDriver.cs", "waitForOptionalDependencies: true", "cache recovery uses nonurgent Lifestream policy")
forbid("Automation/SupplyRecoveryDriver.cs", "ItemBoxData", "recovery driver does not mutate MYC inventory memory")
require_order(
    "Automation/BozjaController.cs",
    "TickAutomaticCeRegistration();",
    "RunCriticalSupplyRecovery(supply);",
    "CE registration runs before critical supply recovery",
)
require_order(
    "Automation/BozjaController.cs",
    "if (current is { } ce && ce.IsLive)",
    "RunCriticalSupplyRecovery(supply);",
    "a live CE outranks supply recovery",
)

# Q54C: low-but-not-critical stock finishes the skirmish already reached, then goes to the cache
# before selecting another objective. Merely travelling toward a fresh skirmish does not qualify
# as "finish current skirmish": the committed latch is required.
require("Automation/BozjaController.cs", "RunRoutineSupplyRecovery(supply);", "routine low stock enters cache recovery")
require(
    "Automation/BozjaController.cs",
    "var finishingCurrentSkirmish = _lastObjective.Kind == ObjectiveKind.Fate",
    "routine refill explicitly identifies the current skirmish",
)
require(
    "Automation/BozjaController.cs",
    "&& _committed",
    "routine refill waits only for a skirmish already reached",
)
require(
    "Automation/BozjaController.cs",
    "&& IsObjectiveStillWorthDoing(_lastObjective);",
    "routine refill stops waiting when that skirmish ends",
)
require(
    "Automation/BozjaController.cs",
    "_supplyRecovery.Tick(critical: false);",
    "routine recovery uses the noncritical supply trip",
)
require_order(
    "Automation/BozjaController.cs",
    "RunCriticalSupplyRecovery(supply);",
    "RunRoutineSupplyRecovery(supply);",
    "critical depletion outranks routine low-watermark refill",
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

# Social request rejection is intentionally narrower than ordinary SelectYesno handling. The
# guard is enabled only by the live Running predicate, binds a queued decision to the same addon
# instance, and accepts either the PartyInvite agent identity or a prompt containing both a social
# subject and incoming-request wording. Rejections are history-only; they do not raise a Dalamud
# notification. These are static boundaries around a live-tested feature, not proof of prompt text.
require(
    "Plugin.cs",
    "new SocialRequestGuard(_config, () => _controller.Running)",
    "social rejection is wired to the controller Running state",
)
require_count(
    "Automation/SocialRequestGuard.cs",
    "!_config.RejectSocialRequestsWhileRunning || !_running()",
    2,
    "Running and user permission are checked both when classifying and immediately before rejection",
)
require(
    "Automation/SocialRequestGuard.cs",
    "agent->ConfirmAddonId == addon->Id",
    "party invitations require the PartyInvite agent to own the same dialog",
)
require(
    "Automation/SocialRequestGuard.cs",
    "addon->Id != _pendingAddonId",
    "queued social rejection cannot transfer to a replacement Yes/No dialog",
)
require(
    "Automation/SocialRequestGuard.cs",
    "if (subject == null)",
    "generic prompts without a social subject remain untouched",
)
require(
    "Automation/SocialRequestGuard.cs",
    "if (!incoming)",
    "social wording alone is insufficient without incoming-request wording",
)
require_count(
    "Automation/SocialRequestGuard.cs",
    "master.No();",
    1,
    "identified social requests have one guarded rejection sink",
)
require(
    "Automation/SocialRequestGuard.cs",
    "DiagnosticsRecorder.Warning($\"{kind}を自動拒否しました。\");",
    "successful rejection is recorded in warning history",
)
forbid(
    "Automation/SocialRequestGuard.cs",
    "Svc.NotificationManager",
    "social-request rejection does not raise a Dalamud notification",
)

# Death recovery has fixed safety semantics rather than configurable heuristics: a CE corpse must
# never be released while the CE is live, ordinary skirmishes receive a 30-second raise window,
# travel/idle receives 10 seconds, and any TextAdvance state changed for unattended recovery must
# be restored. These checks intentionally live on the dedicated driver, not the controller, so the
# orchestrator cannot become the owner of the timing or optional-dependency policy.
require(
    "Automation/DeathRecoveryDriver.cs",
    "private const long SkirmishRaiseWaitMs = 30_000;",
    "skirmish deaths retain the fixed 30-second raise window",
)
require(
    "Automation/DeathRecoveryDriver.cs",
    "private const long TravelRaiseWaitMs = 10_000;",
    "travel and idle deaths retain the fixed 10-second raise window",
)
require(
    "Automation/DeathRecoveryDriver.cs",
    "var waitMs = diedDuringSkirmish ? SkirmishRaiseWaitMs : TravelRaiseWaitMs;",
    "death context selects the matching fixed raise window",
)
require(
    "Automation/DeathRecoveryDriver.cs",
    "if (criticalEngagementLive)",
    "live CE deaths enter the no-release branch",
)
require_order(
    "Automation/DeathRecoveryDriver.cs",
    "if (criticalEngagementLive)",
    "GeneralActions.CastReturn()",
    "the live-CE no-release decision precedes every Return cast",
)
require(
    "Automation/DeathRecoveryDriver.cs",
    "_textAdvance.RestoreOriginalState();",
    "death recovery restores the user's prior TextAdvance state",
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

# Activity/Relic selection is an ordered behavior, not the presence of isolated marker strings.
# Extract complete method bodies, derive the relevant control-flow facts, and evaluate the frozen
# positive/negative scenarios as a group so bypasses and reordered guards fail the same gate.
require_activity_relic_behavior()

# Test repo update semantics. A fresh test version on every non-bot run prevents silent ZIP
# replacement under the same AssemblyVersion.
require(
    ".github/workflows/test-build.yml",
    '$version = "1.0.90.${{ github.run_number }}"',
    "test builds auto-increment 1.0.90.x",
)

# Keep the detailed supply/cache invariants in a focused module while running them in this same
# pre-compile contract step. Importing the module executes its read-only static checks.
import validate_supply_contract  # run dedicated supply invariants

print("v1.1 static contract: PASS")
