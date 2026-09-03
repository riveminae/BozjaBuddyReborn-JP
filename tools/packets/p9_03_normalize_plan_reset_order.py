from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/FieldTravelRouter.cs"
text = P.read_text(encoding="utf-8-sig")

# p9_04 predates measured route-cost planning. It only needs this established Return/Lifestream
# reset sequence to stay contiguous; the number and names of planning fields before it are irrelevant.
downstream_marker = """        _returnStartedMs = 0;
        _returnConfirmationSent = false;
        _optionalLifestreamWaitStartedMs = 0;

        var territory = Svc.ClientState.TerritoryType;
"""
if downstream_marker in text:
    print("Automation/FieldTravelRouter.cs: Plan reset block already compatible with p9_04")
    raise SystemExit(0)

if "PathCostPlanningWaitMs" not in text:
    print("Automation/FieldTravelRouter.cs: measured planning not present; reset normalization not needed")
    raise SystemExit(0)

# Handle the original p2_08 layout generically: Return/Lifestream resets came first, followed by
# one or more _planning* resets. Move that whole planning block before Return without knowing its
# exact future shape. This keeps later P2 refinements from making the compatibility packet brittle.
start = text.find("        _returnStartedMs = 0;\n        _returnConfirmationSent = false;\n        _optionalLifestreamWaitStartedMs = 0;\n")
if start < 0:
    raise RuntimeError("FieldTravelRouter measured planning exists but Return/Lifestream Plan reset block is missing")

planning_start = text.find("        _planning", start)
territory = text.find("        var territory = Svc.ClientState.TerritoryType;\n", start)
if planning_start < 0 or territory < 0 or planning_start >= territory:
    raise RuntimeError("FieldTravelRouter measured planning exists but interleaved planning reset block was not found")

return_block = text[start:planning_start]
planning_block = text[planning_start:territory]
replacement = planning_block + return_block + "        var territory = Svc.ClientState.TerritoryType;\n"
text = text[:start] + replacement + text[territory + len("        var territory = Svc.ClientState.TerritoryType;\n"):]

P.write_text(text, encoding="utf-8")
print("Automation/FieldTravelRouter.cs: Plan reset order normalized before p9_04")
