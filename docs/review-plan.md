# Review plan

The former aggregate branch is split into twelve independent draft PRs, all based directly on upstream `1850608`. Review the input-compatibility fix (#6) first to resolve the PicaVoxel extraction regression. The numeric-formatting fix (#5) also removes the upstream French-locale golden-test failure.

| PR | Scope | Branch |
| --- | --- | --- |
| [#1](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/1) | Preserve values across conditional control flow | `fix/conditional-value-scope` |
| [#2](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/2) | Propagate DirectX subprogram decompilation failures | `fix/directx-fail-fast` |
| [#3](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/3) | Read SFI0 as a 64-bit feature mask | `fix/dxbc-feature-flags` |
| [#4](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/4) | Keep explicit zero texture LOD explicit | `fix/explicit-texture-lod` |
| [#5](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/5) | Use invariant numeric literals in shader output | `fix/invariant-numeric-output` |
| [#6](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/6) | Accept AssetStudio and AssetRipper shader JSON schemas | `fix/metadata-offset-schemas` |
| [#7](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/7) | Preserve register bits and arithmetic in no-fuse mode | `fix/raw-register-semantics` |
| [#8](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/8) | Preserve resource bindings and raw constant-buffer layout | `fix/resource-binding-layouts` |
| [#9](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/9) | Pair shader variants using serialized program references | `fix/serialized-variant-pairing` |
| [#10](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/10) | Preserve structured resource strides and load offsets | `fix/structured-buffer-loads` |
| [#11](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/11) | Keep temporary register identities distinct | `fix/temporary-register-names` |
| [#12](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/12) | Preserve vector lanes in casts and dot products | `fix/vector-expression-widths` |

Each branch carries its own generic regression tests and validation notes. All four PicaVoxel end-to-end tests pass on every branch. Except for the numeric-formatting branch, C# isolation runs use an en-US default culture because upstream's golden comparison already fails under fr-FR. The combined checkout passes under fr-FR without that override.

Several emitter PRs regenerate the same golden file. Their production changes are independent; after merging more than one, regenerate that reference from the combined emitter instead of choosing one branch's snapshot or editing shader text.

`integration/review-validation` is the combined validation checkout, not another upstream PR. The former `fix/dxbc-hlsl-generation` branch remains available as a historical reference. Combined validation: 80 C# tests, 12 Python tests and all four PicaVoxel end-to-end cases pass. No new C# `var`, proprietary fixtures or local machine paths are included in the upstream PRs.

Two local shaders were independently exported with AssetStudioMod CLI v0.20.0 and AssetRipper from the same bundle. All four pipelines passed native compilation and deterministic DXBC execution comparison. Normalized metadata, decompressed blobs, generated shader/program files and serialized constant inputs match across exporters. The source JSON files and game data stay local.
