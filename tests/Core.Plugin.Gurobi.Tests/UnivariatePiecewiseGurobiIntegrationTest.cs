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
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class UnivariatePiecewiseGurobiIntegrationTest {
    [Theory]
    [InlineData(-0.5, 0.0, false)]
    [InlineData(0.0, 0.0, true)]
    [InlineData(0.5, 1.0, true)]
    [InlineData(1.0, 2.0, true)]
    [InlineData(1.0, 3.0, true)]
    [InlineData(1.0, 2.5, false)]
    [InlineData(1.5, 2.0, true)]
    [InlineData(2.0, 1.0, true)]
    [InlineData(2.5, 0.0, false)]
    public async Task FixedInputAndResult_ShouldMatchClosedSegmentGraph(double input, double output, bool feasible) {
        RealVar x = Bounded();
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(x);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(function.ResultVar, output), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "piecewise-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        if (!feasible) {
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

    [Theory]
    [InlineData(ObjectCategory.Minimum, 2.0)]
    [InlineData(ObjectCategory.Maximum, 3.0)]
    public async Task DiscontinuousBreakpoint_ShouldAllowEitherAdjacentResult(ObjectCategory category, double expected) {
        RealVar x = Bounded();
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(x);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 1), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: category,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "piecewise-boundary",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(new Flt64(2), function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    [Fact]
    public async Task InsufficientExplicitBigM_ShouldNotBeReportedAsAValidGraph() {
        RealVar x = Bounded();
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(x, bigM: new Flt64(0.1));
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 0), group: null).IsOk);
        Assert.Equal(Flt64.Zero, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }));
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.Equal(ErrorCode.ORModelInfeasible,
            Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.0, 1.0)]
    public async Task FixedColumnSubstitution_ShouldPreserveTheGraph(double input, double expected) {
        RealVar x = Bounded();
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(x);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "piecewise-fixed",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
        );
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    [Fact]
    public async Task NestedInputAndCondition_ShouldParticipateWithRevalidatedEvidence() {
        RealVar x = Bounded();
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero
        );
        var absolute = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(polynomial, IIntoValue<Flt64>.Identity));
        UnivariateLinearPiecewiseFunction<Flt64> piecewise = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, absolute) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
            slopes: new[] { new Flt64(2), new Flt64(-2) },
            intercepts: new[] { Flt64.Zero, new Flt64(5) },
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var inner = new LinearFunctionSymbolAdapter<Flt64>(piecewise);
        IfFunction<Flt64> condition = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(condition);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(absolute).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, -1.5), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, outer) }, Flt64.Zero),
            name: "piecewise-nested",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-1.5) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(2), model.Tokens.CachedValue(inner, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ChangedDependencyDuringSolve_ShouldRejectOldSolution(int change) {
        RealVar x = Bounded();
        bool merged = change >= 2;
        double fixedInput = merged ? 0.1 : 0.5;
        var nested = new LinearExpressionSymbol(
            name: "pwl-source-gurobi",
            polynomial: new MutableLinearPolynomial<Flt64>(
                merged
                    ? new[] { new LinearMonomial<Flt64>(new Flt64(2), x), new LinearMonomial<Flt64>(new Flt64(7), x) }
                    : new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        var converter = new SwitchingConverter();
        var piecewise = new LinearFunctionSymbolAdapter<Flt64>(UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
            slopes: new[] { new Flt64(2), new Flt64(-2) },
            intercepts: new[] { Flt64.Zero, new Flt64(5) },
            converter: converter
        ).Value);
        using var parent = new LinearMetaModel<Flt64>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Add(piecewise).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddConstraint(relation: Fixed(x, fixedInput), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, piecewise) }, Flt64.Zero),
            name: "pwl-source-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            if (change == 0) {
                nested.AsMutable().SetConstant(Flt64.One);
            }
            else if (change == 1) {
                converter.CorruptValue = -2;
            }
            else if (change == 2) {
                converter.CorruptValue = -9;
            }
            return Task.FromResult(Results.OkInstance);
        });
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Result<Success, ErrorCode, Error<ErrorCode>> applied = model.ApplySolverSolution(
            matrix: matrix,
            solution: solved.Value.Solution.Values
        );
        bool unchanged = change == -1 || change == 3;
        Assert.Equal(unchanged, applied.IsOk);
        Assert.Equal(unchanged ? new Flt64(fixedInput) : Flt64.Zero, parent.Tokens.Find(x)!.Result);
    }

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public double? CorruptValue { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value.ToDouble() == CorruptValue ? Flt64.Zero : value;
    }

    private static RealVar Bounded() {
        var x = new RealVar("piecewise-gurobi-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        return x;
    }

    private static UnivariateLinearPiecewiseFunction<Flt64> Create(RealVar x, Flt64? bigM = null) =>
        UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
            slopes: new[] { new Flt64(2), new Flt64(-2) },
            intercepts: new[] { Flt64.Zero, new Flt64(5) },
            converter: IIntoValue<Flt64>.Identity,
            bigM: bigM
        ).Value;

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
