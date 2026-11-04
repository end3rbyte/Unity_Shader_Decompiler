# Review plan

The former aggregate branch is split into twelve focused draft PRs based on upstream `1850608`. Register bit preservation and structured loads are reviewed together in #10; the redundant #7 is closed. Review the input-compatibility fix (#6) first to resolve the PicaVoxel extraction regression. The numeric-formatting fix (#5) also removes the upstream French-locale golden-test failure.

| PR | Scope | Branch |
| --- | --- | --- |
| [#1](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/1) | Preserve values across conditional control flow | `fix/conditional-value-scope` |
| [#2](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/2) | Propagate DirectX subprogram decompilation failures | `fix/directx-fail-fast` |
| [#3](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/3) | Read SFI0 as a 64-bit feature mask | `fix/dxbc-feature-flags` |
| [#4](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/4) | Keep explicit zero texture LOD explicit | `fix/explicit-texture-lod` |
| [#5](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/5) | Use invariant numeric literals in shader output | `fix/invariant-numeric-output` |
| [#6](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/6) | Accept AssetStudio and AssetRipper shader JSON schemas | `fix/metadata-offset-schemas` |
| [#8](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/8) | Preserve resource bindings and raw constant-buffer layout | `fix/resource-binding-layouts` |
| [#9](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/9) | Restore runtime keyword selection from serialized variants | `fix/serialized-variant-pairing` |
| [#10](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/10) | Preserve register bits and structured-buffer loads | `fix/structured-buffer-loads` |
| [#11](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/11) | Keep temporary register identities distinct | `fix/temporary-register-names` |
| [#12](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/12) | Preserve vector lanes in casts and dot products | `fix/vector-expression-widths` |
| [#13](https://github.com/Abby1591/Unity_Shader_Decompiler/pull/13) | Preserve serialized integer shader properties | `fix/integer-shader-properties` |

Each branch carries its own generic regression tests and validation notes. All four PicaVoxel end-to-end tests pass on every branch. Except for the numeric-formatting branch, C# isolation runs use an en-US default culture because upstream's golden comparison already fails under fr-FR. The combined checkout passes under fr-FR without that override.

Several emitter PRs regenerate the same golden file. After merging more than one, regenerate that reference from the combined emitter instead of choosing one branch's snapshot or editing shader text.

The combined validation snapshot is published as the tag validation/compatibility-2026-11-04, not another upstream PR. The obsolete aggregate branch was deleted. All review changes were merged from upstream, and the reference shader was regenerated through the combined emitter. Initial combined validation: 117 C# tests, 18 Python tests and all four PicaVoxel end-to-end cases pass. No new C# `var`, proprietary fixtures or local machine paths are included in the upstream PRs.

Two local shaders were independently exported with AssetStudioMod CLI v0.20.0 and AssetRipper from the same bundle. All four pipelines passed native compilation and deterministic DXBC execution comparison. Normalized metadata, decompressed blobs, generated shader/program files and serialized constant inputs match across exporters. The source JSON files and game data stay local.

## Review follow-up

- Restore constant-buffer `size` in #9 and protect both serialized-layout and binding-only paths with a Python serialization test. Repair comment encoding in #6 and #9.
- In #10, native `mov`/`movc` and structured-load round trips verify exact output words. The addressing and raw-register fixes form one unit: correct stride and offset alone still lose integer output bits without raw transport. Structured loads reject fused mode explicitly and require `--no-fuse-temps`. The former #7 is consolidated into this branch and needs no separate merge.
- #2 still contains a production fix absent from upstream master; a local master's copy is not an upstream merge.

Fresh isolated validation (Windows): #6 has 51 C# / 10 Python tests, #9 has 54 C# / 2 Python tests, and the combined #10 has 65 C# tests. All four PicaVoxel cases pass in each checkout. Other follow-up changes only clarify PR descriptions; their previous validation remains recorded in their individual documents.

The new native tests recompile with d3dcompiler and evaluate a restricted bit-copy instruction set against explicit expected words. They reject unexpected operations and are not GPU execution tests. External fixture results above describe the earlier local validation, not a new run in this follow-up.

## Runtime validation follow-up

#9 now offers opt-in runtime keyword reconstruction within the original passes, with strict metadata checks and a hash-bound compilation manifest. Its isolated suite passes 74 C# tests, six Python tests and four PicaVoxel cases; nine generic GPU readbacks pass under Unity CLI with Editor 6000.0.75f1 and D3D11. Full application rendering and resource-binding equivalence remain unverified.

#13 is independent: serialized integer property bits must emit as ShaderLab `Integer`. It passes 59 C# tests and four PicaVoxel cases. #10 additionally fixes saturated moves and integer reads that fxc could fold away through float round trips. Its two new native regressions fail before the saturation fix; all 67 C# tests and four PicaVoxel cases pass afterward.

Final local validation with the combined emitter: both external shaders retain four passes and 216 keyword combinations each. Unity CLI with Editor 6000.0.75f1 compiles all 864 stages; all four AssetStudio/AssetRipper pipelines pass 1,728 stage-instance execution comparisons. Generated shaders, program files, manifests and typed constant inputs match across exporters. Generic GPU selection checks pass nine readbacks. Test inputs now preserve integer property payloads and use bounded integer runtime probes from serialized parameter types; floating-point runtime values and resources remain synthetic. No proprietary fixtures are committed.

## Compatibility follow-up

The five updated PRs preserve CLI defaults and public entry points: #2 adds opt-in --fail-on-error; #3 retains all 35 legacy public fields with HasLegacyStatistics=false; #4 keeps the public recognition pass; #9 retains geometry/tessellation stages and the existing log-and-continue policy in expanded mode; #10 accepts default options and dynamic structured offsets while retaining old IR signatures and enum values. Strict export mode rejects invalid or unrecoverable data rather than guessing.

Combined validation: 129 C# tests, 18 Python tests and four PicaVoxel cases pass. The default-mode geometry regression and structured-load compatibility tests fail before their fixes. Isolated suites pass 55 (#2), 54 (#3), 52 (#4), 78 (#9) and 72 (#10) C# tests, plus all four PicaVoxel cases each. Both external exporters reproduce the previously validated shader/program artifacts exactly; these compatibility changes do not constitute a new full-application GPU validation. No private fixtures or machine-specific paths are committed.
