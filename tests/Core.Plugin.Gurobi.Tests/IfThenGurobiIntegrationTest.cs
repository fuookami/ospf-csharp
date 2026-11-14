#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class IfThenGurobiIntegrationTest {
    [Theory]
    [InlineData(-1, -3, 0, true)]
    [InlineData(-1, 4, 0, true)]
    [InlineData(0, -3, 0, true)]
    [InlineData(0, 4, 4, false)]
    [InlineData(0.25, -3, 0, false)]
    [InlineData(0.25, 4, 4, false)]
    [InlineData(0.5, -3, -3, true)]
    [InlineData(0.5, 4, 4, true)]
    [InlineData(1, 0, 0, true)]
    [InlineData(1, -3, 0, false)]
    [InlineData(1, 4, 0, false)]
    public async Task FixedInputAndResult_ShouldHonorConditionalValue(double xValue, double yValue, double result, bool feasible) {
        RealVar x = Real("ifthen-gurobi-x", -2, 2);
        RealVar y = Real("ifthen-gurobi-y", -3, 4);
        IfThenFunction<Flt64> function = Create(x, y);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, xValue), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, yValue), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(function.ResultVar, result), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "ifthen-gurobi-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        if (!feasible) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: result - 1e-7,
            high: result + 1e-7
        );
    }

    [Theory]
    [InlineData(-1, -3, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(0.5, -3, -3)]
    [InlineData(1, 4, 4)]
    public async Task FixedColumns_ShouldPreserveConditionalValue(double xValue, double yValue, double expected) {
        RealVar x = Real("ifthen-fixed-x", -2, 2);
        RealVar y = Real("ifthen-fixed-y", -3, 4);
        IfThenFunction<Flt64> function = Create(x, y);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "ifthen-fixed-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(xValue), [y] = new Flt64(yValue) }
        );
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    private static RealVar Real(string name, double lower, double upper) {
        var variable = new RealVar(name);
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(lower), new Flt64(upper)).Value);
        return variable;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static IfThenFunction<Flt64> Create(ISymbol x, ISymbol y) => IfThenFunction<Flt64>.Create(
        condition: Polynomial(x),
        then: Polynomial(y),
        converter: IIntoValue<Flt64>.Identity,
        strictBoundary: new Flt64(0.5)
    ).Value;

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: Polynomial(symbol),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
