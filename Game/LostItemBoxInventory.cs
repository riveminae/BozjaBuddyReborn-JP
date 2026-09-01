using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BozjaBuddyReborn.Game;

public readonly record struct LostItemBoxSnapshot(
    IReadOnlyDictionary<byte, int> Cache,
    IReadOnlyDictionary<byte, int> Holster,
    int HolsterWeight,
    bool Available)
{
    public int CacheCount(byte rowId) => Cache.TryGetValue(rowId, out var count) ? count : 0;
    public int HolsterCount(byte rowId) => Holster.TryGetValue(rowId, out var count) ? count : 0;
}

/// <summary>
/// Read-only view of the Lost Finds Cache and Lost Holster server-backed state.
///
/// AgentMycItemBox exposes counts but no supported transfer member function.  This class therefore
/// never writes the agent or its ItemBoxData.  Cache/Holster transfer is implemented separately
/// once the game's real UI callback has been established; keeping reads separate makes it
/// impossible for a supply calculation to accidentally become a direct memory edit.
/// </summary>
public sealed unsafe class LostItemBoxInventory(LostActionCatalog catalog)
{
    private readonly LostActionCatalog _catalog = catalog;

    public LostItemBoxSnapshot Read()
    {
        var cache = new Dictionary<byte, int>();
        var holster = new Dictionary<byte, int>();

        try
        {
            var framework = Framework.Instance();
            var ui = framework?.GetUIModule();
            var agents = ui?.GetAgentModule();
            var agent = (AgentMycItemBox*)agents?.GetAgentByInternalId(AgentId.MycItemBox);
            var data = agent?.ItemBoxData;
            if (data == null)
                return new LostItemBoxSnapshot(cache, holster, 0, false);

            var actionToRow = BuildActionToRow();

            foreach (var category in data->ItemCaches)
            {
                foreach (var item in category.Items)
                    Add(cache, actionToRow, item.ActionId, item.Count);
            }

            foreach (var category in data->ItemHolsters)
            {
                foreach (var item in category.Items)
                    Add(holster, actionToRow, item.ActionId, item.Count);
            }

            var weight = 0;
            foreach (var (row, count) in holster)
                weight += _catalog.Weight(row) * Math.Max(0, count);

            return new LostItemBoxSnapshot(cache, holster, weight, true);
        }
        catch
        {
            return new LostItemBoxSnapshot(cache, holster, 0, false);
        }
    }

    private Dictionary<int, byte> BuildActionToRow()
    {
        var result = new Dictionary<int, byte>();
        foreach (var entry in _catalog.All)
        {
            if (entry.ActionId != 0)
                result[(int)entry.ActionId] = entry.RowId;
        }
        return result;
    }

    private static void Add(
        IDictionary<byte, int> destination,
        IReadOnlyDictionary<int, byte> actionToRow,
        int actionId,
        int count)
    {
        if (actionId == 0 || count <= 0 || !actionToRow.TryGetValue(actionId, out var row))
            return;

        destination.TryGetValue(row, out var current);
        destination[row] = current + count;
    }
}
