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

/// <summary>乘积函数原生求解回归 / Native solver regressions for product functions.</summary>
public class ProductGurobiIntegrationTest {
    [Theory]
    [InlineData(2, 9, 0)]
    [InlineData(6, 4, 16)]
    public async Task Products_ShouldReachQuadraticObjectiveAndConstraint(double target, double limit, double expected) {
        var x = new RealVar("product-gurobi-x");
        var y = new RealVar("product-gurobi-y");
        x.Range.Geq(Flt64.Zero);
        x.Range.Leq(new Flt64(4));
        y.Range.Geq(Flt64.Zero);
        y.Range.Leq(new Flt64(4));
        var objectiveFactor = new LinearPolynomial<Flt64>(
            [new LinearMonomial<Flt64>(Flt64.One, x), new LinearMonomial<Flt64>(Flt64.One, y)],
            new Flt64(-target)
        );
        ProductFunction<Flt64> objective = ProductFunction<Flt64>.Create(
            left: objectiveFactor,
            right: objectiveFactor,
            converter: IIntoValue<Flt64>.Identity,
            name: "product-objective"
        ).Value;
        ProductFunction<Flt64> bounded = ProductFunction<Flt64>.Create(
            left: Linear(x, 1),
            right: Linear(y, 1),
            converter: IIntoValue<Flt64>.Identity,
            name: "product-bound"
        ).Value;
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Add(objective).IsOk.Should().BeTrue();
        parent.Add(bounded).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: new LinearInequality<Flt64>(Linear(x, 0), Linear(y, 0), Comparison.EQ),
            group: null
        ).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>([new QuadraticMonomial<Flt64>(Flt64.One, bounded)], Flt64.Zero),
                Rhs: new QuadraticPolynomial<Flt64>([], new Flt64(limit)),
                Comparison: Comparison.LE
            ),
            group: null
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(Flt64.One, objective)], Flt64.Zero),
            name: "product-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;
        bool configured = false;
        GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.IntParam.NonConvex, 2);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiQuadraticSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((IQuadraticTetradModelView)matrix);
        configured.Should().BeTrue();
        solved.IsOk.Should().BeTrue();
        int xi = matrix.Variables.Single(column => column.Origin == x).Index;
        int yi = matrix.Variables.Single(column => column.Origin == y).Index;
        solved.Value.Solution[xi].ToDouble().Should().BeApproximately(1, 1e-4);
        solved.Value.Solution[yi].ToDouble().Should().BeApproximately(1, 1e-4);
        solved.Value.Obj.ToDouble().Should().BeApproximately(expected, 1e-4);
    }

    private static LinearPolynomial<Flt64> Linear(ISymbol symbol, double constant) => new(
        [new LinearMonomial<Flt64>(Flt64.One, symbol)],
        new Flt64(constant)
    );
}
#endif
