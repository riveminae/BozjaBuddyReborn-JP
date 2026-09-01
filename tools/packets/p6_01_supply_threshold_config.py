from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "Configuration.cs"
text = PATH.read_text(encoding="utf-8-sig")
anchor = """    /// <summary>Fast guard between two automatic survival spends; the game remains the final cooldown authority.</summary>\n    public int SurvivalUseGapMs = 750;\n\n"""
insert = """    /// <summary>Fast guard between two automatic survival spends; the game remains the final cooldown authority.</summary>\n    public int SurvivalUseGapMs = 750;\n\n    // --- survival supply watermarks -----------------------------------------\n\n    /// <summary>Routine refill threshold for Resistance Potion Kit reserves in the holster.</summary>\n    public int SupplyPotionKitLow = 2;\n\n    /// <summary>Routine refill threshold for Resistance Reraiser reserves in the holster.</summary>\n    public int SupplyReraiserLow = 1;\n\n    /// <summary>Conservative minimum immediately available/reserve units for the role's main Lost heal.</summary>\n    public int SupplyMainHealLow = 5;\n\n    /// <summary>Routine refill threshold for Lost Manawall reserve units.</summary>\n    public int SupplyEmergencyDefenseLow = 1;\n\n    /// <summary>Target Potion Kit reserve after a differential refill.</summary>\n    public int SupplyPotionKitTarget = 5;\n\n    /// <summary>Target Reraiser reserve after a differential refill.</summary>\n    public int SupplyReraiserTarget = 3;\n\n    /// <summary>Target Lost Manawall reserve after a differential refill.</summary>\n    public int SupplyEmergencyDefenseTarget = 2;\n\n"""
if insert in text:
    print(f"{PATH}: already applied")
elif anchor not in text:
    raise RuntimeError("Configuration supply anchor not found")
else:
    PATH.write_text(text.replace(anchor, insert, 1), encoding="utf-8")
    print(f"{PATH}: patched")
