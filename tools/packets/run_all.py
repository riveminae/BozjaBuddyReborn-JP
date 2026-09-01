from __future__ import annotations

import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PACKETS = pathlib.Path(__file__).resolve().parent


def main() -> int:
    scripts = sorted(
        p for p in PACKETS.glob("p*.py")
        if p.name != pathlib.Path(__file__).name
    )
    if not scripts:
        print("No packet patches to apply.")
        return 0

    for script in scripts:
        print(f"::group::packet {script.name}")
        subprocess.run([sys.executable, str(script)], cwd=ROOT, check=True)
        print("::endgroup::")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
