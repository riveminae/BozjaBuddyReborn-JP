from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")

if "_config = ConfigRecovery.Load(pluginInterface);" in text:
    print("Plugin.cs: guarded config recovery already wired")
else:
    old = """        _config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (ConfigMigration.Apply(_config))
            ConfigSaver.Save(_config);
"""
    new = """        _config = ConfigRecovery.Load(pluginInterface);
"""
    if old not in text:
        raise RuntimeError("Plugin.cs direct migration block missing")
    P.write_text(text.replace(old, new, 1), encoding="utf-8")
    print("Plugin.cs: guarded config recovery wired")
