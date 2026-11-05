# Associates Unity's binary parameter records with exact serialized programs.
"""Extract per-pass, per-stage D3D11 parameter references from shader JSON."""

import copy

from .parameter_records import ParameterReader, read_parameters


STAGES = {"Vertex": (15, 16), "Fragment": (17, 18),
          "Geometry": (19, 20), "Hull": (21,), "Domain": (22,)}


# Checks one program-table entry against the decompressed shader blob.
def _record(blob, entries, index):
    if type(index) is not int or not 0 <= index < len(entries):
        raise ValueError(f"Invalid shader blob index: {index!r}")
    offset, length, segment = entries[index]
    if segment != 0 or offset < 4 + len(entries) * 12 or length < 8:
        raise ValueError(f"Invalid shader blob entry: {index}")
    if offset + length > len(blob):
        raise ValueError(f"Shader blob entry exceeds its segment: {index}")
    return blob[offset:offset + length]


# Reads the serialized program/parameter table for one D3D11 segment.
def _entries(shader, blob):
    if shader.get("m_Platforms") != [4]:
        raise ValueError("Per-program parameters require one D3D11 shader segment")
    reader = ParameterReader(blob)
    entries = [tuple(reader.integer() for _ in range(3)) for _ in range(reader.count())]
    if not entries:
        raise ValueError("Shader blob has no program entries")
    return entries


# Resolves a pass-local name index without guessing missing names.
def _name(index, names):
    if type(index) is not int or index not in names:
        raise ValueError(f"Missing serialized shader name index: {index!r}")
    return names[index]


# Adds a resolved name to a copied common-parameter object.
def _resolve(value, names):
    if isinstance(value, list):
        return [_resolve(item, names) for item in value]
    if not isinstance(value, dict):
        return value
    result = {key: _resolve(item, names) for key, item in value.items()}
    if "m_NameIndex" in value:
        result["resolvedName"] = _name(value["m_NameIndex"], names)
    return result


# Reads and validates the names serialized within one pass.
def _names(shader_pass):
    mapping = shader_pass.get("m_NameIndices")
    if not isinstance(mapping, dict):
        raise ValueError("Missing serialized pass name indices")
    result = {}
    for name, index in mapping.items():
        if not isinstance(name, str) or not name or type(index) is not int or index < 0 or index in result:
            raise ValueError(f"Invalid or duplicate serialized name index: {index!r}")
        result[index] = name
    return result


# Checks common and binary identities where both serialize the same resource.
def _verify_common(common, binary):
    resources = binary["resources"]
    for kind, field in ((0, "m_TextureParams"), (1, "m_ConstantBufferBindings")):
        by_name = {item["name"]: item for item in resources if item["kind"] == kind}
        by_slot = {item["slot"]: item for item in resources if item["kind"] == kind}
        for item in common[field]:
            name = item["resolvedName"]
            if name in by_name and item["m_Index"] != by_name[name]["slot"]:
                raise ValueError(f"Conflicting serialized {name} binding slot")
            if item["m_Index"] in by_slot and by_slot[item["m_Index"]]["name"] != name:
                raise ValueError(f"Conflicting serialized resource name at slot {item['m_Index']}")
    buffers = {item["name"]: item for item in binary["constantBuffers"]}
    for item in common["m_ConstantBuffers"]:
        name = item["resolvedName"]
        if name in buffers and item["m_Size"] != buffers[name]["size"]:
            raise ValueError(f"Conflicting serialized {name} buffer size")


# Reads one referenced code record and its paired parameter record.
def _context(blob, entries, code, parameter_id, stage, group_index, program_index,
             common, keyword_names, mask, cache):
    code_id = code["m_BlobIndex"]
    code_record = _record(blob, entries, code_id)
    header = ParameterReader(code_record)
    if header.integer() != 202012090 or header.integer() != code["m_GpuProgramType"]:
        raise ValueError(f"Shader code record {code_id} does not match its stage")
    if code["m_GpuProgramType"] not in STAGES[stage]:
        raise ValueError(f"Shader code record {code_id} has the wrong stage")
    if parameter_id not in cache:
        cache[parameter_id] = read_parameters(_record(blob, entries, parameter_id))
    binary = cache[parameter_id]
    _verify_common(common, binary)
    indices = code["m_KeywordIndices"]
    if any(type(index) is not int or not 0 <= index < len(keyword_names) for index in indices + mask):
        raise ValueError(f"Invalid shader keyword index in code record {code_id}")
    return {"stage": stage, "groupIndex": group_index, "programIndex": program_index,
            "blobIndex": code_id, "parameterBlobIndex": parameter_id,
            "keywords": [keyword_names[index] for index in indices],
            "keywordMask": [keyword_names[index] for index in mask],
            "commonParameters": common, "binaryParameters": copy.deepcopy(binary)}


# Associates every referenced D3D11 code program with its parameter index.
def _stage(blob, entries, program, stage, names, keyword_names, cache):
    groups = program.get("m_PlayerSubPrograms", [])
    parameters = program.get("m_ParameterBlobIndices")
    if parameters is None or len(groups) != len(parameters):
        raise ValueError(f"Missing or mismatched {stage} parameter indices")
    mask = program["m_SerializedKeywordStateMask"]
    common = _resolve(program["m_CommonParameters"], names)
    if any(field not in common or not isinstance(common[field], list)
           for field in ("m_TextureParams", "m_ConstantBufferBindings", "m_ConstantBuffers")):
        raise ValueError(f"Missing serialized {stage} common resource parameters")
    result = []
    for group_index, (group, parameter_group) in enumerate(zip(groups, parameters)):
        if len(group) != len(parameter_group):
            raise ValueError(f"Mismatched {stage} program and parameter counts")
        for program_index, (code, parameter_id) in enumerate(zip(group, parameter_group)):
            if code["m_GpuProgramType"] in STAGES[stage]:
                result.append(_context(blob, entries, code, parameter_id, stage, group_index,
                                       program_index, common, keyword_names, mask, cache))
            else:
                raise ValueError(f"Unsupported D3D11 program type in {stage}: {code['m_GpuProgramType']}")
    return result


# Extracts contexts without merging different passes, tiers, or variants.
def extract_program_parameters(shader, blob):
    parsed = shader.get("m_ParsedForm", shader)
    passes = [shader_pass for subshader in parsed["m_SubShaders"]
              for shader_pass in subshader["m_Passes"]]
    modern = any("m_ParameterBlobIndices" in shader_pass.get("m_Prog" + stage, {})
                 for shader_pass in passes for stage in STAGES)
    if not modern:
        return None
    entries = _entries(shader, blob)
    cache = {}
    result = []
    for shader_pass in passes:
        names = _names(shader_pass)
        contexts = []
        if shader_pass.get("m_ProgRayTracing", {}).get("m_PlayerSubPrograms"):
            raise ValueError("Ray-tracing parameter references are unsupported")
        for stage in STAGES:
            program = shader_pass.get("m_Prog" + stage, {})
            if program.get("m_PlayerSubPrograms") or program.get("m_ParameterBlobIndices"):
                contexts.extend(_stage(blob, entries, program, stage, names,
                                       parsed["m_KeywordNames"], cache))
        result.append(contexts)
    return result
