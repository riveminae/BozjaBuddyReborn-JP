from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WINDOWS = ROOT / "Windows"

# Calls whose literal arguments are rendered directly to users. Loc.T(...) calls are intentionally
# excluded because the JP fork's Loc.T always returns the Japanese branch.
VISIBLE_METHODS = {
    "Text",
    "TextColored",
    "TextUnformatted",
    "Button",
    "SmallButton",
    "Checkbox",
    "SetTooltip",
    "BeginTabItem",
    "BeginCombo",
    "Selectable",
    "InputTextWithHint",
}

# Pure technical/game vocabulary is allowed to remain Latin when it is itself the canonical term.
# Sentences made only of English words are not allowed just because they contain one of these.
TECH_ONLY = {
    "CE",
    "DPS",
    "RSR",
    "BossMod",
    "vnavmesh",
    "Lifestream",
    "Aethernet",
    "Essence",
    "Deep",
    "Duty Action",
    "Duty Action 1",
    "Duty Action 2",
}

JP_RE = re.compile(r"[\u3040-\u30ff\u3400-\u9fff]")
ASCII_WORD_RE = re.compile(r"[A-Za-z]{2,}")
STRING_RE = re.compile(r'(?<!@)\$?"(?:\\.|[^"\\])*"')


def strip_comments(text: str) -> str:
    # Preserve strings while removing comments well enough for ImGui call discovery. This is not a
    # C# parser; it deliberately errs toward reporting a candidate rather than rewriting source.
    out: list[str] = []
    i = 0
    in_string = False
    while i < len(text):
        if in_string:
            out.append(text[i])
            if text[i] == "\\" and i + 1 < len(text):
                i += 1
                out.append(text[i])
            elif text[i] == '"':
                in_string = False
            i += 1
            continue
        if text[i] == '"':
            in_string = True
            out.append(text[i])
            i += 1
            continue
        if text.startswith("//", i):
            j = text.find("\n", i)
            if j < 0:
                break
            out.append("\n")
            i = j + 1
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = len(text) if j < 0 else j + 2
            continue
        out.append(text[i])
        i += 1
    return "".join(out)


def iter_calls(text: str):
    for match in re.finditer(r"ImGui\.([A-Za-z0-9_]+)\s*\(", text):
        method = match.group(1)
        if method not in VISIBLE_METHODS:
            continue
        start = match.start()
        pos = match.end()
        depth = 1
        in_string = False
        while pos < len(text) and depth:
            ch = text[pos]
            if in_string:
                if ch == "\\":
                    pos += 2
                    continue
                if ch == '"':
                    in_string = False
            else:
                if ch == '"':
                    in_string = True
                elif ch == "(":
                    depth += 1
                elif ch == ")":
                    depth -= 1
            pos += 1
        if depth == 0:
            yield method, start, text[start:pos]


def literal_value(token: str) -> str:
    # We only need the human-visible shape, not a complete C# escape decoder. In particular,
    # running UTF-8 Japanese bytes through Python's unicode_escape corrupts them into C1 control
    # characters and then the Windows runner cannot print the result. Preserve Unicode verbatim
    # and normalize the few escapes relevant to UI text.
    first = token.find('"')
    body = token[first + 1 : -1]
    return (
        body.replace(r"\n", "\n")
        .replace(r"\r", "\r")
        .replace(r"\t", "\t")
        .replace(r'\"', '"')
        .replace(r"\\", "\\")
    )


def is_internal_or_format(value: str) -> bool:
    stripped = value.strip()
    if not stripped:
        return True
    if stripped.startswith("##"):
        return True
    if stripped in TECH_ONLY:
        return True
    if re.fullmatch(r"[%0-9.\-+/:() ]+", stripped):
        return True
    return False


def main() -> int:
    # GitHub's Windows runner may expose a cp1252 stdout even though every repository file is
    # UTF-8. Force the diagnostic stream to match the source we are auditing.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
    except Exception:
        pass

    findings: list[tuple[str, int, str, str]] = []
    for path in sorted(WINDOWS.glob("*.cs")):
        original = path.read_text(encoding="utf-8-sig")
        text = strip_comments(original)
        for method, offset, call in iter_calls(text):
            if "Loc.T(" in call:
                continue
            line = text.count("\n", 0, offset) + 1
            for token in STRING_RE.findall(call):
                value = literal_value(token)
                if is_internal_or_format(value):
                    continue
                if JP_RE.search(value):
                    continue
                if not ASCII_WORD_RE.search(value):
                    continue
                findings.append((path.name, line, method, value.replace("\n", "\\n")))

    if not findings:
        print("visible Japanese UI audit: no English-only direct ImGui literals found")
        return 0

    print("visible Japanese UI audit: review candidates")
    for file, line, method, value in findings:
        print(f"  {file}:{line} ImGui.{method}: {value}")
    print(f"candidates={len(findings)}")
    # Report-only for the first pass. Once current findings are triaged/fixed, CI promotes this to
    # a strict gate so new English-only visible literals cannot creep back in.
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
