# Tests exact per-program shader parameter association and corruption failures.
import struct
import unittest

from libs.program_parameters import extract_program_parameters


# Encodes a padded Unity parameter identifier.
def name(value):
    data = value.encode("utf-8")
    return struct.pack("<i", len(data)) + data + bytes((-len(data)) % 4)


# Builds a minimal versioned code and parameter table.
def blob(texture_slot=1):
    code = struct.pack("<ii", 202012090, 17)
    parameters = (struct.pack("<ii", 202012090, 0) + struct.pack("<i", 1) +
                  name("_Tex") + struct.pack("<iiii", 0, texture_slot, 0, 4))
    start = 4 + 2 * 12
    table = struct.pack("<i", 2)
    table += struct.pack("<iii", start, len(code), 0)
    table += struct.pack("<iii", start + len(code), len(parameters), 0)
    return table + code + parameters


# Builds one pass with an explicit fragment/parameter pair.
def shader(texture_slot=1):
    common = {"m_ConstantBuffers": [], "m_ConstantBufferBindings": [],
              "m_TextureParams": [{"m_NameIndex": 0, "m_Index": texture_slot,
                                   "m_SamplerIndex": 0, "m_Dim": 2}],
              "m_Samplers": []}
    fragment = {"m_PlayerSubPrograms": [[{"m_BlobIndex": 0,
                 "m_GpuProgramType": 17, "m_KeywordIndices": [0]}]],
                "m_ParameterBlobIndices": [[1]],
                "m_SerializedKeywordStateMask": [0], "m_CommonParameters": common}
    return {"m_Platforms": [4], "m_ParsedForm": {"m_KeywordNames": ["MODE"],
            "m_SubShaders": [{"m_Passes": [{"m_NameIndices": {"_Tex": 0},
                            "m_ProgFragment": fragment}]}]}}


# Covers exact references and explicit failures for inconsistent records.
class ProgramParameterTests(unittest.TestCase):
    # Keeps the serialized pass, tier, keyword, and resource identity.
    def test_extracts_exact_program_context(self):
        result = extract_program_parameters(shader(), blob())
        self.assertEqual(len(result), 1)
        context = result[0][0]
        self.assertEqual((context["stage"], context["groupIndex"], context["programIndex"]),
                         ("Fragment", 0, 0))
        self.assertEqual((context["blobIndex"], context["parameterBlobIndex"]), (0, 1))
        self.assertEqual((context["keywords"], context["keywordMask"]), (["MODE"], ["MODE"]))
        self.assertEqual(context["commonParameters"]["m_TextureParams"][0]["resolvedName"], "_Tex")
        self.assertEqual(context["binaryParameters"]["resources"][0]["slot"], 1)

    # Retains separate tier positions even when bytecode is shared.
    def test_repeated_code_reference_keeps_both_contexts(self):
        item = shader()
        program = item["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][0]["m_ProgFragment"]
        program["m_PlayerSubPrograms"].append(list(program["m_PlayerSubPrograms"][0]))
        program["m_ParameterBlobIndices"].append([1])
        contexts = extract_program_parameters(item, blob())[0]
        self.assertEqual([entry["groupIndex"] for entry in contexts], [0, 1])
        self.assertEqual([entry["blobIndex"] for entry in contexts], [0, 0])

    # Rejects a missing parameter association.
    def test_legacy_absence_and_modern_count_mismatch(self):
        item = shader()
        del item["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][0]["m_ProgFragment"]["m_ParameterBlobIndices"]
        self.assertIsNone(extract_program_parameters(item, blob()))
        item["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][0]["m_ProgFragment"]["m_ParameterBlobIndices"] = []
        with self.assertRaisesRegex(ValueError, "mismatched Fragment parameter indices"):
            extract_program_parameters(item, blob())

    # Rejects a contradictory slot instead of selecting either source.
    def test_common_and_binary_slot_conflict_fails(self):
        with self.assertRaisesRegex(ValueError, "Conflicting serialized _Tex binding slot"):
            extract_program_parameters(shader(2), blob(1))

    # Rejects a parameter record that points outside the blob.
    def test_parameter_record_bounds_fail(self):
        data = bytearray(blob())
        struct.pack_into("<i", data, 4 + 12 + 4, 10000)
        with self.assertRaisesRegex(ValueError, "exceeds its segment"):
            extract_program_parameters(shader(), bytes(data))

    # Rejects a code record whose declared stage differs from JSON.
    def test_code_stage_conflict_fails(self):
        data = bytearray(blob())
        struct.pack_into("<i", data, 4 + 24 + 4, 15)
        with self.assertRaisesRegex(ValueError, "does not match its stage"):
            extract_program_parameters(shader(), bytes(data))


if __name__ == "__main__":
    unittest.main()
