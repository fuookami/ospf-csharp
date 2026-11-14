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
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>三值化与条件选择函数契约测试 / Ternary and conditional-selection function contract tests.</summary>
public class TernarySelectionFunctionsTest {
    [Fact]
    public async Task BalanceTernaryzation_ShouldEvaluateThreeStatesAndRegisterSixRows() {
        var x = new RealVar("ternary-x");
        x.Range.Geq(new Flt64(-2.0));
        x.Range.Leq(new Flt64(2.0));
        var function = new BalanceTernaryzationFunction(
            polynomial: Polynomial(x),
            epsilon: new Flt64(0.1),
            fallbackBigM: new Flt64(10.0),
            strictBoundary: new Flt64(1e-10),
            name: "contract-bter"
        );
        Flt64 positiveBoundary = new Flt64(0.1).Plus(new Flt64(1e-10));

        function.HelperVariables.Should().HaveCount(3);
        function.Evaluate(Values((x, -2.0))).Should().Be(new Flt64(-1.0));
        function.Evaluate(Values((x, -0.1))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 0.0))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 0.1))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 2.0))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, positiveBoundary.ToDouble()))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 0.10000000005))).Should().BeNull();
        function.Evaluate(Values()).Should().BeNull();

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-bter_bter_positive_lower",
            "contract-bter_bter_positive_upper",
            "contract-bter_bter_negative_lower",
            "contract-bter_bter_negative_upper",
            "contract-bter_bter_result",
            "contract-bter_bter_exclusive"
        );
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(1.5),
            [function.PositiveVar] = Flt64.One,
            [function.NegativeVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(-1.5),
            [function.PositiveVar] = Flt64.Zero,
            [function.NegativeVar] = Flt64.One,
            [function.ResultVar] = new(-1.0),
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.Zero,
            [function.PositiveVar] = Flt64.Zero,
            [function.NegativeVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.Zero,
            [function.PositiveVar] = Flt64.One,
            [function.NegativeVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(1.5),
            [function.PositiveVar] = Flt64.Zero,
            [function.NegativeVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        // The open strict-gap above epsilon admits no indicator value: positive must be zero
        // (upper row) yet the zero assignment requires the positive branch to stay below epsilon.
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(0.10000000005),
            [function.PositiveVar] = Flt64.Zero,
            [function.NegativeVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeFalse();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(0.10000000005),
            [function.PositiveVar] = Flt64.One,
            [function.NegativeVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(1.5),
            [function.PositiveVar] = Flt64.One,
            [function.NegativeVar] = Flt64.One,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeFalse();
    }

    [Fact]
    public void BalanceTernaryzation_ShouldRejectInvalidConfiguration() {
        LinearPolynomial<Flt64> polynomial = Constant(0.0);

        Action negativeEpsilon = () => new BalanceTernaryzationFunction(
            polynomial: polynomial,
            epsilon: new Flt64(-0.1)
        );
        Action zeroStrictBoundary = () => new BalanceTernaryzationFunction(
            polynomial: polynomial,
            strictBoundary: Flt64.Zero
        );
        Action insufficientFallback = () => new BalanceTernaryzationFunction(
            polynomial: polynomial,
            fallbackBigM: Flt64.Zero
        );

        negativeEpsilon.Should().Throw<ArgumentException>();
        zeroStrictBoundary.Should().Throw<ArgumentException>();
        insufficientFallback.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task OneOf_ShouldEvaluateExactNonzeroCountAndRegisterSelectionRows() {
        var x = new RealVar("oneof-x");
        var y = new RealVar("oneof-y");
        x.Range.Geq(new Flt64(-10.0));
        x.Range.Leq(new Flt64(10.0));
        y.Range.Geq(new Flt64(-10.0));
        y.Range.Leq(new Flt64(10.0));
        var function = new OneOfFunction(
            polynomials: new[] {
                Polynomial(x),
                Polynomial(y),
                Constant(0.0),
            },
            bigM: new Flt64(20.0),
            tolerance: new Flt64(1e-6),
            strictBoundary: new Flt64(0.5),
            name: "contract-oneof"
        );

        function.HelperVariables.Should().HaveCount(7);
        function.Evaluate(Values((x, 0.0), (y, 5.0))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 1.0), (y, 5.0))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 0.0), (y, 0.0))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 0.0), (y, 0.25))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 0.0))).Should().BeNull();

        LinearMechanismModel<Flt64> model = await BuildModel(function, x, y);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Should().HaveCount(14);
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Contain(new[] {
            "contract-oneof_oneof_nz_0_band_ub",
            "contract-oneof_oneof_nz_2_out_ub",
            "contract-oneof_oneof_exactly_one",
            "contract-oneof_oneof_result",
        });
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.Zero,
            [y] = new(5.0),
            [function.IndicatorVars[0]] = Flt64.Zero,
            [function.SideVars[0]] = Flt64.Zero,
            [function.IndicatorVars[1]] = Flt64.One,
            [function.SideVars[1]] = Flt64.One,
            [function.IndicatorVars[2]] = Flt64.Zero,
            [function.SideVars[2]] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(1.0),
            [y] = new(5.0),
            [function.IndicatorVars[0]] = Flt64.One,
            [function.SideVars[0]] = Flt64.One,
            [function.IndicatorVars[1]] = Flt64.One,
            [function.SideVars[1]] = Flt64.One,
            [function.IndicatorVars[2]] = Flt64.Zero,
            [function.SideVars[2]] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.Zero,
            [y] = new(5.0),
            [function.IndicatorVars[0]] = Flt64.Zero,
            [function.SideVars[0]] = Flt64.Zero,
            [function.IndicatorVars[1]] = Flt64.One,
            [function.SideVars[1]] = Flt64.One,
            [function.IndicatorVars[2]] = Flt64.Zero,
            [function.SideVars[2]] = Flt64.Zero,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeFalse();
    }

    [Fact]
    public void OneOf_ShouldRejectEmptyInput() {
        Action create = () => new OneOfFunction(Array.Empty<LinearPolynomial<Flt64>>());

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SameAs_ShouldMeasureWhetherSatisfactionFlagsAgree() {
        var x = new RealVar("same-x");
        var y = new RealVar("same-y");
        var function = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Polynomial(x),
                    Constant(0.0),
                    Comparison.LE
                ),
                new LinearInequality<Flt64>(
                    Polynomial(y),
                    Constant(0.0),
                    Comparison.GE
                ),
            },
            epsilon: new Flt64(0.1),
            constraint: false,
            bigM: new Flt64(20.0),
            name: "contract-same"
        );

        function.HelperVariables.Should().HaveCount(6);
        function.Evaluate(Values((x, -1.0), (y, 1.0))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 0.05), (y, 0.05))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 0.2), (y, -0.2))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, -1.0), (y, -1.0))).Should().Be(Flt64.Zero);

        LinearMechanismModel<Flt64> model = await BuildModel(function, x, y);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-same_ineq_0_sat",
            "contract-same_ineq_0_violated",
            "contract-same_ineq_0_one_hot",
            "contract-same_ineq_1_sat",
            "contract-same_ineq_1_violated",
            "contract-same_ineq_1_one_hot",
            "contract-same_diff_ge_1",
            "contract-same_diff_le_1",
            "contract-same_diff_sum_ub_1",
            "contract-same_diff_sum_lb_1",
            "contract-same_result_diff_1",
            "contract-same_result_sum"
        );
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(-1.0),
            [y] = new(1.0),
            [function.SatisfactionFlags[0]] = Flt64.One,
            [function.SatisfactionFlags[1]] = Flt64.One,
            [function.ViolationFlags[0]!] = Flt64.Zero,
            [function.ViolationFlags[1]!] = Flt64.Zero,
            [function.DiffVars[0]] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(-1.0),
            [y] = new(-1.0),
            [function.SatisfactionFlags[0]] = Flt64.One,
            [function.SatisfactionFlags[1]] = Flt64.Zero,
            [function.ViolationFlags[0]!] = Flt64.Zero,
            [function.ViolationFlags[1]!] = Flt64.One,
            [function.DiffVars[0]] = Flt64.One,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(-1.0),
            [y] = new(-1.0),
            [function.SatisfactionFlags[0]] = Flt64.One,
            [function.SatisfactionFlags[1]] = Flt64.Zero,
            [function.ViolationFlags[0]!] = Flt64.Zero,
            [function.ViolationFlags[1]!] = Flt64.One,
            [function.DiffVars[0]] = Flt64.Zero,
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeFalse();
    }

    [Fact]
    public async Task SameAs_ShouldForceAllFlagsInConstraintMode() {
        var function = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Constant(0.0),
                    Constant(0.0),
                    Comparison.LE
                ),
                new LinearInequality<Flt64>(
                    Constant(0.0),
                    Constant(0.0),
                    Comparison.GE
                ),
            },
            epsilon: new Flt64(0.1),
            constraint: true,
            bigM: new Flt64(10.0),
            name: "contract-same-constraint"
        );

        LinearMechanismModel<Flt64> model = await BuildModel(function);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-same-constraint_ineq_0_sat",
            "contract-same-constraint_ineq_0_violated",
            "contract-same-constraint_ineq_0_one_hot",
            "contract-same-constraint_ineq_1_sat",
            "contract-same-constraint_ineq_1_violated",
            "contract-same-constraint_ineq_1_one_hot",
            "contract-same-constraint_equal_1",
            "contract-same-constraint_result_link"
        );
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [function.SatisfactionFlags[0]] = Flt64.One,
            [function.SatisfactionFlags[1]] = Flt64.Zero,
            [function.ViolationFlags[0]!] = Flt64.Zero,
            [function.ViolationFlags[1]!] = Flt64.One,
            [function.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [function.SatisfactionFlags[0]] = Flt64.One,
            [function.SatisfactionFlags[1]] = Flt64.One,
            [function.ViolationFlags[0]!] = Flt64.Zero,
            [function.ViolationFlags[1]!] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        }).Should().BeTrue();
    }

    [Fact]
    public async Task SameAs_ShouldRegisterEqualitySideRowsAndRejectStrictRelations() {
        var equality = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Constant(0.0),
                    Constant(0.0),
                    Comparison.EQ
                ),
            },
            epsilon: new Flt64(0.1),
            constraint: false,
            bigM: new Flt64(10.0),
            name: "contract-same-eq"
        );

        equality.HelperVariables.Should().HaveCount(4);
        LinearMechanismModel<Flt64> model = await BuildModel(equality);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-same-eq_ineq_0_zero_band_ub",
            "contract-same-eq_ineq_0_zero_band_lb",
            "contract-same-eq_ineq_0_zero_out_lb",
            "contract-same-eq_ineq_0_zero_out_ub",
            "contract-same-eq_ineq_0_one_hot",
            "contract-same-eq_result_single"
        );

        foreach (Comparison comparison in new[] {
            Comparison.LT,
            Comparison.GT,
            Comparison.NE,
        }) {
            int before = mechanism.Constraints.Count;
            var unsupported = new SameAsFunction(
                inequalities: new[] {
                    new LinearInequality<Flt64>(
                        Constant(0.0),
                        Constant(0.0),
                        comparison
                    ),
                },
                epsilon: new Flt64(0.1),
                bigM: new Flt64(10.0),
                name: $"same-{comparison}"
            );

            Try result = unsupported.RegisterConstraints(model);

            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
            ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Code
                .Should().Be(ErrorCode.ApplicationError);
            mechanism.Constraints.Should().HaveCount(before);
        }
    }

    [Theory]
    [InlineData(Comparison.LE, 0.1, true)]
    [InlineData(Comparison.LE, 0.2001, false)]
    [InlineData(Comparison.LT, 0.1, false)]
    [InlineData(Comparison.LT, 0.0999, true)]
    [InlineData(Comparison.GE, -0.1, true)]
    [InlineData(Comparison.GE, -0.2001, false)]
    [InlineData(Comparison.GT, -0.1, false)]
    [InlineData(Comparison.GT, -0.0999, true)]
    [InlineData(Comparison.EQ, 0.1, true)]
    [InlineData(Comparison.EQ, 0.2001, false)]
    [InlineData(Comparison.NE, 0.1001, true)]
    [InlineData(Comparison.NE, 0.1, false)]
    public void SameAs_ShouldEvaluateEveryComparisonWithTolerance(
        Comparison comparison,
        double difference,
        bool expected) {
        var function = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Constant(0.0),
                    Constant(0.0),
                    Comparison.LE
                ),
                new LinearInequality<Flt64>(
                    Constant(difference),
                    Constant(0.0),
                    comparison
                ),
            },
            epsilon: new Flt64(0.1),
            constraint: false,
            bigM: new Flt64(10.0),
            name: $"same-{comparison}"
        );

        function.Evaluate(Values()).Should().Be(expected ? Flt64.One : Flt64.Zero);
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
        params (RealVar Variable, double Value)[] values) =>
        values.ToDictionary(
            item => (ISymbol)item.Variable,
            item => new Flt64(item.Value)
        );
}
