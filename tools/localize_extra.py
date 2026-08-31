#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
p = root / 'plugins/BozjaBuddyReborn'


def patch(rel, old, new):
    f = p / rel
    s = f.read_text(encoding='utf-8-sig')
    if old not in s:
        print(f'[WARN] {rel}: anchor not found')
        return
    f.write_text(s.replace(old, new), encoding='utf-8', newline='\n')

# Use the game's localized PlaceName rows for Z1/Z2/Z3 names instead of hardcoded English.
f = p / 'Game/FieldRegion.cs'
s = f.read_text(encoding='utf-8-sig')
if 'using BozjaBuddyReborn;' not in s:
    s = s.replace('using System.Numerics;\n', 'using System.Numerics;\nusing BozjaBuddyReborn;\n')
old = '''    /// <summary>The in-game name of a region, e.g. "The Northern Plateau".</summary>
    public static string Name(uint territory, FieldRegionId region)
    {
        if (region == FieldRegionId.Unknown)
            return "unknown zone";

        if (territory == BozjaZones.BozjanSouthernFront)
            return region switch
            {
                FieldRegionId.Zone1 => "Southern Entrenchment",
                FieldRegionId.Zone2 => "Old Bozja",
                FieldRegionId.Zone3 => "The Alermuc Climb",
                _ => "unknown zone",
            };

        if (territory == BozjaZones.Zadnor)
            return region switch
            {
                FieldRegionId.Zone1 => "The Southern Plateau",
                FieldRegionId.Zone2 => "The Western Plateau",
                FieldRegionId.Zone3 => "The Northern Plateau",
                _ => "unknown zone",
            };

        return "unknown zone";
    }

    /// <summary>Short label, e.g. "Z3 - The Northern Plateau".</summary>
    public static string Label(uint territory, FieldRegionId region)
        => region == FieldRegionId.Unknown
            ? "unknown zone"
            : $"Z{(byte)region} - {Name(territory, region)}";'''
new = '''    /// <summary>The in-game localized name of a region.</summary>
    public static string Name(uint territory, FieldRegionId region)
    {
        if (region == FieldRegionId.Unknown)
            return Loc.T("unknown zone", "不明なエリア");

        var (placeNameId, fallback) = (territory, region) switch
        {
            (BozjaZones.BozjanSouthernFront, FieldRegionId.Zone1) => (SouthernEntrenchment, "Southern Entrenchment"),
            (BozjaZones.BozjanSouthernFront, FieldRegionId.Zone2) => (OldBozja, "Old Bozja"),
            (BozjaZones.BozjanSouthernFront, FieldRegionId.Zone3) => (AlermucClimb, "The Alermuc Climb"),
            (BozjaZones.Zadnor, FieldRegionId.Zone1) => (SouthernPlateau, "The Southern Plateau"),
            (BozjaZones.Zadnor, FieldRegionId.Zone2) => (WesternPlateau, "The Western Plateau"),
            (BozjaZones.Zadnor, FieldRegionId.Zone3) => (NorthernPlateau, "The Northern Plateau"),
            _ => (0u, Loc.T("unknown zone", "不明なエリア")),
        };

        if (placeNameId == 0)
            return fallback;

        try
        {
            var localized = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>()?
                .GetRowOrDefault(placeNameId)?.Name.ExtractText();
            return string.IsNullOrWhiteSpace(localized) ? fallback : localized;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Short label, e.g. "Z3 - The Northern Plateau".</summary>
    public static string Label(uint territory, FieldRegionId region)
        => region == FieldRegionId.Unknown
            ? Loc.T("unknown zone", "不明なエリア")
            : $"Z{(byte)region} - {Name(territory, region)}";'''
if old in s:
    s = s.replace(old, new)
else:
    print('[WARN] Game/FieldRegion.cs: region-name anchor not found')
f.write_text(s, encoding='utf-8', newline='\n')

# Localize relic-farm location descriptions used by the UI.
f = p / 'Relic/ZoneDrops.cs'
s = f.read_text(encoding='utf-8-sig')
if 'using BozjaBuddyReborn;' not in s:
    s = s.replace('using System.Collections.Generic;\n', 'using System.Collections.Generic;\nusing BozjaBuddyReborn;\n')
old = '''    public string Describe()
    {
        var where = FieldRegions.Label(Territory, Region);
        var what = Activity switch
        {
            DropActivity.Skirmish => "skirmishes",
            DropActivity.CriticalEngagement => "Critical Engagements",
            _ => "skirmishes and Critical Engagements",
        };
        return $"{what} in {where} ({PerClear} per clear)";
    }'''
new = '''    public string Describe()
    {
        var where = FieldRegions.Label(Territory, Region);
        if (Loc.Ja)
        {
            var whatJa = Activity switch
            {
                DropActivity.Skirmish => "スカーミッシュ",
                DropActivity.CriticalEngagement => "クリティカルエンゲージメント",
                _ => "スカーミッシュ / クリティカルエンゲージメント",
            };
            return $"{where} の{whatJa}（1回あたり {PerClear}個）";
        }

        var what = Activity switch
        {
            DropActivity.Skirmish => "skirmishes",
            DropActivity.CriticalEngagement => "Critical Engagements",
            _ => "skirmishes and Critical Engagements",
        };
        return $"{what} in {where} ({PerClear} per clear)";
    }'''
if old in s:
    s = s.replace(old, new)
else:
    print('[WARN] Relic/ZoneDrops.cs: Describe anchor not found')
f.write_text(s, encoding='utf-8', newline='\n')

# Fork attribution and repository URL.
f = p / 'BozjaBuddyReborn.json'
s = f.read_text(encoding='utf-8-sig')
s = s.replace('"Author": "Bozja Buddy Reborn contributors"', '"Author": "Bozja Buddy Reborn contributors / JP localization by riveminae"')
s = s.replace('"RepoUrl": ""', '"RepoUrl": "https://github.com/riveminae/BozjaBuddyReborn-JP"')
f.write_text(s, encoding='utf-8', newline='\n')

print('Extra JP localization applied.')
