from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")


def replace_once(old: str, new: str, marker: str) -> None:
    global text
    if marker in text:
        print(f"Plugin.cs: social guard already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"Plugin.cs anchor missing: {old[:100]!r}")
    text = text.replace(old, new, 1)
    print(f"Plugin.cs: social guard patched ({marker})")


replace_once(
    """    private readonly PartySupportDriver _partySupport;\n    private readonly BozjaController _controller;\n""",
    """    private readonly PartySupportDriver _partySupport;\n    private readonly BozjaController _controller;\n    private readonly SocialRequestGuard _socialRequests;\n""",
    "private readonly SocialRequestGuard _socialRequests;",
)

# Do not anchor on the BozjaController argument list. Other independent packets legitimately add
# constructor dependencies there (SupplyManager is one), and social-request lifetime has no reason
# to know their order. Insert immediately after the completed controller assignment instead.
if "_socialRequests = new SocialRequestGuard(_config, () => _controller.Running);" not in text:
    anchor = "        _socialRequests = new SocialRequestGuard"
    # Older source may already have another social assignment shape; only add if none exists.
    if anchor not in text:
        main_anchor = "        _mainWindow = new MainWindow"
        pos = text.find(main_anchor)
        if pos < 0:
            raise RuntimeError("Plugin.cs main-window anchor missing for social guard")
        text = (
            text[:pos]
            + "        _socialRequests = new SocialRequestGuard(_config, () => _controller.Running);\n\n"
            + text[pos:]
        )
        print("Plugin.cs: social guard lifetime wired")
else:
    print("Plugin.cs: social guard lifetime already wired")

replace_once(
    """        try { _director.ReleaseControl(); }\n        catch { /* best effort */ }\n\n        _link.Dispose();\n""",
    """        try { _director.ReleaseControl(); }\n        catch { /* best effort */ }\n\n        try { _socialRequests.Dispose(); }\n        catch { /* best effort */ }\n\n        _link.Dispose();\n""",
    "try { _socialRequests.Dispose(); }",
)

P.write_text(text, encoding="utf-8")
print("Plugin.cs: social guard wiring ready")
