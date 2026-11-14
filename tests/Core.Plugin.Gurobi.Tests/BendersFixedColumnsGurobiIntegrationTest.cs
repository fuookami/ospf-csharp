#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using BendersSolver = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class BendersFixedColumnsGurobiIntegrationTest {
    [Fact]
    public async Task Subproblem_ShouldEliminateFixedColumnsAndReturnFullSolution() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-fixed-x");
        var y = new BinVar("benders-free-y");
        var z = new BinVar("benders-fixed-z");
        var w = new BinVar("benders-free-w");
        Assert.True(parent.Add(new IVariableItem[] { x, y, z, w }).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] {
                new LinearMonomial<Flt64>(new Flt64(-2), x),
                new LinearMonomial<Flt64>(new Flt64(-3), y),
                new LinearMonomial<Flt64>(new Flt64(4), z),
                new LinearMonomial<Flt64>(new Flt64(-1), w),
            }, Flt64.Zero),
            name: "benders-objective",
            displayName: null
        ).IsOk);
        Assert.True(parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] {
                    new LinearMonomial<Flt64>(Flt64.One, y),
                    new LinearMonomial<Flt64>(Flt64.One, w),
                }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
                Comparison: Comparison.LE
            ),
            group: null
        ).IsOk);
        int nativeColumns = -1;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, variables, _) => {
            nativeColumns = variables.Count;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiBendersDecompositionSolver(callBack: callback);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveSubAsync(
            name: "benders-reduced",
            metaModel: parent,
            objectVariable: x,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [z] = Flt64.Zero }
        );

        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearFeasibleResult feasible = Assert.IsType<BendersSolver.LinearFeasibleResult>(result.Value);
        Assert.Equal(2, nativeColumns);
        Assert.Equal(new Flt64(-5), feasible.Obj);
        Assert.Equal(new[] { Flt64.One, Flt64.One, Flt64.Zero, Flt64.Zero }, feasible.Solution);
        Assert.Equal(feasible.Solution, parent.Tokens.TokensInSolver.Select(token => token.Result!.Value));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public async Task AllFixedSubproblem_ShouldRespectFeasibilityAndRejectStaleEvidence(int mode, bool feasible) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-all-fixed");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.SetSolverSolution(new[] { Flt64.Zero });
        Assert.True(parent.AddConstraint(relation: AtLeastOne(x), group: null).IsOk);
        int nativeColumns = -1;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, variables, _) => {
            nativeColumns = variables.Count;
            if (mode == 2) {
                Assert.True(parent.Add(x).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "benders-all-fixed",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = feasible ? Flt64.One : Flt64.Zero }
            );

        Assert.Equal(0, nativeColumns);
        if (mode == 2) {
            Assert.Equal(ErrorCode.IllegalArgument,
                Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
            Assert.Null(parent.Tokens.Find(x)!.Result);
        }
        else if (feasible) {
            Assert.True(result.IsOk, result.ToString());
            BendersSolver.LinearFeasibleResult output = Assert.IsType<BendersSolver.LinearFeasibleResult>(result.Value);
            Assert.Equal(new[] { Flt64.One }, output.Solution);
            Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
        }
        else {
            Assert.True(result.IsOk, result.ToString());
            BendersSolver.LinearInfeasibleResult output = Assert.IsType<BendersSolver.LinearInfeasibleResult>(result.Value);
            Assert.Single(output.FarkasDualSolution);
            Assert.True(output.FarkasDualSolution.Values.Single().ToDouble() < 0);
            LinearInequalityConstraint<Flt64> origin = parent.RelationConstraints[0];
            LinearInequalityConstraint<Flt64> equalCopy = origin with { };
            Assert.Equal(origin, equalCopy);
            Assert.False(output.FarkasDualSolution.ContainsKey(equalCopy));
            Assert.Equal(Flt64.Zero, parent.Tokens.Find(x)!.Result);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Subproblem_ShouldRejectUnknownOrInvalidFixedValuesBeforeNativeSolve(int invalid) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-invalid-x");
        var unknown = new BinVar("benders-unknown");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.SetSolverSolution(new[] { Flt64.One });
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            return Task.FromResult(Results.OkInstance);
        });
        IVariableItem variable = invalid == 0 ? unknown : x;
        Flt64 value = invalid switch {
            1 => new Flt64(double.NaN),
            2 => new Flt64(double.PositiveInfinity),
            3 => new Flt64(2),
            4 => new Flt64(0.5),
            _ => Flt64.Zero,
        };

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "benders-invalid",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [variable] = value }
            );

        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.False(called);
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Subproblem_FailedFixedValueDumpShouldNotCommitHelpers(bool unknownFixedVariable) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-dump-boundary-x");
        var unknown = new BinVar("benders-dump-boundary-unknown");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        var function = new DelayedHelper();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.Null(parent.Tokens.Find(function.Helper));
        bool nativeCalled = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            nativeCalled = true;
            return Task.FromResult(Results.OkInstance);
        });

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "benders-dump-boundary",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> {
                    [unknownFixedVariable ? unknown : x] = unknownFixedVariable ? Flt64.Zero : new Flt64(2),
                }
            );

        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.False(nativeCalled);
        Assert.Null(parent.Tokens.Find(function.Helper));
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Fact]
    public async Task Subproblem_CommitFailureRollsBackIdentityBeforeRetryingSolve() {
        var registry = new ModelElementIdentityRegistry();
        object reserved = new();
        Assert.Equal("model-local-variable:0", registry.VariableId(reserved, 0).Value);
        ModelElementIdentity[] before = registry.Entries().ToArray();
        using var parent = new LinearMetaModel<Flt64>(identityRegistry: registry);
        var x = new BinVar("benders-identity-x");
        var transient = new RealVar("benders-identity-transient");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        var function = new DelayedHelper();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        bool changed = false;
        bool nativeCalled = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            nativeCalled = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiBendersDecompositionSolver(callBack: callback);
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One };

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> failed = await solver.SolveSubAsync(
            name: "benders-identity-failure",
            metaModel: parent,
            objectVariable: x,
            fixedVariables: fixedVariables,
            registrationStatusCallBack: status => {
                if (!changed && status.ReadySymbolAmount.ToULong() == 1) {
                    changed = true;
                    Assert.True(parent.Add(transient).IsOk);
                    parent.Remove(transient);
                }
                return Results.OkInstance;
            }
        );

        Assert.True(changed);
        Assert.True(failed.IsFailed);
        Assert.False(nativeCalled);
        Assert.Null(parent.Tokens.Find(function.Helper));
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(x)!.Result);
        Assert.Equal(before, registry.Entries());
        Assert.Equal("model-local-variable:0:collision-1", registry.VariableId(new object(), 0).Value);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> retried = await solver.SolveSubAsync(
            name: "benders-identity-retry",
            metaModel: parent,
            objectVariable: x,
            fixedVariables: fixedVariables
        );
        Assert.True(retried.IsOk, retried.ToString());
        Assert.IsType<BendersSolver.LinearFeasibleResult>(retried.Value);
        Assert.True(nativeCalled);
        Assert.NotNull(parent.Tokens.Find(function.Helper));
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
        Assert.True(registry.Validate().IsOk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Subproblem_FailureShouldPreserveParentChangesFromRegistration(bool invalidFixedValue) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-parent-change-x");
        var extra = new RealVar("benders-parent-change-extra");
        Assert.True(parent.Add(x).IsOk);
        var function = new DelayedHelper();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        bool changed = false;
        bool nativeCalled = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            nativeCalled = true;
            return Task.FromResult(Results.OkInstance);
        });

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "benders-parent-change",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = invalidFixedValue ? new Flt64(2) : Flt64.One },
                registrationStatusCallBack: _ => {
                    if (!changed) {
                        changed = true;
                        Assert.True(parent.Add(extra).IsOk);
                    }
                    return Results.OkInstance;
                }
            );

        Assert.True(changed);
        Assert.True(result.IsFailed);
        Assert.False(nativeCalled);
        Assert.NotNull(parent.Tokens.Find(extra));
        Assert.Null(parent.Tokens.Find(function.Helper));
    }

    [Fact]
    public async Task Subproblem_ShouldAllowFixedDelayedHelpers() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-fixed-helper-x");
        Assert.True(parent.Add(x).IsOk);
        var function = new DelayedHelper();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.Null(parent.Tokens.Find(function.Helper));

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver().SolveSubAsync(
                name: "benders-fixed-helper",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> {
                    [x] = Flt64.One,
                    [function.Helper] = new Flt64(7),
                }
            );

        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearFeasibleResult feasible = Assert.IsType<BendersSolver.LinearFeasibleResult>(result.Value);
        Assert.Equal(new[] { Flt64.One, new Flt64(7) }, feasible.Solution);
        Assert.Equal(new Flt64(7), parent.Tokens.Find(function.Helper)!.Result);
    }

    [Fact]
    public async Task Subproblem_NativeFailureAfterSuccessfulDumpRetainsCommittedHelpers() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-native-failure-x");
        Assert.True(parent.Add(x).IsOk);
        var function = new DelayedHelper();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        bool nativeCalled = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            nativeCalled = true;
            return Task.FromResult<Try>(Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed, "native callback failed")));
        });

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "benders-native-failure",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );

        Assert.True(nativeCalled);
        Assert.True(result.IsFailed);
        Assert.NotNull(parent.Tokens.Find(function.Helper));
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    [Fact]
    public async Task Subproblem_ShouldRejectUnreadableFixedMapsBeforeNativeSolve() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-unreadable");
        Assert.True(parent.Add(x).IsOk);
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiBendersDecompositionSolver(callBack: callback);

        foreach (IReadOnlyDictionary<IVariableItem, Flt64>? map in new IReadOnlyDictionary<IVariableItem, Flt64>?[] { null, new UnreadableFixedMap() }) {
            Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveSubAsync(
                name: "benders-unreadable",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: map!
            );
            Assert.Equal(ErrorCode.IllegalArgument,
                Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        Assert.False(called);
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    [Fact]
    public async Task Subproblem_ShouldCaptureFixedValuesBeforeRegistrationCallbacks() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-captured");
        Assert.True(parent.Add(x).IsOk);
        var probe = new FixedValueProbe(x);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(probe)).IsOk);
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One };
        bool called = false;

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver().SolveSubAsync(
                name: "benders-captured",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: fixedValues,
                registrationStatusCallBack: _ => {
                    called = true;
                    fixedValues[x] = Flt64.Zero;
                    return Results.OkInstance;
                }
            );

        Assert.True(called);
        Assert.Equal(Flt64.One, probe.Seen);
        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearFeasibleResult output = Assert.IsType<BendersSolver.LinearFeasibleResult>(result.Value);
        Assert.Equal(new[] { Flt64.One }, output.Solution);
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
        Assert.Equal(Flt64.Zero, fixedValues[x]);
    }

    [Fact]
    public async Task InfeasibleSubproblem_ShouldReturnExpectedRowMultipliers() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-farkas-x");
        var y = new RealVar("benders-farkas-y");
        y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(2)).Value);
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        Assert.True(parent.AddConstraint(relation: AtLeastOne(y), group: null, name: "demand").IsOk);
        Assert.True(parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] {
                    new LinearMonomial<Flt64>(Flt64.One, y),
                    new LinearMonomial<Flt64>(new Flt64(-1), x),
                }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>([], Flt64.Zero),
                Comparison: Comparison.LE
            ),
            group: null,
            name: "capacity"
        ).IsOk);

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver().SolveSubAsync(
                name: "benders-farkas",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }
            );

        Assert.True(result.IsOk, result.ToString());
        BendersSolver.LinearInfeasibleResult infeasible = Assert.IsType<BendersSolver.LinearInfeasibleResult>(result.Value);
        double demand = infeasible.FarkasDualSolution[parent.RelationConstraints[0]].ToDouble();
        double capacity = infeasible.FarkasDualSolution[parent.RelationConstraints[1]].ToDouble();
        Assert.True(demand < 0); // 对 x=0 的聚合右端为负 / The aggregated right-hand side at x=0 is negative.
        Assert.True(capacity > 0);
        Assert.InRange(System.Math.Abs(demand + capacity), 0, 1e-9);
        Assert.Null(parent.Tokens.Find(x)!.Result);
        Assert.Null(parent.Tokens.Find(y)!.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InfeasibleSubproblem_ShouldRejectEvidenceAfterParentChanges(int change) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("stale-farkas-x");
        var y = new RealVar("stale-farkas-y");
        y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(2)).Value);
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        parent.Tokens.SetSolverSolution(new[] { Flt64.Zero, Flt64.Zero });
        Assert.True(parent.AddConstraint(relation: AtLeastOne(y), group: null, name: "demand").IsOk);
        Assert.True(parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] {
                    new LinearMonomial<Flt64>(Flt64.One, y),
                    new LinearMonomial<Flt64>(-Flt64.One, x),
                }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>([], Flt64.Zero),
                Comparison: Comparison.LE
            ),
            group: null,
            name: "capacity"
        ).IsOk);
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterFailure((status, _, _, _) => {
            called = true;
            Assert.Equal(SolverStatus.Infeasible, status);
            if (change == 0) {
                Assert.True(parent.AddConstraint(x, group: null).IsOk);
            }
            else if (change == 1) {
                Assert.True(parent.AddObject(
                    category: ObjectCategory.Minimum,
                    flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
                    name: "added",
                    displayName: null
                ).IsOk);
            }
            else {
                y.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, Flt64.One).Value);
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "stale-farkas",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }
            );

        Assert.True(called);
        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(x)!.Result);
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(y)!.Result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DirectSolvers_ShouldPropagateAfterFailureCallbacks(bool quadratic, bool fatal) {
        var x = new BinVar("direct-failed-callback");
        Error<ErrorCode> error = new Err<ErrorCode>(ErrorCode.ApplicationFailed, "callback failed");
        bool called = false;
        Try failure = fatal
            ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(error)
            : Results.Failed<Success>(error);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using var parent = new QuadraticMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.AddConstraint(relation: AtLeastOne(x), group: null).IsOk);
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel(
                new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }).Value;
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack()
                .AfterFailure((_, _, _, _) => Task.FromResult(failure))
                .AfterFailure((_, _, _, _) => {
                    called = true;
                    return Task.FromResult(Results.OkInstance);
                });
            result = await new GurobiQuadraticSolver(callBack: callback).InvokeAsync((IQuadraticTetradModelView)matrix);
        }
        else {
            using var parent = new LinearMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.AddConstraint(relation: AtLeastOne(x), group: null).IsOk);
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            using LinearTriadModel matrix = mechanism.ToLinearTriadModel(
                new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }).Value;
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack()
                .AfterFailure((_, _, _, _) => Task.FromResult(failure))
                .AfterFailure((_, _, _, _) => {
                    called = true;
                    return Task.FromResult(Results.OkInstance);
                });
            result = await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)matrix);
        }

        Assert.False(called);
        if (fatal) {
            Assert.Same(error, Assert.Single(Assert.IsType<Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Errors));
        }
        else {
            Assert.Same(error, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Error);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Subproblem_ShouldNotTreatFailedEvidenceCallbacksAsCertificates(bool fatal, bool sameCode) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("benders-failed-evidence");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: AtLeastOne(x), group: null).IsOk);
        ErrorCode code = sameCode ? ErrorCode.ORModelInfeasible : ErrorCode.ApplicationFailed;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterFailure((_, _, _, _) =>
            Task.FromResult<Result<Success, ErrorCode, Error<ErrorCode>>>(fatal
                ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(code, "callback failed"))
                : Results.Failed<Success>(new Err<ErrorCode>(code, "callback failed"))));

        Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "benders-failed-evidence",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }
            );

        if (fatal) {
            Assert.IsType<Fatal<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result);
        }
        else {
            Assert.Equal(code, Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    private static LinearInequality<Flt64> AtLeastOne(IVariableItem x) => new(
        Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
        Comparison: Comparison.GE
    );

    private sealed class FixedValueProbe(IVariableItem input) : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        public string Name { get; set; } = "benders-fixed-probe";
        public string? DisplayName { get; set; }
        public Flt64? Seen { get; private set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => new[] { input };
        public LinearPolynomial<Flt64> ResultPolynomial => new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero);

        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
            Seen = values.TryGetValue(input, out Flt64 value) ? value : null;
            return Seen;
        }

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => Results.OkInstance;
    }

    private sealed class DelayedHelper : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        private int _registrations;
        public string Name { get; set; } = "benders-delayed-helper";
        public string? DisplayName { get; set; }
        public RealVar Helper { get; } = new("benders-delayed-helper-variable");
        public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { Helper };
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<Flt64> ResultPolynomial => new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, Helper) }, Flt64.Zero);
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => Flt64.One;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
            ++_registrations == 1 ? Results.OkInstance : tokens.Add(Helper);
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => Results.OkInstance;
    }

    private sealed class UnreadableFixedMap : IReadOnlyDictionary<IVariableItem, Flt64> {
        public int Count => 1;
        public IEnumerable<IVariableItem> Keys => throw new InvalidOperationException("unreadable");
        public IEnumerable<Flt64> Values => throw new InvalidOperationException("unreadable");
        public Flt64 this[IVariableItem key] => throw new InvalidOperationException("unreadable");
        public bool ContainsKey(IVariableItem key) => throw new InvalidOperationException("unreadable");
        public bool TryGetValue(IVariableItem key, out Flt64 value) => throw new InvalidOperationException("unreadable");
        public IEnumerator<KeyValuePair<IVariableItem, Flt64>> GetEnumerator() => throw new InvalidOperationException("unreadable");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
#endif
