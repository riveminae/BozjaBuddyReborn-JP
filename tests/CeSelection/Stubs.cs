// Host-only test doubles. Selection, configuration, region lookup, snapshot, and
// Relic tables are linked from production; no game process or IPC is started.
using System.Numerics;

namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace FFXIVClientStructs.FFXIV.Client.Game.InstanceContent
{
    public enum DynamicEventState : byte { Inactive, Register, Warmup, Battle }
}

namespace ECommons.DalamudServices
{
    public static class Svc
    {
        public static TestClientState ClientState { get; } = new();
        public static List<TestFate> Fates { get; } = [];
    }

    public sealed class TestClientState { public uint TerritoryType { get; set; } = 920; }
    public sealed class TestName { public string TextValue { get; set; } = "Test FATE"; }
    public sealed class TestFate
    {
        public uint FateId { get; set; }
        public byte Progress { get; set; }
        public Vector3 Position { get; set; }
        public float Radius { get; set; }
        public TestName Name { get; } = new();
    }
}

namespace BozjaBuddyReborn
{
    public static class Loc { public static bool Ja => true; }
    public static class ConfigSaver { public static bool Save(Configuration config) => true; }
}

namespace BozjaBuddyReborn.Multibox
{
    public enum ObjectiveKind : byte { None, CriticalEngagement, Fate }
    public readonly record struct SharedObjective(ObjectiveKind Kind, uint Id, Vector3 Position, uint Territory)
    {
        public bool IsSet => Kind != ObjectiveKind.None;
        public static SharedObjective None => default;
    }
}

namespace BozjaBuddyReborn.Game
{
    public enum FieldRegionId : byte { Unknown, Zone1, Zone2, Zone3 }
    public static class BozjaZones
    {
        public const uint BozjanSouthernFront = 920;
        public const uint Zadnor = 975;
        public static bool IsFieldZone(uint territory) => territory is 920 or 975;
        public static string Name(uint territory) => territory.ToString();
    }
    public static class FieldRegions
    {
        public static string Label(uint territory, FieldRegionId region) => $"{territory}:{region}";
        // Tests explicitly populate observed regions; no geometric inference is simulated.
        public static FieldRegionId ClassifyByPosition(uint territory, Vector3 position) => FieldRegionId.Unknown;
    }
    public sealed class CeCatalog
    {
        public bool IsLargeScale(ushort id) => id is 16 or 32;
    }
}

namespace BozjaBuddyReborn.Automation
{
    public sealed class Loadout { }
    public sealed class Movement
    {
        public float EstimateTravelCost(Vector3 position) => position.Length();
        public static float DistanceToPlayer(Vector3 position) => position.Length();
    }
}
