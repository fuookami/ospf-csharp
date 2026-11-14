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
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>满足数量函数契约测试 / Satisfied-amount function contract tests.</summary>
public class SatisfiedAmountFunctionsTest {
    private static readonly Flt64 Eps = new(1e-6);
    private static readonly Flt64 Ten = new(10.0);

    [Fact]
    public async Task SatisfiedAmount_ShouldCountFlagsAndRegisterIndicatorRows() {
        var x = new RealVar("sat-x");
        var y = new RealVar("sat-y");
        Bounded(x);
        Bounded(y);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Row(x, Comparison.LE, Constant(1.0)),
                Row(y, Comparison.GE, Constant(0.0)),
            },
            epsilon: Eps,
            bigM: Ten,
            name: "sat"
        );

        function.HelperVariables.Should().HaveCount(3);
        function.Evaluate(Values((x, 0.0), (y, 1.0))).Should().Be(new Flt64(2.0));
        function.Evaluate(Values((x, 5.0), (y, -1.0))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 0.0))).Should().BeNull();
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");

        LinearMechanismModel<Flt64> model = await BuildModel(function, x, y);
        ConstraintNames(model).Should().Equal(
            "sat_sat_le_ub_0",
            "sat_sat_le_lb_0",
            "sat_sat_residual_0",
            "sat_sat_ge_ub_1",
            "sat_sat_ge_lb_1"
        );
        var values = new Dictionary<ISymbol, Flt64> {
            [x] = new Flt64(5),
            [y] = new Flt64(2),
            [function.FlagVars[0]] = Flt64.Zero,
            [function.FlagVars[1]] = Flt64.One,
            [residual] = new Flt64(4),
        };
        IsFeasible(model, values).Should().BeTrue();
        values[function.FlagVars[0]] = Flt64.One;
        IsFeasible(model, values).Should().BeFalse();
        values[x] = new Flt64(0.5);
        values[residual] = new Flt64(-0.5);
        values[function.FlagVars[0]] = Flt64.Zero;
        IsFeasible(model, values).Should().BeFalse();
        values[function.FlagVars[0]] = Flt64.One;
        IsFeasible(model, values).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmount_Threshold_ShouldReportZeroOneAndAppendAmountRow() {
        var x = new RealVar("sat-threshold-x");
        Bounded(x);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Row(x, Comparison.LE, Constant(1.0)),
                Row(x, Comparison.GE, Constant(0.0)),
            },
            epsilon: Eps,
            amount: new UInt64(2),
            bigM: Ten,
            name: "sat-threshold"
        );

        function.Evaluate(Values((x, 0.5))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 5.0))).Should().Be(Flt64.Zero);
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        ConstraintNames(model).Should().EndWith(new[] { "sat-threshold_amount" });
        var values = new Dictionary<ISymbol, Flt64> {
            [x] = new Flt64(0.5),
            [function.FlagVars[0]] = Flt64.One,
            [function.FlagVars[1]] = Flt64.Zero,
            [residual] = new Flt64(-0.5),
        };
        IsFeasible(model, values).Should().BeFalse();
        values[function.FlagVars[1]] = Flt64.One;
        IsFeasible(model, values).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmount_Equality_ShouldRegisterOneHotBranchesAndResidual() {
        var x = new RealVar("sat-eq-x");
        Bounded(x);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Row(x, Comparison.EQ, Constant(2.0)) },
            epsilon: Eps,
            bigM: Ten,
            name: "sat-eq"
        );

        function.HelperVariables.Should().HaveCount(4);
        function.Evaluate(Values((x, 2.0))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 2.0000005))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 2.01))).Should().Be(Flt64.Zero);

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        ConstraintNames(model).Should().Equal(
            "sat-eq_sat_eq_0_band_ub",
            "sat-eq_sat_eq_0_band_lb",
            "sat-eq_sat_eq_0_out_lb",
            "sat-eq_sat_eq_0_out_ub",
            "sat-eq_sat_eq_0_one_hot",
            "sat-eq_sat_residual_0"
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        IsFeasible(model, Values(
            (x, 2.0),
            (function.FlagVars[0], 1.0),
            (function.EqPositiveVars[0]!, 0.0),
            (function.EqNegativeVars[0]!, 0.0),
            (residual, 0.0)))
            .Should().BeTrue();
        IsFeasible(model, Values(
            (x, 2.0),
            (function.FlagVars[0], 0.0),
            (function.EqPositiveVars[0]!, 0.0),
            (function.EqNegativeVars[0]!, 1.0),
            (residual, 0.0)))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.NE)]
    public async Task SatisfiedAmount_ShouldRejectStrictRelationsWithoutWritingRows(Comparison comparison) {
        var x = new RealVar($"sat-strict-{comparison}");
        Bounded(x);
        var accepted = new SatisfiedAmountFunction(
            inequalities: new[] { Row(x, Comparison.LE, Constant(1.0)) },
            epsilon: Eps,
            bigM: Ten,
            name: $"sat-strict-base-{comparison}"
        );
        LinearMechanismModel<Flt64> model = await BuildModel(accepted, x);
        int before = ConstraintNames(model).Count;

        var rejected = new SatisfiedAmountFunction(
            inequalities: new[] { Row(x, comparison, Constant(1.0)) },
            epsilon: Eps,
            bigM: Ten,
            name: $"sat-strict-{comparison}"
        );
        Try result = rejected.RegisterConstraints(model);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.OREngineModelingException);
        ConstraintNames(model).Should().HaveCount(before);
    }

    [Fact]
    public async Task SatisfiedAmount_ShouldRejectInvalidEpsilon() {
        var x = new RealVar("sat-bad-x");
        Bounded(x);
        var accepted = new SatisfiedAmountFunction(
            inequalities: new[] { Row(x, Comparison.LE, Constant(1.0)) },
            epsilon: Eps,
            bigM: Ten,
            name: "sat-bad-base"
        );
        LinearMechanismModel<Flt64> model = await BuildModel(accepted, x);
        int before = ConstraintNames(model).Count;

        var rejected = new SatisfiedAmountFunction(
            inequalities: new[] { Row(x, Comparison.LE, Constant(1.0)) },
            epsilon: new Flt64(double.NaN),
            name: "sat-bad"
        );
        rejected.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ConstraintNames(model).Should().HaveCount(before);

        var nonPositive = new SatisfiedAmountFunction(
            inequalities: new[] { Row(x, Comparison.EQ, Constant(1.0)) },
            epsilon: Flt64.Zero,
            bigM: Ten,
            name: "sat-bad-eq"
        );
        nonPositive.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ConstraintNames(model).Should().HaveCount(before);
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    [InlineData(Comparison.NE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.GT)]
    public async Task SatisfiedAmountInequality_ShouldEmitBaselineRowsForEverySign(Comparison sign) {
        var x = new RealVar($"baseline-{sign}");
        var function = new SatisfiedAmountInequalityFunction(
            inputs: new[] { Input(Row(x, sign, Constant(3.0)), -4.0, 7.0, 3.0) },
            epsilon: new Flt64(0.25),
            name: "baseline"
        );
        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        LinearConstraintImpl<Flt64>[] rows = ((IMechanismModel<Flt64>)model).Constraints.Cast<LinearConstraintImpl<Flt64>>().ToArray();
        rows.Should().HaveCount(2);
        rows.Select(row => row.Name).Should().Equal("baseline_i0_upper", "baseline_i0_lower");
        rows[0].Sign.Should().Be(ConstraintRelation.LessEqual);
        rows[1].Sign.Should().Be(ConstraintRelation.GreaterEqual);
        rows[0].Rhs.Should().Be(new Flt64(10.25));
        rows[1].Rhs.Should().Be(new Flt64(-4.25));
        rows[0].LinearCells.Single(cell => ReferenceEquals(cell.Token.Variable, x)).Coefficient.Should().Be(Flt64.One);
        rows[1].LinearCells.Single(cell => ReferenceEquals(cell.Token.Variable, x)).Coefficient.Should().Be(Flt64.One);
        rows[0].LinearCells.Single(cell => ReferenceEquals(cell.Token.Variable, function.FlagVars[0])).Coefficient.Should().Be(new Flt64(7.0));
        rows[1].LinearCells.Single(cell => ReferenceEquals(cell.Token.Variable, function.FlagVars[0])).Coefficient.Should().Be(new Flt64(-7.0));
        function.HelperVariables.Select(variable => variable.Name).Should().Equal("baseline_u_0");
        foreach (double residual in new[] { -7.5, -4.0, -0.25, 0.0, 0.25, 0.5, 7.0, 7.5 }) {
            foreach (double flag in new[] { 0.0, 1.0 }) {
                bool expected = residual + 7.0 * flag <= 7.25 && residual - 7.0 * flag >= -7.25;
                IsFeasible(model, Values((x, residual + 3.0), (function.FlagVars[0], flag)))
                    .Should().Be(expected, $"Kotlin row fixture: sign={sign}, d={residual}, u={flag}");
            }
        }
    }

    [Fact]
    public async Task SatisfiedAmountInequality_ShouldCountBySignButRegisterZeroBandImplication() {
        var x = new RealVar("satineq-x");
        var y = new RealVar("satineq-y");
        var function = new SatisfiedAmountInequalityFunction(
            new[] {
                Input(Row(x, Comparison.LE, Constant(1.0)), -1000.0, 1000.0, 1.0),
                Input(Row(y, Comparison.GE, Constant(0.0)), -1000.0, 1000.0, 0.0),
            },
            name: "satineq"
        );

        function.Epsilon.Should().Be(new Flt64(1e-6));
        function.HelperVariables.Should().HaveCount(2);
        function.Evaluate(Values((x, 0.0), (y, 1.0))).Should().Be(new Flt64(2.0));
        function.Evaluate(Values((x, 5.0), (y, -1.0))).Should().Be(Flt64.Zero);
        function.Evaluate(Values((x, 0.0))).Should().BeNull();

        LinearMechanismModel<Flt64> model = await BuildModel(function, x, y);
        ConstraintNames(model).Should().Equal(
            "satineq_i0_upper",
            "satineq_i0_lower",
            "satineq_i1_upper",
            "satineq_i1_lower"
        );
        IsFeasible(model, Values(
            (x, 0.5),
            (y, -1.0),
            (function.FlagVars[0], 1.0),
            (function.FlagVars[1], 0.0)))
            .Should().BeFalse();
        IsFeasible(model, Values(
            (x, 0.5),
            (y, -1.0),
            (function.FlagVars[0], 1.0),
            (function.FlagVars[1], 1.0)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SatisfiedAmountInequality_Equality_ShouldUseZeroBandRows() {
        var x = new RealVar("satineq-eq-x");
        var function = new SatisfiedAmountInequalityFunction(
            new[] { Input(Row(x, Comparison.EQ, Constant(3.0)), -100.0, 100.0, 3.0) },
            name: "satineq-eq"
        );

        function.HelperVariables.Should().HaveCount(1);
        function.Evaluate(Values((x, 3.0))).Should().Be(Flt64.One);
        function.Evaluate(Values((x, 3.5))).Should().Be(Flt64.Zero);

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        ConstraintNames(model).Should().Equal("satineq-eq_i0_upper", "satineq-eq_i0_lower");
        IsFeasible(model, Values((x, 3.0), (function.FlagVars[0], 0.0))).Should().BeTrue();
        IsFeasible(model, Values((x, 3.0), (function.FlagVars[0], 1.0))).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmountInequality_TrivialRanges_ShouldFixFlagValue() {
        var satisfied = new RealVar("satineq-true-x");
        var violated = new RealVar("satineq-false-x");
        var function = new SatisfiedAmountInequalityFunction(
            new[] {
                Input(Row(satisfied, Comparison.LE, Constant(0.0)), -10.0, -2.0, 0.0),
                Input(Row(violated, Comparison.LE, Constant(0.0)), 2.0, 10.0, 0.0),
            },
            name: "satineq-trivial"
        );

        function.Evaluate(Values((satisfied, -5.0), (violated, 5.0))).Should().Be(Flt64.One);

        LinearMechanismModel<Flt64> model = await BuildModel(function, satisfied, violated);
        ConstraintNames(model).Should().Equal(
            "satineq-trivial_i0_flag",
            "satineq-trivial_i1_flag"
        );
        IsFeasible(model, Values(
            (satisfied, -5.0),
            (violated, 5.0),
            (function.FlagVars[0], 1.0),
            (function.FlagVars[1], 0.0)))
            .Should().BeTrue();
        IsFeasible(model, Values(
            (satisfied, -5.0),
            (violated, 5.0),
            (function.FlagVars[0], 0.0),
            (function.FlagVars[1], 0.0)))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0.0, 1, 2, 1)]
    [InlineData(0.0, 2, 2, 1)]
    [InlineData(1.0, 2, 2, 0)]
    [InlineData(1.0, 1, 1, 1)]
    public void SatisfiedAmountInequality_AmountRange_ShouldDecideIndicator(
        double xValue,
        int lower,
        int upper,
        int expected) {
        var x = new RealVar($"satineq-range-{xValue}-{lower}-{upper}");
        var function = new NumerableFunction(
            new[] {
                Input(Row(x, Comparison.LE, Constant(0.0)), -1000.0, 1000.0, 0.0),
                Input(Row(x, Comparison.GE, Constant(0.0)), -1000.0, 1000.0, 0.0),
            },
            SatisfiedAmountRangeFixture.CountRange(lower, upper),
            name: $"satineq-range-{xValue}-{lower}-{upper}"
        );

        function.Evaluate(Values((x, xValue))).Should().Be(new Flt64(expected));
    }

    [Fact]
    public async Task SatisfiedAmountInequality_Aliases_ShouldLinkIndicatorToCountRange() {
        var x = new RealVar("satineq-alias-x");
        LinearConstraintInput<Flt64>[] inputs = new[] {
            Input(Row(x, Comparison.LE, Constant(0.0)), -1000.0, 1000.0, 0.0),
            Input(Row(x, Comparison.GE, Constant(0.0)), -1000.0, 1000.0, 0.0),
        };
        var any = new AnyFunction(inputs, name: "satineq-any");
        var all = new AllFunction(inputs, name: "satineq-all");
        var atLeast = new AtLeastInequalityFunction(inputs, new UInt64(2), name: "satineq-atleast");
        var notAll = new NotAllFunction(inputs, name: "satineq-notall");

        any.AmountFlagVar.Should().NotBeNull();
        atLeast.K.Should().Be(new UInt64(2));
        any.Evaluate(Values((x, 0.0))).Should().Be(Flt64.One);
        all.Evaluate(Values((x, 1.0))).Should().Be(Flt64.Zero);
        atLeast.Evaluate(Values((x, 1.0))).Should().Be(Flt64.Zero);
        notAll.Evaluate(Values((x, 1.0))).Should().Be(Flt64.One);

        LinearMechanismModel<Flt64> anyModel = await BuildModel(any, x);
        ConstraintNames(anyModel).Should().Equal(
            "satineq-any_i0_upper",
            "satineq-any_i0_lower",
            "satineq-any_i1_upper",
            "satineq-any_i1_lower",
            "satineq-any_amount_lb",
            "satineq-any_amount_ub"
        );
        any.HelperVariables.Select(variable => variable.Name).Should().Equal(
            "satineq-any_u_0", "satineq-any_u_1", "satineq-any_y");
        // 基线仅约束 y=1 => count 在区间内；区间内不强制 y=1。
        // Baseline only requires y=1 => count in range; membership does not force y=1.
        IsFeasible(anyModel, Values(
            (x, 0.0), (any.FlagVars[0], 0.0), (any.FlagVars[1], 1.0), (any.AmountFlagVar!, 1.0)))
            .Should().BeTrue();
        IsFeasible(anyModel, Values(
            (x, 0.0), (any.FlagVars[0], 0.0), (any.FlagVars[1], 1.0), (any.AmountFlagVar!, 0.0)))
            .Should().BeTrue();
        IsFeasible(anyModel, Values(
            (x, 0.0), (any.FlagVars[0], 0.0), (any.FlagVars[1], 0.0), (any.AmountFlagVar!, 1.0)))
            .Should().BeFalse();

        LinearMechanismModel<Flt64> allModel = await BuildModel(all, x);
        IsFeasible(allModel, Values(
            (x, 0.0), (all.FlagVars[0], 0.0), (all.FlagVars[1], 1.0), (all.AmountFlagVar!, 1.0)))
            .Should().BeFalse();
        IsFeasible(allModel, Values(
            (x, 0.0), (all.FlagVars[0], 0.0), (all.FlagVars[1], 1.0), (all.AmountFlagVar!, 0.0)))
            .Should().BeTrue();

        LinearMechanismModel<Flt64> notAllModel = await BuildModel(notAll, x);
        IsFeasible(notAllModel, Values(
            (x, 0.0), (notAll.FlagVars[0], 1.0), (notAll.FlagVars[1], 1.0), (notAll.AmountFlagVar!, 1.0)))
            .Should().BeFalse();
        IsFeasible(notAllModel, Values(
            (x, 0.0), (notAll.FlagVars[0], 1.0), (notAll.FlagVars[1], 1.0), (notAll.AmountFlagVar!, 0.0)))
            .Should().BeTrue();
    }

    [Fact]
    public void NotAllFunction_SingleInput_ShouldFallBackToCounting() {
        var x = new RealVar("satineq-notall-single-x");
        var function = new NotAllFunction(
            new[] { Input(Row(x, Comparison.LE, Constant(0.0)), -1000.0, 1000.0, 0.0) },
            name: "satineq-notall-single"
        );

        function.Amount.Should().BeNull();
        function.AmountFlagVar.Should().BeNull();
        function.Evaluate(Values((x, -1.0))).Should().Be(Flt64.One);
    }

    [Fact]
    public async Task SatisfiedAmountInequality_ShouldRejectInvalidConfigurationWithoutWritingRows() {
        var x = new RealVar("satineq-invalid-x");
        var accepted = new SatisfiedAmountInequalityFunction(
            new[] { Input(Row(x, Comparison.LE, Constant(0.0)), -1000.0, 1000.0, 0.0) },
            name: "satineq-invalid-base"
        );
        LinearMechanismModel<Flt64> model = await BuildModel(accepted, x);
        int before = ConstraintNames(model).Count;

        var badEpsilon = new SatisfiedAmountInequalityFunction(
            new[] { Input(Row(x, Comparison.NE, Constant(0.0)), -1000.0, 1000.0, 0.0) },
            epsilon: new Flt64(-1.0),
            name: "satineq-bad-epsilon"
        );
        badEpsilon.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ConstraintNames(model).Should().HaveCount(before);

        LinearConstraintInput<Flt64>[] single = new[] { Input(Row(x, Comparison.LE, Constant(0.0)), -1000.0, 1000.0, 0.0) };
        Action tooLarge = () => new AtLeastInequalityFunction(single, new UInt64(2));
        tooLarge.Should().Throw<ArgumentException>();
        Action zeroThreshold = () => new AtLeastInequalityFunction(single, UInt64.Zero);
        zeroThreshold.Should().Throw<ArgumentException>();
        var unboundedAmount = new NumerableFunction(
            inputs: single,
            amount: ValueRange<UInt64>.Full(),
            name: "satineq-unbounded-amount"
        );
        unboundedAmount.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ConstraintNames(model).Should().HaveCount(before);
    }

    [Fact]
    public async Task SatisfiedAmountInequality_UnboundedRange_ShouldLeaveFlagFree() {
        var x = new RealVar("satineq-free-x");
        var function = new SatisfiedAmountInequalityFunction(
            new[] { Input(Row(x, Comparison.LE, Constant(0.0)), null, null, 0.0) },
            name: "satineq-free"
        );

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        ConstraintNames(model).Should().BeEmpty();
        IsFeasible(model, Values((x, 5.0), (function.FlagVars[0], 1.0))).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmountInequality_EmptyInputs_ShouldFollowBaselineCounting() {
        LinearConstraintInput<Flt64>[] inputs = Array.Empty<LinearConstraintInput<Flt64>>();
        var count = new SatisfiedAmountInequalityFunction(inputs);
        count.Evaluate(Values()).Should().Be(Flt64.Zero);
        count.HelperVariables.Should().BeEmpty();
        ConstraintNames(await BuildModel(count)).Should().BeEmpty();
        var all = new AllFunction(inputs);
        all.Evaluate(Values()).Should().Be(Flt64.One);
        all.HelperVariables.Should().HaveCount(1);
        LinearMechanismModel<Flt64> model = await BuildModel(all);
        ConstraintNames(model).Should().Equal("all_amount_lb", "all_amount_ub");
        IsFeasible(model, Values((all.AmountFlagVar!, 0.0))).Should().BeTrue();
        IsFeasible(model, Values((all.AmountFlagVar!, 1.0))).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmountInequality_ZeroTolerance_ShouldAllowEqualityWithoutSideColumn() {
        var x = new RealVar("zero-tolerance-x");
        var function = new SatisfiedAmountInequalityFunction(
            inputs: new[] { Input(Row(x, Comparison.EQ, Constant(0.0)), -1.0, 1.0, 0.0) },
            epsilon: Flt64.Zero,
            name: "zero-tolerance"
        );
        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        function.HelperVariables.Should().HaveCount(1);
        IsFeasible(model, Values((x, 0.0), (function.FlagVars[0], 1.0))).Should().BeTrue();
        IsFeasible(model, Values((x, 0.5), (function.FlagVars[0], 1.0))).Should().BeFalse();
    }

    private static void Bounded(RealVar variable) {
        variable.Range.Geq(new Flt64(-100.0));
        variable.Range.Leq(new Flt64(100.0));
    }

    private static LinearInequality<Flt64> Row(
        IVariableItem variable,
        Comparison comparison,
        LinearPolynomial<Flt64> rhs) =>
        new(Polynomial(variable), rhs, comparison);

    private static LinearConstraintInput<Flt64> Input(
        LinearInequality<Flt64> relation,
        double? lower,
        double? upper,
        double rhs) {
        ValueRange<Flt64> range = lower is null || upper is null
            ? ValueRange<Flt64>.Full()
            : ((Ok<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>>)
                ValueRange<Flt64>.Of(new Flt64(lower.Value), new Flt64(upper.Value))).Value;
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input =
            LinearConstraintInput<Flt64>.From(relation, range, new Flt64(rhs), relation.Name);
        input.IsOk.Should().BeTrue();
        return ((Ok<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>>)input).Value;
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

    private static IReadOnlyList<string> ConstraintNames(LinearMechanismModel<Flt64> model) =>
        ((IMechanismModel<Flt64>)model).Constraints
            .Select(constraint => constraint.Name)
            .ToArray();

    private static bool IsFeasible(
        LinearMechanismModel<Flt64> model,
        IReadOnlyDictionary<ISymbol, Flt64> values) {
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
        params (ISymbol Symbol, double Value)[] values) =>
        values.ToDictionary(item => item.Symbol, item => new Flt64(item.Value));
}

/// <summary>测试用满足数量区间入口 / Test entry point for satisfied-count ranges.</summary>
internal static class SatisfiedAmountRangeFixture {
    internal static ValueRange<UInt64> CountRange(int lower, int upper) =>
        SatisfiedAmountRange.CountRange(lower, upper);
}
