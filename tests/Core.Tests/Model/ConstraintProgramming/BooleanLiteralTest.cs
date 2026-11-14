#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class BooleanLiteralTest {
    [Fact]
    public void VariableLiteralNegatesAndUsesBoundStableIdentity() {
        var variable = new BinVar("x");
        var id = new VariableId("stable-x");
        var literal = new BooleanLiteral.VariableLiteral(variable, id: id);
        var values = new Dictionary<VariableId, bool> { [id] = true };

        literal.Positive.Should().BeTrue();
        literal.Evaluate(values).Value.Should().BeTrue();
        literal.Negate().Evaluate(values).Value.Should().BeFalse();
        literal.Negate().Negate().Should().Be(literal);
    }

    [Fact]
    public void MissingVariableAndConstantsHaveExplicitSemantics() {
        var variable = new BinVar("x");
        var literal = new BooleanLiteral.VariableLiteral(variable);

        BooleanLiteral.True.Evaluate(new Dictionary<VariableId, bool>()).Value.Should().BeTrue();
        BooleanLiteral.False.Negate().Evaluate(new Dictionary<VariableId, bool>()).Value.Should().BeTrue();
        literal.VariableId.Should().NotBeNull();
        literal.Evaluate(new Dictionary<VariableId, bool>()).Should()
            .BeOfType<Failed<bool, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.DataNotFound);
    }
}
