# Superseded by focused review branches

The aggregate change is now split into eleven focused draft PRs. See [the review plan](review-plan.md) for their scopes, links and validation details.

The extraction regression is fixed: both AssetStudio and AssetRipper JSON schemas are supported, and all four existing PicaVoxel end-to-end tests pass. The combined branch passes 87 C# tests and 14 Python tests, including the new exporter-compatibility regressions. The SFI0 test correction is independently checked against native compiler output; original integration and golden-comparison assertions remain unchanged.

The review follow-up restores constant-buffer sizes in the isolated variant PR, repairs comment encoding, and adds native integer bit-preservation tests. Register bit preservation and structured loads are combined in one PR (#10); see the review plan for scopes and reproducible validation.
