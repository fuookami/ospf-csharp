#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class PositivePartGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        foreach (bool quadratic in new[] { false, true }) {
            foreach (double input in new[] { -4.0, -1.0, 0.0, 2.5, 6.0 }) {
                foreach (bool correct in new[] { false, true }) {
                    yield return new object[] { quadratic, input, correct };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Graph_ShouldRejectIncorrectOutputWithEitherModel(bool quadratic, double input, bool correct) {
        var x = new RealVar("positive-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(6)).Value);
        Result<PositivePartFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = PositivePartFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.True(created.IsOk, created.ToString());
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(created.Value);
        using IAbstractLinearMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        double expected = System.Math.Max(input, 0);
        double output = expected + (correct ? 0 : 1);
        Assert.True(parent.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(adapter, output), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "positive-objective",
            displayName: null
        ).IsOk);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved;
        if (quadratic) {
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent);
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<Flt64> model = built.Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            solved = await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)matrix);
        }
        else {
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<Flt64> model = built.Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            solved = await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        }
        if (!correct) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
