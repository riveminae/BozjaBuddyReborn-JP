from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")

def replace_once(old: str, new: str) -> None:
    global text
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"Plugin.cs anchor missing: {old[:80]!r}")
    text = text.replace(old, new, 1)

replace_once(
    """    private readonly PartySupportDriver _partySupport;\n    private readonly BozjaController _controller;\n""",
    """    private readonly PartySupportDriver _partySupport;\n    private readonly BozjaController _controller;\n    private readonly SocialRequestGuard _socialRequests;\n""",
)
replace_once(
    """        _controller = new BozjaController(\n            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery, _dependencies);\n\n        _mainWindow = new MainWindow""",
    """        _controller = new BozjaController(\n            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery, _dependencies);\n        _socialRequests = new SocialRequestGuard(_config, () => _controller.Running);\n\n        _mainWindow = new MainWindow""",
)
replace_once(
    """        try { _director.ReleaseControl(); }\n        catch { /* best effort */ }\n\n        _link.Dispose();\n""",
    """        try { _director.ReleaseControl(); }\n        catch { /* best effort */ }\n\n        try { _socialRequests.Dispose(); }\n        catch { /* best effort */ }\n\n        _link.Dispose();\n""",
)

P.write_text(text, encoding="utf-8")
print("Plugin.cs: social guard wired")
