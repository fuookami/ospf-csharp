#nullable enable
#if GUROBI_INTEGRATION

using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gurobi;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
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

public class SatisfiedAmountGurobiIntegrationTest {
    [Theory]
    [InlineData(Comparison.LE, -0.3, 1, true, 0.125)]
    [InlineData(Comparison.LE, -0.3, 0, false, 0.125)]
    [InlineData(Comparison.LE, -0.175, 0, true, 0.125)]
    [InlineData(Comparison.LE, -0.175, 1, false, 0.125)]
    [InlineData(Comparison.LE, -0.2375, 0, false, 0.125)]
    [InlineData(Comparison.LE, -0.2375, 1, false, 0.125)]
    [InlineData(Comparison.GE, -0.3, 1, true, 0.125)]
    [InlineData(Comparison.GE, -0.3, 0, false, 0.125)]
    [InlineData(Comparison.GE, -0.425, 0, true, 0.125)]
    [InlineData(Comparison.GE, -0.425, 1, false, 0.125)]
    [InlineData(Comparison.GE, -0.3625, 0, false, 0.125)]
    [InlineData(Comparison.GE, -0.3625, 1, false, 0.125)]
    [InlineData(Comparison.LE, -0.3, 1, true, 0.25)]
    [InlineData(Comparison.LE, -0.04999999999999999, 0, true, 0.25)]
    [InlineData(Comparison.EQ, -0.3, 1, true, 0.25)]
    [InlineData(Comparison.EQ, -0.3, 0, false, 0.25)]
    [InlineData(Comparison.EQ, -0.04999999999999999, 1, true, 0.25)]
    [InlineData(Comparison.EQ, -0.04999999999999999, 0, false, 0.25)]
    [InlineData(Comparison.EQ, -0.55, 1, true, 0.25)]
    [InlineData(Comparison.EQ, -0.55, 0, false, 0.25)]
    [InlineData(Comparison.EQ, 0.2, 0, true, 0.25)]
    [InlineData(Comparison.EQ, 0.2, 1, false, 0.25)]
    [InlineData(Comparison.EQ, -0.8, 0, true, 0.25)]
    [InlineData(Comparison.EQ, -0.8, 1, false, 0.25)]
    [InlineData(Comparison.EQ, 0.075, 0, false, 0.25)]
    [InlineData(Comparison.EQ, 0.075, 1, false, 0.25)]
    [InlineData(Comparison.EQ, -0.675, 0, false, 0.25)]
    [InlineData(Comparison.EQ, -0.675, 1, false, 0.25)]
    public async Task ShiftedIndicator_ShouldSolveWithDeclaredTolerance(
        Comparison sign,
        double input,
        int flag,
        bool expectedFeasible,
        double epsilon) {
        var x = new RealVar("gurobi-residual-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(input)).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(0.3)),
                    Rhs: new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: sign
                ),
            },
            epsilon: new Flt64(epsilon),
            bigM: new Flt64(2)
        );
        if (flag == 1) {
            function.FlagVars[0].Range.SetTrue();
        }
        else {
            function.FlagVars[0].Range.SetFalse();
        }
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        configured.Should().BeTrue("the native feasibility tolerance must be configured");
        if (!expectedFeasible) {
            result.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
            return;
        }
        result.IsOk.Should().BeTrue($"{sign}, x={input}, flag={flag}: {result}");
        RealVar residual = function.ResidualVars[0] ?? throw new System.InvalidOperationException("Missing residual.");
        int residualIndex = matrix.Variables.Single(variable => variable.Origin == residual).Index;
        result.Value.Solution[residualIndex].ToDouble().Should().BeApproximately(input + 0.3, 1e-8);
    }

    [Theory]
    [InlineData(Comparison.LE, -10, 1, true)]
    [InlineData(Comparison.LE, 10, 0, true)]
    [InlineData(Comparison.LE, 10, 1, false)]
    [InlineData(Comparison.GE, -10, 0, true)]
    [InlineData(Comparison.GE, 10, 1, true)]
    [InlineData(Comparison.GE, -10, 1, false)]
    [InlineData(Comparison.EQ, -10, 0, true)]
    [InlineData(Comparison.EQ, 10, 0, true)]
    [InlineData(Comparison.EQ, -0.25, 1, true)]
    [InlineData(Comparison.EQ, -0.25, 0, false)]
    public async Task NestedAutomaticBound_ShouldSolveWithDeclaredTolerance(
        Comparison sign,
        double input,
        int flag,
        bool expectedFeasible) {
        var x = new RealVar("gurobi-nested-bound-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value);
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) },
                new Flt64(0.5)
            ),
            name: "gurobi-nested-bound"
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
                    Rhs: new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: sign
                ),
            },
            epsilon: new Flt64(0.25)
        );
        if (flag == 1) {
            function.FlagVars[0].Range.SetTrue();
        }
        else {
            function.FlagVars[0].Range.SetFalse();
        }
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.AddConstraint(
            new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), new Flt64(input)),
                Comparison: Comparison.EQ
            ),
            null
        ).IsOk.Should().BeTrue();
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

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        configured.Should().BeTrue("the native feasibility tolerance must be configured");
        if (!expectedFeasible) {
            result.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
            return;
        }
        result.IsOk.Should().BeTrue($"{sign}, x={input}, flag={flag}: {result}");
        RealVar residual = function.ResidualVars[0] ?? throw new System.InvalidOperationException("Missing residual.");
        int residualIndex = matrix.Variables.Single(variable => variable.Origin == residual).Index;
        result.Value.Solution[residualIndex].ToDouble().Should().BeApproximately(2 * input + 0.5, 1e-8);
    }

    [Fact]
    public async Task GurobiDump_ShouldRejectStaleInferredBounds() {
        var x = new RealVar("gurobi-stale-bound-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                    Rhs: new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: Comparison.LE
                ),
            },
            epsilon: new Flt64(0.25)
        );
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        var linearSolver = new GurobiLinearSolver();
        var quadraticSolver = new GurobiQuadraticSolver();
        using LinearTriadModel initial = (await linearSolver.DumpAsync(linear)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value);

        (await linearSolver.DumpAsync(linear)).IsFailed.Should().BeTrue();
        (await quadraticSolver.DumpAsync(quadratic)).IsFailed.Should().BeTrue();
        initial.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
    }
}
#endif
