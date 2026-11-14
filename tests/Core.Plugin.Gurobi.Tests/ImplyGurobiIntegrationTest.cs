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

public class ImplyGurobiIntegrationTest {
    [Theory]
    [InlineData(Comparison.GT, -1, 0.25, true)]
    [InlineData(Comparison.GT, 0, 0, true)]
    [InlineData(Comparison.GT, 0.25, 1, false)]
    [InlineData(Comparison.GT, 0.5, 0.25, false)]
    [InlineData(Comparison.GT, 0.5, 0, false)]
    [InlineData(Comparison.GT, 0.5, 0.5, true)]
    [InlineData(Comparison.GE, -1, -0.25, true)]
    [InlineData(Comparison.GE, -0.25, 0, false)]
    [InlineData(Comparison.GE, 0, 0, true)]
    [InlineData(Comparison.GE, 0, -0.5, false)]
    [InlineData(Comparison.LT, 0, -0.25, true)]
    [InlineData(Comparison.LT, -0.25, -1, false)]
    [InlineData(Comparison.LT, -0.5, -0.5, true)]
    [InlineData(Comparison.LT, -0.5, 0, false)]
    [InlineData(Comparison.LE, 1, 0.25, true)]
    [InlineData(Comparison.LE, 0.25, 0, false)]
    [InlineData(Comparison.LE, 0, 0, true)]
    [InlineData(Comparison.LE, 0, 0.5, false)]
    public async Task FixedInputs_ShouldEnforceImplicationAndReturnOne(
        Comparison relation,
        double antecedent,
        double consequent,
        bool feasible) {
        RealVar x = Real("imply-gurobi-x");
        RealVar y = Real("imply-gurobi-y");
        ImplyFunction<Flt64> function = ImplyFunction<Flt64>.Create(
            antecedent: Polynomial(x),
            consequent: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, antecedent), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, consequent), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "imply-gurobi-objective",
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
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
    }

    [Theory]
    [InlineData(-1, 0.25, true)]
    [InlineData(0, 0, true)]
    [InlineData(0.5, 0, false)]
    [InlineData(0.5, 0.5, true)]
    public async Task FixedColumns_ShouldPreserveTheGate(double antecedent, double consequent, bool feasible) {
        RealVar x = Real("imply-fixed-x");
        RealVar y = Real("imply-fixed-y");
        ImplyFunction<Flt64> function = ImplyFunction<Flt64>.Create(
            antecedent: Polynomial(x),
            consequent: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "imply-fixed-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new(antecedent), [y] = new(consequent) }
        );
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
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
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
    }

    private static RealVar Real(string name) {
        var variable = new RealVar(name);
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        return variable;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: Polynomial(symbol),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
