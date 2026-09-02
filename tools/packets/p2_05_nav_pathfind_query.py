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
        raise RuntimeError(f"External/NavmeshIpc.cs anchor missing: {old[:140]!r}")
    text = text.replace(old, new, 1)
    print(f"External/NavmeshIpc.cs: patched ({marker})")


patch(
    "using System;\nusing System.Numerics;\n",
    "using System;\nusing System.Collections.Generic;\nusing System.Numerics;\nusing System.Threading.Tasks;\n",
    "using System.Threading.Tasks;",
)

patch(
    """    private readonly ICallGateSubscriber<float>? _buildProgress;\n    private readonly ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;\n""",
    """    private readonly ICallGateSubscriber<float>? _buildProgress;\n    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>? _pathfind;\n    private readonly ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;\n""",
    "Task<List<Vector3>>>? _pathfind;",
)

patch(
    """        _buildProgress = Bind<ICallGateSubscriber<float>>(() => pi.GetIpcSubscriber<float>(\"vnavmesh.Nav.BuildProgress\"));\n        _pathfindAndMoveTo = Bind<ICallGateSubscriber<Vector3, bool, bool>>(\n""",
    """        _buildProgress = Bind<ICallGateSubscriber<float>>(() => pi.GetIpcSubscriber<float>(\"vnavmesh.Nav.BuildProgress\"));\n        _pathfind = Bind<ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>>(\n            () => pi.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>>(\"vnavmesh.Nav.Pathfind\"));\n        _pathfindAndMoveTo = Bind<ICallGateSubscriber<Vector3, bool, bool>>(\n""",
    '"vnavmesh.Nav.Pathfind"',
)

anchor = """    /// <summary>True while the character is following a path.</summary>\n    public bool PathRunning\n"""
method = """    /// <summary>\n    /// Start a pathfinding-only query without moving the character. The returned task resolves to\n    /// vnavmesh waypoints and is the same IPC primitive BOCCHI/Ocelot uses for traversal costs.\n    /// A missing/unready navmesh returns null; asynchronous task faults remain the caller's to\n    /// observe because they occur after the IPC invocation has returned.\n    /// </summary>\n    public Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly = false)\n    {\n        try\n        {\n            if (_pathfind?.HasFunction != true || !MeshReady)\n                return null;\n            return _pathfind.InvokeFunc(from, to, fly);\n        }\n        catch\n        {\n            return null;\n        }\n    }\n\n"""
if "public Task<List<Vector3>>? Pathfind(" not in text:
    if anchor not in text:
        raise RuntimeError("External/NavmeshIpc.cs PathRunning anchor missing")
    text = text.replace(anchor, method + anchor, 1)
    print("External/NavmeshIpc.cs: non-moving Pathfind query added")
else:
    print("External/NavmeshIpc.cs: non-moving Pathfind query already applied")

P.write_text(text, encoding="utf-8")
