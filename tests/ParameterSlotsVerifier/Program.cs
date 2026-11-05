using System.Text.Json;
using AssetStudio;
using UnityShaderDecompiler.Core.DXBC;
using UnityShaderDecompiler.Core.DXBC.Extraction;
using UnityShaderDecompiler.Core.DXBC.Instructions;

// Checks recovered resource slots against the shipped D3D11 declarations.
internal static class Program
{
    // Verifies one extracted shader folder and reports its context count.
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: ParameterSlotsVerifier <extracted-shader-folder>");
            return 2;
        }
        try
        {
            string folder = args[0];
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "metadata.json")));
            using MemoryStream stream = new MemoryStream(File.ReadAllBytes(Path.Combine(folder, "blob.bin")));
            using BinaryReader reader = new BinaryReader(stream);
            ShaderProgram program = new ShaderProgram(reader, new[] { 2022, 3, 62 });
            program.Read(reader, 0);
            int contexts = VerifyPasses(metadata.RootElement, program);
            Console.WriteLine($"Verified {contexts} D3D11 program contexts.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    // Traverses serialized passes without merging variant contexts.
    private static int VerifyPasses(JsonElement metadata, ShaderProgram program)
    {
        int count = 0;
        foreach (JsonElement subshader in metadata.GetProperty("subShaders").EnumerateArray())
        {
            foreach (JsonElement pass in subshader.GetProperty("passes").EnumerateArray())
            {
                foreach (JsonElement context in pass.GetProperty("programParameters").EnumerateArray())
                {
                    VerifyContext(context, program);
                    count++;
                }
            }
        }
        if (count == 0)
        {
            throw new InvalidDataException("No D3D11 program parameters found.");
        }
        return count;
    }

    // Compares one program's declarations with its serialized resource slots.
    private static void VerifyContext(JsonElement context, ShaderProgram program)
    {
        int index = context.GetProperty("blobIndex").GetInt32();
        ShaderSubProgram? subprogram = program.m_SubPrograms[index];
        if (subprogram is null)
        {
            throw new InvalidDataException($"Missing D3D11 program {index}.");
        }
        UnityShaderBlob blob = UnityShaderBlob.Parse(subprogram.m_ProgramCode);
        DxbcFile file = new DxbcFile();
        file.Load(blob.Dxbc);
        if (file.Shader is null)
        {
            throw new InvalidDataException($"Missing DXBC shader program {index}.");
        }
        HashSet<uint> textures = Slots(context, 0, "m_TextureParams");
        HashSet<uint> buffers = Slots(context, 1, "m_ConstantBufferBindings");
        HashSet<uint> samplers = Samplers(context);
        foreach (Instruction instruction in file.Shader.Instructions)
        {
            switch (instruction.Opcode)
            {
                case Opcode.DclResource:
                    Require(textures, instruction.Operands[0].RegisterIndex, index, "texture");
                    break;
                case Opcode.DclSampler:
                    Require(samplers, instruction.Operands[0].RegisterIndex, index, "sampler");
                    break;
                case Opcode.DclConstantBuffer:
                    Require(buffers, instruction.Operands[0].RegisterIndex, index, "constant buffer");
                    break;
                case Opcode.DclResourceRaw:
                case Opcode.DclResourceStructured:
                    throw new InvalidDataException($"Program {index} declares an unsupported buffer resource.");
            }
        }
    }

    // Collects slots from binary records and matching common parameters.
    private static HashSet<uint> Slots(JsonElement context, int kind, string commonField)
    {
        HashSet<uint> result = new HashSet<uint>();
        foreach (JsonElement resource in context.GetProperty("binaryParameters").GetProperty("resources").EnumerateArray())
        {
            if (resource.GetProperty("kind").GetInt32() == kind)
            {
                result.Add(resource.GetProperty("slot").GetUInt32());
            }
        }
        foreach (JsonElement resource in context.GetProperty("commonParameters").GetProperty(commonField).EnumerateArray())
        {
            result.Add(resource.GetProperty("m_Index").GetUInt32());
        }
        return result;
    }

    // Collects explicitly serialized texture-to-sampler associations.
    private static HashSet<uint> Samplers(JsonElement context)
    {
        HashSet<uint> result = new HashSet<uint>();
        foreach (JsonElement resource in context.GetProperty("binaryParameters").GetProperty("resources").EnumerateArray())
        {
            int kind = resource.GetProperty("kind").GetInt32();
            if (kind == 0)
            {
                int sampler = resource.GetProperty("samplerIndex").GetInt32();
                if (sampler >= 0)
                {
                    result.Add((uint)sampler);
                }
            }
            if (kind == 4)
            {
                result.Add(resource.GetProperty("slot").GetUInt32());
            }
        }
        foreach (JsonElement texture in context.GetProperty("commonParameters").GetProperty("m_TextureParams").EnumerateArray())
        {
            int index = texture.GetProperty("m_SamplerIndex").GetInt32();
            if (index >= 0)
            {
                result.Add((uint)index);
            }
            else if (index != -1)
            {
                throw new InvalidDataException($"Invalid serialized sampler index: {index}.");
            }
        }
        return result;
    }

    // Rejects a declaration with no serialized slot source.
    private static void Require(HashSet<uint> slots, uint slot, int index, string kind)
    {
        if (!slots.Contains(slot))
        {
            throw new InvalidDataException($"Program {index} declares {kind} slot {slot} without parameter metadata.");
        }
    }
}
