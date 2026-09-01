from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def load(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def save(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")


def once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"anchor not found: {label}")
    return text.replace(old, new, 1)


def patch_config() -> None:
    p = "Configuration.cs"
    t = load(p)
    t = t.replace("public int Version { get; set; } = 2;", "public int Version { get; set; } = 3;", 1)

    if "DangerStarExtraClearance" not in t:
        anchor = "    public float DangerClearance = 6f;\n"
        t = once(t, anchor, anchor + '''\n    /// <summary>Additional clearance around ★ enemies; they are always dangerous.</summary>\n    public float DangerStarExtraClearance = 5f;\n\n    /// <summary>Log each previously unseen field-rank raw icon pair once in test diagnostics.</summary>\n    public bool EnemyRankDiagnostics = true;\n''', "danger rank settings")

    if "AutoSurvivalLostActions" not in t:
        anchor = "    // --- lost actions -------------------------------------------------------\n\n"
        block = '''    // --- survivability automation -------------------------------------------\n\n    /// <summary>Run the v1.1 survivability-first Lost Action policy.</summary>\n    public bool AutoSurvivalLostActions = true;\n\n    public float TankSurvivalHealFraction = 0.55f;\n    public float TankSurvivalEmergencyFraction = 0.30f;\n    public float HealerSurvivalHealFraction = 0.70f;\n    public float HealerSurvivalEmergencyFraction = 0.45f;\n    public float DpsSurvivalHealFraction = 0.65f;\n    public float DpsSurvivalEmergencyFraction = 0.40f;\n\n    /// <summary>Fast guard between two automatic survival spends; the game remains the final cooldown authority.</summary>\n    public int SurvivalUseGapMs = 750;\n\n    /// <summary>Per-row bring/refill overrides. Missing = policy default; Deep Essences default false.</summary>\n    public Dictionary<byte, bool> LostActionBringPermissions = [];\n\n    /// <summary>Per-row automatic-use overrides. Missing = policy default; Deep Essences default false.</summary>\n    public Dictionary<byte, bool> LostActionAutoUsePermissions = [];\n\n'''
        t = once(t, anchor, block + anchor, "survival config")

    save(p, t)


def patch_localization() -> None:
    p = "Localization.cs"
    t = load(p)
    t = t.replace(
        '    public static bool Ja => string.Equals(Svc.PluginInterface.UiLanguage, "ja", StringComparison.OrdinalIgnoreCase);\n    public static string T(string en, string ja) => Ja ? ja : en;',
        '    // JP fork requirement: visible UI is Japanese regardless of Dalamud UI language.\n    public static bool Ja => true;\n    public static string T(string en, string ja) => ja;',
        1)
    t = t.replace("    public static string Controller(ControllerState s) => !Ja ? s.ToString() : s switch", "    public static string Controller(ControllerState s) => s switch", 1)
    t = t.replace("    public static string Phase(SignUpPhase s) => !Ja ? s.ToString() : s switch", "    public static string Phase(SignUpPhase s) => s switch", 1)
    t = t.replace("    public static string CeState(DynamicEventState s) => !Ja ? s.ToString() : s switch", "    public static string CeState(DynamicEventState s) => s switch", 1)
    save(p, t)


def patch_survival_policy() -> None:
    p = "Game/SurvivalPolicy.cs"
    t = load(p)
    t = t.replace("GetExcelSheet<Action>(ClientLanguage.English)", "GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English)")
    save(p, t)


def patch_aggro() -> None:
    p = "Automation/AggroAvoidance.cs"
    t = load(p)
    if "using BozjaBuddyReborn.Game;" not in t:
        t = t.replace("using System.Numerics;\n", "using System.Numerics;\nusing BozjaBuddyReborn.Game;\n", 1)

    if "FieldEnemyStrength Strength" not in t:
        t = once(t,
            '''    string Name,\n    byte Level,\n    Vector3 Position,''',
            '''    string Name,\n    byte Level,\n    FieldEnemyStrength Strength,\n    uint NamePlateIconId,\n    byte CharacterDataIcon,\n    Vector3 Position,''',
            "DangerZone strength fields")

    if "_loggedRankDiagnostics" not in t:
        anchor = "    private readonly Dictionary<ulong, long> _suppressed = [];\n"
        t = once(t, anchor, anchor + "    private readonly HashSet<string> _loggedRankDiagnostics = [];\n", "rank diagnostic field")

    old = '''                if (npc.Level < _config.DangerousEnemyMinLevel)\n                {\n                    belowLevel++;\n                    continue;\n                }\n'''
    new = '''                // Save-the-Queen mobs are all level 80. What matters is the field marker\n                // I/II/III/IV/V/★. I-III are intentionally allowed; IV/V/★ are avoided and an\n                // unresolved marker fails safe as dangerous.\n                var strength = EnemyStrengthResolver.Resolve(npc);\n                if (!strength.Dangerous)\n                {\n                    belowLevel++; // retained field name for config-window compatibility: now means safe rank\n                    continue;\n                }\n\n                if (_config.EnemyRankDiagnostics)\n                {\n                    var key = $"{strength.NamePlateIconId}:{strength.CharacterDataIcon}:{strength.EnglishName}";\n                    if (_loggedRankDiagnostics.Add(key))\n                        Svc.Log.Information(\n                            $"[BozjaBuddyReborn] Field-rank diagnostic: name=\\\"{strength.EnglishName}\\\" " +\n                            $"rank={strength.Label}, region={(byte)strength.Region}, " +\n                            $"NamePlateIconId={strength.NamePlateIconId}, CharacterData.Icon={strength.CharacterDataIcon}.");\n                }\n'''
    if old in t:
        t = t.replace(old, new, 1)

    if "Strength: strength.Strength" not in t:
        t = once(t,
            '''                    Name: npc.Name.TextValue,\n                    Level: npc.Level,\n                    Position: npc.Position,''',
            '''                    Name: npc.Name.TextValue,\n                    Level: npc.Level,\n                    Strength: strength.Strength,\n                    NamePlateIconId: strength.NamePlateIconId,\n                    CharacterDataIcon: strength.CharacterDataIcon,\n                    Position: npc.Position,''',
            "DangerZone constructor")

    t = t.replace(
        "        var clearance = blocking.OuterRadius + _config.DangerClearance;",
        "        var clearance = blocking.OuterRadius + _config.DangerClearance\n                        + (blocking.Strength == FieldEnemyStrength.Star ? _config.DangerStarExtraClearance : 0f);",
        1)
    save(p, t)


def patch_holster() -> None:
    p = "Automation/HolsterDriver.cs"
    t = load(p)

    if "private readonly SurvivalPolicy _survival" not in t:
        t = once(t,
            "    private readonly LostActionCatalog _catalog = catalog;\n",
            "    private readonly LostActionCatalog _catalog = catalog;\n    private readonly SurvivalPolicy _survival = new(config, catalog);\n",
            "holster survival policy")

    if "_lastSurvivalUseMs" not in t:
        t = once(t, "    private long _lastUseMs;\n", "    private long _lastUseMs;\n    private long _lastSurvivalUseMs;\n", "holster survival cooldown")

    if "_pendingTargetSelf" not in t:
        t = once(t,
            "    private long _loadIssuedMs;\n",
            "    private long _loadIssuedMs;\n    private bool _pendingTargetSelf;\n    private bool _pendingSurvival;\n",
            "pending self target")

    if "public bool TickTravelSurvival()" not in t:
        old = '''    public bool Tick(bool inCombat)\n    {\n        if (!_config.AutoUseLostActions || _config.AutoLostActions.Count == 0)\n'''
        new = '''    public bool Tick(bool inCombat)\n    {\n        // Absolutely nothing is fired while mounted. Even a benign item/action can force a\n        // dismount and turn an IV/V/★ pull into a death.\n        if (Mount.IsMounted)\n        {\n            Abandon();\n            return false;\n        }\n\n        if (_config.AutoSurvivalLostActions && TrySurvival(travelling: false))\n            return true;\n\n        if (!_config.AutoUseLostActions || _config.AutoLostActions.Count == 0)\n'''
        t = once(t, old, new, "Tick survival prefix")

        marker = "    /// <summary>Reset the cooldown so the next engagement can open with an action.</summary>\n"
        methods = '''    /// <summary>\n    /// Survival-only pass used while travelling on foot. It intentionally only considers\n    /// instant candidates; movement is never paused to cast a heal.\n    /// </summary>\n    public bool TickTravelSurvival()\n    {\n        if (Mount.IsMounted || !_config.AutoSurvivalLostActions)\n        {\n            if (Mount.IsMounted)\n                Abandon();\n            return false;\n        }\n\n        return TrySurvival(travelling: true);\n    }\n\n    private bool TrySurvival(bool travelling)\n    {\n        var me = Svc.Objects.LocalPlayer;\n        if (me == null || me.CurrentHp == 0 || me.IsCasting)\n            return false;\n\n        var now = Environment.TickCount64;\n\n        // Finish an outstanding survival load before choosing anything else. A generic load is\n        // abandoned when we have already left the engagement and are now travelling.\n        if (_phase == Phase.WaitingForLoad)\n        {\n            if (_pendingSurvival)\n                return FinishLoad(now) || _phase == Phase.WaitingForLoad;\n            if (travelling)\n                Abandon();\n        }\n\n        if (now - _lastSurvivalUseMs < _config.SurvivalUseGapMs)\n            return false;\n\n        var holster = FieldState.Holster();\n        if (holster.Length == 0)\n            return false;\n\n        // Potion Kit is prophylaxis: maintain Auto-potion whenever naturally unmounted.\n        if (!_survival.HasAutoPotion()\n            && TrySurvivalNamed("Resistance Potion Kit", holster, now, targetSelf: false))\n            return true;\n\n        var hp = SurvivalPolicy.HpFraction();\n        var list = hp <= _survival.EmergencyThreshold\n            ? _survival.EmergencyPriority(travelling)\n            : hp <= _survival.HealThreshold\n                ? _survival.HealPriority(travelling)\n                : null;\n\n        if (list == null)\n            return false;\n\n        foreach (var name in list)\n            if (TrySurvivalNamed(name, holster, now, targetSelf: true))\n                return true;\n\n        return false;\n    }\n\n    private bool TrySurvivalNamed(string englishName, byte[] holster, long now, bool targetSelf)\n    {\n        var found = _survival.Find(englishName);\n        if (found is not { } entry || !_survival.AutoUseAllowed(entry))\n            return false;\n\n        if (LostActionStatuses.IsActive(entry.StatusId, out _))\n            return false;\n\n        if (entry.IsItem)\n        {\n            if (!UseItem(entry.RowId, entry, holster, now))\n                return false;\n            _lastSurvivalUseMs = now;\n            return true;\n        }\n\n        if (!entry.IsAction)\n            return false;\n\n        var outcome = UseAction(entry.RowId, entry, holster, now, targetSelf, survival: true);\n        if (outcome == Outcome.Fired)\n        {\n            _lastSurvivalUseMs = now;\n            return true;\n        }\n        return outcome == Outcome.Loading;\n    }\n\n'''
        t = once(t, marker, methods + marker, "travel survival methods")

    # overload action use with self-target flags
    t = t.replace(
        "    private Outcome UseAction(byte row, LostActionCatalog.Entry entry, byte[] holster, long now)\n    {",
        "    private Outcome UseAction(byte row, LostActionCatalog.Entry entry, byte[] holster, long now, bool targetSelf = false, bool survival = false)\n    {",
        1)

    old = '''            var press = DutyActions.Press(slot, entry.ActionId);\n            LastResult = press.Message;\n'''
    new = '''            var me = Svc.Objects.LocalPlayer;\n            var press = targetSelf && me != null\n                ? DutyActions.PressAt(slot, entry.ActionId, me.GameObjectId, me.Name.TextValue)\n                : DutyActions.Press(slot, entry.ActionId);\n            LastResult = press.Message;\n'''
    if old in t:
        t = t.replace(old, new, 1)

    if "_pendingTargetSelf = targetSelf;" not in t:
        t = once(t,
            "        _loadIssuedMs = now;\n        LastResult = $\"loading {entry.Name} into duty slot {DriverSlot + 1}\";",
            "        _loadIssuedMs = now;\n        _pendingTargetSelf = targetSelf;\n        _pendingSurvival = survival;\n        LastResult = $\"loading {entry.Name} into duty slot {DriverSlot + 1}\";",
            "pending target assignment")

    old = "        var result = DutyActions.Press(DriverSlot, _pendingActionId);\n\n        LastResult = result.Message;\n        _lastUseMs = now;\n        Abandon();"
    new = '''        var me = Svc.Objects.LocalPlayer;\n        var wasSurvival = _pendingSurvival;\n        var result = _pendingTargetSelf && me != null\n            ? DutyActions.PressAt(DriverSlot, _pendingActionId, me.GameObjectId, me.Name.TextValue)\n            : DutyActions.Press(DriverSlot, _pendingActionId);\n\n        LastResult = result.Message;\n        _lastUseMs = now;\n        if (wasSurvival && result.Fired)\n            _lastSurvivalUseMs = now;\n        Abandon();'''
    if old in t:
        t = t.replace(old, new, 1)

    if "_pendingTargetSelf = false;" not in t:
        t = once(t,
            "        _loadIssuedMs = 0;\n",
            "        _loadIssuedMs = 0;\n        _pendingTargetSelf = false;\n        _pendingSurvival = false;\n",
            "abandon self target")

    save(p, t)


def patch_controller() -> None:
    p = "Automation/BozjaController.cs"
    t = load(p)

    if "public string TravelRoute" not in t:
        anchor = "    public SharedObjective CurrentObjective => _lastObjective;\n"
        t = once(t, anchor, anchor + "    public string TravelRoute => _movement.RouteDescription;\n    public FieldTravelMode TravelMode => _movement.TravelMode;\n    public bool LifestreamAvailable => _movement.LifestreamAvailable;\n", "controller route diagnostics")

    if "_holster.TickTravelSurvival();" not in t:
        anchor = '''            // Approach and travel both drive vnavmesh, so exactly one of them may hold it. Hand\n            // it back BEFORE issuing the travel path, never after.\n            _approach.Release();\n\n            if (!_movement.TravelTo(destination, range))\n'''
        repl = '''            // Approach and travel both drive vnavmesh, so exactly one of them may hold it. Hand\n            // it back BEFORE issuing the travel path, never after.\n            _approach.Release();\n\n            // On-foot survival may use instant Lost Actions. The driver has an absolute mounted\n            // guard, so this can never be the reason a travelling mount is dismissed.\n            _holster.TickTravelSurvival();\n\n            if (!_movement.TravelTo(destination, range))\n'''
        t = once(t, anchor, repl, "controller travel survival")

    # User-visible status is Japanese; log strings stay untouched elsewhere.
    replacements = {
        'Status = "Yielding to BossMod - dodging a mechanic.";': 'Status = "BossModに移動制御を渡してギミックを回避しています。";',
        'Status = "vnavmesh could not start a path.";': 'Status = "vnavmeshで経路を開始できませんでした。";',
        'Status = "Under attack - dismounting to fight back.";': 'Status = "攻撃を受けています。反撃のためマウントから降りています。";',
    }
    for a, b in replacements.items():
        t = t.replace(a, b)

    # Include high-level BOCCHI route in the ordinary travelling status.
    old = '''            Status = $"Travelling to {Describe(objective)} ({distance:F0}y" +\n                     (_movement.RepathCount > 0 ? $", {_movement.RepathCount} repaths" : "") +'''
    new = '''            Status = $"{Describe(objective)}へ移動中 ({distance:F0}y / {_movement.RouteDescription}" +\n                     (_movement.RepathCount > 0 ? $", 再経路 {_movement.RepathCount}" : "") +'''
    if old in t:
        t = t.replace(old, new, 1)

    old = '''                Status = $"Travelling to {Describe(objective)} ({distance:F0}y) - " +\n                         $"routing around {enemy.Name} (Lv{enemy.Level}).";'''
    new = '''                Status = $"{Describe(objective)}へ移動中 ({distance:F0}y) - " +\n                         $"危険な敵 {enemy.Name} [{enemy.Strength switch { Game.FieldEnemyStrength.IV => \"IV\", Game.FieldEnemyStrength.V => \"V\", Game.FieldEnemyStrength.Star => \"★\", _ => \"?\" }}] を迂回中。";'''
    if old in t:
        t = t.replace(old, new, 1)

    save(p, t)


def patch_config_window() -> None:
    p = "Windows/ConfigWindow.cs"
    t = load(p)

    if "DrawSurvival();" not in t:
        anchor = '''        if (ImGui.BeginTabItem(Loc.T("Movement", "移動")))\n        {\n            DrawMovement();\n            ImGui.EndTabItem();\n        }\n'''
        block = '''        if (ImGui.BeginTabItem("生存"))\n        {\n            DrawSurvival();\n            ImGui.EndTabItem();\n        }\n\n'''
        t = once(t, anchor, block + anchor, "survival tab")

    # Replace the demonstrably wrong movement intro/flight controls with the v1.1 controls.
    old = '''        ImGui.TextColored(Grey,\n            "Neither Bozja nor Zadnor has a single aetheryte - there is no in-zone teleport of any\\n" +\n            "kind. Mount travel is the fast travel, so leaving mounting off means jogging the map.");\n        ImGui.Separator();\n'''
    new = '''        ImGui.TextColored(Grey,\n            "南方ボズヤ戦線・ザトゥノル高原のフィールド内エーテライトを利用できます。\\n" +\n            "BOCCHI方式で徒歩/マウント直行と簡易テレポ経路を比較し、速い方を選択します。");\n        ImGui.Separator();\n\n        var bocchi = _config.UseBocchiNavigation;\n        if (ImGui.Checkbox("BOCCHI方式の移動経路を使用する", ref bocchi))\n        {\n            _config.UseBocchiNavigation = bocchi;\n            Save();\n        }\n\n        var aethernet = _config.UseAethernetTravel;\n        if (ImGui.Checkbox("フィールド内の簡易テレポを使用する（Lifestream）", ref aethernet))\n        {\n            _config.UseAethernetTravel = aethernet;\n            Save();\n        }\n\n        var legacy = _config.LegacyMovement;\n        if (ImGui.Checkbox("非常用: 従来の直接移動を使用する", ref legacy))\n        {\n            _config.LegacyMovement = legacy;\n            Save();\n        }\n'''
    if old in t:
        t = t.replace(old, new, 1)

    old = '''        var fly = _config.AllowFlight;\n        if (ImGui.Checkbox("Allow flight", ref fly))\n        {\n            _config.AllowFlight = fly;\n            Save();\n        }\n        ImGui.TextColored(Grey,\n            "The flight path is only used once actually airborne - handing vnavmesh a flight path\\n" +\n            "while grounded gives it a route the character cannot follow, which stalls the run.");\n'''
    if old in t:
        t = t.replace(old, '''        ImGui.TextColored(Grey, "この2エリアではマウント飛行は使用しません。常に地上経路です。");\n\n        var direct = _config.NavigationMaxDirectWalkDistance;\n        ImGui.SetNextItemWidth(200);\n        if (ImGui.SliderFloat("直接移動を優先する距離 (y)", ref direct, 20f, 200f, "%.0f"))\n        {\n            _config.NavigationMaxDirectWalkDistance = direct;\n            Save();\n        }\n\n        var hop = _config.NavigationAethernetHopCost;\n        ImGui.SetNextItemWidth(200);\n        if (ImGui.SliderFloat("簡易テレポの時間換算コスト", ref hop, 10f, 150f, "%.0f"))\n        {\n            _config.NavigationAethernetHopCost = hop;\n            Save();\n        }\n''', 1)

    # Rank-specific enemy UI.
    old = '''        var minLevel = (int)_config.DangerousEnemyMinLevel;\n        ImGui.SetNextItemWidth(200);\n        if (ImGui.SliderInt("Only avoid level >=", ref minLevel, 0, 100))\n        {\n            _config.DangerousEnemyMinLevel = (byte)minLevel;\n            Save();\n        }\n        ImGui.TextColored(Grey, "0 avoids every hostile enemy. The list below shows what levels are really nearby.");\n'''
    if old in t:
        t = t.replace(old, '''        ImGui.TextColored(Grey,\n            "ボズヤ内の敵は通常Lv80のため、レベルではなく固有の強さ I～V / ★ を判定します。\\n" +\n            "I～IIIは無視し、IV・V・★・判定不能だけを迂回します。");\n\n        var star = _config.DangerStarExtraClearance;\n        ImGui.SetNextItemWidth(200);\n        if (ImGui.SliderFloat("★敵の追加安全距離 (y)", ref star, 0f, 20f, "%.0f"))\n        {\n            _config.DangerStarExtraClearance = star;\n            Save();\n        }\n''', 1)

    t = t.replace(
        '            ImGui.TextUnformatted($"Lv{z.Level,-3} {z.Name}   {Movement.DistanceToPlayer(z.Position):F0}y");',
        '            ImGui.TextUnformatted($"[{(z.Strength == FieldEnemyStrength.Star ? "★" : z.Strength == FieldEnemyStrength.Unknown ? "?" : ((byte)z.Strength).ToString())}] {z.Name}   {Movement.DistanceToPlayer(z.Position):F0}y   icon={z.NamePlateIconId}/{z.CharacterDataIcon}");',
        1)

    if "private void DrawSurvival()" not in t:
        marker = "    private void DrawMovement()\n"
        method = '''    private void DrawSurvival()\n    {\n        var enabled = _config.AutoSurvivalLostActions;\n        if (ImGui.Checkbox("生存優先のロストアクション自動使用", ref enabled))\n        {\n            _config.AutoSurvivalLostActions = enabled;\n            Save();\n        }\n        ImGui.TextColored(Grey,\n            "マウント中はロストアクションを一切使用しません。徒歩/戦闘中のみ、HPとロールを見て\\n" +\n            "ポーションキット・リレイザー・緊急防御・回復を使用します。");\n\n        DrawRole("Tank", ref _config.TankSurvivalHealFraction, ref _config.TankSurvivalEmergencyFraction);\n        DrawRole("Healer", ref _config.HealerSurvivalHealFraction, ref _config.HealerSurvivalEmergencyFraction);\n        DrawRole("DPS", ref _config.DpsSurvivalHealFraction, ref _config.DpsSurvivalEmergencyFraction);\n\n        return;\n\n        void DrawRole(string role, ref float heal, ref float emergency)\n        {\n            var h = heal * 100f;\n            var e = emergency * 100f;\n            ImGui.SetNextItemWidth(180);\n            if (ImGui.SliderFloat($"{role} 通常回復 (%)", ref h, 20f, 95f, "%.0f%%"))\n            {\n                heal = Math.Clamp(h / 100f, 0.2f, 0.95f);\n                Save();\n            }\n            ImGui.SetNextItemWidth(180);\n            if (ImGui.SliderFloat($"{role} 緊急 (%)", ref e, 10f, 80f, "%.0f%%"))\n            {\n                emergency = Math.Clamp(e / 100f, 0.1f, heal);\n                Save();\n            }\n        }\n    }\n\n'''
        t = once(t, marker, method + marker, "DrawSurvival method")

    save(p, t)


def patch_main_window() -> None:
    p = "Windows/MainWindow.cs"
    t = load(p)
    t = t.replace('$"Lost Actions: {_controller.LastLostAction}"', '$"ロストアクション: {_controller.LastLostAction}"')
    t = t.replace('"(set by the farm target)"', '"（RelicのFarm対象から自動設定）"')
    t = t.replace('ImGui.TextColored(Yellow, $"Not in a Bozja field zone (currently {BozjaZones.Name(territory)}).")', 'ImGui.TextColored(Yellow, $"南方ボズヤ戦線/ザトゥノル高原の外にいます（現在: {BozjaZones.Name(territory)}）。")')
    t = t.replace('ImGui.TextColored(Yellow, "Bozja director state is not initialised yet.");', 'ImGui.TextColored(Yellow, "ボズヤのフィールド状態を初期化待ちです。");')
    t = t.replace('ImGui.TextColored(Grey, "No Critical Engagements published for this zone.");', 'ImGui.TextColored(Grey, "現在参加可能なクリティカルエンゲージメントはありません。");')
    if "経路:" not in t:
        anchor = '''        ImGui.TextWrapped(_controller.Status);\n\n        // The Lost Action driver'''
        repl = '''        ImGui.TextWrapped(_controller.Status);\n\n        if (_controller.Running)\n        {\n            ImGui.TextColored(Grey, $"経路: {_controller.TravelRoute} / Lifestream: {(_controller.LifestreamAvailable ? "接続" : "未接続")}");\n            var me = Svc.Objects.LocalPlayer;\n            if (me != null && me.MaxHp > 0)\n                ImGui.TextColored(Grey, $"HP: {me.CurrentHp * 100f / me.MaxHp:F0}% / ロール: {SurvivalPolicy.CurrentRole()}");\n        }\n\n        // The Lost Action driver'''
        t = once(t, anchor, repl, "main route diagnostics")
    save(p, t)


def patch_version() -> None:
    p = "BozjaBuddyReborn.csproj"
    t = load(p)
    t = re.sub(r"<Version>[^<]+</Version>", "<Version>1.0.90.2</Version>", t, count=1)
    t = re.sub(r"<AssemblyVersion>[^<]+</AssemblyVersion>", "<AssemblyVersion>1.0.90.2</AssemblyVersion>", t, count=1)
    t = re.sub(r"<FileVersion>[^<]+</FileVersion>", "<FileVersion>1.0.90.2</FileVersion>", t, count=1)
    save(p, t)


if __name__ == "__main__":
    patch_config()
    patch_localization()
    patch_survival_policy()
    patch_aggro()
    patch_holster()
    patch_controller()
    patch_config_window()
    patch_main_window()
    patch_version()
    print("v1.1 survival/rank patch applied")
