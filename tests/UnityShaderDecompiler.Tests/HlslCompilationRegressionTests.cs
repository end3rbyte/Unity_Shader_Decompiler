using System;
using System.Linq;
using Parser.Core.Hlsl;
using Parser.Core.Hlsl.Ast;
using Parser.Core.Analysis;
using Parser.Core.Optimizations;
using Parser.DXBC.Instructions;
using Parser.DXBC.IR;
using Xunit;

namespace UnityShaderDecompiler.Tests;

public sealed class HlslCompilationRegressionTests
{
    [Fact]
    public void IntegerSourceModifierIsAppliedAfterBitInterpretation()
    {
        ShdrParser parser = new ShdrParser();
        parser.Instructions.Add(new Instruction
        {
            Opcode = Opcode.IMax,
            Operands =
            {
                new Operand { RegisterType = RegisterType.Temp, RegisterIndex = 1, Mask = 1 },
                new Operand { RegisterType = RegisterType.Temp, RegisterIndex = 0, Mask = 1,
                    Modifier = ShdrParser.OperandModifier.Neg },
                new Operand { RegisterType = RegisterType.Immediate32, Immediate32Values = new uint[] { 0 } },
            },
        });
        IRProgram program = new IRBuilder(preserveRegisterBits: true).Build(parser);
        IRStatement.IRAssignment assignment = Assert.IsType<IRStatement.IRAssignment>(Assert.Single(program.Statements));
        IRExpression.IntrinsicExpression maximum = Assert.IsType<IRExpression.IntrinsicExpression>(assignment.Expression);
        IRExpression.UnaryExpression negation = Assert.IsType<IRExpression.UnaryExpression>(maximum.Arguments[0]);
        Assert.Equal(IRExpression.UnaryExpression.UnaryOperation.Negate, negation.Operation);
        Assert.Equal(IRValueType.Int, negation.Type);
        IRExpression.IntrinsicExpression cast = Assert.IsType<IRExpression.IntrinsicExpression>(negation.Operand);
        Assert.Equal(IRExpression.IRIntrinsic.AsInt, cast.Intrinsic);
        IRExpression.RegisterExpression register = Assert.IsType<IRExpression.RegisterExpression>(Assert.Single(cast.Arguments));
        Assert.Equal(ShdrParser.OperandModifier.None, register.Register.Modifier);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public void NumericConstantsUseInvariantDecimalSeparators(string culture)
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            Assert.Equal("0.125", Float(0.125f).ToString());
            IRExpression.ConstantExpression vector = new IRExpression.ConstantExpression
            {
                Kind = IRExpression.ConstantExpression.ConstantKind.Double,
                DoubleValues = new[] { 0.125, 1.25 },
            };
            Assert.Equal("double2(0.125, 1.25)", vector.ToString());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    [Trait("Category", "D3D")]
    public void FractionalPartRetainsPrecedingAdditionWhenRecompiled()
    {
        if (!OperatingSystem.IsWindows()) return;

        Parser.DXBC.Chunks.SignatureElement input = new Parser.DXBC.Chunks.SignatureElement
        {
            SemanticName = "TEXCOORD", Mask = 1, ComponentType = 3,
        };
        Parser.DXBC.Chunks.SignatureElement output = new Parser.DXBC.Chunks.SignatureElement
        {
            SemanticName = "SV_Target", Mask = 1, ComponentType = 3,
        };
        HlslFunctionNode function = new HlslFunctionNode
        {
            Name = "frag", Stage = HlslShaderStage.Fragment, RawTemporaryStorage = true,
            InputStruct = new HlslStructNode { Name = "TestInput" },
            OutputStruct = new HlslStructNode { Name = "TestOutput" },
        };
        function.InputStruct.Fields.Add(new HlslFieldNode
        {
            Name = HlslAstBuilder.ToFieldName("TEXCOORD0"), Semantic = "TEXCOORD0", TypeHint = "float",
        });
        function.OutputStruct.Fields.Add(new HlslFieldNode
        {
            Name = HlslAstBuilder.ToFieldName("SV_Target0"), Semantic = "SV_Target0", TypeHint = "float",
        });
        function.InputElementsByRegister[0] = new() { input };
        function.OutputElementsByRegister[0] = new() { output };
        IRRegister source = Temp(0);
        source.RegisterType = RegisterType.Input;
        source.SymbolicName = "TEXCOORD0";
        IRRegister destination = Temp(0);
        destination.RegisterType = RegisterType.Output;
        destination.SymbolicName = "SV_Target0";
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(1),
            Expression = new IRExpression.BinaryExpression
            {
                Operation = IRExpression.BinaryOperation.Add,
                Left = new IRExpression.RegisterExpression { Register = source }, Right = Float(1),
            },
        });
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = destination,
            Expression = new IRExpression.IntrinsicExpression
            {
                Intrinsic = IRExpression.IRIntrinsic.FractionalPart,
                Arguments = { new IRExpression.RegisterExpression { Register = Temp(1) } },
            },
        });
        function.Statements.Statements.Add(new HlslReturnStatement());
        HlslShaderNode shader = new HlslShaderNode { Name = "Test/Fraction" };
        HlslSubShaderNode subShader = new HlslSubShaderNode();
        HlslPassNode pass = new HlslPassNode { FragmentFunction = function };
        pass.Structs.Add(function.InputStruct);
        pass.Structs.Add(function.OutputStruct);
        subShader.Passes.Add(pass);
        shader.SubShaders.Add(subShader);
        string text = HlslPrettyPrinter.Print(shader, fuseTemps: false);
        string hlsl = System.Text.RegularExpressions.Regex.Match(text,
            @"HLSLPROGRAM(.*?)ENDHLSL", System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        Parser.DXBC.DxbcFile dxbc = new Parser.DXBC.DxbcFile();
        dxbc.Load(Parser.RecompileVerify.CompileHlsl(hlsl, "frag", "ps_4_0"));

        // frac(x + 1) loses the add under fxc optimization; round_ni must keep it.
        System.Collections.Generic.List<Instruction> instructions = dxbc.Shader!.Instructions;
        int floor = instructions.FindIndex(i => i.Opcode == Opcode.RoundNI);
        Assert.True(floor > 0);
        Assert.Contains(instructions.Take(floor), i => i.Opcode == Opcode.Add);
        Assert.Contains(instructions.Skip(floor + 1), i => i.Opcode == Opcode.Add);
    }

    [Fact]
    public void PhiCopyAfterNestedConditionalRunsAfterBothBranches()
    {
        IRBlock left = new IRBlock { Id = 0 };
        IRBlock right = new IRBlock { Id = 1 };
        IRBlock join = new IRBlock { Id = 2 };
        join.Statements.Add(new IRStatement.IREndIf());
        join.Predecessors.AddRange(new[] { left, right });
        IRBlock next = new IRBlock { Id = 3 };
        next.Predecessors.Add(join);
        IRStatement.IRPhi phi = new IRStatement.IRPhi { Destination = Temp(2) };
        phi.Operands.Add(Temp(1));
        next.Statements.Add(phi);

        IRLeaveSsa.Run(new() { left, right, join, next });

        Assert.Empty(left.Statements);
        Assert.Empty(right.Statements);
        Assert.IsType<IRStatement.IREndIf>(join.Statements[0]);
        Assert.IsType<IRStatement.IRAssignment>(join.Statements[1]);
    }

    [Fact]
    public void ExplicitZeroLodIsNotReplacedByImplicitDerivatives()
    {
        IRExpression.TextureOperationExpression sample = new IRExpression.TextureOperationExpression
        {
            Operation = IRExpression.TextureOperation.SampleLevel,
            LOD = Float(0), Coordinates = Float(1),
            Resource = new IRRegister { RegisterType = RegisterType.Resource, Index = 0 },
            Sampler = new IRRegister { RegisterType = RegisterType.Sampler, Index = 0 },
        };
        IRBlock block = new IRBlock();
        block.Statements.Add(new IRStatement.IRAssignment { Destination = Temp(0), Expression = sample });

        IRShaderPatternRecognition.Run(new() { block });

        IRStatement.IRAssignment assignment = Assert.IsType<IRStatement.IRAssignment>(block.Statements[0]);
        IRExpression.TextureOperationExpression actual = Assert.IsType<IRExpression.TextureOperationExpression>(assignment.Expression);
        Assert.Equal(IRExpression.TextureOperation.SampleLevel, actual.Operation);
        Assert.Same(sample.LOD, actual.LOD);
    }

    [Fact]
    public void RawTemporaryStorageKeepsComparisonMasksAndIntegerArithmetic()
    {
        HlslFunctionNode function = new HlslFunctionNode
        {
            Name = "frag", Stage = HlslShaderStage.Fragment, RawTemporaryStorage = true,
        };
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(0),
            Expression = new IRExpression.BinaryExpression
            {
                Operation = IRExpression.BinaryOperation.LessThan,
                Left = Float(1), Right = Float(2),
            },
        });
        IRExpression.IntrinsicExpression signedRead = new IRExpression.IntrinsicExpression
        {
            Intrinsic = IRExpression.IRIntrinsic.AsInt,
            Arguments = { new IRExpression.RegisterExpression { Register = Temp(0) } },
        };
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(1),
            Expression = new IRExpression.BinaryExpression
            {
                Operation = IRExpression.BinaryOperation.Add,
                Left = signedRead,
                Right = new IRExpression.ConstantExpression
                {
                    Kind = IRExpression.ConstantExpression.ConstantKind.Int, RawValues = new uint[] { 2 },
                },
            },
        });
        string source = Print(function);
        Assert.Contains("uint r0_x_1", source);
        Assert.Contains("? 0xffffffffu : 0u", source);
        Assert.Contains("asint(asfloat(r0_x_1))", source);
        Assert.Contains("uint r1_x_1 = asuint(", source);
    }

    [Fact]
    public void ValueWrittenInOnlyOneBranchIsDeclaredOutsideTheIf()
    {
        // A value written in one branch and read after the if must be
        // declared outside the branch to avoid HLSL X3004.
        IRRegister value = Temp(0);
        IRRegister merged = Temp(1);
        HlslIfStatement branch = new HlslIfStatement
        {
            Condition = Float(1),
            Then = new HlslBlockStatement(),
            Else = new HlslBlockStatement(),
        };
        branch.Then.Statements.Add(new HlslAssignmentStatement
        {
            Destination = merged,
            Expression = Float(2),
        });
        branch.Else.Statements.Add(new HlslAssignmentStatement
        {
            Destination = value,
            Expression = Float(3),
        });
        branch.Else.Statements.Add(new HlslAssignmentStatement
        {
            Destination = merged,
            Expression = new IRExpression.RegisterExpression { Register = value },
        });

        HlslFunctionNode function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
        function.Statements.Statements.Add(branch);
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(2),
            Expression = new IRExpression.RegisterExpression { Register = value },
        });

        string source = Print(function);
        int declaration = source.IndexOf("float r0_x_1;", StringComparison.Ordinal);
        int ifStart = source.IndexOf("if (", StringComparison.Ordinal);
        Assert.True(declaration >= 0 && declaration < ifStart, source);
        Assert.Contains("r0_x_1 = 3", source);
        Assert.DoesNotContain("float r0_x_1 = 3", source);
    }

    [Fact]
    public void StructuredBufferDeclarationUsesItsActualStride()
    {
        HlslShaderNode shader = new HlslShaderNode { Name = "Test/Structured" };
        HlslSubShaderNode subShader = new HlslSubShaderNode();
        HlslPassNode pass = new HlslPassNode();
        pass.Resources.Add(new HlslResourceNode
        {
            Name = "t7", Kind = HlslResourceKind.Texture, Slot = 7,
            TypeHint = "StructuredBuffer", StructureStride = 32,
        });
        subShader.Passes.Add(pass);
        shader.SubShaders.Add(subShader);

        string source = HlslPrettyPrinter.Print(shader, fuseTemps: false);
        Assert.Contains("uint value[8];", source);
        Assert.Contains("StructuredBuffer<t7_element> t7", source);
    }

    [Fact]
    public void PartialCbufferMetadataKeepsRawRegisterOffsets()
    {
        HlslShaderNode shader = new HlslShaderNode { Name = "Test/CbufferLayout" };
        HlslSubShaderNode subShader = new HlslSubShaderNode();
        HlslPassNode pass = new HlslPassNode();
        HlslResourceNode resource = new HlslResourceNode
        {
            Name = "NamedBuffer", Kind = HlslResourceKind.ConstantBuffer, Slot = 1,
        };
        resource.Variables.Add(new HlslCBufferVariable
        {
            Name = "NamedValue", TypeName = "float4", Offset = 0, Size = 16,
        });
        resource.Variables.Add(new HlslCBufferVariable
        {
            Name = "cb1_values", TypeName = "float4", Offset = 0, Size = 32, ArraySize = 2,
        });
        pass.Resources.Add(resource);
        IRRegister read = new IRRegister
        {
            RegisterType = RegisterType.ConstantBuffer,
            Type = IRValueType.Float,
            Mask = 15,
            ComponentMode = Operand.OperandComponentMode.Mask,
        };
        read.Indices.Add(1);
        read.Indices.Add(0);
        HlslFunctionNode function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(0, 15),
            Expression = new IRExpression.RegisterExpression { Register = read },
        });
        pass.FragmentFunction = function;
        subShader.Passes.Add(pass);
        shader.SubShaders.Add(subShader);

        string source = HlslPrettyPrinter.Print(shader, fuseTemps: false);
        Assert.Contains("cbuffer NamedBuffer : register(b1) { float4 cb1_values[2]; };", source);
        Assert.Contains("cb1_values[0]", source);
        Assert.DoesNotContain("NamedValue", source);
    }

    [Fact]
    public void NormalizeSimplificationKeepsTheDotProductLanes()
    {
        string body = "float lengthSquared = dot(cb1_values[5].xyz, cb1_values[5].xyz);\n"
            + "float inverseLength = rsqrt(lengthSquared);\n"
            + "float3 result = ((inverseLength.xxxx * cb1_values[5].xyzx)).xyz;\n";

        string simplified = HlslMathSimplify.Simplify(body);
        Assert.Matches(@"normalize\(cb1_values\s*\[5\]\.xyz\)", simplified);
        Assert.DoesNotMatch(@"normalize\(cb1_values\s*\[5\]\)", simplified);
    }

    [Fact]
    public void ThreeComponentDotDoesNotReadTheFourthLane()
    {
        HlslFunctionNode function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(2),
            Expression = new IRExpression.DotProductExpression
            {
                Components = 3,
                Left = new IRExpression.RegisterExpression { Register = Temp(0, 15) },
                Right = new IRExpression.RegisterExpression { Register = Temp(1, 15) },
            },
        });

        string source = Print(function);
        Assert.Matches(@"dot\(\(.+\)\.xyz, \(.+\)\.xyz\)", source);
    }

    [Fact]
    public void SemanticNamesDoNotMergeDifferentTemporaryRegisters()
    {
        IRRegister first = Temp(1);
        IRRegister second = Temp(4);
        first.SymbolicName = "InputValue";
        second.SymbolicName = "InputValue";
        HlslFunctionNode function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = first, Expression = Float(2),
        });
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = second, Expression = Float(3),
        });
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(5),
            Expression = new IRExpression.RegisterExpression { Register = first },
        });

        string source = Print(function);
        Assert.Contains("float InputValue_r1_x_1 = 2", source);
        Assert.Contains("float InputValue_r4_x_1 = 3", source);
        Assert.Contains("= InputValue_r1_x_1;", source);
    }


    [Fact]
    public void VectorCastPreservesWidthForComponentAccess()
    {
        // A scalar cast followed by vector component access causes HLSL
        // X3018 (invalid subscript).
        IRRegister sourceRegister = Temp(0, 15);
        IRExpression.IntrinsicExpression cast = new IRExpression.IntrinsicExpression
        {
            Intrinsic = IRExpression.IRIntrinsic.CastFloat,
        };
        cast.Arguments.Add(new IRExpression.RegisterExpression { Register = sourceRegister });
        HlslFunctionNode function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
        function.Statements.Statements.Add(new HlslAssignmentStatement
        {
            Destination = Temp(1, 15),
            Expression = cast,
        });

        string source = Print(function);
        Assert.Contains("(float4)", source);
        Assert.DoesNotContain("(float)r0_xyzw_1", source);
    }

    private static string Print(HlslFunctionNode function)
    {
        HlslShaderNode shader = new HlslShaderNode { Name = "Test/Scope" };
        HlslSubShaderNode subShader = new HlslSubShaderNode();
        subShader.Passes.Add(new HlslPassNode { FragmentFunction = function });
        shader.SubShaders.Add(subShader);
        return HlslPrettyPrinter.Print(shader, fuseTemps: false);
    }

    private static IRRegister Temp(uint index, byte mask = 1)
    {
        IRRegister register = new IRRegister
        {
            RegisterType = RegisterType.Temp,
            Index = index,
            Type = IRValueType.Float,
            Mask = mask,
            ComponentMode = Operand.OperandComponentMode.Mask,
        };
        for (int component = 0; component < 4; component++)
            if ((mask & (1 << component)) != 0)
                register.SsaVersion[component] = 1;
        return register;
    }

    private static IRExpression.ConstantExpression Float(float value) => new()
    {
        RawValues = new[] { unchecked((uint)BitConverter.SingleToInt32Bits(value)) },
    };
}
