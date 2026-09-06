# Japanese live-review packet acceptance

Date: 2026-09-06. User request: prepare live-game test perspectives for a native
Japanese human reviewer with no project context; leave no English in those
perspectives and use official FFXIV Japanese terms.

Scope: requirements 2–15, 17–20 and 23; execution packets P15-01/02/03. This is a
test-design/documentation packet, not a claim of implementation or live acceptance.
No production behavior, existing tests, CI, fixtures or acceptance thresholds may
be changed by this packet.

## Frozen acceptance criteria

1. The reviewer-facing artifact is `docs/testing/実機受入試験票.md`. Its entire raw
   contents, including headings, tables, hidden links and comments, contain no
   Latin-script characters. Compatibility forms such as full-width letters,
   mathematical letters and Roman numerals are also rejected. No command, URL,
   English acronym or untranslated internal state is passed to the reviewer.
2. The document identifies audience, required preparation, Japanese-only operation
   references, consent/consumable limits, immediate stop conditions, definitions,
   per-case setup/action/expected result/evidence, result classifications, and
   cleanup. Missing setup and unavailable conditions cannot become passes.
3. Requirements are the expected behavior, not the current partial implementation.
   Cover both fields, installation/update without manual archives, stable fallback,
   migration, single-start loop, transactional initialization, survival permissions
   and thresholds, supply/CE conflicts, actual CE target identity, Relic behavior,
   navigation/danger/escape, recovery/dependency failures, social negatives,
   Japanese presentation and diagnostics. All variants receive separate results.
4. Official names are verified against publisher-authored Japanese material.
   Maintainer-only source links and requirement mapping live outside the
   reviewer-facing artifact. Community blogs are not official terminology evidence.
5. No human/native-speaker or live-game execution is claimed. Source-only checks
   (licenses, architecture, game-memory safety, builds) remain maintainer gates.

## Deterministic language check

Run from repository root with Python 3 (standard library only):

```powershell
python -B -c "from pathlib import Path; import unicodedata as u; p=Path('docs/testing/実機受入試験票.md'); s=u.normalize('NFKD', p.read_text(encoding='utf-8')); bad=[(i,c) for i,c in enumerate(s) if 'LATIN' in u.name(c,'')]; assert not bad, bad[:10]; print('PASS: no Latin characters, including compatibility forms')"
```

Prove that the same predicate rejects `Start`, `ＣＥ`, `Ⅳ`, `𝑥`, accented and
non-decomposing Latin letters, and an English URL hidden behind a Japanese label.
Then run it on the artifact. This only proves absence of Latin characters, not
semantic translation quality; official term comparison and readability review
are separate required checks.

## Objective document review

Read only the reviewer artifact, without using source code or implementation
rationale. For each case, identify prerequisites, the action, observable pass
conditions and evidence. Confirm every case maps to authoritative requirements;
verify that preparation-only fault injection is not presented as an existing
product control. Record exact executed commands, exits, coverage and limitations
below after verification. User-disabled orchestration remains disabled; do not
claim an independent agent review.

## Added clipboard requirement (before implementation)

The user additionally requested sharing test results by copying logs to the
clipboard. Add an opt-in, in-memory test recorder beside existing diagnostics.
It must not drive gameplay or change the existing developer log/copy path.

- Record test and variant numbers, numeric plugin version, start/end/observation
  times, human-selected outcome (initially undecided), and structured gameplay
  observations. Never infer pass from state or lack of errors.
- Export Japanese-only labels and official game terms. Never ingest arbitrary
  log/status strings, user text, names, world/chat/party data, paths or credentials.
  Numeric event identifiers are game data, not character identifiers.
- Capture on the framework update even while the window is closed, at most once
  per second. Store at most 512 samples. Report dropped samples, capture failures
  and long sampling gaps; absence of evidence is not a pass. This sampled summary
  is not an action trace or proof of server acknowledgement.
- End freezes the sample sequence. New recording must not replace an uncopied
  session. Clipboard access happens only after an explicit user click; failed copy
  must leave the session available. Never write the shared report to disk/network.
- Link the production managed recorder into a package-free .NET executable test.
  Freeze tests before implementation, observe red, then green without changing
  inputs. Add that executable to existing CI, retaining all previous checks.
- Run recorder tests, existing CE selection tests, both packet replays with
  unchanged tracked diff, static contract, Japanese UI audit, Debug/Release builds,
  diff check. Actual in-game UI/clipboard behavior remains a human live gate.

## Observed verification

Local tools: .NET SDK 10.0.400; Python 3.13. Each command below was invoked with
the installed executable's resolved path, from the repository root.

