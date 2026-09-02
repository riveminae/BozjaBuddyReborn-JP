from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "External/NavmeshIpc.cs"
text = P.read_text(encoding="utf-8-sig")


def patch(old: str, new: str, marker: str) -> None:
    global text
    if marker in text:
        print(f"External/NavmeshIpc.cs: already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"External/NavmeshIpc.cs anchor missing: {old[:160]!r}")
    text = text.replace(old, new, 1)
    print(f"External/NavmeshIpc.cs: patched ({marker})")


patch(
    "using System.Numerics;\nusing System.Threading.Tasks;\n",
    "using System.Numerics;\nusing System.Threading;\nusing System.Threading.Tasks;\n",
    "using System.Threading;",
)

patch(
    """    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>? _pathfind;\n    private readonly ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;\n""",
    """    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>? _pathfind;\n    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>? _pathfindCancelable;\n    private readonly ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;\n""",
    "CancellationToken, Task<List<Vector3>>>? _pathfindCancelable;",
)

patch(
    """        _pathfind = Bind<ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>>(\n            () => pi.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>(\"vnavmesh.Nav.Pathfind\"));\n        _pathfindAndMoveTo = Bind<ICallGateSubscriber<Vector3, bool, bool>>(\n""",
    """        _pathfind = Bind<ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>>(\n            () => pi.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>(\"vnavmesh.Nav.Pathfind\"));\n        _pathfindCancelable = Bind<ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>>(\n            () => pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(\"vnavmesh.Nav.PathfindCancelable\"));\n        _pathfindAndMoveTo = Bind<ICallGateSubscriber<Vector3, bool, bool>>(\n""",
    '"vnavmesh.Nav.PathfindCancelable"',
)

anchor = """    /// <summary>True while the character is following a path.</summary>\n"""
method = """    /// <summary>\n    /// Start a pathfinding-only query that this plugin can cancel without touching movement or\n    /// reloading vnavmesh. Used by short route-cost planning windows so a slow telemetry query\n    /// can never overlap the real SimpleMove request that follows.\n    /// </summary>\n    public Task<List<Vector3>>? PathfindCancelable(\n        Vector3 from, Vector3 to, CancellationToken cancellationToken, bool fly = false)\n    {\n        try\n        {\n            if (_pathfindCancelable?.HasFunction != true || !MeshReady)\n                return null;\n            return _pathfindCancelable.InvokeFunc(from, to, fly, cancellationToken);\n        }\n        catch\n        {\n            return null;\n        }\n    }\n\n"""
if "public Task<List<Vector3>>? PathfindCancelable(" not in text:
    if anchor not in text:
        raise RuntimeError("External/NavmeshIpc.cs PathRunning anchor missing")
    text = text.replace(anchor, method + anchor, 1)
    print("External/NavmeshIpc.cs: cancelable non-moving Pathfind query added")
else:
    print("External/NavmeshIpc.cs: cancelable non-moving Pathfind query already applied")

P.write_text(text, encoding="utf-8")
