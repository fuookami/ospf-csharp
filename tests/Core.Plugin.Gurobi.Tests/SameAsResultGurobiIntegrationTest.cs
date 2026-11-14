#nullable enable
#if GUROBI_INTEGRATION

using System;
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
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>相同状态结果的原生求解回归 / Native solver regression for same-status results.</summary>
public class SameAsResultGurobiIntegrationTest {
    [Theory]
    [InlineData(1, true, 0, 1, -1, true)]
    [InlineData(1, true, 0, 0, -1, false)]
    [InlineData(1, false, 0, 1, -1, true)]
    [InlineData(1, false, 1, 0, -1, false)]
    [InlineData(3, true, 0, 1, -1, true)]
    [InlineData(3, true, 7, 1, -1, true)]
    [InlineData(3, true, 0, 0, -1, false)]
    [InlineData(3, true, 7, 0, -1, false)]
    [InlineData(3, true, 1, 1, -1, false)]
    [InlineData(3, true, 1, 0, -1, false)]
    [InlineData(3, false, 0, 1, -1, true)]
    [InlineData(3, false, 7, 1, -1, true)]
    [InlineData(3, false, 0, 0, -1, false)]
    [InlineData(3, false, 1, 0, -1, true)]
    [InlineData(3, false, 6, 0, -1, true)]
    [InlineData(3, false, 2, 0, -1, true)]
    [InlineData(3, false, 1, 1, -1, false)]
    [InlineData(3, false, 1, 0, 0, false)]
    [InlineData(3, false, 0, 1, 1, false)]
    public async Task SelectedAssignments_ShouldMatchResultAndDifferenceVariables(
        int count,
        bool constraint,
        int inputMask,
        int expectedResult,
        int forcedDifference,
        bool feasible) {
        RealVar[] inputs = Enumerable.Range(0, count).Select(index => new RealVar($"same-input-{index}")).ToArray();
        foreach (RealVar input in inputs) {
            input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        }
        var function = new SameAsFunction(
            inequalities: inputs.Select(input => Polynomial(input).Le(Flt64.Zero)).ToArray(),
            epsilon: new Flt64(0.125),
            constraint: constraint,
            bigM: new Flt64(4)
        );
        using var parent = new LinearMetaModel<Flt64>();
        foreach (RealVar input in inputs) {
            parent.Add(input).IsOk.Should().BeTrue();
        }
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        for (int index = 0; index < count; ++index) {
            Fix(
                model: parent,
                variable: inputs[index],
                value: new Flt64((inputMask & (1 << index)) != 0 ? -1 : 1)
            );
        }
        Fix(
            model: parent,
            variable: function.ResultVar,
            value: new Flt64(expectedResult)
        );
        if (forcedDifference >= 0) {
            Fix(
                model: parent,
                variable: function.DiffVars[0],
                value: new Flt64(forcedDifference)
            );
        }
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
        if (!feasible) {
            solved.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
            return;
        }
        solved.IsOk.Should().BeTrue($"constraint={constraint}, inputs={inputMask}, result={expectedResult}: {solved}");
        for (int index = 0; index < count; ++index) {
            int flagIndex = matrix.Variables.Single(variable => variable.Origin == function.SatisfactionFlags[index]).Index;
            solved.Value.Solution[flagIndex].ToDouble().Should().BeApproximately((inputMask >> index) & 1, 1e-8);
            if (index > 0 && !constraint) {
                int differenceIndex = matrix.Variables.Single(variable => variable.Origin == function.DiffVars[index - 1]).Index;
                int expectedDifference = ((inputMask >> index) ^ inputMask) & 1;
                solved.Value.Solution[differenceIndex].ToDouble().Should().BeApproximately(expectedDifference, 1e-8);
            }
        }
    }

    internal static void Fix(LinearMetaModel<Flt64> model, IVariableItem variable, Flt64 value) {
        model.AddConstraint(
            new LinearInequality<Flt64>(
                Lhs: Polynomial(variable),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), value),
                Comparison: Comparison.EQ
            ),
            null
        ).IsOk.Should().BeTrue();
    }

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );
}
#endif
