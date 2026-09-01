from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
P = ROOT / "Automation/BozjaController.cs"
text = P.read_text(encoding="utf-8-sig")

# Field wiring.
if "private readonly SafeStopCoordinator _safeStop" not in text:
    old = """    private readonly DeathRecoveryDriver _deathRecovery;
    private readonly DependencySupervisor _dependencies;

"""
    new = """    private readonly DeathRecoveryDriver _deathRecovery;
    private readonly DependencySupervisor _dependencies;
    private readonly SafeStopCoordinator _safeStop = new();

"""
    if old not in text:
        raise RuntimeError("BozjaController.cs SafeStop field anchor missing")
    text = text.replace(old, new, 1)

# Start() reset. Other packets legitimately insert their own reset calls between dependencies and
# death recovery, so inspect only the Start block and insert directly after dependencies.Reset.
start_begin = text.find("    public void Start()")
start_end = text.find("        // A run starts from nothing.", start_begin)
if start_begin < 0 or start_end < 0:
    raise RuntimeError("BozjaController.cs Start block missing")
start_block = text[start_begin:start_end]
if "_safeStop.Reset();" not in start_block:
    anchor = "        _dependencies.Reset();\n"
    pos = text.find(anchor, start_begin, start_end)
    if pos < 0:
        raise RuntimeError("BozjaController.cs dependencies reset missing inside Start")
    pos += len(anchor)
    text = text[:pos] + "        _safeStop.Reset();\n" + text[pos:]

# Timeout handling. Once composed, use the semantic marker rather than an exact surrounding block.
if "var safeStop = _safeStop.Tick" not in text:
    old = """            // P9-03 adds the safe-return policy. Until that packet is present, timeout fails closed.
            Stop($"必須プラグインが60秒以内に復帰しませんでした: {dependency.MissingText}。");
            return;
        }

        if (!_navmesh.MeshReady)
"""
    new = """            var safeStop = _safeStop.Tick(Svc.Condition[ConditionFlag.InCombat]);
            Status = safeStop.JapaneseStatus + $" ({dependency.MissingText})";
            if (safeStop.StopNow)
                Stop(Status);
            return;
        }

        // A recovered dependency cancels any pending pre-stop Return state.
        _safeStop.Reset();

        if (!_navmesh.MeshReady)
"""
    if old not in text:
        raise RuntimeError("BozjaController.cs dependency timeout fallback anchor missing")
    text = text.replace(old, new, 1)

P.write_text(text, encoding="utf-8")
print("Automation/BozjaController.cs: safe dependency-stop policy ready")
