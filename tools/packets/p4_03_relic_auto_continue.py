from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def patch(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding="utf-8-sig")
    if new in text:
        print(f"{path}: already applied")
        return
    if old not in text:
        raise RuntimeError(f"anchor missing in {path}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"{path}: patched")


patch(
    "Configuration.cs",
    """public enum TravelAggroResponse : byte\n{\n""",
    """public enum RelicFarmStopMode : byte\n{\n    Unlimited = 0,\n    SelectedMaterialComplete = 1,\n    CurrentStageComplete = 2,\n}\n\npublic enum TravelAggroResponse : byte\n{\n""",
)
patch(
    "Configuration.cs",
    """    /// <summary>Item id of the relic material to farm, or 0 for \"anything\".</summary>\n    public uint FarmMaterialItemId;\n\n""",
    """    /// <summary>Item id of the relic material to farm, or 0 for \"anything\".</summary>\n    public uint FarmMaterialItemId;\n\n    /// <summary>After an explicitly-selected material completes, continue to the next shortage in this territory.</summary>\n    public bool RelicAutoContinue = true;\n\n    /// <summary>Default is unattended/unlimited; optional stops can end at the selected material or current stage.</summary>\n    public RelicFarmStopMode RelicFarmStopMode = RelicFarmStopMode.Unlimited;\n\n""",
)

patch(
    "Automation/BozjaController.cs",
    """using BozjaBuddyReborn.Multibox;\n""",
    """using BozjaBuddyReborn.Multibox;\nusing BozjaBuddyReborn.Relic;\n""",
)
patch(
    "Automation/BozjaController.cs",
    """    private readonly SignUpRunner _signUps;\n    private readonly PartySupportDriver _partySupport;\n""",
    """    private readonly SignUpRunner _signUps;\n    private readonly PartySupportDriver _partySupport;\n    private readonly RelicFarmCoordinator _relicFarm;\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        _signUps = signUps;\n        _partySupport = partySupport;\n    }\n""",
    """        _signUps = signUps;\n        _partySupport = partySupport;\n        _relicFarm = new RelicFarmCoordinator(config, new RelicTracker());\n    }\n""",
)
patch(
    "Automation/BozjaController.cs",
    """        Engagements = CriticalEngagements.Read(_catalog);\n        CurrentRegion = FieldRegions.Current();\n\n        // Critical Engagements are a remote UI workflow, not a travel objective. Register while\n""",
    """        Engagements = CriticalEngagements.Read(_catalog);\n        CurrentRegion = FieldRegions.Current();\n\n        // The first Relic target is always manual. Once that explicit target becomes satisfied,\n        // advance inside the current territory before CE/FATE selection. Existing sticky-objective\n        // permission checks decide whether the current skirmish remains valid for the new target.\n        var relicUpdate = _relicFarm.Tick(Svc.ClientState.TerritoryType);\n        if (relicUpdate.Stop)\n        {\n            Stop(relicUpdate.JapaneseStatus);\n            return;\n        }\n        if (relicUpdate.ChangedTarget)\n        {\n            Svc.Log.Information(\n                $\"[BozjaBuddyReborn] Resistance Relic farm target advanced from item {relicUpdate.PreviousItemId} to {relicUpdate.CurrentItemId}.\");\n        }\n\n        // Critical Engagements are a remote UI workflow, not a travel objective. Register while\n""",
)
