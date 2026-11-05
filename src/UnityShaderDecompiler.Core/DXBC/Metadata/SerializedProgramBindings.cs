using System.Text.Json;

namespace UnityShaderDecompiler.Core.DXBC.Metadata;

// Resolves resource slots from one serialized program reference.
public sealed class SerializedProgramBindings
{
    public Dictionary<uint, string> Textures { get; } = new();
    public Dictionary<uint, string> Samplers { get; } = new();
    public List<CbufferMetadata> ConstantBuffers { get; } = new();

    // Combines the common and binary records, rejecting conflicting slots.
    public static SerializedProgramBindings Parse(JsonElement record)
    {
        SerializedProgramBindings result = new();
        JsonElement common = record.GetProperty("commonParameters");
        JsonElement binary = record.GetProperty("binaryParameters");
        Dictionary<uint, string> bufferNames = new();
        foreach (JsonElement item in common.GetProperty("m_ConstantBufferBindings").EnumerateArray())
            AddName(bufferNames, item.GetProperty("m_Index").GetUInt32(), item.GetProperty("resolvedName").GetString()!, "buffer");
        foreach (JsonElement item in common.GetProperty("m_TextureParams").EnumerateArray())
            result.AddTexture(item.GetProperty("m_Index").GetUInt32(), item.GetProperty("resolvedName").GetString()!, item.GetProperty("m_SamplerIndex").GetInt32());
        if (common.GetProperty("m_Samplers").GetArrayLength() != 0 || common.GetProperty("m_UAVParams").GetArrayLength() != 0
            || common.GetProperty("m_BufferParams").GetArrayLength() != 0)
            throw new InvalidDataException("Unsupported serialized sampler, UAV, or buffer parameters.");
        foreach (JsonElement item in binary.GetProperty("resources").EnumerateArray())
        {
            uint slot = item.GetProperty("slot").GetUInt32();
            string name = item.GetProperty("name").GetString()!;
            switch (item.GetProperty("kind").GetInt32())
            {
                case 0: result.AddTexture(slot, name, item.GetProperty("samplerIndex").GetInt32()); break;
                case 1: AddName(bufferNames, slot, name, "buffer"); break;
                default: throw new InvalidDataException($"Unsupported serialized resource kind at slot {slot}.");
            }
        }
        foreach (KeyValuePair<uint, string> binding in bufferNames)
            result.ConstantBuffers.Add(BuildBuffer(binding.Key, binding.Value, record.GetProperty("stage").GetString()!, common, binary));
        return result;
    }

    // Resolves a texture and its explicitly paired sampler slot.
    private void AddTexture(uint slot, string name, int samplerIndex)
    {
        AddName(Textures, slot, name, "texture");
        if (samplerIndex >= 0)
            AddName(Samplers, (uint)samplerIndex, "sampler" + name, "sampler");
    }

