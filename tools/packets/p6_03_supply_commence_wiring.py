from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Plugin.cs"
text = P.read_text(encoding="utf-8-sig")


def patch(old: str, new: str, marker: str) -> None:
    global text
    if marker in text:
        print(f"Plugin.cs: already applied ({marker})")
        return
    if old not in text:
        raise RuntimeError(f"Plugin.cs anchor missing: {old[:160]!r}")
    text = text.replace(old, new, 1)
    print(f"Plugin.cs: patched ({marker})")


patch(
    """    private readonly HolsterDriver _holster;\n    private readonly ErrandRunner _errands;\n""",
    """    private readonly HolsterDriver _holster;\n    private readonly LostItemBoxInventory _lostItemInventory;\n    private readonly SupplyManager _supplies;\n    private readonly ErrandRunner _errands;\n""",
    "private readonly SupplyManager _supplies;",
)

patch(
    """        _holster = new HolsterDriver(_config, _lostActions);\n        _errands = new ErrandRunner(_movement, _navmesh);\n        _loadoutDriver = new LoadoutDriver(_lostActions);\n        _signUps = new SignUpRunner();\n""",
    """        _holster = new HolsterDriver(_config, _lostActions);\n        _lostItemInventory = new LostItemBoxInventory(_lostActions);\n        _supplies = new SupplyManager(_config, _lostActions, _lostItemInventory);\n        _errands = new ErrandRunner(_movement, _navmesh);\n        _loadoutDriver = new LoadoutDriver(_lostActions);\n\n        // Registration itself is always immediate and remote. Only the second-phase Commence is\n        // gated, and only for the one Q109C exception: confirmed complete loss of Potion Kit\n        // protection AND usable self-healing. An unavailable inventory read is not critical in\n        // SupplyManager, so this fails open rather than sacrificing a CE to uncertain telemetry.\n        _signUps = new SignUpRunner(() => !_supplies.Evaluate().CriticalNoRecovery);\n""",
    "new SignUpRunner(() => !_supplies.Evaluate().CriticalNoRecovery)",
)

P.write_text(text, encoding="utf-8")
