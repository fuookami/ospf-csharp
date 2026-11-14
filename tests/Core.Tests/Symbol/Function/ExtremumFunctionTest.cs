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

public class ExtremumFunctionTest {
    [Fact]
    public async Task Max_ShouldEvaluateAndRegisterExactRows() {
        var x = new RealVar("max-x");
        x.Range.Geq(new Flt64(-2.0));
        x.Range.Leq(new Flt64(5.0));
        var y = new RealVar("max-y");
        y.Range.Geq(Flt64.Zero);
        y.Range.Leq(new Flt64(4.0));
        var function = new MaxFunction<Flt64>(
            polynomials: new[] { Polynomial(x), Polynomial(y) },
            converter: IIntoValue<Flt64>.Identity,
            name: "contract-max"
        );

        function.ResultVar.LowerBound!.Value.Unwrap().Should().Be(Flt64.Zero);
        function.ResultVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(5.0));
        function.HelperVariables.Should().HaveCount(3);
        function.Evaluate(Values(x, 2.0, y, 4.0)).Should().Be(new Flt64(4.0));

        LinearMechanismModel<Flt64> model = await BuildModel(function, x, y);
        var mechanism = (IMechanismModel<Flt64>)model;

        mechanism.Constraints.Should().HaveCount(5);
        mechanism.Constraints.Select(constraint => constraint.Name).Should().BeEquivalentTo(
            new[] {
                "contract-max_max_lb_0",
                "contract-max_max_lb_1",
                "contract-max_max_ub_0",
                "contract-max_max_ub_1",
                "contract-max_max_select_one",
            }
        );
        IsFeasible(model, function, x, y, 2.0, 4.0, 4.0, 0.0, 1.0).Should().BeTrue();
        IsFeasible(model, function, x, y, 2.0, 4.0, 5.0, 0.0, 1.0).Should().BeFalse();
    }

    [Fact]
    public async Task Min_ShouldEvaluateAndRegisterExactRows() {
        var x = new RealVar("min-x");
        x.Range.Geq(new Flt64(-2.0));
        x.Range.Leq(new Flt64(5.0));
        var y = new RealVar("min-y");
        y.Range.Geq(Flt64.Zero);
        y.Range.Leq(new Flt64(4.0));
        var function = new MinFunction<Flt64>(
            polynomials: new[] { Polynomial(x), Polynomial(y) },
            converter: IIntoValue<Flt64>.Identity,
            name: "contract-min"
        );

        function.ResultVar.LowerBound!.Value.Unwrap().Should().Be(new Flt64(-2.0));
        function.ResultVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(4.0));
        function.Evaluate(Values(x, 2.0, y, 4.0)).Should().Be(new Flt64(2.0));

        LinearMechanismModel<Flt64> model = await BuildModel(function, x, y);
        var mechanism = (IMechanismModel<Flt64>)model;

        mechanism.Constraints.Should().HaveCount(5);
        mechanism.Constraints.Select(constraint => constraint.Name).Should().BeEquivalentTo(
            new[] {
                "contract-min_min_ub_0",
                "contract-min_min_ub_1",
                "contract-min_min_lb_0",
                "contract-min_min_lb_1",
                "contract-min_min_select_one",
            }
        );
        IsFeasible(model, function, x, y, 2.0, 4.0, 2.0, 1.0, 0.0).Should().BeTrue();
        IsFeasible(model, function, x, y, 2.0, 4.0, 1.0, 1.0, 0.0).Should().BeFalse();
    }

    [Fact]
    public void Wrappers_ShouldDelegateToMatchingExtremum() {
        var x = new RealVar("wrapper-x");
        var y = new RealVar("wrapper-y");
        var minMax = new MinMaxFunction<Flt64>(
            polynomials: new[] { Polynomial(x), Polynomial(y) },
            converter: IIntoValue<Flt64>.Identity,
            name: "wrapper-minmax"
        );
        var maxMin = new MaxMinFunction<Flt64>(
            polynomials: new[] { Polynomial(x), Polynomial(y) },
            converter: IIntoValue<Flt64>.Identity,
            name: "wrapper-maxmin"
        );
        IReadOnlyDictionary<ISymbol, Flt64> values = Values(x, -3.0, y, 2.0);

        minMax.Evaluate(values).Should().Be(new Flt64(2.0));
        maxMin.Evaluate(values).Should().Be(new Flt64(-3.0));
        minMax.ResultVar.Name.Should().Be("wrapper-minmax_max");
        maxMin.ResultVar.Name.Should().Be("wrapper-maxmin_min");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyInput() {
        Action max = () => new MaxFunction<Flt64>(Array.Empty<LinearPolynomial<Flt64>>(), IIntoValue<Flt64>.Identity);
        Action min = () => new MinFunction<Flt64>(Array.Empty<LinearPolynomial<Flt64>>(), IIntoValue<Flt64>.Identity);

        max.Should().Throw<ArgumentException>();
        min.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RegisterConstraints_WhenExplicitBigMIsNegative_ShouldFail() {
        var maxInput = new RealVar("negative-big-m-max-input");
        var minInput = new RealVar("negative-big-m-min-input");
        foreach ((IMathFunctionSymbol<Flt64> Function, RealVar Input) test in new[] {
            (Function: (IMathFunctionSymbol<Flt64>)new MaxFunction<Flt64>(
                polynomials: new[] { Polynomial(maxInput) },
                converter: IIntoValue<Flt64>.Identity,
                bigM: new Flt64(-1.0),
                name: "negative-big-m-max"
            ), Input: maxInput),
            (Function: (IMathFunctionSymbol<Flt64>)new MinFunction<Flt64>(
                polynomials: new[] { Polynomial(minInput) },
                converter: IIntoValue<Flt64>.Identity,
                bigM: new Flt64(-1.0),
                name: "negative-big-m-min"
            ), Input: minInput),
        }) {
            using var metaModel = new LinearMetaModel<Flt64>(test.Function.Name);
            metaModel.Add(test.Input).IsOk.Should().BeTrue();
            metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(test.Function)).IsOk.Should().BeTrue();

            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

            result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
            ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
                .Should().Be(ErrorCode.IllegalArgument);
        }
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
        IMathFunctionSymbol<Flt64> function,
        RealVar x,
        RealVar y,
        double xValue,
        double yValue,
        double result,
        double firstSelector,
        double secondSelector) {
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(xValue),
            [y] = new Flt64(yValue),
            [function.HelperVariables[0]] = new Flt64(result),
            [function.HelperVariables[1]] = new Flt64(firstSelector),
            [function.HelperVariables[2]] = new Flt64(secondSelector),
        };
        var solution = model.Tokens.TokensInSolver
            .Select(token => values[token.Variable])
            .ToList();
        return ((IMechanismModel<Flt64>)model).Constraints
            .All(constraint => constraint.IsTrue(solution) == true);
    }

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        RealVar x,
        double xValue,
        RealVar? y,
        double yValue) {
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(xValue) };
        if (y is not null) {
            values[y] = new Flt64(yValue);
        }
        return values;
    }

    private static LinearPolynomial<Flt64> Polynomial(RealVar variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );
}
