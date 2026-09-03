from __future__ import annotations
from pathlib import Path

root = Path(__file__).resolve().parents[2]
p = root / "Configuration.cs"
text = p.read_text(encoding="utf-8-sig")
anchor = """    public List<uint> PriorityEngagements = [];\n\n    // --- zone-targeted farming ---------------------------------------------\n"""
replacement = """    public List<uint> PriorityEngagements = [];\n\n    /// <summary>Reject strictly identified incoming social requests while the runner is active.</summary>\n    public bool RejectSocialRequestsWhileRunning = true;\n\n    // --- zone-targeted farming ---------------------------------------------\n"""
if replacement in text:
    print("Configuration.cs: already applied")
elif anchor not in text:
    raise RuntimeError("social config anchor not found")
else:
    p.write_text(text.replace(anchor, replacement, 1), encoding="utf-8")
    print("Configuration.cs: patched")
