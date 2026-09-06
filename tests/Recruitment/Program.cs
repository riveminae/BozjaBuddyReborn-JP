using System.Reflection;
using System.Runtime.InteropServices;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.Game;
using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

try { Tests.Run(); }
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {(ex.InnerException ?? ex).Message}");
    Environment.ExitCode = 1;
}

internal static unsafe class Tests
{
    private static int _checks;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new Exception($"check {_checks}: {label}"); }
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static object? Invoke(SignUpRunner? runner, string method, params object[] args)
        => typeof(SignUpRunner).GetMethod(method, Private)!.Invoke(runner, args);
    private static void Set(SignUpRunner runner, string field, object value)
        => typeof(SignUpRunner).GetField(field, Private)!.SetValue(runner, value);
    private static object Box(AtkUnitBase* addon) => Pointer.Box(addon, typeof(AtkUnitBase*));
    private static List<LabelledButton> Buttons(UiFixture ui)
        => (List<LabelledButton>)Invoke(null, "CollectButtons", Box(ui.Addon))!;
    private static SignUpRunner Begin(ushort id = 20, Func<bool>? gate = null)
    {
        CriticalEngagements.RegisteredEventId = null;
        CriticalEngagements.CurrentEvent = null;
        CriticalEngagements.Events = [new(10, true, false), new(20, true, false)];
        ClickHelper.Calls = 0;
        var runner = new SignUpRunner(gate);
        runner.Begin(id);
        return runner;
    }

    public static void Run()
    {
        RecruitmentEvent[] events = [new(10, "一番", 1), new(20, "二番", 1), new(30, "三番", 1)];
        LabelledButton[] buttons = [new(101, "参加希望", "一番"), new(202, "参加希望", "二番"), new(303, "参加希望", "三番")];
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var eventOrder in orders)
        foreach (var buttonOrder in orders)
        for (var target = 0; target < 3; target++)
            Check(RecruitmentTargeting.Find(eventOrder.Select(i => events[i]).ToArray(),
                buttonOrder.Select(i => buttons[i]).ToArray(), events[target].Id, RecruitmentAction.Register)?.Button
                == buttons[target].Button, "event and UI orders are independent");

        LabelledButton? Find(RecruitmentEvent[]? e = null, LabelledButton[]? b = null, ushort id = 20,
            RecruitmentAction action = RecruitmentAction.Register) => RecruitmentTargeting.Find(e ?? events, b ?? buttons, id, action);
        Check(Find(id: 0) == null && Find(id: 99) == null, "unknown target");
        Check(Find(e: [events[1], events[1]]) == null, "duplicate IDs");
        Check(Find(e: [events[1], new(99, " 二番 ", 3)]) == null, "duplicate normalized names even in another state");
        Check(Find(e: [new(20, " ", 1)]) == null, "empty target name");
        Check(Find(b: [buttons[1], buttons[1]]) == null, "ambiguous matching buttons");
        Check(Find(b: [new(0, "参加希望", "二番")]) == null, "null handle");
        Check(Find(b: [new(1, "参加希望", "三番")]) == null, "other row");
        Check(Find(b: [new(1, "参加希望", "")]) == null, "no title");
        Check(Find(b: [new(1, "参加希望を取り消す", "二番")]) == null, "no Japanese label prefix");
        Check(Find(b: [new(1, "deploy now", "二番")]) == null, "no English label prefix");
        Check(Find(b: [new(1, "辞退", "二番")]) == null, "never withdraw");
        Check(Find(b: [new(1, "register unknown", "二番")]) == null, "unknown decorated label");
        foreach (byte state in new byte[] { 0, 2, 3, 255 })
            Check(Find(e: [new(20, "二番", state)]) == null, "registration requires Register state");
        foreach (var label in new[] { "戦闘突入", "commence", "deploy now", " COMMENCE " })
            Check(Find(e: [new(20, "二番", 2)], b: [new(1, label, "二番")], action: RecruitmentAction.Commence)?.Button == 1,
                "exact commence labels");
        Check(Find(action: RecruitmentAction.Commence) == null, "no commence in Register state");
        Check(Find(e: [new(20, "二番", 3)], b: [new(1, "戦闘突入", "二番")], action: RecruitmentAction.Commence) == null,
            "stale commence label in Underway");

        var data = new MycDynamicEventData { Count = 2,
            First = new() { Id = 20, Name = TestText.From("二番"), State = 1 },
            Second = new() { Id = 10, Name = TestText.From("一番"), State = 2 } };
        var agent = new AgentMycBattleAreaInfo { MycDynamicEventData = &data };
        var observed = (List<RecruitmentEvent>)Invoke(null, "ReadRecruitmentEvents", Pointer.Box(&agent, typeof(AgentMycBattleAreaInfo*)))!;
        Check(observed.Count == 2 && observed[0] == events[1] && observed[1] == new RecruitmentEvent(10, "一番", 2),
            "read only the published agent count with IDs, names, and state");
        data.Count = 4;
        Check(((List<RecruitmentEvent>)Invoke(null, "ReadRecruitmentEvents", Pointer.Box(&agent, typeof(AgentMycBattleAreaInfo*)))!).Count == 0,
            "invalid agent count refuses the entire snapshot");
        agent.MycDynamicEventData = null;
        Check(((List<RecruitmentEvent>)Invoke(null, "ReadRecruitmentEvents", Pointer.Box(&agent, typeof(AgentMycBattleAreaInfo*)))!).Count == 0,
            "unavailable agent snapshot");

        using var ui = new UiFixture();
        var first = ui.AddRow("一番", 101);
        var second = ui.AddRow("二番", 202);
        List<RecruitmentEvent> recruitment = [events[0], events[1]];
        Check(Buttons(ui).Count == 2, "collect separate native row scopes");
        second.Button->IsEnabled = false;
        Check(RecruitmentTargeting.Find(recruitment, Buttons(ui), 20, RecruitmentAction.Register) == null, "disabled target has no fallback");
        second.Button->IsEnabled = true;
        second.Owner->AtkResNode.Visible = false;
        Check(Buttons(ui).Count == 1, "hidden ancestor hides the row");
        second.Owner->AtkResNode.Visible = true;
        second.Actions->Visible = false;
        Check(Buttons(ui).Count == 1, "hidden action parent");
        second.Actions->Visible = true;
        second.Title->AtkResNode.Visible = false;
        Check(Buttons(ui).Count == 1, "hidden title");
        second.Title->AtkResNode.Visible = true;
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.ParentNode = second.Root;
        Check(Buttons(ui).Count == 1, "button outside action container");
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.ParentNode = second.Actions;
        second.Title->AtkResNode.ParentNode = second.Actions;
        Check(Buttons(ui).Count == 1, "title outside row root");
        second.Title->AtkResNode.ParentNode = second.Root;
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.Visible = false;
        Check(Buttons(ui).Count == 1, "hidden button");
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.Visible = true;
        Check(Buttons(ui).Count == 2, "restored row shape");
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.NodeId = 99;
        Check(Buttons(ui).Count == 1, "unknown action layout fails closed");
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.NodeId = 10;

        var runner = Begin();
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1 && ClickHelper.LastParam == 202, "runner sends the selected row's attached event");
        Check(runner.Phase == SignUpPhase.AwaitingSelection && runner.Status.Contains("登録先の確認"), "click is not acknowledgement");
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1, "click settle prevents a second operation");
        Set(runner, "_clickSettleUntilMs", 0L);
        CriticalEngagements.RegisteredEventId = 20;
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1 && runner.Status.Contains("参加申請済み"), "actual matching registration acknowledged");
        Set(runner, "_lapsedSinceMs", Environment.TickCount64 - 4000);
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(runner.Phase == SignUpPhase.AwaitingSelection && ClickHelper.Calls == 1, "registered target never re-registers");

        first.Label->NodeText = TestText.From("戦闘突入");
        recruitment[0] = new(10, "一番", 2);
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1, "another row's commence is ignored");
        second.Label->NodeText = TestText.From("戦闘突入");
        recruitment[1] = new(20, "二番", 2);
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 2 && ClickHelper.LastParam == 202 && runner.Phase == SignUpPhase.Commencing, "commence same target");
        Set(runner, "_clickSettleUntilMs", 0L);
        Invoke(runner, "StepCommencing", new List<LabelledButton>(), recruitment);
        Check(runner.Phase != SignUpPhase.Done, "button disappearance alone is not success");
        CriticalEngagements.CurrentEvent = new(20, false, true);
        Invoke(runner, "StepCommencing", Buttons(ui), recruitment);
        Check(runner.Phase != SignUpPhase.Done, "still-visible commence is not acknowledged");
        CriticalEngagements.CurrentEvent = new(10, false, true);
        Invoke(runner, "StepCommencing", new List<LabelledButton>(), recruitment);
        Check(runner.Phase != SignUpPhase.Done, "another running event is not success");
        CriticalEngagements.CurrentEvent = new(20, false, true);
        Invoke(runner, "StepCommencing", new List<LabelledButton>(), recruitment);
        Check(!runner.Active && runner.Phase == SignUpPhase.Done, "matching participation plus removed commence");

        first.Label->NodeText = TestText.From("参加希望");
        second.Label->NodeText = TestText.From("参加希望");
        recruitment = [events[0], events[1]];
        runner = Begin();
        CriticalEngagements.RegisteredEventId = 10;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(!runner.Active && ClickHelper.Calls == 0, "mismatched existing registration stops without a second signup");
        runner = Begin();
        CriticalEngagements.RegisteredEventId = 20;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(runner.Active && ClickHelper.Calls == 0, "matching existing registration does not click Register");
        runner = Begin(0);
        CriticalEngagements.RegisteredEventId = 10;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(runner.Active && ClickHelper.Calls == 0, "untargeted manual action preserves existing registration");
        runner = Begin();
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Set(runner, "_clickSettleUntilMs", 0L);
        CriticalEngagements.RegisteredEventId = 10;
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(!runner.Active && ClickHelper.Calls == 1, "mismatched acknowledgement stops");

        runner = Begin();
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Set(runner, "_clickSettleUntilMs", 0L);
        Set(runner, "_lapsedSinceMs", Environment.TickCount64 - 4000);
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), new List<LabelledButton> { Buttons(ui)[0] }, recruitment);
        Check(runner.Phase == SignUpPhase.AwaitingSelection, "another Register row cannot mark our registration lapsed");
        Set(runner, "_lapsedSinceMs", Environment.TickCount64 - 4000);
        Invoke(runner, "StepAwaitSelection", Box(ui.Addon), Buttons(ui), recruitment);
        Check(runner.Phase == SignUpPhase.Registering, "our unacknowledged Register can retry after the grace period");

        var safe = false;
        runner = Begin(20, () => safe);
        second.Label->NodeText = TestText.From("戦闘突入");
        recruitment[1] = new(20, "二番", 2);
        CriticalEngagements.RegisteredEventId = 20;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 0, "critical survival supply holds commence");
        safe = true;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1 && ClickHelper.LastParam == 202, "recovered supply commences the same target");
        Set(runner, "_clickSettleUntilMs", 0L);
        CriticalEngagements.RegisteredEventId = 10;
        Invoke(runner, "StepCommencing", new List<LabelledButton>(), recruitment);
        Check(!runner.Active && runner.Phase != SignUpPhase.Done, "mismatched registration during commence is not success");

        runner = Begin(20, () => false);
        second.Label->NodeText = TestText.From("参加希望");
        recruitment[1] = events[1];
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1, "critical supply never delays remote registration");

        runner = Begin();
        second.Label->NodeText = TestText.From("参加希望");
        recruitment[1] = events[1];
        second.Event->State.EventType = AtkEventType.MouseOver;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 0, "non-click attached event is not invoked");
        second.Event->State.EventType = AtkEventType.ButtonClick;
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.AtkEventManager.Event = null;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 0, "missing attached event is not guessed");
        second.Event->State.EventType = AtkEventType.MouseOver;
        second.Event->NextEvent = first.Event;
        second.Button->AtkComponentBase.OwnerNode->AtkResNode.AtkEventManager.Event = second.Event;
        Invoke(runner, "StepRegister", Box(ui.Addon), Buttons(ui), recruitment);
        Check(ClickHelper.Calls == 1 && ClickHelper.LastParam == 101, "uses an attached click event's actual parameter, not its row index");
        Console.WriteLine($"PASS: {_checks} recruitment checks (production runner/collector/targeting; synthetic host only).");
    }
}

