import json
import tempfile
import unittest
from pathlib import Path

from libs.metadata import ShaderMetadata


class ShaderMetadataTests(unittest.TestCase):
    def test_variant_references_keep_stage_keyword_masks(self):
        shader_pass = {"m_ProgFragment": {
            "m_SerializedKeywordStateMask": [0, 1],
            "m_PlayerSubPrograms": [[
                {"m_GpuProgramType": 17, "m_BlobIndex": 7, "m_KeywordIndices": [1]},
                {"m_GpuProgramType": 23, "m_BlobIndex": 8, "m_KeywordIndices": []},
            ]],
        }}
        self.assertEqual(ShaderMetadata._directx_programs(shader_pass, ["MODE", "DETAIL"]), [
            {"stage": "Fragment", "blobIndex": 7, "keywords": ["DETAIL"],
             "keywordMask": ["DETAIL", "MODE"]},
        ])
        with self.assertRaises(ValueError):
            ShaderMetadata._directx_programs(shader_pass, ["MODE"])

    def test_constant_buffer_offsets_use_serialized_byte_offsets(self):
        shader = {
            "m_Name": "Custom/Test",
            "m_ParsedForm": {
                "m_SubShaders": [{
                    "m_Passes": [{
                        "m_NameIndices": {"Globals": 1, "Tint": 2, "Transform": 3},
                        "m_ProgVertex": {"m_CommonParameters": {
                            "m_ConstantBufferBindings": [{"m_NameIndex": 1, "m_Index": 2}],
                            "m_ConstantBuffers": [{
                                "m_NameIndex": 1,
                                "m_Size": 128,
                                "m_VectorParams": [{
                                    "m_NameIndex": 2,
                                    "m_OffsetInConstantBuffer": 20,
                                    "m_Dim": 3,
                                }],
                                "m_MatrixParams": [{
                                    "m_NameIndex": 3,
                                    "m_OffsetInConstantBuffer": 48,
                                    "m_RowCount": 4,
                                }],
                            }],
                        }},
                    }],
                }],
            },
        }
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "shader.json"
            path.write_text(json.dumps(shader), encoding="utf-8")
            metadata = ShaderMetadata(str(path)).build()

        buffers = metadata["subShaders"][0]["passes"][0]["constantBuffers"]
        self.assertEqual(buffers[0]["slot"], 2)
        self.assertEqual(
            [(v["name"], v["offset"]) for v in buffers[0]["variables"]],
            [("Tint", 20), ("Transform", 48)],
        )


if __name__ == "__main__":
    unittest.main()