| Command/check | Exit | Observed evidence |
|---|---:|---|
| Language command above, before artifact creation | 1 | FileNotFoundError; absent artifact not accepted |
| `dotnet run --project tests/LiveReview/LiveReview.Tests.csproj`, before recorder implementation | 1 | CS2001 for missing production source |
| Same recorder command, final source | 0 | 94 checks passed |
| Same recorder command, active verdict / uncopied replacement / unbounded buffer mutants | 1 each | Each rejected by the corresponding assertion; mutations restored |
| `dotnet run --project tests/CeSelection/CeSelection.Tests.csproj` | 0 | 144/144 passed |
| `python -B tools/packets/run_all.py`, twice after final changes | 0 each | Tracked diff plus new source/test/document hashes unchanged |
| `python -B tools/validate_v110_contract.py` | 0 | v1.1 and survival supply contracts passed |
| `python -B tools/audit_visible_japanese.py` | 0 | Existing visible UI audit passed; not proof of every runtime string |
| `dotnet build BozjaBuddyReborn.csproj -c Debug --no-restore --nologo` | 0 | Build succeeded |
| `dotnet build BozjaBuddyReborn.csproj -c Release --no-restore --nologo` | 0 | Build succeeded |
| `git diff --check` | 0 | No whitespace errors |
| Release ZIP entry inspection using `System.IO.Compression.ZipFile.OpenRead` | 0 | 6 entries, plugin DLL present, no LiveReview.Tests/CeSelection.Tests executable |
| Final document completeness/language command below | 0 | 55 cases, all four fields in each, no Latin, 7 text mutants rejected |

Both local builds retain NU1900: NuGet vulnerability metadata was unavailable.
No audit setting was disabled. The initial packet replay failed on the existing
`p14_02_stable_fallback_ui.py` contiguous field marker. Moving the new fields
outside that marker fixed replay without editing the packet or its validation.

### Test-runner incident and recovery

The initial three source mutations were detected, but uncaught assertion
exceptions triggered a Windows application-error dialog. One process remained
alive and locked the test executable, causing a subsequent build to fail with
MSB3027/MSB3021. This was a test harness error, not a plugin/game crash.

Only the verified repository-local test process and its test child were stopped.
The test program now wraps the unchanged assertions in a top-level catch that
prints `FAIL` and sets `Environment.ExitCode = 1`. It does not swallow failures
into success. Re-running all three mutants then returned exit 1 normally; final
source returned exit 0 (94 checks), and process inspection found zero remaining
`LiveReview.Tests` processes.

Frozen pre-implementation Program.cs SHA-256:
`673b53cb5812642c405c3d6f3cf5b4dbd2800c4d0f91cfafc50c0535a0795970`.
Removing only the added outer catch/try from final source reproduces exactly that
hash. No assertion, fixture, expected value or condition changed.
Final Program.cs SHA-256:
`ea9cb0222b571e4be3bd2a760b2fbcb701c93293901c8a75757880c238bcf6c3`.
Unchanged project file SHA-256:
`f6152bf4c6e7004fb43c972d2333804d6ed8f85a2520ba5ebf7adfc7dee8e759`.

### Document check command

```powershell
python -B -c "from pathlib import Path; import re,unicodedata as u; s=Path('docs/testing/実機受入試験票.md').read_text(encoding='utf-8'); cases=re.findall(r'^### 試験(\d{2})[^\n]*\n(.*?)(?=^### |^## |\Z)',s,re.M|re.S); assert [n for n,_ in cases]==[f'{i:02}' for i in range(1,56)]; assert all(all(label in body for label in ('準備：','操作：','合格条件：','記録：')) for _,body in cases); bad=lambda text: any('LATIN' in u.name(c,'') for c in u.normalize('NFKD',text)); assert not bad(s); assert all(bad(s+m) for m in ('Start','ＣＥ','Ⅳ','𝑥','é','ł','[日本語](https://example.com)')); print('PASS: 55 complete cases; no Latin; 7 forbidden-text mutants rejected')"
```

### Direct review and limits

- Direct requirement/readability review: original requirements mapped to the 55
  cases in `docs/research/japanese-review-terminology.md`; setup/action/observable
  expectation/evidence are present in every case. Preparation gates explicitly
  cover unavailable fault injection, unidentified controls and missing stock.
- Actual clipboard call, closed-window framework capture, game data correctness,
  all field journeys, configuration restore and a native human's comprehension
  have NOT been executed here. Those remain live gates; no independent agent or
  human review is claimed. No main merge acceptance is granted by this packet.
- Source review: added recorder only consumes primitive/structured game values;
  no raw logs, names or free text. Its exceptions increment a counter. Existing
  controller, combat, CE click arguments and developer diagnostics are unchanged.
- This is a sampled summary, not a full action log. Inventory/objective/event
  caches may be stale while stopped/waiting; exported text and the trial sheet
  explicitly require comparison with actual game UI. No server write is inferred.
