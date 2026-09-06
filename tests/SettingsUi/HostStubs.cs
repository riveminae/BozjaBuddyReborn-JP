// Only game/services and the outer Dalamud window host are synthetic. Production settings,
// catalog, permission policy and both windows are linked; widgets use native ImGui.
using System.Numerics;
using System.Collections;

namespace Dalamud.Configuration { public interface IPluginConfiguration { int Version { get; set; } } }
namespace Dalamud.Game { public enum ClientLanguage { English, Japanese } }
namespace Dalamud.Interface.Windowing
{
    public abstract class Window(string name)
    {
        public string WindowName => name;
        public WindowSizeConstraints SizeConstraints;
        public abstract void Draw();
    }
    public struct WindowSizeConstraints { public Vector2 MinimumSize, MaximumSize; }
}
namespace BozjaBuddyReborn
{
    public static class Loc { public static string T(string en, string ja) => ja; }
    public static class ConfigSaver
    {
        public static int Saves;
        public static string LastSaved = "";
        public static void Save(Configuration config)
        {
            Saves++;
            LastSaved = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        }
    }
}
namespace Lumina.Excel.Sheets
{
    public readonly record struct TestText(string TextValue) { public string ExtractText() => TextValue; }
    public readonly record struct RowRef(uint RowId);
    public readonly record struct Action(uint RowId, TestText Name, bool CanTargetParty = false, byte Range = 30, byte DeadTargetBehaviour = 0);
    public readonly record struct Item(uint RowId, TestText Name);
    public readonly record struct Status(uint RowId, TestText Name);
    public readonly record struct ClassJob(uint RowId, byte Role);
    public readonly record struct MYCTemporaryItem(uint RowId, RowRef Action, byte Weight = 1, byte Max = 99, byte Type = 2);
}
namespace ECommons.DalamudServices
{
    public static class Svc
    {
        public static readonly TestData Data = new();
        public static readonly TestObjects Objects = new();
        public static readonly TestClientState ClientState = new();
    }
    public sealed class TestClientState { public uint TerritoryType = 920; }
    public sealed class TestObjects { public TestPlayer? LocalPlayer = new(); }
    public sealed class TestPlayer
    {
        public uint CurrentHp = 100, MaxHp = 100;
        public Lumina.Excel.Sheets.RowRef ClassJob = new(1);
    }
    public sealed class TestData
    {
        public readonly Dictionary<(Type, Dalamud.Game.ClientLanguage), object> Sheets = [];
        public byte Role = 1;
        public TestSheet<T>? GetExcelSheet<T>(Dalamud.Game.ClientLanguage language = Dalamud.Game.ClientLanguage.English) where T : struct
        {
            if (typeof(T) == typeof(Lumina.Excel.Sheets.ClassJob))
                return (TestSheet<T>)(object)new TestSheet<Lumina.Excel.Sheets.ClassJob>(new() { [1] = new(1, Role) });
            return Sheets.TryGetValue((typeof(T), language), out var sheet) ? (TestSheet<T>)sheet : null;
        }
    }
    public sealed class TestSheet<T>(Dictionary<uint, T> rows) : IEnumerable<T> where T : struct
    {
        public T? GetRowOrDefault(uint id) => rows.TryGetValue(id, out var row) ? row : null;
        public IEnumerator<T> GetEnumerator() => rows.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace BozjaBuddyReborn.Automation
{
    public sealed class Loadout { }
    public sealed class SupplyManager
    { public int CacheUnavailableForInstanceCount; public void ResetInstanceCacheAvailability() => CacheUnavailableForInstanceCount = 0; }
    public sealed class AggroAvoidance
    {
        public readonly record struct Census(int Combatants = 0, int Accepted = 0, int NotHostile = 0, int BelowLevel = 0,
            int AlreadyOnUs = 0, int OtherFloor = 0, int Suppressed = 0, int OutOfRange = 0);
        public readonly record struct Zone(Game.FieldEnemyStrength Strength, string Name, Vector3 Position, uint NamePlateIconId, uint CharacterDataIcon);
        public Census LastCensus => default;
        public List<Zone> Scan() => [];
    }
    public static class Movement { public static float DistanceToPlayer(Vector3 position) => 0; }
}
namespace BozjaBuddyReborn.Game
{
    public enum FieldEnemyStrength : byte { Unknown, I, II, III, IV, V, Star }
    public enum FieldRegion : byte { None, One, Two, Three }
    public static class FieldRegions
    { public static readonly FieldRegion[] All = [FieldRegion.One, FieldRegion.Two, FieldRegion.Three]; public static string Label(uint territory, FieldRegion region) => $"地域{(byte)region}"; }
    public static class BozjaZones
    {
        public const uint BozjanSouthernFront = 920, Zadnor = 975;
        public static bool IsFieldZone(uint territory) => territory is 920 or 975;
        public static string Name(uint territory) => territory == 920 ? "南方ボズヤ戦線" : "ザトゥノル高原";
    }
    public sealed class RegionResolver { public int LearnedCount; public void Forget() => LearnedCount = 0; }
    public static class MapCoords { public static Vector2? PlayerMapPosition() => new(10, 10); }
    public static class LostActionStatuses
    {
        public static bool IsEssence(byte row) => row == 56;
        public static bool IsActive(uint status, out float remaining) { remaining = 0; return false; }
        public static Dictionary<byte,uint> Resolve(Func<byte,(uint,string)?> lookup, uint max) => new() { [4] = 104 };
        public static void InvalidateNames() { }
    }
    public static class LostActionDurations { public static float Seconds(uint action) => 600; }
}
namespace BozjaBuddyReborn.Relic
{
    public enum DropActivity { Skirmish, CriticalEngagement }
    public readonly record struct ZoneDrop(uint ItemId, Game.FieldRegion Region, DropActivity Activity)
    { public string Describe() => "南方ボズヤ戦線のスカーミッシュ"; }
    public static class ZoneDrops
    {
        public static ZoneDrop? For(uint itemId) => itemId == 1000 ? new(1000, Game.FieldRegion.One, DropActivity.Skirmish) : null;
        public static IEnumerable<ZoneDrop> ForTerritory(uint territory) => [];
    }
    public sealed class RelicStage
    { public int Order = 1; public string Name = "試験段階", ItemLevel = "", QuestName = "試験クエスト", Note = ""; public bool OneTime; public List<int> Materials = []; }
    public readonly record struct MaterialProgress(uint ItemId, string Name, int Held, int Required, string Source)
    { public bool Satisfied => Held >= Required; public float Fraction => (float)Held / Required; }
    public readonly record struct StageProgress(RelicStage Stage, bool QuestComplete, byte QuestSequence, bool QuestAccepted, IReadOnlyList<MaterialProgress> Materials)
    { public bool MaterialsReady => Materials.All(m => m.Satisfied); }
    public sealed class RelicTracker
    {
        public static bool BozjaUnlocked => true;
        public static bool ZadnorUnlocked => true;
        public StageProgress? CurrentStage() => new(new(), false, 1, true, []);
        public List<StageProgress> ReadAll() => [CurrentStage()!.Value];
        public List<MaterialProgress> OutstandingMaterials() => [new(1000, "試験素材", 0, 5, "試験場所")];
    }
}
