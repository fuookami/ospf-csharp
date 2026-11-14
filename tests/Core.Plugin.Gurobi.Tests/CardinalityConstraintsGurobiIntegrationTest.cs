#nullable enable
#if GUROBI_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class CardinalityConstraintsGurobiIntegrationTest {
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, false, 3)]
    [InlineData(false, false, 4)]
    [InlineData(false, false, 5)]
    [InlineData(false, false, 6)]
    [InlineData(false, false, 7)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, false, 3)]
    [InlineData(true, false, 4)]
    [InlineData(true, false, 5)]
    [InlineData(true, false, 6)]
    [InlineData(true, false, 7)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    [InlineData(true, true, 3)]
    public async Task BinaryAssignments_ShouldMatchTheRequestedCount(
        bool exactly,
        bool repeated,
        int mask) {
        var x = new BinVar("x");
        var y = new BinVar("y");
        var z = new BinVar("z");
        IVariableItem[] variables = repeated ? new IVariableItem[] { x, y } : new IVariableItem[] { x, y, z };
        IVariableItem[] indicators = repeated ? new IVariableItem[] { x, y, x } : variables;
        int amount = repeated ? 2 : 1;
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> created = exactly
            ? CardinalityConstraints.Exactly(
                indicators: indicators,
                amount: amount,
                converter: IIntoValue<Flt64>.Identity
            )
            : CardinalityConstraints.AtMost(
                indicators: indicators,
                limit: amount,
                converter: IIntoValue<Flt64>.Identity
            );
        Assert.True(created.IsOk, created.ToString());
        using var parent = new LinearMetaModel<Flt64>();
        for (int index = 0; index < variables.Length; ++index) {
            Assert.True(parent.Add(variables[index]).IsOk);
            Assert.True(parent.AddConstraint(
                relation: Fixed(variables[index], (mask >> index) & 1),
                group: null
            ).IsOk);
        }
        Assert.True(parent.AddConstraint(created.Value[0], group: null).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        int count = ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0) +
            (repeated ? ((mask & 1) != 0 ? 1 : 0) : ((mask & 4) != 0 ? 1 : 0));
        bool feasible = exactly ? count == amount : count <= amount;
        if (feasible) {
            Assert.True(solved.IsOk, solved.ToString());
        }
        else {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
        }
    }

    private static LinearInequality<Flt64> Fixed(IVariableItem variable, int value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
