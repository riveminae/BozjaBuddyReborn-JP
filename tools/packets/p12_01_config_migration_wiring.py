from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")
old = "        _config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();\n\n"
new = "        _config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();\n        if (ConfigMigration.Apply(_config))\n            ConfigSaver.Save(_config);\n\n"
if new in text:
    print("Plugin.cs: config migration already wired")
elif old in text:
    P.write_text(text.replace(old, new, 1), encoding="utf-8")
    print("Plugin.cs: config migration wired")
else:
    raise RuntimeError("Plugin.cs config-load anchor missing")
