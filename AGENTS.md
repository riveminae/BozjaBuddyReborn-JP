# AGENTS.md — BozjaBuddyReborn-JP v1.1 implementation rules

This repository has a user-approved v1.1 specification. Do not infer product behavior from the current partial implementation.

## Mandatory read order before changing code

Read these files before making any implementation decision:

1. `docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md` — authoritative product requirements.
2. `docs/design/BozjaBuddyReborn-JP_v1.1.0_detailed-design.md` — authoritative architecture and state-machine design.
3. `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_execution-plan.md` — task sequencing and packet boundaries.
4. `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_progress.md` — implementation status only. This is NOT a behavior specification.
5. Existing source code — implementation evidence only. Existing behavior must not override the documents above.

If these conflict, precedence is exactly:

`requirements > detailed design > execution plan > progress tracker > existing code`

Do not silently resolve a conflict in favor of existing code. Preserve the requirement and refactor the code.

## Non-negotiable v1.1 behavior

- Primary target is long-running autonomous farming inside Bozjan Southern Front (920) and Zadnor (975).
- `Start` is the normal single user action. The plugin should perform dependency checks, maintenance, required initialization, activity selection, travel, combat, CE handling, recovery, and continue the loop.
- Do not automate entering Bozja/Zadnor from outside the field. Starting outside must be rejected. Leaving the supported field while Running must stop automation.
- Preserve the existing working BBR skirmish combat flow. Do not rewrite combat merely to make another subsystem cleaner.
- CE registration is remote. Register immediately when an eligible CE opens, continue the current skirmish while the lottery runs, and Commence immediately when selected except for the explicitly specified critical-survival-stock exception.
- At most one CE registration at a time.
- Enabled Castrum/Dalriada large-scale content has the highest CE priority, including over Resistance Relic-target CEs.
- Resistance Relic farming starts only from an explicit user-selected target. Do not invent/select a first target automatically. After that target completes, automatic continuation may select the next outstanding target under the rules in the requirements.
- When a Relic target is active and no matching event exists, do not run unrelated skirmishes; stage near the useful field aethernet instead.
- Navigation must follow the BOCCHI model as closely as practical. Reuse/vendored BOCCHI navigation source rather than re-inventing equivalent algorithms from scratch.
- Compare Direct, Walk→Aethernet→Walk, and Return→Aethernet→Walk using the BOCCHI cost model. Lifestream is optional; event travel must fall back quickly if unavailable.
- Ground navigation only. Do not reintroduce an AllowFlight user option.
- Enemy routing policy is fixed: I/II/III are not avoided; IV/V/★ are avoided; Unknown is treated as dangerous. ★ receives extra clearance.
- If dangerous enemies aggro during travel, continue fleeing rather than stopping to fight. Mounted travel must not trigger Lost Actions, attacks, heals, or other BBR actions that could intentionally dismount the player.
- Lost Action automation is survival-first. Tank/Healer/DPS thresholds, Essence priorities, Potion Kit, Reraiser, Manawall, Cure IV behavior, and bring/auto-use permission semantics are defined in the requirements and must not be replaced by generic heuristics.
- `持込` and `自動使用` are independent Lost Action permissions.
- Deep/rare survival Essences remain default `持込OFF / 自動使用OFF` even when they are highest in the priority table.
- Initialize is transactional. Never directly write server-backed Cache/Holster counts. Never guess undocumented callback values. If a safe transfer mechanism is not established, keep transfer as BLOCKED rather than faking completion.
- Critical survival depletion may interrupt an ordinary skirmish for Cache recovery. Routine low stock finishes an already-reached current skirmish before resupply. CE registration continues during supply movement.
- Death recovery rules are fixed: during an active CE do not camp-respawn; ordinary skirmish waits 30 seconds for raise; travel waits 10 seconds. TextAdvance is optional but required for autonomous acceptance/release behavior; restore its previous enabled state afterward.
- Required dependencies wait up to 60 seconds. Survival automation remains active where safe during that wait. Timeout follows the safe-stop policy; do not continue indefinitely.
- Lifestream is optional: event travel does not wait 30 seconds; nonurgent staging/supply may wait up to 30 seconds before direct fallback.
- During Running, reject only dialogs positively identified as social requests. Never click No on generic Yes/No dialogs just because they are visible.
- User-visible UI/status/errors are Japanese-fixed in this JP fork. Internal troubleshooting logs remain English where practical.
- The test branch uses `1.0.90.x`. Stable target is `1.1.0.0`.
- Do not merge to `main` automatically. Stable promotion requires the acceptance criteria and explicit user approval.

## Architecture rules

- `BozjaController` is an orchestrator, not a dumping ground for navigation, transfer protocol, dependency policy, social-dialog parsing, Relic planning, or Lost Action policy.
- Long operations must be state machines across ticks. Do not use blocking sleeps, busy loops, or synchronous waits on game-state changes.
- BBR-specific adapters should wrap vendored BOCCHI/Ocelot-derived logic rather than editing copied algorithms into unrelated controller code.
- Preserve current safety contracts in `tools/validate_v110_contract.py` unless the authoritative requirements explicitly require changing the invariant.
- Do not remove static/CI guards simply because they make a proposed implementation fail. Fix the implementation or update the guard only when the specification changed.

## Working method

- Work on `feat/bocchi-navigation`; do not push v1.1 work directly to `main`.
- Prefer one coherent task/packet per commit. Push frequently.
- Before coding, identify the requirement section and task packet the change satisfies.
- After coding, run/inspect the test workflow and update `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_progress.md` only with evidence-backed status.
- `DONE` means the implementation satisfies the relevant requirement, not merely that code exists or compiles.
- If public source/client structs cannot establish a safe game interaction, document the blocker and continue unrelated work rather than guessing.

## Before declaring a feature complete

Check all of the following:

1. Does behavior match the requirements document, not just current code?
2. Does the responsibility live in the module specified by the detailed design?
3. Did the change preserve existing proven skirmish combat behavior?
4. Did it avoid undocumented memory writes/guessed callbacks?
5. Does Debug and Release build pass?
6. Do static contracts and Japanese UI audit pass?
7. Is the progress tracker status supported by actual evidence?

If any answer is no, the feature is not complete.
