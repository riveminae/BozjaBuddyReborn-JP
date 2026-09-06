// Only host/game services are replaced. The runner, row collector, and targeting are production code.
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BozjaBuddyReborn
{
    internal static class Loc { public static string T(string en, string ja) => ja; }
}
namespace BozjaBuddyReborn.Automation
{
    internal enum ControllerState { Blocked }
    internal static class DiagnosticsRecorder
    { public static void Warning(string status, ControllerState state) { } }
}
namespace BozjaBuddyReborn.Game
{
    internal static class FieldState { public static bool InFieldZone => true; }
    internal static class BozjaZones { public static string Name(ushort id) => id.ToString(); }
    internal readonly record struct TestEvent(ushort EventId, bool IsJoinable, bool IsRunning);
    internal static class CriticalEngagements
    {
        public static ushort? RegisteredEventId;
        public static TestEvent? CurrentEvent;
        public static List<TestEvent> Events = [];
        public static TestEvent? Current(object? catalog) => CurrentEvent;
        public static List<TestEvent> Read(object? catalog) => Events;
    }
}
namespace Dalamud.Game.ClientState.Conditions
{
    internal enum ConditionFlag { BetweenAreas, BetweenAreas51, OccupiedInCutSceneEvent }
}
namespace ECommons.DalamudServices
{
    internal static class Svc
    {
        public static readonly TestLogger Log = new();
        public static readonly TestFramework Framework = new();
        public static readonly TestObjects Objects = new();
        public static readonly TestCondition Condition = new();
        public static readonly TestClient ClientState = new();
    }
    internal sealed class TestLogger
    {
        public void Information(string text) { }
        public void Warning(string text) { }
        public void Error(Exception ex, string text) { }
    }
    internal sealed class TestFramework { public bool IsInFrameworkUpdateThread => true; }
    internal sealed class TestObjects { public object LocalPlayer => this; }
    internal sealed class TestClient { public ushort TerritoryType => 920; }
    internal sealed class TestCondition
    { public bool this[Dalamud.Game.ClientState.Conditions.ConditionFlag flag] => false; }
}
namespace ECommons.UIHelpers
{
    internal static class AddonFinder { public static List<TestYesNo> YesNo = []; }
    internal sealed class TestYesNo
    {
        public string Text = "";
        public bool RespectDisabledButtons;
        public void Yes() { }
    }
}
namespace ECommons.Automation.UIInput
{
    internal enum EventType { MouseClick = 1, ButtonClick = 2 }
    internal sealed class EventData : IDisposable
    {
        public static unsafe EventData ForNormalTarget(AtkComponentNode* node, AtkUnitBase* addon) => new();
        public void Dispose() { }
    }
    internal sealed class InputData : IDisposable
    {
        public static InputData Empty() => new();
        public void Dispose() { }
    }
    internal static class ClickHelper
    {
        public static int Calls;
        public static uint LastParam;
        public static unsafe void InvokeReceiveEvent(void* listener, EventType type, uint param, EventData data, InputData input)
        { Calls++; LastParam = param; }
    }
}
namespace FFXIVClientStructs.FFXIV.Component.GUI
{
    internal enum AtkLoadState { Loaded = 3 }
    internal enum NodeType : ushort { Res = 1, Text = 3, Component = 1007 }
    internal enum ComponentType { Custom, Button }
    internal enum AtkEventType { MouseClick = 1, ButtonClick = 2, MouseOver = 3 }
    internal unsafe struct AtkEvent { public EventState State; public uint Param; public AtkEvent* NextEvent; }
    internal struct EventState { public AtkEventType EventType; }
    internal unsafe struct AtkEventManager { public AtkEvent* Event; }
    internal struct TestText
    {
        private int _id;
        private static readonly List<string> Values = [""];
        public static TestText From(string value) { Values.Add(value); return new() { _id = Values.Count - 1 }; }
        public override string ToString() => Values[_id];
    }
    internal unsafe struct AtkResNode
    {
        public uint NodeId;
        public NodeType Type;
        public AtkResNode* ParentNode;
        public AtkEventManager AtkEventManager;
        public bool Visible;
        public bool IsVisible() => Visible;
        public uint GetBaseNodeId() => NodeId;
    }
    internal unsafe struct AtkComponentNode { public AtkResNode AtkResNode; public AtkComponentBase* Component; }
    internal struct AtkTextNode { public AtkResNode AtkResNode; public TestText NodeText; }
    internal unsafe struct AtkComponentBase
    {
        public AtkComponentNode* OwnerNode;
        public AtkUldManager UldManager;
        public ComponentType Type;
        public ComponentType GetComponentType() => Type;
    }
    internal unsafe struct AtkComponentButton
    {
        public AtkComponentBase AtkComponentBase;
        public AtkTextNode* ButtonTextNode;
        public bool IsEnabled;
    }
    internal unsafe struct AtkUldManager
    { public AtkLoadState LoadedState; public AtkResNode** NodeList; public ushort NodeListCount; }
    internal struct AtkUnitBase { public bool IsVisible; public AtkUldManager UldManager; public byte AtkEventListener; }
}
namespace FFXIVClientStructs.FFXIV.Client.UI.Agent
{
    internal enum AgentId { MycBattleAreaInfo }
    internal unsafe struct AgentModule
    {
        public static AgentModule* Instance() => null;
        public void* GetAgentByInternalId(AgentId id) => null;
    }
    internal struct AgentInterface
    {
        public bool IsAgentActive() => false;
        public void Show() { }
        public uint GetAddonId() => 0;
    }
    internal unsafe struct AgentMycBattleAreaInfo { public MycDynamicEventData* MycDynamicEventData; }
    internal struct MycDynamicEvent { public ushort Id; public byte State; public TestText Name; }
    internal unsafe struct MycDynamicEventData
    {
        public byte Count;
        public MycDynamicEvent First;
        public MycDynamicEvent Second;
        public MycDynamicEvent Third;
        public Span<MycDynamicEvent> Array
        { get { fixed (MycDynamicEvent* pointer = &First) return new(pointer, 3); } }
    }
}
namespace FFXIVClientStructs.FFXIV.Client.UI
{
    internal unsafe struct RaptureAtkUnitManager
    {
        public static RaptureAtkUnitManager* Instance() => null;
        public AtkUnitBase* GetAddonById(ushort id) => null;
    }
}
