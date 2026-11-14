#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gurobi;
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
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>相同状态端点的原生求解回归 / Native solver regressions for same-status boundaries.</summary>
public class SameAsBoundaryGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        foreach ((Comparison comparison, double difference, int expected) in new[] {
            (Comparison.LE, -0.5, 1),
            (Comparison.LE, 0.125, 1),
            (Comparison.LE, 0.1875, -1),
            (Comparison.LE, 0.25, 0),
            (Comparison.GE, 0.5, 1),
            (Comparison.GE, -0.125, 1),
            (Comparison.GE, -0.1875, -1),
            (Comparison.GE, -0.25, 0),
            (Comparison.EQ, 0.125, 1),
            (Comparison.EQ, -0.125, 1),
            (Comparison.EQ, 0.1875, -1),
            (Comparison.EQ, -0.1875, -1),
            (Comparison.EQ, 0.25, 0),
            (Comparison.EQ, -0.25, 0),
        }) {
            for (int shape = 0; shape < 3; ++shape) {
                for (int flag = 0; flag <= 1; ++flag) {
                    yield return new object[] { comparison, difference, expected, shape, flag };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task BoundaryAndGap_ShouldMatchNativeSolver(
        Comparison comparison,
        double difference,
        int expected,
        int shape,
        int flag) {
        var x = new RealVar("same-native-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        ISymbol symbol = x;
        double offset = shape == 0 ? 0 : 0.375;
        if (shape == 2) {
            symbol = new LinearExpressionSymbol(
                polynomial: new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                    new Flt64(offset)
                ),
                name: "same-native-nested"
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
            bigM: shape == 1 ? new Flt64(4) : null
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        SameAsResultGurobiIntegrationTest.Fix(
            model: parent,
            variable: x,
            value: new Flt64(difference - offset)
        );
        SameAsResultGurobiIntegrationTest.Fix(
            model: parent,
            variable: function.SatisfactionFlags[0],
            value: new Flt64(flag)
        );
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue($"model construction: {built}");
        using LinearMechanismModel<Flt64> mechanism = built.Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        configured.Should().BeTrue();
        if (expected != flag) {
            solved.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
            return;
        }
        solved.IsOk.Should().BeTrue($"{comparison}, difference={difference}, shape={shape}, flag={flag}: {solved}");
        if (function.ResidualVars[0] is { } residual) {
            Value(
                matrix: matrix,
                output: solved.Value,
                variable: residual
            ).Should().BeApproximately(difference, 1e-8);
        }
        if (comparison == Comparison.EQ) {
            Value(
                matrix: matrix,
                output: solved.Value,
                variable: function.EqPositiveVars[0]!
            ).Should().BeApproximately(flag == 0 && difference > 0 ? 1 : 0, 1e-8);
            Value(
                matrix: matrix,
                output: solved.Value,
                variable: function.EqNegativeVars[0]!
            ).Should().BeApproximately(flag == 0 && difference < 0 ? 1 : 0, 1e-8);
        }
        else {
            Value(
                matrix: matrix,
                output: solved.Value,
                variable: function.ViolationFlags[0]!
            ).Should().BeApproximately(1 - flag, 1e-8);
        }
    }

    private static double Value(LinearTriadModel matrix, FeasibleSolverOutput<Flt64> output, IVariableItem variable) {
        int index = matrix.Variables.Single(column => column.Origin == variable).Index;
        return output.Solution[index].ToDouble();
    }
}
#endif
