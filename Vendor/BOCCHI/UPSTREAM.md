# BOCCHI upstream provenance

BOCCHI-derived navigation code in this directory is tracked against:

- Repository: `OhKannaDuh/BOCCHI`
- Pinned commit: `7847b00c313d6a7ddfe9ee126e46e10f547db9da`
- License: GNU AGPL-3.0

## Watched upstream sources

The weekly workflow watches these BOCCHI source paths because they feed the vendored navigation model used by this fork:

- `BOCCHI.Common/Data/Zones/NavigationApproach.cs`
- `BOCCHI.Common/Data/Zones/Graph/Traversal/WalkTeleportWalkCalculator.cs`
- `BOCCHI.Common/Data/Zones/Graph/Traversal/ReturnTeleportWalkCalculator.cs`

When additional BOCCHI source is vendored, add its original upstream path to `.github/workflows/check-bocchi-upstream.yml` in the same commit.

Do not auto-merge upstream changes. Review them against the BBR adapter and the AGPL attribution before updating the pinned commit.
