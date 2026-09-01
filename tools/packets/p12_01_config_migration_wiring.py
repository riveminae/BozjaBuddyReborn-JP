from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")n
# CI replays packets against the final composed branch. P12-02 inserts character-state wiring
# immediately after migration, so the old exact multiline "new" block is no longer stable.
if "ConfigMigration.Apply(_config)" in text:
    print("Plugin.cs: config migration already wired")
else:
    anchor = "        _config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();\n"
    if anchor not in text:
        raise RuntimeError("Plugin.cs config-load anchor missing")
    insertion = "        if (ConfigMigration.Apply(_config))\n            ConfigSaver.Save(_config);\n"
    P.write_text(text.replace(anchor, anchor + insertion, 1), encoding="utf-8")
    print("Plugin.cs: config migration wired")