    // Refuses two different names for the same binding slot.
    private static void AddName(Dictionary<uint, string> names, uint slot, string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name) || names.TryGetValue(slot, out string? existing) && existing != name)
            throw new InvalidDataException($"Conflicting serialized {kind} binding at slot {slot}.");
        names[slot] = name;
    }

    // Joins a named binding to its layout in the same program record.
    private static CbufferMetadata BuildBuffer(uint slot, string name, string stage, JsonElement common, JsonElement binary)
    {
        JsonElement? commonBuffer = FindNamed(common.GetProperty("m_ConstantBuffers"), "resolvedName", name);
        JsonElement? binaryBuffer = FindNamed(binary.GetProperty("constantBuffers"), "name", name);
        if (commonBuffer is null && binaryBuffer is null)
            throw new InvalidDataException($"Missing serialized layout for buffer {name} at b{slot}.");
        int size = commonBuffer?.GetProperty("m_Size").GetInt32() ?? binaryBuffer!.Value.GetProperty("size").GetInt32();
        if (binaryBuffer is JsonElement binaryLayout && binaryLayout.GetProperty("size").GetInt32() != size)
            throw new InvalidDataException($"Conflicting serialized size for buffer {name}.");
        CbufferMetadata result = new() { Slot = checked((int)slot), Name = name, Stage = stage, Size = size };
        if (binaryBuffer is JsonElement binaryValues)
        {
            if (binaryValues.GetProperty("structures").GetArrayLength() != 0)
                throw new InvalidDataException($"Unsupported structure layout in buffer {name}.");
            foreach (JsonElement variable in binaryValues.GetProperty("variables").EnumerateArray())
                AddVariable(result, BinaryVariable(variable));
        }
        if (commonBuffer is JsonElement commonValues)
        {
            if (commonValues.GetProperty("m_StructParams").GetArrayLength() != 0)
                throw new InvalidDataException($"Unsupported structure layout in buffer {name}.");
            foreach (JsonElement variable in commonValues.GetProperty("m_VectorParams").EnumerateArray())
                AddVariable(result, CommonVariable(variable, false));
            foreach (JsonElement variable in commonValues.GetProperty("m_MatrixParams").EnumerateArray())
                AddVariable(result, CommonVariable(variable, true));
        }
        return result;
    }

    // Looks up a buffer by its serialized name, rejecting duplicate names.
    private static JsonElement? FindNamed(JsonElement array, string field, string name)
    {
        JsonElement? found = null;
        foreach (JsonElement item in array.EnumerateArray())
            if (item.GetProperty(field).GetString() == name)
            {
                if (found is not null) throw new InvalidDataException($"Duplicate serialized buffer {name}.");
                found = item;
            }
        return found;
    }

    // Converts a binary variable without changing its type or offset.
    private static CbufferVariableMetadata BinaryVariable(JsonElement item)
    {
        int kind = item.GetProperty("type").GetInt32();
        if (kind is not (0 or 1)) throw new InvalidDataException($"Unsupported serialized variable type {kind}.");
        int rows = item.GetProperty("rows").GetInt32();
        int columns = item.GetProperty("columns").GetInt32();
        bool matrix = item.GetProperty("isMatrix").GetBoolean();
        if (matrix && columns != 4 || !matrix && rows != 1)
            throw new InvalidDataException("Unsupported serialized matrix or vector shape.");
        return new CbufferVariableMetadata { Name = item.GetProperty("name").GetString()!, Offset = item.GetProperty("offset").GetInt32(),
            Dim = columns, RowCount = matrix ? rows : 0, IsMatrix = matrix, ArraySize = item.GetProperty("arraySize").GetInt32(),
            BaseType = kind == 0 ? "float" : "int" };
    }

    // Converts a Unity common variable with its verified scalar type.
    private static CbufferVariableMetadata CommonVariable(JsonElement item, bool matrix)
    {
        int kind = item.GetProperty("m_Type").GetInt32();
        if (kind != 0) throw new InvalidDataException($"Unsupported common variable type {kind}.");
        return new CbufferVariableMetadata { Name = item.GetProperty("resolvedName").GetString()!,
            Offset = item.GetProperty("m_OffsetInConstantBuffer").GetInt32(),
            Dim = matrix ? 4 : item.GetProperty("m_Dim").GetInt32(),
            RowCount = matrix ? item.GetProperty("m_RowCount").GetInt32() : 0,
            IsMatrix = matrix, ArraySize = item.GetProperty("m_ArraySize").GetInt32() };
    }

    // Merges only identical declarations at the same byte offset.
    private static void AddVariable(CbufferMetadata buffer, CbufferVariableMetadata variable)
    {
        CbufferVariableMetadata? existing = buffer.Variables.FirstOrDefault(item => item.Offset == variable.Offset);
        if (existing is not null)
        {
            if (existing.Name != variable.Name || existing.TypeName != variable.TypeName || existing.ArraySize != variable.ArraySize)
                throw new InvalidDataException($"Conflicting serialized member at {buffer.Name}+{variable.Offset}.");
            return;
        }
        if (variable.Offset < 0 || variable.Offset + variable.SizeBytes > buffer.Size)
            throw new InvalidDataException($"Serialized member exceeds buffer {buffer.Name}.");
        buffer.Variables.Add(variable);
    }
}
