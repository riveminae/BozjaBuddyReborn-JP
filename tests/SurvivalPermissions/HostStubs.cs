// Game/service boundaries only. Production configuration, permission policy and all three
// consumers are linked by the project; no game process, IPC, or native function is used.
using System.Numerics;
using System.Collections;

namespace Dalamud.Configuration { public interface IPluginConfiguration { int Version { get; set; } } }
namespace Dalamud.Game { public enum ClientLanguage { English, Japanese } }
namespace Dalamud.Game.ClientState.Conditions { public enum ConditionFlag { BetweenAreas, BetweenAreas51 } }
namespace Dalamud.Game.ClientState.Objects.Types { public interface IBattleChara { } }
namespace BozjaBuddyReborn.Automation
{
    public sealed class Loadout { }
    public static class Mount { public static bool IsMounted; }
}
namespace Lumina.Excel.Sheets
{
    public readonly record struct TestText(string TextValue) { public string ExtractText() => TextValue; }
    public readonly record struct Action(uint RowId, TestText Name);
    public readonly record struct Status(uint RowId, TestText Name);
    public readonly record struct ClassJob(uint RowId, byte Role);
}
namespace ECommons.DalamudServices
{
    public static class Svc
    {
        public static readonly TestData Data = new();
        public static readonly TestObjects Objects = new();
        public static readonly TestCondition Condition = new();
        public static readonly TestLog Log = new();
    }
    public sealed class TestLog { public void Information(string text) { } public void Debug(string text) { } }
    public sealed class TestCondition
    { public bool this[Dalamud.Game.ClientState.Conditions.ConditionFlag flag] => false; }
    public sealed class TestObjects { public TestPlayer? LocalPlayer = new(); }
    public sealed class TestPlayer
    {
        public uint CurrentHp = 100, MaxHp = 100;
        public bool IsCasting;
        public ulong GameObjectId = 1;
        public Lumina.Excel.Sheets.TestText Name = new("試験用");
        public Lumina.Excel.Sheets.ClassJob ClassJob = new(1, 1);
        public Vector3 Position;
    }
    public sealed class TestData
    {
        public readonly Dictionary<uint, Lumina.Excel.Sheets.Action> Actions = [];
        public bool ActionsAvailable = true;
        public byte Role = 1;
        public TestSheet<T>? GetExcelSheet<T>(Dalamud.Game.ClientLanguage language = Dalamud.Game.ClientLanguage.English) where T : struct
        {
            if (typeof(T) == typeof(Lumina.Excel.Sheets.Action))
                return ActionsAvailable ? (TestSheet<T>)(object)new TestSheet<Lumina.Excel.Sheets.Action>(Actions) : null;
            if (typeof(T) == typeof(Lumina.Excel.Sheets.ClassJob))
                return (TestSheet<T>)(object)new TestSheet<Lumina.Excel.Sheets.ClassJob>(new() { [1] = new(1, Role) });
            if (typeof(T) == typeof(Lumina.Excel.Sheets.Status))
                return (TestSheet<T>)(object)new TestSheet<Lumina.Excel.Sheets.Status>(new() { [99] = new(99, new("Auto-potion")) });
            return null;
        }
    }
    public sealed class TestSheet<T>(Dictionary<uint, T> rows) : IEnumerable<T> where T : struct
    {
        public T? GetRowOrDefault(uint id) => rows.TryGetValue(id, out var row) ? row : null;
        public IEnumerator<T> GetEnumerator() => rows.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace BozjaBuddyReborn.Game
{
    public sealed class LostActionCatalog
    {
        public readonly record struct Entry(byte RowId, uint ActionId, string Name, bool IsItem = false,
            bool IsPartyHeal = false, uint StatusId = 0)
        {
            public bool IsAction => !IsItem;
            public bool IsPartySupport => IsPartyHeal || StatusId != 0;
            public bool HasDuration => DurationSeconds > 0;
            public float DurationSeconds => 600;
            public float Range => 30;
        }
        public List<Entry> Entries = [];
        public IEnumerable<Entry> All => Entries;
        public bool TryGet(byte id, out Entry entry)
        { foreach (var e in Entries) if (e.RowId == id) { entry = e; return true; } entry = default; return false; }
        public string Name(byte id) => TryGet(id, out var entry) ? entry.Name : "未取得";
    }
    public readonly record struct LostItemBoxSnapshot(IReadOnlyDictionary<byte, int> Cache,
        IReadOnlyDictionary<byte, int> Holster, int HolsterWeight, bool Available)
    {
        public int CacheCount(byte row) => Cache.GetValueOrDefault(row);
        public int HolsterCount(byte row) => Holster.GetValueOrDefault(row);
    }
    public sealed class LostItemBoxInventory
    { public LostItemBoxSnapshot Snapshot = new(new Dictionary<byte,int>(), new Dictionary<byte,int>(), 0, true); public LostItemBoxSnapshot Read() => Snapshot; }
    public static class FieldState
    {
        public static bool Available = true;
        public static byte[] Held = [];
        public static int Uses;
        public static byte LastRow;
        public static byte[] Holster() => Held;
        public static bool UseFromHolster(uint index, uint slot) { Uses++; LastRow = Held[index]; return true; }
    }
    public readonly record struct DutySlot(uint ActionId, int CurCharges, float CooldownRemaining = 0)
    {
        public bool Ready => CurCharges > 0;
        public static DutySlot Empty => default;
    }
    public readonly record struct TestPress(bool Fired, string Message = "", bool TargetRefused = false);
    public static class DutyActions
    {
        public const int SlotCount = 2;
        public static readonly DutySlot[] Slots = new DutySlot[2];
        public static int Presses;
        public static DutySlot Read(int slot) => Slots[slot];
        public static TestPress Press(int slot, uint expected)
        {
            if (Slots[slot].ActionId != expected || !Slots[slot].Ready) return new(false);
            Presses++; return new(true);
        }
        public static TestPress PressAt(int slot, uint expected, ulong id, string name) => Press(slot, expected);
    }
    public static class LostActionStatuses
    {
        public static readonly HashSet<uint> Active = [];
        public static bool IsActive(uint status, out float remaining) { remaining = 0; return Active.Contains(status); }
        public static string Name(uint status) => "試験効果";
    }
    public static class PartyView
    {
        public readonly record struct Member(ulong Id, string Name, ECommons.DalamudServices.TestPlayer Chara, bool IsSelf = false)
        {
            public bool IsDead => Chara.CurrentHp == 0;
            public float HpFraction => (float)Chara.CurrentHp / Chara.MaxHp;
        }
        public static List<Member> Members = [];
        public static List<Member> Snapshot() => Members;
        public static bool IsInParty(ulong id) => Members.Any(m => m.Id == id);
        public static bool HasStatus(ECommons.DalamudServices.TestPlayer chara, uint status, out float remaining)
        { remaining = 0; return false; }
    }
}
