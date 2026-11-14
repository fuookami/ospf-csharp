#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>取整函数契约测试 / Rounding-family contract tests.</summary>
public class RoundingFunctionTest {
    [Fact]
    public void Evaluate_ShouldMatchRoundingSemantics() {
        var x = new RealVar("rounding-evaluate-x");
        LinearPolynomial<Flt64> polynomial = Polynomial(x);
        var floor = new FloorFunction<Flt64>(
            polynomial: polynomial,
            converter: IIntoValue<Flt64>.Identity,
            name: "evaluate-floor"
        );
        var ceiling = new CeilingFunction<Flt64>(
            polynomial: polynomial,
            converter: IIntoValue<Flt64>.Identity,
            name: "evaluate-ceil"
        );
        var rounding = new RoundingFunction<Flt64>(
            polynomial: polynomial,
            converter: IIntoValue<Flt64>.Identity,
            name: "evaluate-round"
        );
        IReadOnlyDictionary<ISymbol, Flt64> positive = Values(x, 1.2);
        IReadOnlyDictionary<ISymbol, Flt64> negative = Values(x, -1.2);
        IReadOnlyDictionary<ISymbol, Flt64> roundUp = Values(x, 1.6);
        IReadOnlyDictionary<ISymbol, Flt64> positiveMidpoint = Values(x, 2.5);
        IReadOnlyDictionary<ISymbol, Flt64> negativeMidpoint = Values(x, -2.5);

        floor.Evaluate(positive).Should().Be(new Flt64(1.0));
        floor.Evaluate(negative).Should().Be(new Flt64(-2.0));
        ceiling.Evaluate(positive).Should().Be(new Flt64(2.0));
        ceiling.Evaluate(negative).Should().Be(new Flt64(-1.0));
        rounding.Evaluate(positive).Should().Be(new Flt64(1.0));
        rounding.Evaluate(roundUp).Should().Be(new Flt64(2.0));
        rounding.Evaluate(positiveMidpoint).Should().Be(new Flt64(2.0));
        rounding.Evaluate(negativeMidpoint).Should().Be(new Flt64(-2.0));
        floor.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().BeNull();
    }

