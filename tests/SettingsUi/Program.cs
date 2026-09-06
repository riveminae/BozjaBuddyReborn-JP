using System.Numerics;
using System.Text.Json;
using BozjaBuddyReborn;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Relic;
using BozjaBuddyReborn.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using SettingsUiTests;
using Native = Dalamud.Bindings.ImGui.ImGui;
using GameAction = Lumina.Excel.Sheets.Action;

try { Run(); }
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    Environment.ExitCode = 1;
}

static unsafe void Run()
{
    if (!OperatingSystem.IsWindows()) throw new Exception("Native settings test requires Windows.");
    var checks = 0;
    void Check(bool ok, string label) { checks++; if (!ok) throw new Exception($"check {checks}: {label}"); }
    var rows = new (byte Row, string English, string Japanese, byte Type)[] {
        (1, "Resistance Potion Kit", "レジスタンスポーションキット", 2),
        (4, "Lost Protect", "ロスト・プロテス", 1),
        (5, "Lost Utility Test", "検索対象の試験アクション", 1),
        (26, "Lost Cure IV", "ロスト・ケアルジャ", 1),
        (56, "Deep Essence of the Bloodsucker", "吸血鬼の秘薬", 2),
        (57, "Essence of the Bloodsucker", "吸血鬼の薬", 2),
        (97, "Lost Full Cure", "ロスト・フルケア", 1) };
    var japanese = rows.ToDictionary(r => (uint)r.Row + 100, r => new GameAction((uint)r.Row + 100, new(r.Japanese), r.Row == 4));
    Svc.Data.Sheets[(typeof(GameAction), ClientLanguage.English)] = new TestSheet<GameAction>(
        rows.ToDictionary(r => (uint)r.Row + 100, r => new GameAction((uint)r.Row + 100, new(r.English), r.Row == 4)));
    Svc.Data.Sheets[(typeof(GameAction), ClientLanguage.Japanese)] = new TestSheet<GameAction>(japanese);
    Svc.Data.Sheets[(typeof(MYCTemporaryItem), ClientLanguage.English)] = new TestSheet<MYCTemporaryItem>(
        rows.ToDictionary(r => (uint)r.Row, r => new MYCTemporaryItem(r.Row, new((uint)r.Row + 100), Type: r.Type)));
    var config = new Configuration();
    config.AutoLostActions.Add(5);
    var catalog = new LostActionCatalog();
    var policy = new SurvivalPolicy(config, catalog);
    var window = new ConfigWindow(config, catalog, new(), new(), new());
    var relic = new RelicWindow(new RelicTracker(), config);
    // Let the pre-change production window compile, then fail its actual missing-tab assertion.
    typeof(ConfigWindow).GetProperty("DrawRelicContents")?.SetValue(window, (System.Action)relic.Draw);
    var context = Native.CreateContext();
    var frames = 0;
    var clicks = 0;
    var maxVertices = 0;
    try
    {
        var io = Native.GetIO();
        io.IniFilename = null;
        io.DisplaySize = new(1280, 1024);
        io.DeltaTime = 1f / 60;
        io.Fonts.AddFontDefault();
        Check(io.Fonts.Build(), "native font atlas builds");

        void Frame(System.Action? draw = null)
        {
            if (++frames > 450) throw new Exception("Native frame ceiling reached.");
            ImGuiProbe.Items.Clear();
            Native.NewFrame();
            Native.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
            Native.SetNextWindowSize(new Vector2(1100, 960), ImGuiCond.Always);
            Native.Begin("設定試験###headless", ImGuiWindowFlags.NoSavedSettings);
            (draw ?? window.Draw)();
            Native.End();
            Native.Render();
            maxVertices = Math.Max(maxVertices, Native.GetDrawData().TotalVtxCount);
        }
        bool Has(string label, string kind = "text") => ImGuiProbe.Items.Any(i => i.Label == label && i.Kind == kind);
        void Click(string label, string kind)
        {
            var matches = ImGuiProbe.Items.Where(i => i.Label == label && i.Kind == kind && i.Visible).ToArray();
            if (matches.Length != 1) throw new Exception($"Visible native target must be unique: {kind} {label}; count={matches.Length}");
            var item = matches[0];
            var point = new Vector2(item.Min.X + Math.Min(12, (item.Max.X - item.Min.X) / 2), (item.Min.Y + item.Max.Y) / 2);
            io.AddMousePosEvent(point.X, point.Y); Frame();
            io.AddMouseButtonEvent(0, true); Frame();
            io.AddMouseButtonEvent(0, false); Frame(); Frame();
            clicks++;
        }
        void Search(string text)
        {
            Click("名前で検索##permissions", "input");
            io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame();
            io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.ModCtrl, false); Frame();
            io.AddInputCharacters(text); Frame(); Frame();
        }
        Frame(); Frame(); Frame();
        Check(maxVertices > 0, "real native ImGui emits draw vertices");
        var expectedTabs = new[] { "周回", "生存", "ロストアクション", "移動", "レジスタンスウェポン", "詳細設定" };
        Check(ImGuiProbe.Items.Where(i => i.Kind == "tab" && i.TabDepth == 1).Select(i => i.Label).SequenceEqual(expectedTabs), "exactly six required top-level tabs in order");
        Check(ConfigSaver.Saves == 0 && config.FarmMaterialItemId == 0, "drawing settings never saves or selects an initial relic target");

        Click("戦闘の連携設定", "header");
        Check(Has("BossMod: AoE回避", "checkbox"), "existing combat integration controls remain reachable");
        Click("戦闘の連携設定", "header");

        Click("生存", "tab");
        Check(Has("生存優先のロストアクション自動使用", "checkbox"), "existing survival settings remain reachable");
        Click("ロストアクション", "tab");
        Check(Has("生存プリセット", "tab") && Has("パーティ支援", "tab"), "independent Lost Action sections exist with ordinary use off");
        Click("生存プリセット", "tab");
        Check(Has("レジスタンスポーションキット") && Has("吸血鬼の秘薬"), "normal preset comes from role policy and uses Japanese names");
        Check(!Has("検索対象の試験アクション"), "unrelated utility is not in the normal survival preset");
        Check(catalog.TryGet(56, out var deep) && !policy.BringAllowed(deep) && !policy.AutoUseAllowed(deep), "rare defaults remain denied");

        Click("持込##bring56", "checkbox");
        Check(policy.BringAllowed(deep) && !policy.AutoUseAllowed(deep), "native carry click changes only carry permission");
        Click("自動使用##use56", "checkbox");
        Check(policy.BringAllowed(deep) && policy.AutoUseAllowed(deep), "both permissions can be on");
        Click("持込##bring56", "checkbox");
        Check(!policy.BringAllowed(deep) && policy.AutoUseAllowed(deep), "carry off/use on is reachable");
        Click("自動使用##use56", "checkbox");
        Check(!policy.BringAllowed(deep) && !policy.AutoUseAllowed(deep), "both can return off");
        Check(config.AutoLostActions.SequenceEqual(new byte[] { 5 }) && ConfigSaver.Saves == 4, "permission clicks preserve ordinary selection and save once each");
        var saved = JsonSerializer.Deserialize<Configuration>(ConfigSaver.LastSaved, new JsonSerializerOptions { IncludeFields = true })!;
        Check(saved.LostActionBringPermissions[56] == false && saved.LostActionAutoUsePermissions[56] == false, "separate dictionaries survive serialized save");

        Svc.Data.Role = 4; Frame();
        Check(Has("ロスト・フルケア") && !Has("ロスト・ケアルジャ"), "healer preset changes with production role policy");
        Svc.Data.Role = 2; Frame();
        Check(Has("ロスト・ケアルジャ") && !Has("ロスト・フルケア"), "damage-role preset restores its own recovery candidate");
        Svc.Objects.LocalPlayer = null; Frame();
        Check(!ImGuiProbe.Items.Any(i => i.Kind == "checkbox" && i.Label.StartsWith("持込##")), "unknown role does not invent a normal preset");
        Svc.Objects.LocalPlayer = new(); Frame();
        Svc.Data.Sheets.Remove((typeof(GameAction), ClientLanguage.Japanese)); Frame();
        Check(!ImGuiProbe.Items.Any(i => i.Kind == "text" && rows.Any(r => r.English == i.Label)), "missing Japanese sheet never exposes English names");
        Check(ImGuiProbe.Items.Any(i => i.Kind == "text" && i.Label.Contains("名称未取得")), "missing Japanese names are explicit");
        Svc.Data.Sheets[(typeof(GameAction), ClientLanguage.Japanese)] = new TestSheet<GameAction>(japanese); Frame();
        Check(Has("ロスト・ケアルジャ"), "Japanese names retry when the sheet returns");
        Click("パーティ支援", "tab");
        Check(Has("使用間隔 (ms)", "slider"), "party controls remain reachable with ordinary use disabled");
        Check(ImGuiProbe.Items.Any(i => i.Kind == "checkbox" && i.Label.StartsWith("ロスト・プロテス"))
            && !ImGuiProbe.Items.Any(i => i.Kind == "checkbox" && i.Label.StartsWith("Lost Protect")), "party action names also stay Japanese on the English host");

        Click("詳細設定", "tab");
        Check(Has("名前で検索##permissions", "input"), "advanced full-catalog search is reachable");
        Search("検索対象");
        Check(Has("検索対象の試験アクション") && !Has("吸血鬼の秘薬") && !Has("レジスタンスポーションキット"), "native text entry filters the complete catalog");
        Click("持込##bring5", "checkbox");
        Check(config.LostActionBringPermissions[5] == false && !config.LostActionAutoUsePermissions.ContainsKey(5), "advanced carry control also remains independent");
        Click("自動使用##use5", "checkbox");
        Check(config.LostActionAutoUsePermissions[5] == false, "advanced full-catalog use control is wired");
        Click("経路の詳細設定", "header");
        Check(Has("直接移動を優先する距離 (y)", "slider") && Has("簡易テレポの時間換算コスト", "slider")
            && Has("デジョンの時間換算コスト", "slider"), "all three navigation costs are available in advanced settings");
        var previousCost = config.NavigationReturnCost;
        Click("デジョンの時間換算コスト", "slider");
        Check(config.NavigationReturnCost != previousCost && config.NavigationReturnCost >= 10, "native cost slider changes the existing bounded value");
        Click("経路の詳細設定", "header");
        Click("通常使用の優先候補", "header");
        Check(ImGuiProbe.Items.Any(i => i.Kind == "checkbox" && i.Label.EndsWith("##la5")), "ordinary priority selection is preserved in advanced settings");
        Click("通常使用の優先候補", "header");

        Click("移動方式と表示の切り替え", "header");
        Check(Has("非常用: 従来の直接移動を使用する", "checkbox"), "legacy movement escape remains available");
        Click("非常用: 従来の直接移動を使用する", "checkbox");
        Check(config.LegacyMovement, "native legacy escape is wired");
        Click("非常用: 従来の直接移動を使用する", "checkbox");
        Check(!config.LegacyMovement, "legacy escape can be restored");
        var testEdition = typeof(ConfigWindow).Assembly.GetName().Version is { Major: 1, Minor: 0, Build: 90 };
        Check(Has("BOCCHI方式の移動経路を使用する", "checkbox") == testEdition
            && Has("テスト用: 経路・危険敵をworld上に表示する", "checkbox") == testEdition, "test-only navigation switches follow assembly edition");
        if (testEdition)
        {
            Click("BOCCHI方式の移動経路を使用する", "checkbox");
            Check(!config.UseBocchiNavigation, "test navigation switch is wired");
            Click("BOCCHI方式の移動経路を使用する", "checkbox");
            Check(config.UseBocchiNavigation, "test navigation switch restores the default");
            Check(!config.DebugWorldOverlay, "debug overlay remains default off");
            Click("テスト用: 経路・危険敵をworld上に表示する", "checkbox");
            Check(config.DebugWorldOverlay, "test overlay switch is wired");
            Click("テスト用: 経路・危険敵をworld上に表示する", "checkbox");
            Check(!config.DebugWorldOverlay, "test overlay can return off");
        }
        Click("移動方式と表示の切り替え", "header");
        Click("移動", "tab");
        Check(Has("長距離移動ではマウントを使用する", "checkbox"), "existing mount setting remains reachable");
        Click("デジョンを使う経路を許可する", "checkbox");
        Check(!config.UseReturnRouting, "native return-routing control writes the existing value");
        Click("レジスタンスウェポン", "tab");
        Check(Has("南方ボズヤ戦線: 解放済み") && Has("周回開始##farm1000", "button"), "existing relic renderer and explicit material selection are embedded");
        Check(config.FarmMaterialItemId == 0, "opening relic tab never selects the first material");
        Click("停止条件", "combo");
        Click("指定素材完了で停止", "selectable");
        Check(config.RelicFarmStopMode == RelicFarmStopMode.SelectedMaterialComplete, "selected-material stop mode can be chosen");
        Click("停止条件", "combo");
        Click("現在段階完了で停止", "selectable");
        Check(config.RelicFarmStopMode == RelicFarmStopMode.CurrentStageComplete, "current-stage stop mode can be chosen");
        Click("停止条件", "combo");
        Click("無限周回", "selectable");
        Check(config.RelicFarmStopMode == RelicFarmStopMode.Unlimited, "unlimited mode can be restored");
        Click("完了後は次の不足素材へ継続する", "checkbox");
        Check(!config.RelicAutoContinue, "continuation remains its existing independent setting");
        Click("周回開始##farm1000", "button");
        Check(config.FarmMaterialItemId == 1000, "only explicit native material button selects a target");
        Frame(relic.Draw); Frame(relic.Draw);
        Check(Has("停止##farm1000", "button"), "standalone relic window still uses the same selection");
        Check(maxVertices > 100 && clicks >= 25, "nontrivial native draw and input journey completed");
        Console.WriteLine($"PASS: {checks} settings UI checks; edition={typeof(ConfigWindow).Assembly.GetName().Version}; native frames={frames}, clicks={clicks}, maxVertices={maxVertices}; game/services synthetic, no desktop/game input.");
    }
    finally { Native.DestroyContext(context); }
}
