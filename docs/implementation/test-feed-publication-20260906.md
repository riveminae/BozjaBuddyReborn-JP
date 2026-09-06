# Test-feed publication acceptance

Scope: original request to install/update through the existing Dalamud repository,
including the Japanese live-review clipboard addition. Test-only publication;
not stable promotion or main merge acceptance.

## Frozen publication checks

- The raw feed root is a JSON array even when it contains one plugin. Reject an
  object root; do not normalize an invalid input into an array inside the checker.
- Preserve `InternalName=BozjaBuddyReborn`, API level 15 and the test channel.
  Install, Update and Testing URLs identify the same immutable versioned ZIP.
- Manifest versions equal the validated CI artifact's assembly version. Publish
  the exact artifact bytes; compare SHA-256 before and after public download.
- Create a previously absent version tag; never retarget an existing tag or
  overwrite a release asset. Keep the stable feed/main unchanged.
- Validate the public feed and archive again after publication. Actual Dalamud
  update/reload and the live clipboard journey remain human checks.

## Discovery

At `295d941`, the existing tracked `pluginmaster-test.json` root is an object.
The workflow expression `@($entry) | ConvertTo-Json` unwraps a one-entry array in
the PowerShell pipeline. The version checker accepted it because indexing a
PowerShell scalar with `[0]` does not prove JSON array shape.

Read-only root-kind check using `System.Text.Json.JsonDocument.Parse` rejected the
existing file with exit 1 (`Object`). Change serialization to
`ConvertTo-Json -InputObject @($entry)` and add a raw JSON root/count check before
the existing version/URL checks. No existing gate is removed.

The array requirement is confirmed by the
[official Dalamud custom repository guide](https://dalamud.dev/plugin-publishing/custom-repositories/).
The fixed tracked feed and the exact one-entry serialization expression both
passed the same `System.Text.Json` root/count check with exit 0.

## Validated release input

- Source: `dea238af366f01a533d3ade2c008dc0061c5f1fb`.
- [CI run 34021233543](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34021233543),
  run number 169: all checks succeeded, including the raw array/count guard,
  94 recorder checks, 144 CE cases, static/UI audits, packet replay/idempotency,
  Debug/Release and package version checks.
- Artifact: `BozjaBuddyReborn-JP-v1.1-test-1.0.90.169`; downloaded without rebuilding.
- `dist-test/BozjaBuddyReborn-1.0.90.169.zip` SHA-256:
  `e449b65b879bba899fdbad3971f7d13bed4ab4f1353cc308e82fddd600781380`.
- Local artifact inspection: raw array/count, InternalName, both version fields,
  API level, packaged manifest version, and absence of test executables passed.
- Release feed changes only version/immutable URLs and a Japanese changelog from
  the validated artifact metadata. No stable feed or main change.

Publish the tag and feature-branch feed atomically, then validate the public raw
feed and archive hash. A successful public download does not replace the human
Dalamud update/reload/clipboard tests in the Japanese trial sheet.

## Published and verified

- Release commit: `38e7f831bbc9cdfca89305957b0c6c914591c9a0`.
  New annotated tag `test-1.0.90.169` and the feature feed were pushed atomically.
  No existing tag, stable feed or main ref was changed.
- Authenticated publishing was followed by an unauthenticated `Invoke-WebRequest`
  to the exact raw feature-feed URL (no cache-busting query): HTTP 200. Raw JSON
  root/count, both version fields, InternalName and all three pinned URLs passed.
- The pinned public ZIP was downloaded to a new temporary file. Length 563397
  bytes; SHA-256 exactly matches the CI artifact hash above. Both publication
  verification commands exited 0.
- [Release-commit CI run 34021457587](https://github.com/riveminae/BozjaBuddyReborn-JP/actions/runs/34021457587)
  also completed successfully. Its newly numbered candidate is not auto-published;
  the public feed remains the deliberately published `1.0.90.169`.
- Actual Dalamud installation/update, loaded-game UI and human review remain
  unexecuted. PR #3 remains draft; unmet v1.1 behavior is not promoted to main.
