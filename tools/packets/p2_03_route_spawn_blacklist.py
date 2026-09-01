from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str, marker: str | None = None) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    marker = marker or new
    if marker in text:
        print(f"{path}: route-spawn blacklist already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:140]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: route-spawn blacklist patched")


patch(
    "Automation/TargetSelector.cs",
    """    private readonly RegionResolver _regions = regions;\n    private readonly Movement _movement = movement;\n\n""",
    """    private readonly RegionResolver _regions = regions;\n    private readonly Movement _movement = movement;\n\n    // Route failures are scoped to a single live skirmish spawn. They are intentionally not\n    // persisted in Configuration: once the FATE disappears (or reaches completion), the same\n    // FateId is eligible again on its next spawn. This prevents one bad navmesh route from\n    // turning into an infinite unattended retry loop without permanently suppressing content.\n    private readonly HashSet<uint> _routeBlacklistedFates = [];\n\n    public int RouteBlacklistedFateCount => _routeBlacklistedFates.Count;\n\n    public void BlacklistFateForCurrentSpawn(uint fateId)\n    {\n        if (fateId != 0)\n            _routeBlacklistedFates.Add(fateId);\n    }\n\n    public void ClearRouteBlacklist() => _routeBlacklistedFates.Clear();\n\n    private void PruneRouteBlacklist()\n    {\n        if (_routeBlacklistedFates.Count == 0)\n            return;\n\n        var live = new HashSet<uint>();\n        try\n        {\n            foreach (var fate in Svc.Fates)\n                if (fate != null && fate.Progress < 100)\n                    live.Add(fate.FateId);\n        }\n        catch\n        {\n            // FATE table temporarily unavailable: keep the blacklist rather than accidentally\n            // re-enabling the exact spawn that just wedged us. The next readable tick prunes it.\n            return;\n        }\n\n        _routeBlacklistedFates.RemoveWhere(id => !live.Contains(id));\n    }\n\n""",
    "private readonly HashSet<uint> _routeBlacklistedFates",
)

patch(
    "Automation/TargetSelector.cs",
    """    public bool StillPermitted(ObjectiveKind kind, uint id, Vector3 position)\n    {\n        if (kind == ObjectiveKind.None)\n            return false;\n\n""",
    """    public bool StillPermitted(ObjectiveKind kind, uint id, Vector3 position)\n    {\n        if (kind == ObjectiveKind.None)\n            return false;\n\n        PruneRouteBlacklist();\n        if (kind == ObjectiveKind.Fate && _routeBlacklistedFates.Contains(id))\n            return false;\n\n""",
    "if (kind == ObjectiveKind.Fate && _routeBlacklistedFates.Contains(id))",
)

patch(
    "Automation/TargetSelector.cs",
    """    private Choice SelectFate(bool deterministic)\n    {\n        uint bestId = 0;\n""",
    """    private Choice SelectFate(bool deterministic)\n    {\n        PruneRouteBlacklist();\n\n        uint bestId = 0;\n""",
    "private Choice SelectFate(bool deterministic)\n    {\n        PruneRouteBlacklist();",
)

patch(
    "Automation/TargetSelector.cs",
    """                if (_config.BlockedEngagements.Contains(fate.FateId))\n                    continue;\n\n                if (!PassesFarmFilter(ObjectiveKind.Fate, fate.FateId, fate.Position, DropActivity.Skirmish))\n""",
    """                if (_config.BlockedEngagements.Contains(fate.FateId))\n                    continue;\n\n                if (_routeBlacklistedFates.Contains(fate.FateId))\n                    continue;\n\n                if (!PassesFarmFilter(ObjectiveKind.Fate, fate.FateId, fate.Position, DropActivity.Skirmish))\n""",
    "if (_routeBlacklistedFates.Contains(fate.FateId))",
)

patch(
    "Automation/BozjaController.cs",
    """        _dependencies.Reset();\n        _safeStop.Reset();\n""",
    """        _dependencies.Reset();\n        _safeStop.Reset();\n        _selector.ClearRouteBlacklist();\n""",
    "_selector.ClearRouteBlacklist();",
)

patch(
    "Automation/BozjaController.cs",
    """    public bool LifestreamAvailable => _movement.LifestreamAvailable;\n\n    /// <summary>Live engagement snapshot from the last tick, for the UI.</summary>\n""",
    """    public bool LifestreamAvailable => _movement.LifestreamAvailable;\n    public int RouteBlacklistCount => _selector.RouteBlacklistedFateCount;\n\n    /// <summary>Live engagement snapshot from the last tick, for the UI.</summary>\n""",
    "public int RouteBlacklistCount =>",
)

patch(
    "Automation/BozjaController.cs",
    """            if (_movement.Stuck)\n            {\n                Status = $\"Stuck en route to {Describe(objective)} ({distance:F0}y) - no progress \" +\n                         $\"for {_movement.SecondsWithoutProgress:F0}s across {_movement.RepathCount} \" +\n                         \"re-paths. vnavmesh may not be able to reach it from here.\";\n                return;\n            }\n""",
    """            if (_movement.Stuck)\n            {\n                var failedRepaths = _movement.RepathCount;\n                var failedSeconds = _movement.SecondsWithoutProgress;\n\n                if (objective.Kind == ObjectiveKind.Fate)\n                {\n                    _selector.BlacklistFateForCurrentSpawn(objective.Id);\n                    Svc.Log.Warning(\n                        $\"[BozjaBuddyReborn] Route to skirmish FateId {objective.Id} remained stuck after \" +\n                        $\"{failedRepaths} repaths / {failedSeconds:F0}s without progress; blacklisting this live spawn.\");\n                    DiagnosticsRecorder.Warning(\n                        $\"スカーミッシュ #{objective.Id} への経路を確立できなかったため、この出現中は除外します。\",\n                        ControllerState.Travelling);\n                    Status = $\"スカーミッシュ #{objective.Id} へ到達できないため、この出現中は除外して次の対象を探します。\";\n                }\n                else\n                {\n                    Svc.Log.Warning(\n                        $\"[BozjaBuddyReborn] Route to objective {objective.Kind} #{objective.Id} remained stuck after \" +\n                        $\"{failedRepaths} repaths / {failedSeconds:F0}s without progress; abandoning the objective.\");\n                    DiagnosticsRecorder.Warning(\n                        \"現在の目的地へ到達できないため、対象を解除して再選択します。\",\n                        ControllerState.Travelling);\n                    Status = \"現在の目的地へ到達できないため、対象を解除して再選択します。\";\n                }\n\n                _movement.Stop();\n                _approach.Release();\n                _director.Travel(_config.UseBossModAvoidance);\n                _lastObjective = SharedObjective.None;\n                _committed = false;\n                _returning = false;\n                _reportedArrival = false;\n                _arrivedAtMs = 0;\n                _arrivalNote = null;\n                return;\n            }\n""",
    "BlacklistFateForCurrentSpawn(objective.Id)",
)

patch(
    "Windows/MainWindow.cs",
    """        sb.AppendLine($\"route={_controller.TravelRoute}\");\n""",
    """        sb.AppendLine($\"route={_controller.TravelRoute}\");\n        sb.AppendLine($\"routeSpawnBlacklist={_controller.RouteBlacklistCount}\");\n""",
    "routeSpawnBlacklist=",
)
