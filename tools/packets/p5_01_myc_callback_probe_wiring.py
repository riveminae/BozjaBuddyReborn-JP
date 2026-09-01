from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    if new in text:
        print(f"{path}: already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: patched")


patch(
    "Configuration.cs",
    """    public bool LogUiCallbacks;\n\n""",
    """    public bool LogUiCallbacks;\n\n    /// <summary>Test-only: log receive events from MYCItemBox/MYCItemBagTrade to identify safe transfer callbacks.</summary>\n    public bool LogMycItemBoxCallbacks;\n\n""",
)

patch(
    "Plugin.cs",
    """    private readonly Movement _movement;\n    private readonly CombatApproach _approach;\n""",
    """    private readonly Movement _movement;\n    private readonly MycItemBoxCallbackProbe _mycItemBoxProbe;\n    private readonly CombatApproach _approach;\n""",
)
patch(
    "Plugin.cs",
    """        _movement = new Movement(_navmesh, _config, _aggroAvoidance, pluginInterface);\n        _approach = new CombatApproach(_navmesh, _config);\n""",
    """        _movement = new Movement(_navmesh, _config, _aggroAvoidance, pluginInterface);\n        _mycItemBoxProbe = new MycItemBoxCallbackProbe(_config);\n        _approach = new CombatApproach(_navmesh, _config);\n""",
)
patch(
    "Plugin.cs",
    """        _link.Dispose();\n\n        ConfigSaver.Save(_config);\n""",
    """        _link.Dispose();\n        try { _mycItemBoxProbe.Dispose(); }\n        catch { /* best effort */ }\n\n        ConfigSaver.Save(_config);\n""",
)
