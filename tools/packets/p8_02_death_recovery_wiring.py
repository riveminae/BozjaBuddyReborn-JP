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


# Plugin owns TextAdvance IPC + recovery driver and injects it before later dependency packets.
patch(
    "Plugin.cs",
    """    private readonly NavmeshIpc _navmesh;\n    private readonly CombatDirector _director;\n    private readonly MultiboxLink _link = new();\n""",
    """    private readonly NavmeshIpc _navmesh;\n    private readonly CombatDirector _director;\n    private readonly TextAdvanceIpc _textAdvance;\n    private readonly DeathRecoveryDriver _deathRecovery;\n    private readonly MultiboxLink _link = new();\n""",
)
patch(
    "Plugin.cs",
    """        _navmesh = new NavmeshIpc(pluginInterface);\n        _director = new CombatDirector(pluginInterface, _config);\n        _aggroAvoidance = new AggroAvoidance(_config);\n""",
    """        _navmesh = new NavmeshIpc(pluginInterface);\n        _director = new CombatDirector(pluginInterface, _config);\n        _textAdvance = new TextAdvanceIpc(pluginInterface);\n        _deathRecovery = new DeathRecoveryDriver(_textAdvance);\n        _aggroAvoidance = new AggroAvoidance(_config);\n""",
)
patch(
    "Plugin.cs",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport);\n""",
    """            _config, _catalog, _selector, _movement, _director, _approach, _holster, _link, _navmesh, _regions,\n            _errands, _loadoutDriver, _signUps, _partySupport, _deathRecovery);\n""",
)

# Controller field/constructor.
patch(
    "Automation/BozjaController.cs",
    """    private readonly SignUpRunner _signUps;\n    private readonly PartySupportDriver _partySupport;\n\n""",
    """    private readonly SignUpRunner _signUps;\n    private readonly PartySupportDriver _partySupport;\n    private readonly DeathRecoveryDriver _deathRecovery;\n\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        LoadoutDriver loadouts,\n        SignUpRunner signUps,\n        PartySupportDriver partySupport)\n""",
    """        LoadoutDriver loadouts,\n        SignUpRunner signUps,\n        PartySupportDriver partySupport,\n        DeathRecoveryDriver deathRecovery)\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _signUps = signUps;\n        _partySupport = partySupport;\n    }\n""",
    """        _signUps = signUps;\n        _partySupport = partySupport;\n        _deathRecovery = deathRecovery;\n    }\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _director.Resync();\n        _holster.Reset();\n        ResetYieldState();\n""",
    """        _director.Resync();\n        _deathRecovery.CancelAndRestore();\n        _holster.Reset();\n        ResetYieldState();\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _approach.Release();\n        _movement.Stop();\n        _director.ReleaseControl();\n\n        // A sign-up outlived Stop""",
    """        _approach.Release();\n        _movement.Stop();\n        _director.ReleaseControl();\n        _deathRecovery.CancelAndRestore();\n\n        // A sign-up outlived Stop""",
)

old_death = """        // DEAD. There was no death handling at all before this, and Bozja kills you regularly.\n        // Everything below assumes a living character that can move and fight, and the state\n        // carried across a corpse run is actively harmful: a stale _committed made the runner\n        // stop 30y+ short of the objective it was returning to (see the leash), and a stale\n        // _reportedArrival meant the multibox barrier was never told this box had left.\n        if (Svc.Condition[ConditionFlag.Unconscious] || Svc.Objects.LocalPlayer?.CurrentHp == 0)\n        {\n            State = ControllerState.Blocked;\n            Status = \"Dead - waiting for a raise or a return to the base camp.\";\n            _committed = false;\n            _returning = false;\n            _reportedArrival = false;\n            _arrivedAtMs = 0;\n            _approach.Release();\n            _movement.Stop();\n            _director.Travel(_config.UseBossModAvoidance);\n            return;\n        }\n\n"""
new_death = """        // Timed unattended death recovery. TextAdvance is enabled only for the corpse window and\n        // restored after revival. CE deaths never cast Return while the CE remains live; a committed\n        // skirmish gets a 30s raise window and travel/idle gets 10s.\n        var dead = Svc.Condition[ConditionFlag.Unconscious] || Svc.Objects.LocalPlayer?.CurrentHp == 0;\n        if (dead)\n        {\n            var currentWhileDead = CriticalEngagements.Current(_catalog);\n            var inLiveCe = currentWhileDead is { } deadCe && deadCe.IsLive;\n            var diedDuringSkirmish = _lastObjective.Kind == ObjectiveKind.Fate\n                                     && (_committed || State is ControllerState.Engaged or ControllerState.Holding);\n            var recovery = _deathRecovery.Tick(true, inLiveCe, diedDuringSkirmish);\n\n            State = ControllerState.Blocked;\n            Status = recovery.JapaneseStatus;\n            _committed = false;\n            _returning = false;\n            _reportedArrival = false;\n            _arrivedAtMs = 0;\n            _approach.Release();\n            _movement.Stop();\n            _director.Travel(_config.UseBossModAvoidance);\n\n            if (recovery.Fatal)\n                Stop(recovery.JapaneseStatus);\n            return;\n        }\n\n        // A live-again tick is what restores TextAdvance to the exact state it had before death.\n        _deathRecovery.Tick(false, false, false);\n\n"""
patch("Automation/BozjaController.cs", old_death, new_death)
