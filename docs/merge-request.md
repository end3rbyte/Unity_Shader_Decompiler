# Preserve DXBC semantics and serialized variant coverage

Fix raw register storage, comparison masks, integer negation, dot widths, constant-buffer offsets and nested-branch phi copies. Preserve explicit texture LOD and floating-point rounding, including the addition that fxc removes from `frac(x + integer)`. Numeric literals use invariant formatting.

Use serialized program indices and stage keyword masks when available. Fragment-only keywords can reuse a vertex program; missing stages or uncovered programs fail explicitly. The existing expansion into separate passes remains; runtime keyword selection is not implemented by this change.

Validation: 74 C# tests, including native D3D compilation, and two Python extraction tests. Regression cases use generic inputs or existing checked-in fixtures and require no external shader files or local paths.

Existing integration assertions are unchanged: recovered buffer names and matrix names remain required. The emitter now retains the buffer name for raw storage and keeps samplers with distinct slots separate.

The SFI0 test correction is retained: [Microsoft's container definition](https://github.com/microsoft/DirectXShaderCompiler/blob/main/include/dxc/DxilContainer/DxilContainer.h) specifies one 64-bit feature mask. A new test independently compiles a double-precision shader with the native compiler and verifies its eight-byte SFI0 payload.

The golden comparison assertion is unchanged. Its expected output is regenerated because the old output contained an undeclared sampler and placed fallback storage after named members, shifting byte offsets. New native tests compile both golden stages and verify that raw storage starts at byte zero with the expected size and binding. Other golden changes reflect the separately tested lane, LOD and temporary-name fixes.
