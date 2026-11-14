#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
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

/// <summary>离散算术函数契约测试 / Discrete arithmetic function contract tests.</summary>
public class DiscreteArithmeticFunctionTest {
    [Fact]
    public async Task Mod_ShouldEvaluateFloorModuloAndRegisterThreeRows() {
        var x = new RealVar("contract-mod-x");
        var function = new ModFunction(
            polynomial: Polynomial(x),
            divisor: new Flt64(2.0),
            name: "contract-mod"
        );

        function.HelperVariables.Should().HaveCount(3);
        function.Evaluate(Values(x, 5.0)).Should().Be(Flt64.One);
        function.Evaluate(Values(x, -5.0)).Should().Be(Flt64.One);
        function.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().BeNull();

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-mod_mod_decompose",
            "contract-mod_mod_r_ub",
            "contract-mod_mod_result"
        );
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(5.0),
            [function.QuotientVar] = new(2.0),
            [function.RemainderVar] = new(1.0),
            [function.ResultVar] = new(1.0),
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(5.0),
            [function.QuotientVar] = new(2.0),
            [function.RemainderVar] = new(1.0),
            [function.ResultVar] = new(0.0),
        }).Should().BeFalse();
    }

    [Fact]
    public void Mod_WhenDivisorIsNotPositiveAndFinite_ShouldRejectConstruction() {
        LinearPolynomial<Flt64> polynomial = Polynomial(new RealVar("invalid-mod-x"));

        foreach (double divisor in new[] {
            0.0,
            -1.0,
            double.PositiveInfinity,
            double.NaN,
        }) {
            Action create = () => new ModFunction(
                polynomial: polynomial,
                divisor: new Flt64(divisor)
            );
            create.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public async Task Semi_ShouldExposeDefaultsAndRegisterBothDomainRows() {
        var function = new SemiFunction(
            lb: new Flt64(2.0),
            ub: new Flt64(5.0),
            name: "contract-semi"
        );

        function.HelperVariables.Should().HaveCount(2);
        function.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().BeNull();
        function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [function.IndicatorVar] = Flt64.Zero,
            [function.ResultVar] = new Flt64(5.0),
        }).Should().Be(Flt64.Zero);
        function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [function.IndicatorVar] = Flt64.One,
            [function.ResultVar] = new Flt64(0.0),
        }).Should().Be(new Flt64(2.0));

        var defaults = new SemiFunction(name: "default-semi");
        defaults.LowerBound.Should().Be(Flt64.Zero);
        defaults.UpperBound.Should().Be(new Flt64(1e6));

        LinearMechanismModel<Flt64> model = await BuildModel(function);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-semi_semi_upper",
            "contract-semi_semi_lower"
        );
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [function.IndicatorVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [function.IndicatorVar] = Flt64.One,
            [function.ResultVar] = new Flt64(1.0),
        }).Should().BeFalse();
    }

    [Fact]
    public void Semi_ShouldInferFiniteBoundsAndRejectInvalidOverrides() {
        var variable = new RealVar("semi-inference-x");
        variable.Range.Geq(new Flt64(-2.0));
        variable.Range.Leq(new Flt64(7.0));

        var inferred = SemiFunction.From(
            variable: variable,
            name: "semi-inferred"
        );
        inferred.LowerBound.Should().Be(new Flt64(-2.0));
        inferred.UpperBound.Should().Be(new Flt64(7.0));

        Action reversed = () => new SemiFunction(
            lb: new Flt64(5.0),
            ub: new Flt64(2.0)
        );
        reversed.Should().Throw<ArgumentException>();
        Action nonFinite = () => new SemiFunction(
            lb: new Flt64(double.PositiveInfinity),
            ub: new Flt64(2.0)
        );
        nonFinite.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task InStepRange_ShouldDelegateFloorAndRegisterBounds() {
        var upper = new RealVar("contract-step-upper");
        var function = new InStepRangeFunction(
            lowerBound: Constant(1.0),
            upperBound: Polynomial(upper),
            step: new Flt64(3.0),
            name: "contract-step"
        );

        function.HelperVariables.Should().HaveCount(2);
        function.Evaluate(Values(upper, 10.0)).Should().Be(new Flt64(10.0));
        function.Evaluate(Values(upper, 9.0)).Should().Be(new Flt64(7.0));
        function.Evaluate(Values(upper, 0.0)).Should().BeNull();

        LinearMechanismModel<Flt64> model = await BuildModel(function, upper);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-step_q_floor_lb",
            "contract-step_q_floor_ub",
            "contract-step_q_floor_result",
            "contract-step_bounds"
        );
        var feasibleValues = new Dictionary<IVariableItem, Flt64> {
            [upper] = new(1.0),
            [function.Floor.KVar] = Flt64.Zero,
            [function.Floor.ResultVar] = Flt64.Zero,
        };
        var solution = model.Tokens.TokensInSolver
            .Select(token => feasibleValues[token.Variable])
            .ToList();
        IEnumerable<string> failedConstraints = ((IMechanismModel<Flt64>)model).Constraints
            .Where(constraint => constraint.IsTrue(solution) != true)
            .Select(constraint => constraint.Name);
        failedConstraints.Should().BeEmpty();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [upper] = new(1.0),
            [function.Floor.KVar] = new(1.0),
            [function.Floor.ResultVar] = new(1.0),
        }).Should().BeFalse();

        Flt64 result = function.ResultPolynomial.Constant;
        foreach (LinearMonomial<Flt64> monomial in function.ResultPolynomial.Monomials) {
            Flt64 value = monomial.Symbol == upper
                ? new Flt64(1.0)
                : monomial.Symbol == function.Floor.ResultVar
                    ? Flt64.Zero
                    : Flt64.Zero;
            result = result.Plus(monomial.Coefficient.Times(value));
        }
        result.Should().Be(new Flt64(1.0));
    }

    [Fact]
    public void InStepRange_ShouldRejectNonPositiveOrNonFiniteInputs() {
        LinearPolynomial<Flt64> bound = Constant(0.0);
        foreach (double step in new[] {
            0.0,
            -1.0,
            double.PositiveInfinity,
        }) {
            Action create = () => new InStepRangeFunction(
                lowerBound: bound,
                upperBound: bound,
                step: new Flt64(step)
            );
            create.Should().Throw<ArgumentException>();
        }

        Action nonFinite = () => new InStepRangeFunction(
            lowerBound: Constant(double.PositiveInfinity),
            upperBound: bound,
            step: Flt64.One
        );
        nonFinite.Should().Throw<ArgumentException>();
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

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );

    private static LinearPolynomial<Flt64> Constant(double value) => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        new Flt64(value)
    );

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        RealVar variable,
        double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new(value) };
}
