from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")

# CI replays packets against the final composed branch. P12-03 upgrades this direct migration
# wiring to ConfigRecovery.Load, so either marker means the migration responsibility is present.
if "ConfigRecovery.Load(pluginInterface)" in text or "ConfigMigration.Apply(_config)" in text:
    print("Plugin.cs: config migration already wired")
else:
    anchor = "        _config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();\n"
    if anchor not in text:
        raise RuntimeError("Plugin.cs config-load anchor missing")
    insertion = "        if (ConfigMigration.Apply(_config))\n            ConfigSaver.Save(_config);\n"
    P.write_text(text.replace(anchor, anchor + insertion, 1), encoding="utf-8")
    print("Plugin.cs: config migration wired")
