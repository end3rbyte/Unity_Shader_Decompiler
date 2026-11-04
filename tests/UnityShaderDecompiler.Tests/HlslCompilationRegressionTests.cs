using System;
using System.Linq;
using Parser.Core.Hlsl.Ast;
using Parser.DXBC.Instructions;
using Parser.DXBC.IR;
using Xunit;

namespace UnityShaderDecompiler.Tests;

public sealed class HlslCompilationRegressionTests
{
    [Fact]
    public void ValueWrittenInOnlyOneBranchIsDeclaredOutsideTheIf()
    {
        // A value written in one branch and read after the if must be
        // declared outside the branch to avoid HLSL X3004.
        IRRegister value = Temp(0);
        IRRegister merged = Temp(1);
        var branch = new HlslIfStatement
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

        var function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
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
        var shader = new HlslShaderNode { Name = "Test/Structured" };
        var subShader = new HlslSubShaderNode();
        var pass = new HlslPassNode();
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
    public void VectorCastPreservesWidthForComponentAccess()
    {
        // A scalar cast followed by vector component access causes HLSL
        // X3018 (invalid subscript).
        IRRegister sourceRegister = Temp(0, 15);
        var cast = new IRExpression.IntrinsicExpression
        {
            Intrinsic = IRExpression.IRIntrinsic.CastFloat,
        };
        cast.Arguments.Add(new IRExpression.RegisterExpression { Register = sourceRegister });
        var function = new HlslFunctionNode { Name = "frag", Stage = HlslShaderStage.Fragment };
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
        var shader = new HlslShaderNode { Name = "Test/Scope" };
        var subShader = new HlslSubShaderNode();
        subShader.Passes.Add(new HlslPassNode { FragmentFunction = function });
        shader.SubShaders.Add(subShader);
        return HlslPrettyPrinter.Print(shader, fuseTemps: false);
    }

    private static IRRegister Temp(uint index, byte mask = 1)
    {
        var register = new IRRegister
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