internal sealed unsafe class UiFixture : IDisposable
{
    private readonly List<nint> _owned = [];
    public AtkUnitBase* Addon;
    private T* Allocate<T>(int count = 1) where T : unmanaged
    {
        var pointer = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * count));
        _owned.Add((nint)pointer);
        return pointer;
    }
    public UiFixture()
    {
        Addon = Allocate<AtkUnitBase>();
        Addon->IsVisible = true;
        Addon->UldManager = new() { LoadedState = AtkLoadState.Loaded, NodeList = (AtkResNode**)Allocate<nint>(8) };
    }
    public Row AddRow(string name, uint param)
    {
        var root = Allocate<AtkResNode>(); *root = new() { NodeId = 1, Type = NodeType.Res, Visible = true };
        var title = Allocate<AtkTextNode>(); title->AtkResNode = new() { NodeId = 5, Type = NodeType.Text, ParentNode = root, Visible = true };
        title->NodeText = TestText.From(name);
        var actions = Allocate<AtkResNode>(); *actions = new() { NodeId = 9, Type = NodeType.Res, ParentNode = root, Visible = true };
        var row = Allocate<AtkComponentBase>();
        var owner = Allocate<AtkComponentNode>(); owner->AtkResNode = new() { NodeId = (uint)(100 + Addon->UldManager.NodeListCount), Type = NodeType.Component, Visible = true };
        owner->Component = row; row->OwnerNode = owner;
        Addon->UldManager.NodeList[Addon->UldManager.NodeListCount++] = (AtkResNode*)owner;
        var buttonNode = Allocate<AtkComponentNode>(); buttonNode->AtkResNode = new() { NodeId = 10, Type = NodeType.Component, ParentNode = actions, Visible = true };
        var button = Allocate<AtkComponentButton>(); button->IsEnabled = true;
        buttonNode->Component = (AtkComponentBase*)button;
        button->AtkComponentBase = new() { OwnerNode = buttonNode, Type = ComponentType.Button };
        var label = Allocate<AtkTextNode>(); label->NodeText = TestText.From("参加希望"); button->ButtonTextNode = label;
        var attached = Allocate<AtkEvent>(); attached->State.EventType = AtkEventType.ButtonClick; attached->Param = param;
        buttonNode->AtkResNode.AtkEventManager.Event = attached;
        row->UldManager = new() { LoadedState = AtkLoadState.Loaded, NodeListCount = 4, NodeList = (AtkResNode**)Allocate<nint>(4) };
        row->UldManager.NodeList[0] = root; row->UldManager.NodeList[1] = (AtkResNode*)title;
        row->UldManager.NodeList[2] = actions; row->UldManager.NodeList[3] = (AtkResNode*)buttonNode;
        return new() { Root = root, Title = title, Actions = actions, Owner = owner, Button = button, Label = label, Event = attached };
    }
    public void Dispose() { foreach (var pointer in _owned) NativeMemory.Free((void*)pointer); _owned.Clear(); }
    public struct Row
    {
        public AtkResNode* Root;
        public AtkTextNode* Title;
        public AtkResNode* Actions;
        public AtkComponentNode* Owner;
        public AtkComponentButton* Button;
        public AtkTextNode* Label;
        public AtkEvent* Event;
    }
}
