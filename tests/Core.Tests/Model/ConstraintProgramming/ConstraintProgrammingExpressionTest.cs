#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class ConstraintProgrammingExpressionTest {
    [Fact]
    public void LinearExpressionShouldMergeStableIdsRemoveZerosAndEvaluateExactly() {
        var variable = new IntVar("amount");
        var other = new IntVar("other");
        var stableId = new VariableId("variable:amount");
        ConstraintProgrammingExpression.Linear expression = ConstraintProgrammingExpression.CreateLinear(
            terms: new[] {
                new ConstraintProgrammingExpression.Term(
                    variable: variable,
                    coefficient: new Int64(2),
                    id: stableId),
                new ConstraintProgrammingExpression.Term(
                    variable: variable,
                    coefficient: new Int64(3),
                    id: stableId),
                new ConstraintProgrammingExpression.Term(
                    variable: other,
                    coefficient: new Int64(4)),
                new ConstraintProgrammingExpression.Term(
                    variable: other,
                    coefficient: new Int64(-4)),
            },
            constant: new Int64(1)).Value;

        expression.Terms.Should().ContainSingle();
        expression.Terms[0].VariableId.Should().Be(stableId);
        expression.Terms[0].Coefficient.Should().Be(new Int64(5));
        expression.Variables.Should().Equal(stableId);
        expression.Evaluate(
            new Dictionary<VariableId, Int64> { [stableId] = new Int64(3) })
            .Value.Should().Be(new Int64(16));
    }

    [Fact]
    public void ExpressionConstructionAndEvaluationShouldRejectExactOverflow() {
        var variable = new IntVar("amount");
        VariableId id = Id(variable);
        Result<ConstraintProgrammingExpression.Linear, ErrorCode, Error<ErrorCode>> mergedOverflow =
            ConstraintProgrammingExpression.CreateLinear(
            terms: new[] {
                new ConstraintProgrammingExpression.Term(variable, new Int64(long.MaxValue)),
                new ConstraintProgrammingExpression.Term(variable, new Int64(1)),
            });
        var direct = new ConstraintProgrammingExpression.Linear(
            terms: new[] {
                new ConstraintProgrammingExpression.Term(variable, new Int64(long.MaxValue)),
            });
        Result<ConstraintProgrammingExpression.Linear, ErrorCode, Error<ErrorCode>> sumOverflow =
            ConstraintProgrammingExpression.Sum(
            new[] {
                ConstraintProgrammingExpression.CreateConstant(long.MaxValue),
                ConstraintProgrammingExpression.CreateConstant(1),
            });
        var negatedMinimum = ConstraintProgrammingExpression.Negate(
            ConstraintProgrammingExpression.CreateConstant(long.MinValue));

        mergedOverflow.IsFailed.Should().BeTrue();
        direct.Evaluate(
            new Dictionary<VariableId, Int64> { [id] = new Int64(2) })
            .IsFailed.Should().BeTrue();
        sumOverflow.IsFailed.Should().BeTrue();
        negatedMinimum.Should().BeOfType<ConstraintProgrammingExpression.Invalid>();
        negatedMinimum.Evaluate(new Dictionary<VariableId, Int64>()).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void VariableFactoryShouldEnforceIntegerTypeAndDomainSemantics() {
        var integer = new IntVar("integer");
        var unsigned = new UIntVar("unsigned");
        var binary = new BinVar("binary");
        ConstraintProgrammingExpression.Variable integerExpression =
            ConstraintProgrammingExpression.CreateVariable(integer).Value;
        ConstraintProgrammingExpression.Variable unsignedExpression =
            ConstraintProgrammingExpression.CreateVariable(unsigned).Value;
        ConstraintProgrammingExpression.Variable binaryExpression =
            ConstraintProgrammingExpression.CreateVariable(binary).Value;

        integerExpression.VariableId.Should().Be(Id(integer));
        integerExpression.Domain.LowerBound.Should().Be(new Int64(long.MinValue));
        unsignedExpression.Domain.LowerBound.Should().Be(Int64.Zero);
        binaryExpression.Domain.Should().Be(IntegerDomain.Boolean);
        ConstraintProgrammingExpression.CreateVariable(new RealVar("real")).IsFailed.Should().BeTrue();
        ConstraintProgrammingExpression.CreateVariable(
            variable: binary,
            domain: new IntegerDomain.Interval(new Int64(-1), new Int64(1))).IsFailed.Should().BeTrue();
        ConstraintProgrammingExpression.CreateVariable(
            variable: unsigned,
            domain: new IntegerDomain.Interval(new Int64(-1), new Int64(1))).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void EvaluationShouldReturnStructuredMissingAndDomainErrors() {
        var variable = new IntVar("value");
        ConstraintProgrammingExpression.Variable expression = ConstraintProgrammingExpression.CreateVariable(
            variable: variable,
            domain: IntegerDomain.CreateInterval(new Int64(0), new Int64(2)).Value).Value;

        Result<Int64, ErrorCode, Error<ErrorCode>> missing =
            expression.Evaluate(new Dictionary<VariableId, Int64>());
        Result<Int64, ErrorCode, Error<ErrorCode>> outside = expression.Evaluate(
            new Dictionary<VariableId, Int64> { [expression.VariableId] = new Int64(3) });

        missing.Should().BeOfType<Failed<Int64, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.DataNotFound);
        outside.Should().BeOfType<Failed<Int64, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    internal static VariableId Id(IVariableItem variable) =>
        new($"{variable.Identifier}:{variable.Index}");
}
