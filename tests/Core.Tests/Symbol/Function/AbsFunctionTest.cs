#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 绝对值函数契约测试 / Absolute-value function contract tests.
/// </summary>
public class AbsFunctionTest {
    [Fact]
    public void Constructor_ShouldTightenHelperBoundsAndInferBigM() {
        var x = new RealVar("abs-bounds-x");
        x.Range.Geq(new Flt64(-3.0));
        x.Range.Leq(new Flt64(7.0));
        LinearPolynomial<Flt64> polynomial = Polynomial(x);

        var function = new AbsFunction<Flt64>(
            converter: IIntoValue<Flt64>.Identity,
            polynomial: polynomial,
            name: "abs-bounds"
        );

        function.ResultVar.LowerBound!.Value.Unwrap().Should().Be(Flt64.Zero);
        function.ResultVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(7.0));
        function.PositiveVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(7.0));
        function.NegativeVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(3.0));
        function.PositiveBigM.Should().Be(new Flt64(7.0));
        function.NegativeBigM.Should().Be(new Flt64(3.0));
        function.HelperVariables.Should().HaveCount(4);
        function.HelperVariables.Select(variable => variable.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Evaluate_ShouldReturnAbsoluteValueAndPreserveMissingInputs() {
        var x = new RealVar("abs-evaluate-x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(2.0), x) },
            Flt64.One
        );
        var function = new AbsFunction<Flt64>(
            converter: IIntoValue<Flt64>.Identity,
            polynomial: polynomial,
            name: "abs-evaluate"
        );

        function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [x] = new Flt64(3.0),
        }).Should().Be(new Flt64(7.0));
        function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [x] = new Flt64(-4.0),
        }).Should().Be(new Flt64(7.0));
        function.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().BeNull();
        function.ResultPolynomial.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(function.ResultVar);
        function.ResultPolynomial.Monomials[0].Coefficient.Should().Be(Flt64.One);
    }

    [Fact]
    public async Task RegisterConstraints_ShouldBuildFourLinearizedRows() {
        var x = new RealVar("abs-rows-x");
        x.Range.Geq(new Flt64(-3.0));
        x.Range.Leq(new Flt64(7.0));
        var function = new AbsFunction<Flt64>(
            converter: IIntoValue<Flt64>.Identity,
            polynomial: Polynomial(x),
            name: "abs-rows"
        );
        using var metaModel = new LinearMetaModel<Flt64>("abs-rows");
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.IsOk.Should().BeTrue();
        LinearMechanismModel<Flt64> model =
            ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Should().HaveCount(4);
        mechanism.Constraints.Select(constraint => constraint.Name).Should().BeEquivalentTo(
            new[] {
                "abs-rows_abs_result",
                "abs-rows_abs_decompose",
                "abs-rows_abs_pos_ub",
                "abs-rows_abs_neg_ub",
            }
        );
        IsFeasible(
            model,
            function,
            x,
            xValue: new Flt64(7.0),
            result: new Flt64(7.0),
            positive: new Flt64(7.0),
            negative: Flt64.Zero,
            sign: Flt64.One
        ).Should().BeTrue();
        IsFeasible(
            model,
            function,
            x,
            xValue: new Flt64(-3.0),
            result: new Flt64(3.0),
            positive: Flt64.Zero,
            negative: new Flt64(3.0),
            sign: Flt64.Zero
        ).Should().BeTrue();
        IsFeasible(
            model,
            function,
            x,
            xValue: new Flt64(7.0),
            result: new Flt64(7.0),
            positive: Flt64.Zero,
            negative: Flt64.Zero,
            sign: Flt64.One
        ).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterConstraints_WhenExplicitBigMIsNegative_ShouldFail() {
        var x = new RealVar("abs-negative-big-m-x");
        var function = new AbsFunction<Flt64>(
            converter: IIntoValue<Flt64>.Identity,
            polynomial: Polynomial(x),
            bigM: new Flt64(-1.0),
            name: "abs-negative-big-m"
        );
        using var metaModel = new LinearMetaModel<Flt64>("abs-negative-big-m");
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public async Task RegisterConstraints_WhenCoefficientIsNotFinite_ShouldFail() {
        var x = new RealVar("abs-nonfinite-x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(
                    new Flt64(double.PositiveInfinity),
                    x
                ),
            },
            Flt64.Zero
        );
        var function = new AbsFunction<Flt64>(
            converter: IIntoValue<Flt64>.Identity,
            polynomial: polynomial,
            name: "abs-nonfinite"
        );
        using var metaModel = new LinearMetaModel<Flt64>("abs-nonfinite");
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    private static LinearPolynomial<Flt64> Polynomial(RealVar variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );

    private static bool IsFeasible(
        LinearMechanismModel<Flt64> model,
        AbsFunction<Flt64> function,
        RealVar variable,
        Flt64 xValue,
        Flt64 result,
        Flt64 positive,
        Flt64 negative,
        Flt64 sign) {
        var values = new Dictionary<IVariableItem, Flt64> {
            [variable] = xValue,
            [function.ResultVar] = result,
            [function.PositiveVar] = positive,
            [function.NegativeVar] = negative,
            [function.SignVar] = sign,
        };
        var solution = model.Tokens.TokensInSolver
            .Select(token => values[token.Variable])
            .ToList();
        return ((IMechanismModel<Flt64>)model).Constraints
            .All(constraint => constraint.IsTrue(solution) == true);
    }
}