    [Fact]
    public async Task FloorAndCeiling_ShouldRegisterThreeExactRows() {
        var x = new RealVar("integer-rounding-x");
        var floor = new FloorFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            name: "rows-floor"
        );
        var ceiling = new CeilingFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            name: "rows-ceil"
        );

        LinearMechanismModel<Flt64> floorModel = await BuildModel(floor, x);
        var floorMechanism = (IMechanismModel<Flt64>)floorModel;
        floorMechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "rows-floor_floor_lb",
            "rows-floor_floor_ub",
            "rows-floor_floor_result"
        );
        IsFeasible(
            floorModel,
            new Dictionary<IVariableItem, Flt64> {
                [x] = new(1.2),
                [floor.KVar] = new(1.0),
                [floor.ResultVar] = new(1.0),
            }
        ).Should().BeTrue();
        IsFeasible(
            floorModel,
            new Dictionary<IVariableItem, Flt64> {
                [x] = new(1.2),
                [floor.KVar] = new(2.0),
                [floor.ResultVar] = new(2.0),
            }
        ).Should().BeFalse();

        LinearMechanismModel<Flt64> ceilingModel = await BuildModel(ceiling, x);
        var ceilingMechanism = (IMechanismModel<Flt64>)ceilingModel;
        ceilingMechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "rows-ceil_ceil_ub",
            "rows-ceil_ceil_lb",
            "rows-ceil_ceil_result"
        );
        IsFeasible(
            ceilingModel,
            new Dictionary<IVariableItem, Flt64> {
                [x] = new(1.2),
                [ceiling.KVar] = new(2.0),
                [ceiling.ResultVar] = new(2.0),
            }
        ).Should().BeTrue();
    }

    [Fact]
    public async Task Rounding_ShouldRegisterSevenExactRows() {
        var x = new RealVar("rounding-rows-x");
        var function = new RoundingFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            name: "rows-round"
        );
        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        var mechanism = (IMechanismModel<Flt64>)model;

        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "rows-round_round_k_lb",
            "rows-round_round_k_ub",
            "rows-round_round_decompose",
            "rows-round_round_b_ub",
            "rows-round_round_r_lb",
            "rows-round_round_r_ub",
            "rows-round_round_result"
        );
        IsFeasible(
            model,
            new Dictionary<IVariableItem, Flt64> {
                [x] = new(1.4),
                [function.KVar] = new(1.0),
                [function.FractionVar] = new(0.4),
                [function.RoundUpVar] = Flt64.Zero,
                [function.ResultVar] = new(1.0),
            }
        ).Should().BeTrue();
        IsFeasible(
            model,
            new Dictionary<IVariableItem, Flt64> {
                [x] = new(1.6),
                [function.KVar] = new(1.0),
                [function.FractionVar] = new(0.6),
                [function.RoundUpVar] = Flt64.One,
                [function.ResultVar] = new(2.0),
            }
        ).Should().BeTrue();
        IsFeasible(
            model,
            new Dictionary<IVariableItem, Flt64> {
                [x] = new(1.6),
                [function.KVar] = new(1.0),
                [function.FractionVar] = new(0.6),
                [function.RoundUpVar] = Flt64.One,
                [function.ResultVar] = new(1.0),
            }
        ).Should().BeFalse();
    }

    [Fact]
    public async Task RoundingMidpoint_ShouldRetainBaselineEvaluationModelMismatch() {
        var x = new RealVar("rounding-midpoint-x");
        var function = new RoundingFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity
        );
        using LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new(2.5),
            [function.KVar] = new(2),
            [function.FractionVar] = new(0.5),
            [function.RoundUpVar] = Flt64.One,
            [function.ResultVar] = new(3),
        };

        function.Evaluate(Values(x, 2.5)).Should().Be(new Flt64(2));
        IsFeasible(model, values).Should().BeTrue();
        values[function.ResultVar] = new Flt64(2);
        IsFeasible(model, values).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterConstraints_WhenInputIsNotFinite_ShouldFail() {
        var x = new RealVar("rounding-nonfinite-x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(
                    new Flt64(double.PositiveInfinity),
                    x
                ),
            },
            Flt64.Zero
        );

        foreach (IMathFunctionSymbol<Flt64> function in new IMathFunctionSymbol<Flt64>[] {
            new FloorFunction<Flt64>(
                polynomial: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                name: "nonfinite-floor"
            ),
            new CeilingFunction<Flt64>(
                polynomial: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                name: "nonfinite-ceil"
            ),
            new RoundingFunction<Flt64>(
                polynomial: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                name: "nonfinite-round"
            ),
        }) {
            using var metaModel = new LinearMetaModel<Flt64>(function.Name);
            metaModel.Add(x).IsOk.Should().BeTrue();
            metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

            result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
            ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
                .Should().Be(ErrorCode.IllegalArgument);
        }
    }

    [Fact]
    public async Task Rounding_WhenExplicitBigMIsNotFinite_ShouldFail() {
        var x = new RealVar("rounding-nonfinite-m-x");
        var function = new RoundingFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(double.PositiveInfinity),
            name: "nonfinite-round-m"
        );
        using var metaModel = new LinearMetaModel<Flt64>(function.Name);
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    private static async Task<LinearMechanismModel<Flt64>> BuildModel(
        IMathFunctionSymbol<Flt64> function,
        params IVariableItem[] variables) {
        using var metaModel = new LinearMetaModel<Flt64>(function.Name);
        foreach (IVariableItem variable in variables) {
            metaModel.Add(variable).IsOk.Should().BeTrue();
        }
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);
        result.IsOk.Should().BeTrue();
        return ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
    }

    private static bool IsFeasible(
        LinearMechanismModel<Flt64> model,
        IReadOnlyDictionary<IVariableItem, Flt64> values) {
        var solution = model.Tokens.TokensInSolver
            .Select(token => values[token.Variable])
            .ToList();
        return ((IMechanismModel<Flt64>)model).Constraints
            .All(constraint => constraint.IsTrue(solution) == true);
    }

    private static LinearPolynomial<Flt64> Polynomial(RealVar variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        RealVar variable,
        double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new(value) };
}
