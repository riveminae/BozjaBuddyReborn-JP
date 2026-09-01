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


# P8 runs before P9 and owns TextAdvance/death-recovery injection. Compose on top of that shape.
patch(
    "Plugin.cs",
    """    private readonly NavmeshIpc _navmesh;\n    private readonly CombatDirector _director;\n    private readonly TextAdvanceIpc _textAdvance;\n""",
    """    private readonly NavmeshIpc _navmesh;\n    private readonly CombatDirector _director;\n    private readonly DependencySupervisor _dependencies;\n    private readonly TextAdvanceIpc _textAdvance;\n""",
)
patch(
    "Plugin.cs",
    """        _navmesh = new NavmeshIpc(pluginInterface);\n        _director = new CombatDirector(pluginInterface, _config);\n        _textAdvance = new TextAdvanceIpc(pluginInterface);\n""",
    """        _navmesh = new NavmeshIpc(pluginInterface);\n        _director = new CombatDirector(pluginInterface, _config);\n        _dependencies = new DependencySupervisor(_navmesh, _director);\n        _textAdvance = new TextAdvanceIpc(pluginInterface);\n""",
)
patch(
    "Plugin.cs",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery);\n""",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery, _dependencies);\n""",
)

# Controller field + constructor, after P8's death-recovery field.
patch(
    "Automation/BozjaController.cs",
    """    private readonly PartySupportDriver _partySupport;\n    private readonly DeathRecoveryDriver _deathRecovery;\n""",
    """    private readonly PartySupportDriver _partySupport;\n    private readonly DeathRecoveryDriver _deathRecovery;\n    private readonly DependencySupervisor _dependencies;\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        SignUpRunner signUps,\n        PartySupportDriver partySupport,\n        DeathRecoveryDriver deathRecovery)\n""",
    """        SignUpRunner signUps,\n        PartySupportDriver partySupport,\n        DeathRecoveryDriver deathRecovery,\n        DependencySupervisor dependencies)\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _partySupport = partySupport;\n        _deathRecovery = deathRecovery;\n    }\n""",
    """        _partySupport = partySupport;\n        _deathRecovery = deathRecovery;\n        _dependencies = dependencies;\n    }\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _director.Resync();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n""",
    """        _director.Resync();\n        _dependencies.Reset();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n""",
)

# Replace the one-off vnav availability guard with the supervisor gate.
patch(
    "Automation/BozjaController.cs",
    """        if (!_navmesh.Available)\n        {\n            State = ControllerState.Blocked;\n            Status = \"vnavmesh is not installed - movement is unavailable.\";\n            return;\n        }\n\n        if (!_navmesh.MeshReady)\n""",
    """        var dependency = _dependencies.Snapshot();\n        if (!dependency.Ready)\n        {\n            State = ControllerState.Blocked;\n            _movement.Stop();\n\n            // Waiting for a required plugin must not turn into a free death. The survivability\n            // driver remains allowed while unmounted; its own mounted invariant prevents a heal\n            // from dismounting the character during travel.\n            _holster.Tick(inCombat: Svc.Condition[ConditionFlag.InCombat]);\n\n            if (dependency.Health == DependencyHealth.WaitingRequired)\n            {\n                Status = $\"必須プラグインとの接続が失われました: {dependency.MissingText}。\" +\n                         $\"復帰を待っています（残り{Math.Ceiling(dependency.Remaining.TotalSeconds):F0}秒）。\";\n                return;\n            }\n\n            // P9-03 adds the safe-return policy. Until that packet is present, timeout fails closed.\n            Stop($\"必須プラグインが60秒以内に復帰しませんでした: {dependency.MissingText}。\");\n            return;\n        }\n\n        if (!_navmesh.MeshReady)\n""",
)
