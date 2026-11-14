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
using Fuookami.Ospf.Math.Geometry;
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

public class BivariatePiecewiseGurobiIntegrationTest {
    [Theory]
    [InlineData(0, 0, 2, true)]
    [InlineData(1, 0, 10, true)]
    [InlineData(0, 1, 20, true)]
    [InlineData(0.25, 0.25, 8.5, true)]
    [InlineData(0.8, 0.8, 0, false)]
    public async Task FixedInputs_ShouldSolveTheTriangleSurface(double xValue, double yValue, double expected, bool feasible) {
        var x = new RealVar("surface-gurobi-x");
        var y = new RealVar("surface-gurobi-y");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(Create(x, y));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, xValue), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, yValue), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: Objective(adapter),
            name: "surface-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
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
            low: expected - 1e-7,
            high: expected + 1e-7
        );
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, 8.5)]
    [InlineData(ObjectCategory.Maximum, 10.5)]
    public async Task OverlappingTriangles_ShouldAllowEitherSurface(ObjectCategory category, double expected) {
        var x = new RealVar("surface-overlap-x");
        var y = new RealVar("surface-overlap-y");
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(x),
            y: Polynomial(y),
            triangles: new[] { Triangle(0), Triangle(2) },
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 0.25), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, 0.25), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: category,
            flattenData: Objective(adapter),
            name: "surface-overlap",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.25), [y] = new Flt64(0.25) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(new Flt64(8.5), function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.25), [y] = new Flt64(0.25) }));
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

    [Theory]
    [InlineData(0.0, 0.0, 2.0)]
    [InlineData(1.0, 0.0, 10.0)]
    [InlineData(0.25, 0.25, 8.5)]
    public async Task FixedColumnSubstitution_ShouldPreserveTheSurface(double xValue, double yValue, double expected) {
        var x = new RealVar("surface-fixed-x");
        var y = new RealVar("surface-fixed-y");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(Create(x, y));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: Objective(adapter),
            name: "surface-fixed",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(xValue), [y] = new Flt64(yValue) }
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
    public async Task NestedAbsoluteAndCondition_ShouldSolveWithPreparedEvidence() {
        var x = new RealVar("surface-nested-x");
        var y = new RealVar("surface-nested-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, Flt64.One).Value);
        var absolute = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(Polynomial(x), IIntoValue<Flt64>.Identity));
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(absolute),
            y: Polynomial(y),
            triangles: new[] { Triangle(0) },
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var surface = new LinearFunctionSymbolAdapter<Flt64>(function);
        var condition = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, surface) }, new Flt64(-8)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(condition).IsOk);
        Assert.True(parent.Add(surface).IsOk);
        Assert.True(parent.Add(absolute).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, -0.25), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, 0.25), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: Objective(condition),
            name: "surface-nested",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-0.25), [y] = new Flt64(0.25) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(8.5), model.Tokens.CachedValue(surface, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(condition, (IReadOnlyList<Flt64>?)null));
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedNestedInputDuringSolve_ShouldRejectOldSolution(bool change) {
        var x = new RealVar("surface-source-gurobi-x");
        var y = new RealVar("surface-source-gurobi-y");
        var nested = new LinearExpressionSymbol(
            name: "surface-source-gurobi",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        var surface = new LinearFunctionSymbolAdapter<Flt64>(BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(nested),
            y: Polynomial(y),
            triangles: new[] { Triangle(0) },
            converter: IIntoValue<Flt64>.Identity
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Add(surface).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 0.25), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, 0.25), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: Objective(surface),
            name: "surface-source-objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            if (change) {
                nested.AsMutable().SetConstant(Flt64.One);
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
        Assert.Equal(!change, applied.IsOk);
        Assert.Equal(change ? Flt64.Zero : new Flt64(0.25), parent.Tokens.Find(x)!.Result);
    }

    [Fact]
    public async Task PreparedSurfaceCache_ShouldNotBeTreatedAsALinearRelaxationSolution() {
        var x = new RealVar("surface-relaxation-x");
        var y = new RealVar("surface-relaxation-y");
        var distant = new Triangle<Dim3, Flt64>(
            P1: Point<Dim3, Flt64>.Point3(
                x: new Flt64(2),
                y: Flt64.Zero,
                z: Flt64.Zero
            ),
            P2: Point<Dim3, Flt64>.Point3(
                x: new Flt64(3),
                y: Flt64.Zero,
                z: Flt64.Zero
            ),
            P3: Point<Dim3, Flt64>.Point3(
                x: new Flt64(2),
                y: Flt64.One,
                z: Flt64.Zero
            )
        );
        var surface = new LinearFunctionSymbolAdapter<Flt64>(BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(x),
            y: Polynomial(y),
            triangles: new[] { Triangle(0), distant },
            converter: IIntoValue<Flt64>.Identity
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(surface).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 0.25), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(y, 0.25), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: Objective(surface),
            name: "surface-relaxation",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.25), [y] = new Flt64(0.25) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(8.5), model.Tokens.CachedValue(surface, (IReadOnlyList<Flt64>?)null));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        using LinearTriadModel relaxed = matrix.RelaxToContinuous().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)relaxed);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.True(solved.Value.Obj.ToDouble() < 8.5 - 1e-7);
    }

    private static BivariateLinearPiecewiseFunction<Flt64> Create(RealVar x, RealVar y) =>
        BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(x),
            y: Polynomial(y),
            triangles: new[] { Triangle(0) },
            converter: IIntoValue<Flt64>.Identity
        ).Value;

    private static Triangle<Dim3, Flt64> Triangle(double offset) => new(
        P1: Point<Dim3, Flt64>.Point3(
            x: Flt64.Zero,
            y: Flt64.Zero,
            z: new Flt64(2 + offset)
        ),
        P2: Point<Dim3, Flt64>.Point3(
            x: Flt64.One,
            y: Flt64.Zero,
            z: new Flt64(10 + offset)
        ),
        P3: Point<Dim3, Flt64>.Point3(
            x: Flt64.Zero,
            y: Flt64.One,
            z: new Flt64(20 + offset)
        )
    );

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static LinearFlattenData<Flt64> Objective(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: Polynomial(symbol),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
