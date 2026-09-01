using System.Collections.Generic;
using System.Numerics;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Custom aethernet nodes used inside the Save the Queen field zones.
///
/// These are not normal Aetheryte-sheet rows.  Lifestream models them as custom
/// aetherytes and teleports to a destination by PlaceName row id.  Coordinates and
/// ids are kept in one table so routing never relies on translated camp names.
///
/// Data source: Lifestream custom aethernet definitions / BOCCHI navigation data.
/// </summary>
public static class FieldAethernet
{
    public readonly record struct Node(
        uint Territory,
        Vector3 Position,
        uint PlaceNameId,
        uint CustomAetheryteId,
        bool IsBaseCamp = false);

    public const uint BaseBozjaId = 69_420_200;
    public const uint BaseZadnorId = 69_420_300;

    private static readonly Node[] Bozja =
    [
        new(BozjaZones.BozjanSouthernFront, new Vector3(-202.0f, 0f, 847.0f), 3529, BaseBozjaId, true),
        new(BozjaZones.BozjanSouthernFront, new Vector3(486.8f, 0f, 531.3f), 3530, BaseBozjaId + 1),
        new(BozjaZones.BozjanSouthernFront, new Vector3(-258.0f, 0f, 534.4f), 3531, BaseBozjaId + 2),
        new(BozjaZones.BozjanSouthernFront, new Vector3(169.8f, 0f, 192.3f), 3575, BaseBozjaId + 3),
    ];

    private static readonly Node[] Zadnor =
    [
        new(BozjaZones.Zadnor, new Vector3(679.7f, 0f, 660.0f), 3664, BaseZadnorId, true),
        new(BozjaZones.Zadnor, new Vector3(-356.5f, 0f, 758.4f), 3665, BaseZadnorId + 1),
        new(BozjaZones.Zadnor, new Vector3(-689.4f, 0f, -292.2f), 3666, BaseZadnorId + 2),
        new(BozjaZones.Zadnor, new Vector3(106.4f, 0f, -130.8f), 3667, BaseZadnorId + 3),
    ];

    public static IReadOnlyList<Node> ForTerritory(uint territory) => territory switch
    {
        BozjaZones.BozjanSouthernFront => Bozja,
        BozjaZones.Zadnor => Zadnor,
        _ => [],
    };

    public static Node? BaseCamp(uint territory)
    {
        foreach (var node in ForTerritory(territory))
            if (node.IsBaseCamp)
                return node;
        return null;
    }
}
