#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>相同状态指示行的端点回归 / Same-status indicator-row boundary regressions.</summary>
public class SameAsBoundaryRowsTest {
    [Theory]
    [InlineData(Comparison.LE, 0, false)]
    [InlineData(Comparison.LE, 1, false)]
    [InlineData(Comparison.LE, 2, false)]
    [InlineData(Comparison.LE, 0, true)]
    [InlineData(Comparison.LE, 1, true)]
    [InlineData(Comparison.LE, 2, true)]
    [InlineData(Comparison.GE, 0, false)]
    [InlineData(Comparison.GE, 1, false)]
    [InlineData(Comparison.GE, 2, false)]
    [InlineData(Comparison.GE, 0, true)]
    [InlineData(Comparison.GE, 1, true)]
    [InlineData(Comparison.GE, 2, true)]
    [InlineData(Comparison.EQ, 0, false)]
    [InlineData(Comparison.EQ, 1, false)]
    [InlineData(Comparison.EQ, 2, false)]
    [InlineData(Comparison.EQ, 0, true)]
    [InlineData(Comparison.EQ, 1, true)]
    [InlineData(Comparison.EQ, 2, true)]
    public async Task RowsAndMatrix_ShouldAcceptOnlyTheCorrectBranch(
        Comparison comparison,
        int shape,
        bool explicitBigM) {
        var x = new RealVar("same-rows-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        ISymbol symbol = x;
        double offset = shape == 0 ? 0 : 0.375;
        if (shape == 2) {
            symbol = new LinearExpressionSymbol(
                polynomial: new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                    new Flt64(offset)
                ),
                name: "same-rows-nested"
            );
        }
        var function = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
                        new Flt64(shape == 1 ? offset : 0)
                    ),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(0.125),
            constraint: false,
            bigM: explicitBigM ? new Flt64(4) : null
        );
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;

        foreach (double difference in new[] { -1, -0.25, -0.1875, -0.125, 0, 0.125, 0.1875, 0.25, 1 }) {
            int expectedFlag = ExpectedFlag(comparison, difference);
            Flt64? evaluated = function.Evaluate(new Dictionary<ISymbol, Flt64> {
                [symbol] = new Flt64(shape == 2 ? difference : difference - offset),
            });
            Assert.Equal(expectedFlag < 0 ? null : Flt64.One, evaluated);
            int sideCount = comparison == Comparison.EQ ? 2 : 1;
            for (int flag = 0; flag <= 1; ++flag) {
                for (int sideMask = 0; sideMask < 1 << sideCount; ++sideMask) {
                    for (int result = 0; result <= 1; ++result) {
                        var values = new Dictionary<IVariableItem, Flt64> {
                            [x] = new Flt64(difference - offset),
                            [function.SatisfactionFlags[0]] = new Flt64(flag),
                            [function.ResultVar] = new Flt64(result),
                        };
                        if (function.ResidualVars[0] is { } residual) {
                            values[residual] = new Flt64(difference);
                        }
                        bool sideCorrect;
                        if (comparison == Comparison.EQ) {
                            values[function.EqPositiveVars[0]!] = new Flt64(sideMask & 1);
                            values[function.EqNegativeVars[0]!] = new Flt64((sideMask >> 1) & 1);
                            sideCorrect = flag == 1 ? sideMask == 0 : sideMask == (difference > 0 ? 1 : 2);
                        }
                        else {
                            values[function.ViolationFlags[0]!] = new Flt64(sideMask);
                            sideCorrect = sideMask == 1 - flag;
                        }
                        bool expected = expectedFlag == flag && sideCorrect && result == 1;
                        bool mechanismFeasible = SatisfiedAmountResidualTest.ExactFeasible(model, values);
                        bool matrixFeasible = SatisfiedAmountResidualTest.ExactFeasible(matrix, values);
                        Assert.True(expected == mechanismFeasible && expected == matrixFeasible,
                            $"{comparison}, shape={shape}, explicit={explicitBigM}, d={difference}, " +
                            $"flag={flag}, side={sideMask}, result={result}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0, 0.25)]
    [InlineData(0.125, 0.125)]
    [InlineData(0.125, 0.1)]
    [InlineData(0.125, double.NaN)]
    [InlineData(0.125, double.PositiveInfinity)]
    public async Task InvalidBoundaries_ShouldReturnNullAndNotWriteRows(double epsilon, double strict) {
        var x = new RealVar("same-invalid-x");
        var function = new SameAsFunction(
            inequalities: new[] { Polynomial(x).Le(Flt64.Zero) },
            epsilon: new Flt64(epsilon),
            bigM: new Flt64(2),
            strictBoundary: new Flt64(strict)
        );
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        foreach (IVariableItem helper in function.HelperVariables) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(
            relation: Polynomial(x).Le(Flt64.One),
            from: (IIntermediateSymbol?)null
        ).IsOk);

        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    public async Task MissingResidual_ShouldNotWriteEarlierRows(Comparison comparison) {
        var x = new RealVar("same-missing-residual-x");
        var function = new SameAsFunction(
            inequalities: new[] {
                Polynomial(x).Le(Flt64.Zero),
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                        new Flt64(0.375)
                    ),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(4)
        );
        RealVar residual = Assert.IsType<RealVar>(function.ResidualVars[1]);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        foreach (IVariableItem helper in function.HelperVariables.Where(helper => helper.Key != residual.Key)) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(
            relation: Polynomial(x).Le(Flt64.One),
            from: (IIntermediateSymbol?)null
        ).IsOk);

        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Theory]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.EQ, false)]
    [InlineData(Comparison.LE, true)]
    [InlineData(Comparison.GE, true)]
    [InlineData(Comparison.EQ, true)]
    public async Task AdjacentBoundariesWithLargeM_ShouldKeepActiveThresholdsExact(Comparison comparison, bool subnormal) {
        var x = new RealVar("same-adjacent-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        double epsilon = subnormal ? double.Epsilon : 0.125;
        double strict = System.Math.BitIncrement(epsilon);
        var function = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: Polynomial(x),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(epsilon),
            constraint: false,
            bigM: new Flt64(1e16),
            strictBoundary: new Flt64(strict)
        );
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        double[] signs = comparison == Comparison.EQ ? new[] { -1.0, 1.0 } :
            new[] { comparison == Comparison.GE ? -1.0 : 1.0 };
        foreach (double sign in signs) {
            foreach (int expectedFlag in new[] { 0, 1 }) {
                double value = sign * (expectedFlag == 1 ? epsilon : strict);
                foreach (int flag in new[] { 0, 1 }) {
                    var values = new Dictionary<IVariableItem, Flt64> {
                        [x] = new Flt64(value),
                        [function.SatisfactionFlags[0]] = new Flt64(flag),
                        [function.ResultVar] = Flt64.One,
                    };
                    if (comparison == Comparison.EQ) {
                        values[function.EqPositiveVars[0]!] = new Flt64(flag == 0 && sign > 0 ? 1 : 0);
                        values[function.EqNegativeVars[0]!] = new Flt64(flag == 0 && sign < 0 ? 1 : 0);
                    }
                    else {
                        values[function.ViolationFlags[0]!] = new Flt64(1 - flag);
                    }
                    bool expected = flag == expectedFlag;
                    Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                    Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
                }
            }
        }
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    public async Task Relaxation_ShouldRoundUpAndRetainDomainEndpoints(Comparison comparison) {
        var x = new RealVar("same-round-up-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var function = new SameAsFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: Polynomial(x),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(double.Epsilon),
            constraint: false
        );
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double value in new[] { -1.0, 1.0 }) {
            int flag = comparison switch {
                Comparison.LE => value < 0 ? 1 : 0,
                Comparison.GE => value > 0 ? 1 : 0,
                _ => 0,
            };
            var values = new Dictionary<IVariableItem, Flt64> {
                [x] = new Flt64(value),
                [function.SatisfactionFlags[0]] = new Flt64(flag),
                [function.ResultVar] = Flt64.One,
            };
            if (comparison == Comparison.EQ) {
                values[function.EqPositiveVars[0]!] = new Flt64(value > 0 ? 1 : 0);
                values[function.EqNegativeVars[0]!] = new Flt64(value < 0 ? 1 : 0);
            }
            else {
                values[function.ViolationFlags[0]!] = new Flt64(1 - flag);
            }
            Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, values));
            Assert.True(SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
        }
    }

    [Theory]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.LE, true)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.GE, true)]
    [InlineData(Comparison.EQ, false)]
    [InlineData(Comparison.EQ, true)]
    public async Task LaterInexactCoefficientMerge_ShouldNotWriteEarlierRows(Comparison comparison, bool shifted) {
        var x = new RealVar("same-inexact-coefficient-x");
        var function = new SameAsFunction(
            inequalities: new[] {
                Polynomial(x).Le(Flt64.Zero),
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] {
                            new LinearMonomial<Flt64>(Flt64.One, x),
                            new LinearMonomial<Flt64>(new Flt64(System.Math.Pow(2, -54)), x),
                        },
                        shifted ? new Flt64(0.375) : Flt64.Zero
                    ),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(4)
        );
        await AssertRegistrationFailsWithoutWriting(function, x);
    }

    [Theory]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.EQ, false)]
    [InlineData(Comparison.EQ, true)]
    public async Task LaterMissingBranchFlag_ShouldNotWriteEarlierRows(Comparison comparison, bool negative) {
        var x = new RealVar("same-missing-branch-x");
        var function = new SameAsFunction(
            inequalities: new[] {
                Polynomial(x).Le(Flt64.Zero),
                new LinearInequality<Flt64>(
                    Lhs: Polynomial(x),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: comparison
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(4)
        );
        IVariableItem? missing = comparison == Comparison.EQ
            ? negative ? function.EqNegativeVars[1] : function.EqPositiveVars[1]
            : function.ViolationFlags[1];
        Assert.NotNull(missing);
        await AssertRegistrationFailsWithoutWriting(
            function: function,
            input: x,
            missing: missing
        );
    }

    [Fact]
    public async Task LaterInexactConstantDifference_ShouldNotWriteEarlierRows() {
        var x = new RealVar("same-inexact-constant-x");
        var function = new SameAsFunction(
            inequalities: new[] {
                Polynomial(x).Le(Flt64.Zero),
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                        new Flt64(1e16)
                    ),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                    Comparison: Comparison.EQ
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(1e16)
        );
        await AssertRegistrationFailsWithoutWriting(function, x);
    }

    [Fact]
    public async Task OverflowingRelaxation_ShouldNotWriteRows() {
        var x = new RealVar("same-overflow-x");
        var function = new SameAsFunction(
            inequalities: new[] { Polynomial(x).Le(Flt64.Zero) },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(1e308),
            strictBoundary: new Flt64(1e308)
        );
        await AssertRegistrationFailsWithoutWriting(function, x);
    }

    private static async Task AssertRegistrationFailsWithoutWriting(
        SameAsFunction function,
        RealVar input,
        IVariableItem? missing = null) {
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(input).IsOk);
        foreach (IVariableItem helper in function.HelperVariables.Where(helper => helper.Key != missing?.Key)) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(
            relation: Polynomial(input).Le(Flt64.One),
            from: (IIntermediateSymbol?)null
        ).IsOk);

        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
    }

    private static int ExpectedFlag(Comparison comparison, double difference) => comparison switch {
        Comparison.LE => difference <= 0.125 ? 1 : difference >= 0.25 ? 0 : -1,
        Comparison.GE => difference >= -0.125 ? 1 : difference <= -0.25 ? 0 : -1,
        Comparison.EQ => System.Math.Abs(difference) <= 0.125 ? 1 : System.Math.Abs(difference) >= 0.25 ? 0 : -1,
        _ => -1,
    };

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );
}
