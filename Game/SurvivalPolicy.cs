using System;
using System.Collections.Generic;
using Dalamud.Game;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Game;

public enum SurvivalRole : byte
{
    Unknown = 0,
    Tank = 1,
    Healer = 2,
    Dps = 3,
}

/// <summary>
/// The survivability-first policy agreed for v1.1.  It deliberately resolves Lost Actions by the
/// English Action-sheet name at runtime instead of baking MYCTemporaryItem row numbers into the
/// controller. That keeps the policy readable on a Japanese client and avoids confusing two
/// similarly named items after a game-data update.
/// </summary>
public sealed class SurvivalPolicy(Configuration config, LostActionCatalog catalog)
{
    private readonly Configuration _config = config;
    private readonly LostActionCatalog _catalog = catalog;
    private readonly Dictionary<string, LostActionCatalog.Entry> _byEnglishName = new(StringComparer.Ordinal);
    private bool _indexed;
    private uint _autoPotionStatusId;

    public SurvivalRole Role => CurrentRole();

    public float HealThreshold => Role switch
    {
        SurvivalRole.Tank => _config.TankSurvivalHealFraction,
        SurvivalRole.Healer => _config.HealerSurvivalHealFraction,
        _ => _config.DpsSurvivalHealFraction,
    };

    public float EmergencyThreshold => Role switch
    {
        SurvivalRole.Tank => _config.TankSurvivalEmergencyFraction,
        SurvivalRole.Healer => _config.HealerSurvivalEmergencyFraction,
        _ => _config.DpsSurvivalEmergencyFraction,
    };

    public IEnumerable<string> EssencePriority => Role switch
    {
        SurvivalRole.Tank =>
        ["Deep Essence of the Bloodsucker", "Essence of the Bloodsucker", "Deep Essence of the Guardian", "Essence of the Guardian"],
        SurvivalRole.Healer =>
        ["Deep Essence of the Templar", "Essence of the Templar", "Deep Essence of the Veteran", "Essence of the Veteran"],
        _ =>
        ["Deep Essence of the Beast", "Essence of the Beast", "Deep Essence of the Platebearer", "Essence of the Platebearer", "Deep Essence of the Veteran", "Essence of the Veteran"],
    };

    public IEnumerable<string> EmergencyPriority(bool travelling, bool includeReraiser)
    {
        // Reraiser is intentionally edge-triggered by HolsterDriver: remaining below the emergency
        // threshold for several ticks must not consume one every time the prior attempt has no
        // recognisable status. A fresh risk window begins only after HP leaves and re-enters the
        // emergency band. Lost Reraise remains a normal emergency fallback when standing still.
        if (includeReraiser)
            yield return "Resistance Reraiser";
        if (!travelling)
            yield return "Lost Reraise";

        yield return "Lost Manawall";

        if (Role == SurvivalRole.Healer)
        {
            yield return "Lost Full Cure";
            yield break;
        }

        // Cure IV and II are instant. III/Cure cast and are therefore combat/hold-only fallbacks.
        yield return "Lost Cure IV";
        yield return "Lost Cure II";
        if (!travelling)
        {
            yield return "Lost Cure III";
            yield return "Lost Cure";
        }
    }

    public IEnumerable<string> HealPriority(bool travelling)
    {
        if (Role == SurvivalRole.Healer)
        {
            // Healers cannot equip Lost Cure I-IV; Full Cure is their emergency Lost Action.
            if (!travelling)
                yield return "Lost Full Cure";
            yield break;
        }

        yield return "Lost Cure IV";
        yield return "Lost Cure II";
        if (!travelling)
        {
            yield return "Lost Cure III";
            yield return "Lost Cure";
        }
    }

    public LostActionCatalog.Entry? Find(string englishName)
    {
        EnsureIndex();
        return _byEnglishName.TryGetValue(englishName, out var entry) ? entry : null;
    }

    public bool BringAllowed(LostActionCatalog.Entry entry)
        => Permission(_config.LostActionBringPermissions, entry,
            defaultValue: !IsDeep(entry));

    public bool AutoUseAllowed(LostActionCatalog.Entry entry)
        => Permission(_config.LostActionAutoUsePermissions, entry,
            defaultValue: !IsDeep(entry));

    public bool HasAutoPotion()
    {
        var status = AutoPotionStatusId();
        return status != 0 && LostActionStatuses.IsActive(status, out _);
    }

    public static float HpFraction()
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null || me.MaxHp == 0)
            return 1f;
        return Math.Clamp((float)me.CurrentHp / me.MaxHp, 0f, 1f);
    }

    public static SurvivalRole CurrentRole()
    {
        try
        {
            var me = Svc.Objects.LocalPlayer;
            if (me == null)
                return SurvivalRole.Unknown;

            var row = Svc.Data.GetExcelSheet<ClassJob>()?.GetRowOrDefault(me.ClassJob.RowId);
            if (row == null)
                return SurvivalRole.Unknown;

            return row.Value.Role switch
            {
                1 => SurvivalRole.Tank,
                4 => SurvivalRole.Healer,
                2 or 3 => SurvivalRole.Dps,
                _ => SurvivalRole.Unknown,
            };
        }
        catch
        {
            return SurvivalRole.Unknown;
        }
    }

    private void EnsureIndex()
    {
        if (_indexed)
            return;

        _byEnglishName.Clear();
        try
        {
            var actions = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>(ClientLanguage.English);
            if (actions == null)
                return;

            foreach (var entry in _catalog.All)
            {
                if (entry.ActionId == 0)
                    continue;
                var row = actions.GetRowOrDefault(entry.ActionId);
                var name = row?.Name.ExtractText() ?? string.Empty;
                if (name.Length > 0)
                    _byEnglishName[name] = entry;
            }

            _indexed = true;
        }
        catch
        {
            // Data manager can be temporarily unavailable during zoning; retry later.
        }
    }

    private uint AutoPotionStatusId()
    {
        if (_autoPotionStatusId != 0)
            return _autoPotionStatusId;

        try
        {
            var sheet = Svc.Data.GetExcelSheet<Status>(ClientLanguage.English);
            if (sheet == null)
                return 0;
            foreach (var row in sheet)
            {
                if (row.Name.ExtractText() != "Auto-potion")
                    continue;
                _autoPotionStatusId = row.RowId;
                return _autoPotionStatusId;
            }
        }
        catch
        {
            // retry on the next pass
        }

        return 0;
    }

    private bool IsDeep(LostActionCatalog.Entry entry)
    {
        EnsureIndex();
        foreach (var (name, indexed) in _byEnglishName)
            if (indexed.RowId == entry.RowId)
                return name.StartsWith("Deep Essence of ", StringComparison.Ordinal);
        return false;
    }

    private static bool Permission(Dictionary<byte, bool> overrides, LostActionCatalog.Entry entry, bool defaultValue)
        => overrides.TryGetValue(entry.RowId, out var enabled) ? enabled : defaultValue;
}
