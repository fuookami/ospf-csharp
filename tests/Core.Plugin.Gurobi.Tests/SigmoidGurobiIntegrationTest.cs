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
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class SigmoidGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        foreach (Comparison relation in new[] { Comparison.GT, Comparison.GE, Comparison.LT, Comparison.LE }) {
            foreach (double input in new[] { -0.5, -0.25, 0.0, 0.25, 0.5 }) {
                for (int result = 0; result <= 1; ++result) {
                    yield return new object[] { relation, input, result };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FixedInputAndResult_ShouldHonorThreeValuedBoundary(Comparison relation, double input, int result) {
        var x = new RealVar("sigmoid-gurobi-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        Result<SigmoidFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        SigmoidFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(function.IndicatorVar, result), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "sigmoid-objective",
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
        TruthValue expected = function.Classify(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) }).Value;
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
    }

    [Theory]
    [InlineData(-1.0, 0)]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.0, 1)]
    public async Task FixedColumnSubstitution_ShouldRetainTheResult(double input, int expected) {
        var x = new RealVar("sigmoid-fixed-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "sigmoid-fixed-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
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
    [InlineData(-1.0, 1)]
    [InlineData(0.0, 0)]
    public async Task NestedAbsoluteValue_ShouldParticipateInTheSolve(double input, int expected) {
        var x = new RealVar("sigmoid-nested-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var xPolynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero
        );
        var abs = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(xPolynomial, IIntoValue<Flt64>.Identity));
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, abs) }, new Flt64(-0.5)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.Add(abs).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "sigmoid-nested-objective",
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

    [Theory]
    [InlineData(-1.0, 0)]
    [InlineData(1.0, 1)]
    public async Task SigmoidAsNestedCondition_ShouldParticipateInTheSolve(double input, int expected) {
        var x = new RealVar("sigmoid-consumer-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var inner = new LinearFunctionSymbolAdapter<Flt64>(function);
        IfFunction<Flt64> consumer = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(consumer);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, outer) }, Flt64.Zero),
            name: "sigmoid-consumer-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(expected), model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Assert.Contains(matrix.Variables, variable => variable.Origin == x);
        Assert.Contains(matrix.Variables, variable => variable.Origin == function.IndicatorVar);
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    [Fact]
    public async Task CachedNestedBranch_ShouldNotBeTreatedAsALinearRelaxationSolution() {
        var x = new RealVar("sigmoid-relaxation-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        SigmoidFunction<Flt64> producer = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var inner = new LinearFunctionSymbolAdapter<Flt64>(producer);
        IfFunction<Flt64> consumer = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(consumer);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 1), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, outer) }, Flt64.Zero),
            name: "sigmoid-relaxation-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        using LinearTriadModel relaxed = matrix.RelaxToContinuous().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)relaxed);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: -1e-7,
            high: 1e-7
        );
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
