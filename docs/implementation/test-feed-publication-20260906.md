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
