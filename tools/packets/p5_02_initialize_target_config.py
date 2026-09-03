from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Configuration.cs"
text = P.read_text(encoding="utf-8-sig")
anchor = """    /// <summary>Target Reraiser reserve after a differential refill.</summary>\n    public int SupplyReraiserTarget = 3;\n\n    /// <summary>Target Lost Manawall reserve after a differential refill.</summary>\n"""
replacement = """    /// <summary>Target Reraiser reserve after a differential refill.</summary>\n    public int SupplyReraiserTarget = 3;\n\n    /// <summary>Target reserve for the role's primary self-healing Lost Action.</summary>\n    public int SupplyMainHealTarget = 10;\n\n    /// <summary>Target Lost Manawall reserve after a differential refill.</summary>\n"""
if replacement in text:
    print("Configuration.cs: already applied")
elif anchor not in text:
    raise RuntimeError("initialize target config anchor missing")
else:
    P.write_text(text.replace(anchor, replacement, 1), encoding="utf-8")
    print("Configuration.cs: initialize target config patched")
