#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class CardinalityFunctionGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        foreach (bool exactly in new[] { false, true }) {
            foreach (bool repeated in new[] { false, true }) {
                for (int mask = 0; mask < (repeated ? 4 : 8); ++mask) {
                    for (int output = 0; output <= 1; ++output) {
                        yield return new object[] { exactly, repeated, mask, output };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FixedInputsAndDownstreamResult_ShouldEnforceTheTruthTable(
        bool exactly,
        bool repeated,
        int mask,
        int output) {
        var x = new BinVar("x");
        var y = new BinVar("y");
        var z = new BinVar("z");
        IVariableItem[] variables = repeated ? new IVariableItem[] { x, y } : new IVariableItem[] { x, y, z };
        IVariableItem[] indicators = repeated ? new IVariableItem[] { x, y, x } : variables;
        int amount = repeated ? 2 : 1;
        IMathFunctionSymbol<Flt64> function = exactly
            ? ExactlyFunction<Flt64>.Create(
                indicators: indicators,
                amount: amount,
                converter: IIntoValue<Flt64>.Identity
            ).Value
            : AtMostFunction<Flt64>.Create(
                indicators: indicators,
                limit: amount,
                converter: IIntoValue<Flt64>.Identity
            ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        for (int i = 0; i < variables.Length; ++i) {
            Assert.True(parent.Add(variables[i]).IsOk);
            Assert.True(parent.AddConstraint(relation: Fixed(variables[i], (mask >> i) & 1), group: null).IsOk);
        }
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(adapter, output), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "cardinality-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        int count = ((mask & 1) != 0 ? 1 : 0) + ((mask & 2) != 0 ? 1 : 0) +
            (repeated ? ((mask & 1) != 0 ? 1 : 0) : ((mask & 4) != 0 ? 1 : 0));
        bool expected = exactly ? count == amount : count <= amount;
        if (output != (expected ? 1 : 0)) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: output - 1e-7,
            high: output + 1e-7
        );
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, int value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
