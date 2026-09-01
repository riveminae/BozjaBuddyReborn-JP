using System;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn;

public static class ConfigMigration
{
    public const int CurrentVersion = 4;

    public static bool Apply(Configuration config)
    {
        var changed = false;
        var from = config.Version;

        if (config.AllowFlight)
        {
            config.AllowFlight = false;
            changed = true;
        }

        config.BlockedEngagements ??= [];
        config.PriorityEngagements ??= [];
        config.LearnedRegions ??= [];
        config.IdleSpots ??= [];
        config.LostActionBringPermissions ??= [];
        config.LostActionAutoUsePermissions ??= [];
        config.AutoLostActions ??= [];
        config.PartySupportActions ??= [];

        changed |= NormaliseFraction(ref config.TankSurvivalHealFraction, 0.55f);
        changed |= NormaliseFraction(ref config.TankSurvivalEmergencyFraction, 0.30f);
        changed |= NormaliseFraction(ref config.HealerSurvivalHealFraction, 0.70f);
        changed |= NormaliseFraction(ref config.HealerSurvivalEmergencyFraction, 0.45f);
        changed |= NormaliseFraction(ref config.DpsSurvivalHealFraction, 0.65f);
        changed |= NormaliseFraction(ref config.DpsSurvivalEmergencyFraction, 0.40f);

        changed |= EnsureOrder(ref config.TankSurvivalHealFraction, ref config.TankSurvivalEmergencyFraction, 0.55f, 0.30f);
        changed |= EnsureOrder(ref config.HealerSurvivalHealFraction, ref config.HealerSurvivalEmergencyFraction, 0.70f, 0.45f);
        changed |= EnsureOrder(ref config.DpsSurvivalHealFraction, ref config.DpsSurvivalEmergencyFraction, 0.65f, 0.40f);

        changed |= Positive(ref config.NavigationMaxDirectWalkDistance, 80f);
        changed |= Positive(ref config.NavigationAethernetHopCost, 50f);
        changed |= Positive(ref config.NavigationReturnCost, 40f);
        changed |= Positive(ref config.DangerStarExtraClearance, 5f);

        if (config.NewSkirmishMaxProgress is 0 or > 100)
        {
            config.NewSkirmishMaxProgress = 80;
            changed = true;
        }

        if (config.Version != CurrentVersion)
        {
            config.Version = CurrentVersion;
            changed = true;
        }

        if (changed)
            Svc.Log.Information($"[BozjaBuddyReborn] Migrated configuration from schema {from} to {CurrentVersion}.");
        return changed;
    }

    private static bool NormaliseFraction(ref float value, float fallback)
    {
        if (float.IsFinite(value) && value > 0f && value <= 1f)
            return false;
        value = fallback;
        return true;
    }

    private static bool EnsureOrder(ref float heal, ref float emergency, float healFallback, float emergencyFallback)
    {
        if (emergency < heal)
            return false;
        heal = healFallback;
        emergency = emergencyFallback;
        return true;
    }

    private static bool Positive(ref float value, float fallback)
    {
        if (float.IsFinite(value) && value > 0f)
            return false;
        value = fallback;
        return true;
    }
}
