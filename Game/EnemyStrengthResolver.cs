using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Game;

public enum FieldEnemyStrength : byte
{
    Unknown = 0,
    I = 1,
    II = 2,
    III = 3,
    IV = 4,
    V = 5,
    Star = 6,
}

public readonly record struct FieldEnemyStrengthInfo(
    FieldEnemyStrength Strength,
    string EnglishName,
    FieldRegionId Region,
    uint NamePlateIconId,
    byte CharacterDataIcon)
{
    public bool Known => Strength != FieldEnemyStrength.Unknown;
    public bool Dangerous => Strength is FieldEnemyStrength.IV or FieldEnemyStrength.V or FieldEnemyStrength.Star
                             || Strength == FieldEnemyStrength.Unknown;
    public string Label => Strength switch
    {
        FieldEnemyStrength.I => "I",
        FieldEnemyStrength.II => "II",
        FieldEnemyStrength.III => "III",
        FieldEnemyStrength.IV => "IV",
        FieldEnemyStrength.V => "V",
        FieldEnemyStrength.Star => "★",
        _ => "?",
    };
}

/// <summary>
/// Resolves the Save-the-Queen I..V/★ strength marker instead of using npc.Level (all field mobs
/// are level 80).  The primary seed is a territory+region+English BNpc name table so it works on a
/// Japanese client. Whenever a seeded mob is seen, the raw nameplate icon pair is learned in
/// memory and can classify another mob even when position is too close to a region boundary.
/// Unknown deliberately fails safe as dangerous.
///
/// The raw fields are also retained for diagnostics: FFXIVClientStructs exposes
/// GameObject.NamePlateIconId and CharacterData.Icon ("for nameplates").  A live test can therefore
/// prove the direct icon mapping later without guessing it now.
/// </summary>
public static unsafe class EnemyStrengthResolver
{
    private static readonly Dictionary<(uint Territory, FieldRegionId Region, string Name), FieldEnemyStrength> ByName = Build();
    private static readonly Dictionary<(uint NamePlateIconId, byte CharacterDataIcon), FieldEnemyStrength> LearnedRaw = [];
    private static readonly Dictionary<uint, string> EnglishNameCache = [];

    public static FieldEnemyStrengthInfo Resolve(IBattleNpc npc)
    {
        var territory = Svc.ClientState.TerritoryType;
        var region = FieldRegions.ClassifyByPosition(territory, npc.Position);
        var englishName = EnglishName(npc.NameId);

        uint namePlateIcon = 0;
        byte characterIcon = 0;
        try
        {
            var obj = npc.Struct();
            if (obj != null)
            {
                namePlateIcon = obj->NamePlateIconId;
                characterIcon = ((Character*)obj)->CharacterData.Icon;
            }
        }
        catch
        {
            // The static table still answers; raw data is diagnostic only.
        }

        FieldEnemyStrength strength = FieldEnemyStrength.Unknown;
        if (namePlateIcon != 0 || characterIcon != 0)
            LearnedRaw.TryGetValue((namePlateIcon, characterIcon), out strength);

        if (strength == FieldEnemyStrength.Unknown
            && region != FieldRegionId.Unknown
            && englishName.Length > 0
            && ByName.TryGetValue((territory, region, englishName), out var seeded))
        {
            strength = seeded;
            if (namePlateIcon != 0 || characterIcon != 0)
                LearnedRaw[(namePlateIcon, characterIcon)] = seeded;
        }

        return new FieldEnemyStrengthInfo(strength, englishName, region, namePlateIcon, characterIcon);
    }

