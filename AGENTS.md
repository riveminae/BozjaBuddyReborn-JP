# AGENTS.md

`main` is the stable branch. Do not implement BozjaBuddyReborn-JP v1.1 feature work directly on `main`.

For any v1.1 / BOCCHI-navigation / survival / CE / Relic / Lost Action / supply / recovery work:

1. Checkout `feat/bocchi-navigation` first.
2. Read that branch's root `AGENTS.md`.
3. Read `SPEC.md` and the authoritative documents under `docs/requirements`, `docs/design`, and `docs/implementation` on that branch.
4. Implement and validate there.
5. Do not merge back to `main` without explicit user approval and the documented acceptance evidence.

If a task was started from `main`, switching to the feature branch is part of the task; do not recreate the v1.1 design from the stable source tree.
