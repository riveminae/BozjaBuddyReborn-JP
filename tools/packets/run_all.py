from __future__ import annotations

import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PACKETS = pathlib.Path(__file__).resolve().parent
PACKET_RE = re.compile(r"^p(\d+)_(\d+)_")


def packet_key(path: pathlib.Path) -> tuple[int, int, str]:
    match = PACKET_RE.match(path.name)
    if not match:
        return (10_000, 10_000, path.name)
    return (int(match.group(1)), int(match.group(2)), path.name)


def main() -> int:
    scripts = sorted(
        (p for p in PACKETS.glob("p*.py") if p.name != pathlib.Path(__file__).name),
        key=packet_key,
    )
    if not scripts:
        print("No packet patches to apply.")
        return 0

    # GitHub's Windows runner can expose cp1252 to child Python processes. Several packet markers
    # intentionally contain Japanese UI text, so one successful source patch must never turn into
    # a failed CI run merely because the packet tries to describe that patch on stdout.
    child_env = os.environ.copy()
    child_env["PYTHONIOENCODING"] = "utf-8:backslashreplace"
    child_env["PYTHONUTF8"] = "1"

    for script in scripts:
        print(f"::group::packet {script.name}")
        subprocess.run([sys.executable, str(script)], cwd=ROOT, check=True, env=child_env)
        print("::endgroup::")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
