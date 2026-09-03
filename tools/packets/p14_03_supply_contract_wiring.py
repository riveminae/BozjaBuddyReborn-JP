from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "tools/validate_v110_contract.py"
text = P.read_text(encoding="utf-8-sig")

marker = "import validate_supply_contract  # run dedicated supply invariants"
if marker in text:
    print("tools/validate_v110_contract.py: supply contract already wired")
    raise SystemExit(0)

anchor = 'print("v1.1 static contract: PASS")\n'
if anchor not in text:
    raise RuntimeError("validate_v110_contract.py final PASS anchor missing")

replacement = f'''# Keep the detailed supply/cache invariants in a focused module while running them in this same\n# pre-compile contract step. Importing the module executes its read-only static checks.\n{marker}\n\n{anchor}'''
P.write_text(text.replace(anchor, replacement, 1), encoding="utf-8")
print("tools/validate_v110_contract.py: supply contract wired")
