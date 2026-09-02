from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/FieldTravelRouter.cs"
text = P.read_text(encoding="utf-8-sig")

# p9_04 predates measured route-cost planning and keys one idempotency check to the established
# Return/Lifestream reset block. p2_08 adds planning state; keep it immediately BEFORE that block
# so the later packet still recognizes its own already-applied feature.
normalized = """        _planningStart = Vector3.Zero;
        _planningDeparturePoint = Vector3.Zero;
        _planningStartedMs = 0;
        _planningCancelSent = false;
        _returnStartedMs = 0;
        _returnConfirmationSent = false;
        _optionalLifestreamWaitStartedMs = 0;

        var territory = Svc.ClientState.TerritoryType;
"""

if normalized in text:
    print("Automation/FieldTravelRouter.cs: Plan reset order already normalized")
    raise SystemExit(0)

interleaved = """        _returnStartedMs = 0;
        _returnConfirmationSent = false;
        _optionalLifestreamWaitStartedMs = 0;
        _planningStart = Vector3.Zero;
        _planningDeparturePoint = Vector3.Zero;
        _planningStartedMs = 0;
        _planningCancelSent = false;

        var territory = Svc.ClientState.TerritoryType;
"""

if interleaved not in text:
    if "PathCostPlanningWaitMs" not in text:
        print("Automation/FieldTravelRouter.cs: measured planning not present; reset normalization not needed")
        raise SystemExit(0)
    raise RuntimeError("FieldTravelRouter measured planning exists but Plan reset layout is unexpected")

P.write_text(text.replace(interleaved, normalized, 1), encoding="utf-8")
print("Automation/FieldTravelRouter.cs: Plan reset order normalized before p9_04")
