# BozjaBuddyReborn-JP

Japanese-localized fork of Bozja Buddy Reborn.

## v1.1 development source of truth

Before changing v1.1 code, read `AGENTS.md`.

The user-approved specification is maintained in this order:

1. `docs/requirements/BozjaBuddyReborn-JP_v1.1.0.md` — authoritative requirements
2. `docs/design/BozjaBuddyReborn-JP_v1.1.0_detailed-design.md` — architecture / detailed design
3. `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_execution-plan.md` — implementation packets
4. `docs/implementation/BozjaBuddyReborn-JP_v1.1.0_progress.md` — status/evidence only

`SPEC.md` is a short summary for quick orientation. It does not override the full requirements.

Development for v1.1 occurs on `feat/bocchi-navigation`. Do not treat the current partial implementation as the product specification, and do not merge to `main` without explicit user approval and acceptance evidence.
# XIVLauncher テスト版の導入

Dalamud設定の `Custom Plugin Repositories` に次のURLを一度だけ追加してください。

`https://raw.githubusercontent.com/riveminae/BozjaBuddyReborn-JP/feat/bocchi-navigation/pluginmaster-test.json`

その後、プラグイン一覧から **Bozja Buddy Reborn JP TEST** を通常どおり導入します。以後はDalamudの通常更新で新しいテスト版を受け取れます。ZIPを手動配置しないでください。Stable repositoryとは同時に有効化しないでください。
