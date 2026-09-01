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


# Plugin owns a single supervisor and injects it into the controller.
patch(
    "Plugin.cs",
    """    private readonly NavmeshIpc _navmesh;\n    private readonly CombatDirector _director;\n""",
    """    private readonly NavmeshIpc _navmesh;\n    private readonly CombatDirector _director;\n    private readonly DependencySupervisor _dependencies;\n""",
)
patch(
    "Plugin.cs",
    """        _navmesh = new NavmeshIpc(pluginInterface);\n        _director = new CombatDirector(pluginInterface, _config);\n        _aggroAvoidance = new AggroAvoidance(_config);\n""",
    """        _navmesh = new NavmeshIpc(pluginInterface);\n        _director = new CombatDirector(pluginInterface, _config);\n        _dependencies = new DependencySupervisor(_navmesh, _director);\n        _aggroAvoidance = new AggroAvoidance(_config);\n""",
)
patch(
    "Plugin.cs",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport);\n""",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _dependencies);\n""",
)

# Controller field + constructor.
patch(
    "Automation/BozjaController.cs",
    """    private readonly SignUpRunner _signUps;\n    private readonly PartySupportDriver _partySupport;\n""",
    """    private readonly SignUpRunner _signUps;\n    private readonly PartySupportDriver _partySupport;\n    private readonly DependencySupervisor _dependencies;\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        LoadoutDriver loadouts,\n        SignUpRunner signUps,\n        PartySupportDriver partySupport)\n""",
    """        LoadoutDriver loadouts,\n        SignUpRunner signUps,\n        PartySupportDriver partySupport,\n        DependencySupervisor dependencies)\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _signUps = signUps;\n        _partySupport = partySupport;\n    }\n""",
    """        _signUps = signUps;\n        _partySupport = partySupport;\n        _dependencies = dependencies;\n    }\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _director.Resync();\n        _holster.Reset();\n        ResetYieldState();\n""",
    """        _director.Resync();\n        _dependencies.Reset();\n        _holster.Reset();\n        ResetYieldState();\n""",
)

# Replace the one-off vnav availability guard with the supervisor gate.
patch(
    "Automation/BozjaController.cs",
    """        if (!_navmesh.Available)\n        {\n            State = ControllerState.Blocked;\n            Status = \"vnavmesh is not installed - movement is unavailable.\";\n            return;\n        }\n\n        if (!_navmesh.MeshReady)\n""",
    """        var dependency = _dependencies.Snapshot();\n        if (!dependency.Ready)\n        {\n            State = ControllerState.Blocked;\n            _movement.Stop();\n\n            // Waiting for a required plugin must not turn into a free death. The survivability\n            // driver remains allowed while unmounted; its own mounted invariant prevents a heal\n            // from dismounting the character during travel.\n            _holster.Tick(inCombat: Svc.Condition[ConditionFlag.InCombat]);\n\n            if (dependency.Health == DependencyHealth.WaitingRequired)\n            {\n                Status = $\"必須プラグインとの接続が失われました: {dependency.MissingText}。\" +\n                         $\"復帰を待っています（残り{Math.Ceiling(dependency.Remaining.TotalSeconds):F0}秒）。\";\n                return;\n            }\n\n            // P9-03 adds the safe-return policy. Until that packet is present, timeout fails closed\n            // rather than silently resuming with a required combat/movement dependency missing.\n            Stop($\"必須プラグインが60秒以内に復帰しませんでした: {dependency.MissingText}。\");\n            return;\n        }\n\n        if (!_navmesh.MeshReady)\n""",
)
