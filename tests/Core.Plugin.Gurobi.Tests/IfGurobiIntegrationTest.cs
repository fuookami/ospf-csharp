#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
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
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class IfGurobiIntegrationTest {
    [Theory]
    [InlineData(-0.5, 0, true)]
    [InlineData(-0.5, 1, false)]
    [InlineData(-0.25, 0, false)]
    [InlineData(-0.25, 1, false)]
    [InlineData(0.0, 0, false)]
    [InlineData(0.0, 1, true)]
    [InlineData(0.5, 0, false)]
    [InlineData(0.5, 1, true)]
    [InlineData(1.0, 0, false)]
    [InlineData(1.0, 1, true)]
    [InlineData(1.25, 0, false)]
    [InlineData(1.25, 1, false)]
    [InlineData(1.5, 0, true)]
    [InlineData(1.5, 1, false)]
    public async Task Interval_ShouldHonorEndpointsAndGaps(double value, int result, bool feasible) {
        var x = new RealVar("ifin-gurobi-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        Result<IfInFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfInFunction<Flt64>.Create(
            x: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        Assert.True(created.IsOk, created.ToString());
        IfInFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, value), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(function.ResultVar, result), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "ifin-gurobi-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(configured);
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
    [InlineData(-0.5, 0)]
    [InlineData(0.0, 1)]
    [InlineData(1.0, 1)]
    [InlineData(1.5, 0)]
    public async Task IntervalFixedColumnSubstitution_ShouldRetainTheResult(double value, int expected) {
        var x = new RealVar("ifin-fixed-column");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        Result<IfInFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfInFunction<Flt64>.Create(
            x: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        Assert.True(created.IsOk, created.ToString());
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(created.Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "ifin-fixed-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(value) }
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

    [Theory]
    [InlineData(Comparison.GT, 0, 0)]
    [InlineData(Comparison.GT, 2, 1)]
    [InlineData(Comparison.GE, 0, 0)]
    [InlineData(Comparison.GE, 2, 1)]
    [InlineData(Comparison.LT, 0, 1)]
    [InlineData(Comparison.LT, 2, 0)]
    [InlineData(Comparison.LE, 0, 1)]
    [InlineData(Comparison.LE, 2, 0)]
    public async Task ConstraintInputFactory_ShouldSolveNormalizedRelation(Comparison relation, double value, int expected) {
        var x = new RealVar("if-input-gurobi-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var inequality = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(new Flt64(2), x)], Flt64.One),
            Rhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], new Flt64(2)),
            Comparison: relation
        );
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input = LinearConstraintInput<Flt64>.From(
            relation: inequality,
            lhsRange: ValueRange<Flt64>.Full(),
            rhsConstant: new Flt64(2)
        );
        Assert.True(input.IsOk, input.ToString());
        Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.From(
            inequality: input.Value,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        Assert.True(created.IsOk, created.ToString());
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(created.Value).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, value), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(Flt64.One, created.Value)], Flt64.Zero),
            name: "if-input-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    public static IEnumerable<object[]> Cases() {
        foreach (Comparison relation in new[] { Comparison.GT, Comparison.GE, Comparison.LT, Comparison.LE }) {
            foreach (double value in new[] { -0.5, -0.25, 0.0, 0.25, 0.5 }) {
                for (int result = 0; result <= 1; ++result) {
                    yield return new object[] { relation, value, result };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FixedInputAndResult_ShouldHonorThreeValuedBoundary(Comparison relation, double value, int result) {
        var x = new RealVar("if-gurobi-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, value), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(function.ResultVar, result), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "if-gurobi-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(configured);
        TruthValue expected = ConditionClassification.Classify(
            difference: new Flt64(value),
            relation: relation,
            strictBoundary: new Flt64(0.5)
        ).Value;
        bool feasible = expected != TruthValue.Undefined && result == (expected == TruthValue.True ? 1 : 0);
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
        Assert.InRange(
            actual: solved.Value.Solution[matrix.Variables.Single(column => column.Origin == function.ConditionVar).Index].ToDouble(),
            low: value - 1e-7,
            high: value + 1e-7
        );
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task NestedInput_ShouldHonorThreeValuedBoundary(Comparison relation, double value, int result) {
        var x = new RealVar("if-nested-gurobi-x");
        var y = new RealVar("if-nested-gurobi-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, Flt64.One),
            name: "if-nested-inner"
        );
        var outer = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner), new LinearMonomial<Flt64>(Flt64.One, y) },
                new Flt64(-1)),
            name: "if-nested-outer"
        );
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, outer) }, new Flt64(-1)
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, (value - 0.5) / 4), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, 0.5), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(function.ResultVar, result), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "if-nested-gurobi-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(configured);
        TruthValue expected = ConditionClassification.Classify(
            difference: new Flt64(value),
            relation: relation,
            strictBoundary: new Flt64(0.5)
        ).Value;
        bool feasible = expected != TruthValue.Undefined && result == (expected == TruthValue.True ? 1 : 0);
        if (!feasible) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Solution[matrix.Variables.Single(column => column.Origin == function.ConditionVar).Index].ToDouble(),
            low: value - 1e-7,
            high: value + 1e-7
        );
    }

    [Fact]
    public async Task NestedInputFixedColumnSubstitution_ShouldRetainTheCorrectBranch() {
        var x = new RealVar("if-nested-fixed-column");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, Flt64.One),
            name: "if-nested-fixed-inner"
        );
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, new Flt64(-2)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        );
        Assert.True(created.IsOk, created.ToString());
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(created.Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "if-nested-fixed-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
        );
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
    }

    [Fact]
    public async Task FixedColumnSubstitution_ShouldRetainTheCorrectBranch() {
        var x = new RealVar("if-fixed-column");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(0.5)
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "if-fixed-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.5) }
        );
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
    }

    [Fact]
    public async Task FreeInput_ShouldOptimizeTheConditionResult() {
        var x = new RealVar("if-free");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "if-free-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
        double input = solved.Value.Solution[matrix.Variables.Single(column => column.Origin == x).Index].ToDouble();
        Assert.InRange(
            actual: input,
            low: 1 - 1e-7,
            high: 2 + 1e-7
        );
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
