from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str, marker: str | None = None) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    if (marker and marker in text) or new in text:
        print(f"{path}: already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: patched")

patch(
    "Configuration.cs",
    """    /// <summary>Item id of the relic material to farm, or 0 for \"anything\".</summary>\n    public uint FarmMaterialItemId;\n\n""",
    """    /// <summary>Runtime compatibility mirror of the current character's farm target.</summary>\n    public uint FarmMaterialItemId;\n\n    /// <summary>Resistance Relic farm target keyed by Dalamud LocalContentId.</summary>\n    public Dictionary<ulong, uint> CharacterFarmMaterialItemIds = [];\n\n""",
    "CharacterFarmMaterialItemIds",
)

patch(
    "ConfigMigration.cs",
    """        config.LostActionAutoUsePermissions ??= [];\n        config.AutoLostActions ??= [];\n""",
    """        config.LostActionAutoUsePermissions ??= [];\n        config.CharacterFarmMaterialItemIds ??= [];\n        config.AutoLostActions ??= [];\n""",
    "config.CharacterFarmMaterialItemIds ??= [];",
)

patch(
    "Plugin.cs",
    """    private readonly RelicTracker _relicTracker = new();\n\n""",
    """    private readonly RelicTracker _relicTracker = new();\n    private readonly CharacterRelicStateStore _characterRelicState;\n\n""",
    "private readonly CharacterRelicStateStore _characterRelicState;",
)
patch(
    "Plugin.cs",
    """        if (ConfigMigration.Apply(_config))\n            ConfigSaver.Save(_config);\n\n""",
    """        if (ConfigMigration.Apply(_config))\n            ConfigSaver.Save(_config);\n        _characterRelicState = new CharacterRelicStateStore(_config);\n\n""",
    "_characterRelicState = new CharacterRelicStateStore(_config);",
)
patch(
    "Plugin.cs",
    """    private void OnUpdate(object _)\n    {\n        SyncCallbackLogging();\n""",
    """    private void OnUpdate(object _)\n    {\n        _characterRelicState.Tick();\n        SyncCallbackLogging();\n""",
    "_characterRelicState.Tick();",
)
