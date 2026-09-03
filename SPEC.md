# BozjaBuddyReborn-JP v1.1 — approved specification summary

This is a concise index of the user-approved v1.1 behavior. Full normative details are in `docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md`.

If this summary and the full requirements differ, the full requirements win.

## Goal

Inside Bozjan Southern Front and Zadnor, the normal user flow is:

`Start → maintain survival loadout if needed → choose useful activity → travel efficiently → fight skirmish / remotely register for CE → Commence when selected → recover/maintain → repeat`

The intended result is long-running autonomous field farming with minimal user intervention.

## Hard requirements

### Supported field / lifecycle

- Support territory 920 (Bozjan Southern Front) and 975 (Zadnor).
- Do not automate field entry from outside.
- Start outside the supported fields: reject.
- Leave the supported field while Running: stop.
- Preserve the existing working BBR skirmish combat behavior.

### CE

- CE registration is remote; do not travel to a CE just to register.
- When an eligible CE opens, register immediately from the current location.
- While lottery is pending, continue the current skirmish and normal farming.
- If selected, Commence immediately, except when survival stock is critically empty as specified below.
- Only one CE registration at a time.
- CE priority:
  1. enabled Castrum/Dalriada large-scale content,
  2. explicit Resistance Relic target CE,
  3. configured `PriorityEngagements`,
  4. other eligible CE.
- After large-scale content, default is maintenance then resume normal farming; configurable off.

### Resistance Relic

- First farm target is always explicitly chosen by the user.
- Never auto-pick the first Relic target.
- When selected material completes, auto-continue to the next outstanding material if enabled.
- If the current activity is still valid for the next material, continue it; otherwise abandon/reselect immediately.
- Do not automate changing territory to chase the next material.
- While a Relic target is active, unrelated skirmishes are not filler. If no matching activity exists, stage near the useful aethernet and wait.
- Default stop mode is infinite farming; optional stop-at-material / stop-at-stage / continue modes exist.

### Navigation / BOCCHI

- Reuse BOCCHI navigation concepts/source directly where practical; do not invent a loosely similar system from scratch.
- Compare Direct, Walk→Aethernet→Walk, and Return→Aethernet→Walk routes using one cost model.
- BOCCHI defaults: MaxDirectWalkDistance 80, AethernetHopCost 50, ReturnCost 40; Advanced may configure them.
- Return routing default ON and configurable.
- Lifestream is optional.
- Event travel: if Lifestream is unavailable/fails, fall back quickly to direct vnavmesh.
- Nonurgent staging/supply: may wait up to 30 seconds for Lifestream, then direct fallback.
- Ground navigation only; no user-facing flight option.
- Same leg may re-path three times; then re-plan alternate route; if still impossible blacklist that spawn until it disappears.
- If all activities fail, go to a useful staging point and wait for new spawns.
- Manual movement input yields navigation; resume after about 3 seconds without movement input.
- Manual target changes do not pause/override combat targeting logic.

### Enemy avoidance

- I / II / III: do not avoid; pulling them while travelling is acceptable.
- IV / V / ★: avoid their detection footprint.
- Unknown rank: treat as dangerous.
- ★ gets extra configurable clearance.
- If a dangerous enemy is accidentally pulled during travel, keep moving/flee; do not stop to fight merely because of aggro.
- While mounted, BBR must not fire Lost Actions, attacks, heals, or other actions that intentionally dismount the player.
- Once naturally on foot, survival actions may be used.

### Lost Actions / survival

- Survival first, not damage optimization.
- HP thresholds:
  - Tank: normal heal 55%, emergency 30%.
  - Healer: normal heal 70%, emergency 45%.
  - DPS: normal heal 65%, emergency 40%.
- Per Lost Action there are two independent permissions: `持込` and `自動使用`.
- Bring OFF + auto-use ON is valid: do not add it during Initialize, but use it if already present.
- Bring ON + auto-use OFF is valid: may stock it but never automatically consume it.
- Default survival candidates are bring ON / auto-use ON.
- Deep/rare survival Essences are default bring OFF / auto-use OFF.
- Tank Essence priority: Deep Bloodsucker → Bloodsucker → Deep Guardian → Guardian.
- DPS: Deep Beast → Beast → Deep Platebearer/Veteran → Platebearer/Veteran.
- Healer: Deep Templar → Templar → Deep Veteran → Veteran.
- Existing Essence is respected by default; overwrite OFF.
- Potion Kit is maintained automatically when appropriate.
- Reraiser is applied on the first crossing into the role emergency HP range, not spammed continuously.
- Lost Manawall is an emergency defensive candidate for every role.
- Lost Cure IV is preferred recovery for Tank/DPS where usable.
- Fill near Holster weight 99 with a balanced survival package, not one item monopolizing capacity.

### Initialize / Cache

- Normal UI action is Start; Initialize runs automatically only when needed.
- Advanced has Force Initialize.
- Initialize is transactional: snapshot → preflight → return → build role preset → load → Duty Actions/Essence → verify.
- On failure after modification, roll back to the pre-init snapshot.
- If rollback fails, stop instead of farming with a corrupted/empty Holster.
- Never directly modify server-backed Cache/Holster counts in memory.
- Never guess undocumented callback values.
- If a safe transfer mechanism cannot be proven, that transfer stays BLOCKED rather than being faked.

### Resupply

- Default low-water marks: Potion Kit 2, Reraiser 1, main heal ~5 uses, emergency defense ~1 set.
- Refill to preset target counts, differential top-up only.
- Normal low stock: finish an already-reached current skirmish, then resupply before selecting another.
- Critical state = Potion Kit protection absent AND usable self-heal absent: interrupt ordinary skirmish and resupply immediately.
- CE registration continues during resupply.
- If CE is selected while critical recovery is completely absent, minimum survival refill may delay Commence; otherwise Commence immediately.
- Cache out-of-stock entries are remembered for the current instance so the plugin does not loop back repeatedly.

### Death recovery

- During an active CE, never camp-respawn while the CE is still active. Remain dead and wait for a raise until CE ends.
- Skirmish death: wait 30 seconds for raise, then camp respawn.
- Travel death: wait 10 seconds, then camp respawn.
- TextAdvance is optional for normal farming but needed for autonomous acceptance/release behavior.
- If TextAdvance is installed but disabled, temporarily enable it for recovery then restore the prior OFF state.
- If autonomous recovery is impossible because TextAdvance is unavailable, stop rather than pretending recovery succeeded.

### Dependencies / social requests

- vnavmesh, Rotation Solver Reborn, and BossMod are required.
- Required dependency loss: wait up to 60 seconds; safe survival automation may continue during the wait.
- On timeout use safe-stop behavior; do not wait forever.
- Lifestream remains optional.
- During Running, reject positively identified social requests (party/CWPT/alliance/friend/LS/CWLS/trade etc.).
- Do not reject arbitrary generic Yes/No dialogs.

### UI / diagnostics / release

- User-visible UI, status, errors, and tooltips are Japanese-fixed in this JP fork.
- Troubleshooting logs remain English where practical.
- Main status should expose current state, objective, next action/route, HP/role, survival stock, CE state, and relevant dependency state compactly.
- Diagnostics include state history, warnings, dependencies, route, inventory state, and privacy-safe copy-to-clipboard.
- Debug overlay is optional/default OFF.
- Test versions are `1.0.90.x`; stable target is `1.1.0.0`.
- `main` must not be auto-merged. Stable promotion requires acceptance testing and explicit user approval.
