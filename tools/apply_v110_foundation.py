from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def load(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def save(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"anchor not found for {label}")
    return text.replace(old, new, 1)


def patch_configuration() -> None:
    path = "Configuration.cs"
    text = load(path)
    text = text.replace("public int Version { get; set; } = 1;", "public int Version { get; set; } = 2;", 1)
    if "UseBocchiNavigation" not in text:
        old = '''    /// <summary>Allow vnavmesh to fly. Bozja and Zadnor both permit flight.</summary>\n    public bool AllowFlight = true;\n\n    /// <summary>\n    /// Summon a mount for long hauls. Neither field zone has an aetheryte, so mount travel is\n    /// the only fast travel available - with this off, the character jogs the whole map.\n    /// </summary>\n    public bool UseMount = true;\n'''
        new = '''    /// <summary>\n    /// Compatibility field retained for migration from 1.0.x. Save the Queen field zones are\n    /// ground-only; v1.1 never asks vnavmesh for a flying path.\n    /// </summary>\n    public bool AllowFlight = false;\n\n    /// <summary>Use the BOCCHI-derived field travel planner instead of legacy direct paths.</summary>\n    public bool UseBocchiNavigation = true;\n\n    /// <summary>Use the Bozja/Zadnor custom aethernet through optional Lifestream IPC.</summary>\n    public bool UseAethernetTravel = true;\n\n    /// <summary>Allow Return -> base camp routes when that leg becomes available in the planner.</summary>\n    public bool UseReturnRouting = true;\n\n    /// <summary>Emergency escape hatch retained in stable builds.</summary>\n    public bool LegacyMovement;\n\n    /// <summary>BOCCHI default: walk directly when the goal is within this many yalms.</summary>\n    public float NavigationMaxDirectWalkDistance = 80f;\n\n    /// <summary>BOCCHI yalm-equivalent cost assigned to one custom-aethernet hop.</summary>\n    public float NavigationAethernetHopCost = 50f;\n\n    /// <summary>BOCCHI yalm-equivalent cost assigned to Return.</summary>\n    public float NavigationReturnCost = 40f;\n\n    /// <summary>Do not choose a fresh skirmish already at or above this progress.</summary>\n    public byte NewSkirmishMaxProgress = 80;\n\n    /// <summary>Summon a mount for long ground hauls.</summary>\n    public bool UseMount = true;\n'''
        text = replace_once(text, old, new, "Configuration movement settings")
    save(path, text)


def patch_movement() -> None:
    path = "Automation/Movement.cs"
    text = load(path)
    if "using Dalamud.Plugin;" not in text:
        text = text.replace("using BozjaBuddyReborn.External;\n", "using BozjaBuddyReborn.External;\nusing Dalamud.Plugin;\n", 1)

    old_ctor = "public sealed class Movement(NavmeshIpc navmesh, Configuration config, AggroAvoidance avoidance)"
    new_ctor = "public sealed class Movement(NavmeshIpc navmesh, Configuration config, AggroAvoidance avoidance, IDalamudPluginInterface pluginInterface)"
    if old_ctor in text:
        text = text.replace(old_ctor, new_ctor, 1)

    if "private readonly FieldTravelRouter _fieldRouter" not in text:
        anchor = "    private readonly AggroAvoidance _avoidance = avoidance;\n"
        insert = anchor + "    private readonly FieldTravelRouter _fieldRouter = new(new LifestreamIpc(pluginInterface), config);\n"
        text = replace_once(text, anchor, insert, "Movement router field")

    if "public FieldTravelMode TravelMode" not in text:
        anchor = "    public bool Busy => _navmesh.Busy;\n"
        insert = anchor + '''\n    /// <summary>High-level BOCCHI-style route currently in use.</summary>\n    public FieldTravelMode TravelMode => _fieldRouter.Mode;\n    public string RouteDescription => _fieldRouter.RouteDescription;\n    public bool LifestreamAvailable => _fieldRouter.LifestreamAvailable;\n'''
        text = replace_once(text, anchor, insert, "Movement diagnostics")

    if "private bool TravelDirectTo" not in text:
        old = '''    public bool TravelTo(Vector3 destination, float range)\n    {\n        if (!_navmesh.Available || !_navmesh.MeshReady)\n'''
        new = '''    public bool TravelTo(Vector3 destination, float range)\n    {\n        if (_config.LegacyMovement || !_config.UseBocchiNavigation)\n            return TravelDirectTo(destination, range);\n\n        var directive = _fieldRouter.Resolve(destination, range);\n        if (directive.HoldMovement)\n        {\n            // A teleport is process-global movement just like vnavmesh.  Never leave our old\n            // path walking underneath it; unlike Stop(), this intentionally keeps router state.\n            if (_navmesh.OwnedBy(NavClient.Travel))\n                _navmesh.Stop(NavClient.Travel);\n            return true;\n        }\n\n        return TravelDirectTo(directive.Destination, directive.Range);\n    }\n\n    private bool TravelDirectTo(Vector3 destination, float range)\n    {\n        if (!_navmesh.Available || !_navmesh.MeshReady)\n'''
        text = replace_once(text, old, new, "Movement TravelTo wrapper")

    text = text.replace(
        "        // Long haul still ahead: get mounted. Bozja and Zadnor have no aetherytes, so this is\n        // the only fast travel there is.\n",
        "        // Long ground haul still ahead: get mounted. Higher-level routing may have split\n        // the trip at an in-zone aethernet node before this direct leg is issued.\n",
        1,
    )
    text = text.replace(
        "if (!_navmesh.MoveCloseTo(legTarget, legRange, Mount.ShouldFly(_config.AllowFlight), NavClient.Travel))",
        "if (!_navmesh.MoveCloseTo(legTarget, legRange, false, NavClient.Travel))",
        1,
    )

    if "_fieldRouter.IsRoutingTo(destination)" not in text:
        old = '''        var me = Svc.Objects.LocalPlayer;\n        if (me == null)\n            return false;\n\n        var target = _basisSnapped != Vector3.Zero && HorizontalDistance(destination, _basisRaw) <= 1f\n'''
        new = '''        var me = Svc.Objects.LocalPlayer;\n        if (me == null)\n            return false;\n\n        // While walking to a departure shard or waiting on Lifestream, the direct Movement\n        // basis names that intermediate leg.  It must never make the controller believe the\n        // final activity has been reached.\n        if (!_config.LegacyMovement && _fieldRouter.IsRoutingTo(destination) && !_fieldRouter.OnFinalLeg)\n            return false;\n\n        var target = _basisSnapped != Vector3.Zero && HorizontalDistance(destination, _basisRaw) <= 1f\n'''
        text = replace_once(text, old, new, "Movement HasArrived router gate")

    if "_fieldRouter.Reset();" not in text:
        old = '''    public void Stop()\n    {\n        // Cheap when there is genuinely nothing to stop'''
        new = '''    public void Stop()\n    {\n        _fieldRouter.Reset();\n\n        // Cheap when there is genuinely nothing to stop'''
        text = replace_once(text, old, new, "Movement Stop router reset")

    save(path, text)


def patch_plugin() -> None:
    path = "Plugin.cs"
    text = load(path)
    text = text.replace(
        "_movement = new Movement(_navmesh, _config, _aggroAvoidance);",
        "_movement = new Movement(_navmesh, _config, _aggroAvoidance, pluginInterface);",
        1,
    )
    save(path, text)


def patch_selector() -> None:
    path = "Automation/TargetSelector.cs"
    text = load(path)

    # CE is now a remote UI operation.  Select() chooses only physical travel objectives.
    text = text.replace(
        "        var wantCe = _config.DoCriticalEngagements && requiredActivity != DropActivity.Skirmish;",
        "        var wantCe = false; // CE registration is remote; BozjaController/SignUpRunner owns it.",
        1,
    )

    text = text.replace(
        "                if (fate.Progress >= 100)\n                    continue;",
        "                if (fate.Progress >= _config.NewSkirmishMaxProgress)\n                    continue;",
        1,
    )

    if "SelectRegistration(" not in text:
        anchor = "    private Choice SelectEngagement(IReadOnlyList<CeSnapshot> engagements, bool deterministic)\n"
        method = '''    /// <summary>\n    /// Pick the single CE to register for remotely. Large-scale engagements, when explicitly\n    /// enabled, outrank every other CE; otherwise the current relic filter and configured\n    /// PriorityEngagements determine eligibility/rank.\n    /// </summary>\n    public CeSnapshot? SelectRegistration(IReadOnlyList<CeSnapshot> engagements, bool deterministic)\n    {\n        CeSnapshot? best = null;\n        var bestRank = int.MaxValue;\n        var bestDistance = float.MaxValue;\n\n        foreach (var ce in engagements)\n        {\n            if (!IsEligible(ce))\n                continue;\n\n            var largeScale = _catalog.IsLargeScale(ce.EventId);\n            var rank = largeScale ? int.MinValue : PriorityRank(ce.EventId);\n            var distance = ce.HasPosition ? Movement.DistanceToPlayer(ce.Position) : float.MaxValue;\n\n            if (best == null || Better(rank, ce.EventId, distance, bestRank, best.Value.EventId, bestDistance, deterministic))\n            {\n                best = ce;\n                bestRank = rank;\n                bestDistance = distance;\n            }\n        }\n\n        return best;\n    }\n\n'''
        text = replace_once(text, anchor, method + anchor, "TargetSelector remote CE selector")

    old = '''        if (_catalog.IsLargeScale(ce.EventId) && !_config.EngageLargeScale)\n            return false;\n\n        // The game refuses registration under 10 seconds; require enough margin to also travel.\n'''
    new = '''        var largeScale = _catalog.IsLargeScale(ce.EventId);\n        if (largeScale && !_config.EngageLargeScale)\n            return false;\n\n        // The game refuses registration under 10 seconds; remote registration still keeps a\n        // small UI margin, but no travel margin is needed any more.\n'''
    if old in text:
        text = text.replace(old, new, 1)

    old = '''        // Region/activity gate for the current farm target.\n        if (!PassesFarmFilter(ObjectiveKind.CriticalEngagement, ce.EventId, ce.Position,\n                DropActivity.CriticalEngagement))\n            return false;\n'''
    new = '''        // Explicitly-enabled Castrum/Dalriada are absolute priority by requirement and bypass\n        // a Resistance Relic filter. Ordinary CEs remain constrained by the selected material.\n        if (!largeScale && !PassesFarmFilter(ObjectiveKind.CriticalEngagement, ce.EventId, ce.Position,\n                DropActivity.CriticalEngagement))\n            return false;\n'''
    if old in text:
        text = text.replace(old, new, 1)

    save(path, text)


def patch_controller() -> None:
    path = "Automation/BozjaController.cs"
    text = load(path)

    if "TickAutomaticCeRegistration();" not in text:
        anchor = '''        Engagements = CriticalEngagements.Read(_catalog);\n        CurrentRegion = FieldRegions.Current();\n\n        // --- already registered and fighting -------------------------------\n'''
        replacement = '''        Engagements = CriticalEngagements.Read(_catalog);\n        CurrentRegion = FieldRegions.Current();\n\n        // Critical Engagements are a remote UI workflow, not a travel objective. Register while\n        // continuing the current skirmish; SignUpRunner will press Commence immediately if this\n        // box wins the draw. This is intentionally before objective selection.\n        TickAutomaticCeRegistration();\n\n        // --- already registered and fighting -------------------------------\n'''
        text = replace_once(text, anchor, replacement, "Controller CE registration hook")

    if "private void TickAutomaticCeRegistration()" not in text:
        anchor = "    // ------------------------------------------------------------- engagement\n"
        method = '''    private void TickAutomaticCeRegistration()\n    {\n        if (!_config.DoCriticalEngagements || _signUps.Active)\n            return;\n\n        // Once registered, the existing SignUpRunner owns the lottery/Commence state. Starting\n        // a second attempt here would reopen the window and risk withdrawing the first one.\n        if (CriticalEngagements.RegisteredEventId is { } registered && registered != 0)\n            return;\n\n        var selected = _selector.SelectRegistration(Engagements, deterministic: _config.MultiboxEnabled);\n        if (selected is not { } ce)\n            return;\n\n        _signUps.Begin(ce.EventId);\n        Svc.Log.Information(\n            $"[BozjaBuddyReborn] Auto-registering remotely for CE #{ce.EventId} \\\"{ce.Name}\\\"; no travel to CE marker required.");\n    }\n\n'''
        text = replace_once(text, anchor, method + anchor, "Controller CE registration method")

    # A CE should never survive as the sticky physical objective after the remote-registration change.
    if "_lastObjective.Kind != ObjectiveKind.CriticalEngagement" not in text:
        old = '''        if (_config.StickyObjective\n            && IsObjectiveStillWorthDoing(_lastObjective)\n'''
        new = '''        if (_config.StickyObjective\n            && _lastObjective.Kind != ObjectiveKind.CriticalEngagement\n            && IsObjectiveStillWorthDoing(_lastObjective)\n'''
        text = replace_once(text, old, new, "Controller CE sticky exclusion")

    save(path, text)


def patch_signup() -> None:
    path = "Automation/SignUpRunner.cs"
    text = load(path)
    if "private ushort _preferredEventId;" not in text:
        text = text.replace(
            "    private ushort _targetEventId;\n",
            "    private ushort _targetEventId;\n    private ushort _preferredEventId;\n",
            1,
        )
    if "public void Begin(ushort preferredEventId = 0)" not in text:
        text = text.replace("    public void Begin()\n    {", "    public void Begin(ushort preferredEventId = 0)\n    {", 1)
        text = text.replace(
            "        _targetEventId = 0;\n        _loggedButtons = string.Empty;",
            "        _targetEventId = 0;\n        _preferredEventId = preferredEventId;\n        _loggedButtons = string.Empty;",
            1,
        )
        text = text.replace(
            '        Svc.Log.Information("[BozjaBuddyReborn] Sign-up: begin.");',
            '        Svc.Log.Information($"[BozjaBuddyReborn] Sign-up: begin (preferred CE #{_preferredEventId}).");',
            1,
        )
    # For the first test build, record the requested id and refuse to pretend the window row map is known.
    # If the first registering event differs, log it; button-event diagnostics will let the live client
    # prove the row mapping before we make targeted multi-CE presses.
    old = "            _targetEventId = FirstRegisteringEventId();\n\n            if (Click(addon, register, \"Register\"))"
    new = '''            var first = FirstRegisteringEventId();\n            _targetEventId = _preferredEventId != 0 ? _preferredEventId : first;\n            if (_preferredEventId != 0 && first != 0 && first != _preferredEventId)\n                Svc.Log.Warning(\n                    $"[BozjaBuddyReborn] Preferred CE #{_preferredEventId} differs from the first recruitment row #{first}; " +\n                    "using the current button order for this test build. Capture callback/button diagnostics before tightening row targeting.");\n\n            if (Click(addon, register, "Register"))'''
    if old in text:
        text = text.replace(old, new, 1)
    save(path, text)


def patch_version() -> None:
    path = "BozjaBuddyReborn.csproj"
    text = load(path)
    # Version properties occur near the end of the giant historical comment. Only replace XML values.
    text = re.sub(r"<Version>[^<]+</Version>", "<Version>1.0.90.1</Version>", text, count=1)
    text = re.sub(r"<AssemblyVersion>[^<]+</AssemblyVersion>", "<AssemblyVersion>1.0.90.1</AssemblyVersion>", text, count=1)
    text = re.sub(r"<FileVersion>[^<]+</FileVersion>", "<FileVersion>1.0.90.1</FileVersion>", text, count=1)
    save(path, text)


if __name__ == "__main__":
    patch_configuration()
    patch_movement()
    patch_plugin()
    patch_selector()
    patch_controller()
    patch_signup()
    patch_version()
    print("v1.1 foundation patch applied")