    private static string EnglishName(uint nameId)
    {
        if (nameId == 0)
            return string.Empty;
        if (EnglishNameCache.TryGetValue(nameId, out var cached))
            return cached;

        try
        {
            var row = Svc.Data.GetExcelSheet<BNpcName>(ClientLanguage.English)?.GetRowOrDefault(nameId);
            var name = row?.Singular.ExtractText() ?? string.Empty;
            if (name.Length > 0)
                EnglishNameCache[nameId] = name;
            return name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Dictionary<(uint, FieldRegionId, string), FieldEnemyStrength> Build()
    {
        var d = new Dictionary<(uint, FieldRegionId, string), FieldEnemyStrength>();

        static void Add(Dictionary<(uint, FieldRegionId, string), FieldEnemyStrength> map,
            uint territory, FieldRegionId region, FieldEnemyStrength strength, params string[] names)
        {
            foreach (var name in names)
                map[(territory, region, name)] = strength;
        }

        const uint b = BozjaZones.BozjanSouthernFront;
        Add(d, b, FieldRegionId.Zone1, FieldEnemyStrength.I, "Bozjan Nepenthes", "Bozjan Orobon", "4th Legion Slasher");
        Add(d, b, FieldRegionId.Zone1, FieldEnemyStrength.II, "Bozjan Korrigan", "Bozjan Mudpuppy", "4th Legion Nimrod", "Earth Sprite");
        Add(d, b, FieldRegionId.Zone1, FieldEnemyStrength.III, "Bozjan Geshunpest", "Bozjan Matamata", "4th Legion Roader", "4th Legion Death Claw", "Wind Sprite");
        Add(d, b, FieldRegionId.Zone1, FieldEnemyStrength.IV, "Water Sprite");
        Add(d, b, FieldRegionId.Zone1, FieldEnemyStrength.V, "Bozjan Wraith", "Lightning Sprite");
        Add(d, b, FieldRegionId.Zone1, FieldEnemyStrength.Star, "Tideborn Angel", "Fern Flower", "Ink Claw");

        Add(d, b, FieldRegionId.Zone2, FieldEnemyStrength.I, "Bozjan Doblyn", "Bozjan Sabotender", "4th Legion Vanguard");
        Add(d, b, FieldRegionId.Zone2, FieldEnemyStrength.II, "Red Chocobo", "Bozjan Tormentor", "4th Legion Avenger", "Water Sprite");
        Add(d, b, FieldRegionId.Zone2, FieldEnemyStrength.III, "Bozjan Worm", "Bozjan Antlion", "Bozjan Wight", "4th Legion Gunship", "Lightning Sprite");
        Add(d, b, FieldRegionId.Zone2, FieldEnemyStrength.IV, "Bozjan Bandersnatch", "Bozjan Biast", "Bozjan Taipan", "Wind Sprite");
        Add(d, b, FieldRegionId.Zone2, FieldEnemyStrength.V, "Bozjan Dullahan", "Earth Sprite");
        Add(d, b, FieldRegionId.Zone2, FieldEnemyStrength.Star, "Psoglav", "Viy", "Smok");

        Add(d, b, FieldRegionId.Zone3, FieldEnemyStrength.I, "Bozjan Screamer", "Bozjan Elbst", "4th Legion Hexadrone");
        Add(d, b, FieldRegionId.Zone3, FieldEnemyStrength.II, "Bozjan Phobosuchus", "Bozjan Ranunculus", "4th Legion Scorpion", "Lightning Sprite");
        Add(d, b, FieldRegionId.Zone3, FieldEnemyStrength.III, "Bozjan Ochu", "Bozjan Monitor", "Bozjan Gravekeeper", "4th Legion Armored Weapon", "Water Sprite");
        Add(d, b, FieldRegionId.Zone3, FieldEnemyStrength.IV, "Bozjan Snake", "Bozjan Wadjet", "Bozjan Goobbue", "Earth Sprite");
        Add(d, b, FieldRegionId.Zone3, FieldEnemyStrength.V, "Bozjan Anzu", "Bozjan Doll", "Bozjan Elasmoth", "Bozjan Rider", "Wind Sprite");
        Add(d, b, FieldRegionId.Zone3, FieldEnemyStrength.Star, "Patty", "Clingy Clare", "Bird of Barathrum");

        const uint z = BozjaZones.Zadnor;
        Add(d, z, FieldRegionId.Zone1, FieldEnemyStrength.I, "Zadnor Beetle", "Zadnor Hippogryph", "4th Legion Nimrod");
        Add(d, z, FieldRegionId.Zone1, FieldEnemyStrength.II, "Zadnor Ziz", "Zadnor Gaur", "4th Legion Infantry", "Lightning Sprite");
        Add(d, z, FieldRegionId.Zone1, FieldEnemyStrength.III, "Zadnor Dhalmel", "Zadnor Bhoot", "4th Legion Gunship", "4th Legion Hexadrone", "Water Sprite");
        Add(d, z, FieldRegionId.Zone1, FieldEnemyStrength.IV, "Ice Sprite");
        Add(d, z, FieldRegionId.Zone1, FieldEnemyStrength.V, "Zadnor Dullahan", "Wind Sprite");
        Add(d, z, FieldRegionId.Zone1, FieldEnemyStrength.Star, "Vinegaroon Executioner", "Anancus", "Stratogryph");

        Add(d, z, FieldRegionId.Zone2, FieldEnemyStrength.I, "Zadnor Crawler", "Zadnor Stoneshell", "4th Legion Death Machine");
        Add(d, z, FieldRegionId.Zone2, FieldEnemyStrength.II, "Zadnor Exoray", "Zadnor Abaddon", "4th Legion Armored Weapon", "Wind Sprite");
        Add(d, z, FieldRegionId.Zone2, FieldEnemyStrength.III, "Zadnor Grizzly", "Zadnor Banshee", "4th Legion Satellite", "4th Legion Colossus", "Ice Sprite");
        Add(d, z, FieldRegionId.Zone2, FieldEnemyStrength.IV, "Zadnor Sasquatch", "Zadnor Coeurl", "Zadnor Leshy", "Lightning Sprite");
        Add(d, z, FieldRegionId.Zone2, FieldEnemyStrength.V, "Zadnor Gourmand", "Water Sprite");
        Add(d, z, FieldRegionId.Zone2, FieldEnemyStrength.Star, "Earth Eater", "Aglaophotis", "Lord Ochu");

        Add(d, z, FieldRegionId.Zone3, FieldEnemyStrength.I, "Zadnor Wamouracampa", "Zadnor Cliffmole", "4th Legion Roader");
        Add(d, z, FieldRegionId.Zone3, FieldEnemyStrength.II, "Zadnor Yamaa", "Zadnor Raptor", "4th Legion Rearguard", "Water Sprite");
        Add(d, z, FieldRegionId.Zone3, FieldEnemyStrength.III, "Zadnor Wamoura", "Imperial Dead", "4th Legion Cavalry", "4th Legion Helldiver", "Lightning Sprite");
        Add(d, z, FieldRegionId.Zone3, FieldEnemyStrength.IV, "Zadnor Harpy", "Zadnor Lycanthrope", "Zadnor Decotitus", "Wind Sprite");
        Add(d, z, FieldRegionId.Zone3, FieldEnemyStrength.V, "Zadnor Wivre", "Zadnor Golem", "Zadnor Gagana", "Zadnor Haunt", "Ice Sprite");
        Add(d, z, FieldRegionId.Zone3, FieldEnemyStrength.Star, "Glyptodon", "Molten Scorpion", "Vapula");

        return d;
    }
}
