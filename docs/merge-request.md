# Superseded by focused review branches

The aggregate change is now split into twelve independent draft PRs. See [the review plan](review-plan.md) for their scopes, links and validation details.

The extraction regression is fixed: both AssetStudio and AssetRipper JSON schemas are supported, and all four existing PicaVoxel end-to-end tests pass. The combined branch passes 80 C# tests and 12 Python tests, including the new exporter-compatibility regressions. The SFI0 test correction is independently checked against native compiler output; original integration and golden-comparison assertions remain unchanged.
