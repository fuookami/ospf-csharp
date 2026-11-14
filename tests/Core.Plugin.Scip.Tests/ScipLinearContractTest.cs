#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using FrameworkBenders = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using FrameworkColumns = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;
namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

public class ScipLinearContractTest {
    [Fact]
    public void MissingOrInvalidLibraryReturnsEnvironmentError() {
        Result<ScipNative, ErrorCode, Error<ErrorCode>> missing = ScipNative.Load(
            homePath: null,
            fallback: "ospf-nonexistent-scip-library-39a4");
        Assert.Equal(ErrorCode.OREngineEnvironmentLost, Assert.IsType<Failed<ScipNative, ErrorCode, Error<ErrorCode>>>(missing).Code);
        string other = OperatingSystem.IsWindows() ? "kernel32.dll" : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6";
        Result<ScipNative, ErrorCode, Error<ErrorCode>> invalid = ScipNative.Load(null, other);
        Assert.Equal(ErrorCode.OREngineEnvironmentLost, Assert.IsType<Failed<ScipNative, ErrorCode, Error<ErrorCode>>>(invalid).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public async Task MalformedMatrixIsRejectedBeforeNativeCalls(int change) {
        ModelViewVariable variable = new(
            index: change == 1 ? 1 : 0,
            lowerBound: new Flt64(change == 2 ? double.NaN : 0),
            upperBound: new Flt64(change == 3 ? -1 : 1),
            type: Binary.Instance,
            name: "x");
        SparseVector row = new([new SparseVectorEntry(change == 4 ? 2 : 0, new Flt64(change == 5 ? double.NaN : 1))]);
        using LinearTriadModel model = Model(
            variable: variable,
            row: row,
            relation: change == 6 ? null! : ConstraintRelation.Equal,
            right: new Flt64(change == 7 ? double.PositiveInfinity : 1),
            objective: [new LinearObjectiveCell(change == 8 ? 2 : 0, new Flt64(change == 9 ? double.NaN : 1))],
            category: change == 10 ? (ObjectCategory)99 : ObjectCategory.Minimum,
            constant: change == 0 ? double.NaN : 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Fact]
    public void SnapshotCombinesDuplicateRowsAndChecksNativeInfinity() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x"),
            row: new SparseVector([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(0, new Flt64(2))]),
            relation: ConstraintRelation.LessEqual,
            right: new Flt64(3),
            objective: [new LinearObjectiveCell(0, Flt64.One), new LinearObjectiveCell(0, new Flt64(2))],
            category: ObjectCategory.Maximum,
            constant: 4);
        Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> result = ScipLinearSnapshot.Capture(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(3, Assert.Single(result.Value.Rows[0].Coefficients));
        Assert.Equal(3, Assert.Single(result.Value.Objective));
        Assert.True(result.Value.Fits(10));
        Assert.False(result.Value.Fits(4));
        model.Dispose();
        Assert.False(ScipLinearSnapshot.Capture(model).IsOk);
        Assert.Equal(3, Assert.Single(result.Value.Rows[0].Coefficients));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(2)]
    public void SnapshotAcceptsFiniteInitialValuesAsHints(double initial) {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x",
                initialResult: new Flt64(initial)),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> result = ScipLinearSnapshot.Capture(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(initial, Assert.Single(result.Value.Columns).Initial);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SnapshotRejectsNonfiniteInitialValues(double initial) {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x",
                initialResult: new Flt64(initial)),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> result = ScipLinearSnapshot.Capture(model);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Theory]
    [InlineData(1e20, false)]
    [InlineData(-1e20, false)]
    [InlineData(1e19, true)]
    [InlineData(-1e19, true)]
    public void SnapshotChecksInitialValuesAgainstNativeInfinity(double initial, bool fits) {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x",
                initialResult: new Flt64(initial)),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> result = ScipLinearSnapshot.Capture(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(fits, result.Value.Fits(1e20));
    }

    [Theory]
    [InlineData(double.NegativeInfinity, 1)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(-9007199254740992d, 1)]
    [InlineData(0, 9007199254740992d)]
    public async Task RejectsIntegerDomainsOutsideExactBinary64Range(double lower, double upper) {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: new Flt64(lower),
                upperBound: new Flt64(upper),
                type: Integer.Instance,
                name: "integer"),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Fact]
    public async Task UnsupportedFeaturesAreNotSilentlyIgnored() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x"),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        var solver = new ScipLinearSolver();
        // IIS 入口已接入真实诊断链路：可行模型返回正常解，失败只可能来自真实求解或环境缺失，不再是“特性被拒绝”。
        // The IIS entry runs the real diagnostic pipeline now: a feasible model returns its solution, and a failure can
        // only come from a real solve or a missing environment, never from an unsupported-feature rejection.
        Result<SolverOutput, ErrorCode, Error<ErrorCode>> iisRun = await solver.InvokeAsync(model: model, iisConfig: new IisConfig());
        Assert.False(iisRun is Failed<SolverOutput, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.Other });
        // 状态回调已被支持，失败只可能来自真实求解或环境缺失，不再是“特性被拒绝”。
        // Status callbacks are supported now, so a failure can only come from a real solve or a missing
        // environment, never from an unsupported-feature rejection.
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> callbackReport = await solver.InvokeReportAsync(
            model: model,
            solvingStatusCallBack: _ => Results.OkInstance);
        Assert.False(callbackReport is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.Other });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> cancelled = await solver.InvokeReportAsync(model: model, cancellationToken: cancellation.Token);
        Assert.True(cancelled.IsOk);
        Assert.Equal(TerminationReason.Cancelled, cancelled.Value.TerminationReason);
        Assert.Null(cancelled.Value.Solution);
    }

    /// <summary>
    /// 运行参数与 backend 配置门在触碰原生库之前拒绝，因此无需 SCIP 安装即可确定通过。
    /// Runtime and backend configuration gates are rejected before the native library is touched, so this
    /// holds deterministically without a SCIP installation.
    /// </summary>
    [Fact]
    public async Task RuntimeAndBackendGatesFailBeforeNativeWork() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Continuous.Instance,
                name: "x"),
            row: new SparseVector(),
            relation: ConstraintRelation.LessEqual,
            right: Flt64.One,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        await AssertIllegal(model, new SolverConfig { ThreadCount = 0UL });
        await AssertIllegal(model, new SolverConfig { TimeLimit = TimeSpan.FromSeconds(-1) });
        await AssertIllegal(model, new SolverConfig { Gap = new Flt64(-1) });
        await AssertIllegal(model, new SolverConfig {
            BackendConfiguration = new GurobiSolverConfig(Server: "host"),
        });
        await AssertIllegal(model, new SolverConfig {
            ThreadCount = 2UL,
            BackendConfiguration = new ScipSolverConfig(Deterministic: true),
        });
        await AssertIllegal(model, new SolverConfig {
            BackendConfiguration = new ScipSolverConfig(RandomSeed: (long)int.MaxValue + 1L),
        });
        // 合法的后端配置不会被门拒绝，只能因缺少原生库或真实求解结果结束。
        // A valid backend configuration passes the gates and can only end in a missing library or a real solve.
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> accepted = await new ScipLinearSolver(new SolverConfig {
            ThreadCount = 1UL,
            TimeLimit = TimeSpan.FromSeconds(5),
            BackendConfiguration = new ScipSolverConfig(
                Presolve: false,
                RandomSeed: 7L,
                Deterministic: true,
                NativeParameters: new Dictionary<string, BackendParameterValue> {
                    ["lp/algorithm"] = new BackendParameterValue.Text("1"),
                }),
        }).InvokeReportAsync(model);
        Assert.False(accepted is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.IllegalArgument });
    }

    [Fact]
    public async Task PoolChecksCapacityAndCancellationBeforeNativeLoading() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x"),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        var solver = new ScipLinearSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> oversized = await solver.InvokeReportAsync(
            model: model,
            solutionAmount: (ulong)int.MaxValue + 1);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(oversized).Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> cancelled = await solver.InvokeReportAsync(
            model: model,
            solutionAmount: 2,
            cancellationToken: cancellation.Token);
        Assert.True(cancelled.IsOk);
        Assert.Equal(TerminationReason.Cancelled, cancelled.Value.TerminationReason);
        Assert.Null(cancelled.Value.Solution);
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> legacy = await solver.InvokeAsync(
            model: model,
            solutionAmount: 2,
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(legacy).Code);
    }

    [Fact]
    public async Task CancellableTokenDoesNotHideInvalidModel() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 1,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "invalid"),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        using var cancellation = new CancellationTokenSource();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(
            model: model,
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Fact]
    public async Task CancellationDuringSnapshotReturnsCancelledWithoutNativeLibrary() {
        using LinearTriadModel model = Model(
            variable: new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x"),
            row: new SparseVector(),
            relation: ConstraintRelation.Equal,
            right: Flt64.Zero,
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        using var cancellation = new CancellationTokenSource();
        using var view = new CancellingView(model, cancellation);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(
            model: view,
            cancellationToken: cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Null(result.Value.Provenance);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = await new ScipLinearSolver().InvokeAsync(
            model: view,
            cancellationToken: cancellation.Token);
        Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(legacy).Code);
    }

    [Theory]
    [InlineData(1, true, TerminationReason.Cancelled)]
    [InlineData(1, false, TerminationReason.Interrupted)]
    [InlineData(15, true, TerminationReason.Interrupted)]
    [InlineData(5, true, TerminationReason.TimeLimit)]
    [InlineData(11, true, TerminationReason.Completed)]
    [InlineData(12, true, TerminationReason.Completed)]
    [InlineData(13, true, TerminationReason.Completed)]
    [InlineData(14, true, TerminationReason.Completed)]
    public void NativeTerminationIsNotOverwrittenByLateCancellation(int status, bool interrupted, TerminationReason expected) =>
        Assert.Equal(expected, ScipLinearNativeSolver.Termination(status, interrupted));

    [Fact]
    public async Task DumpAndDecompositionRejectUnavailableCapabilitiesBeforeMutatingModel() {
        using var meta = new LinearMetaModel<Flt64>();
        var solver = new ScipLinearSolver();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> dump = await solver.DumpAsync(
            model: meta,
            rcb: null,
            dcb: null);
        Assert.True(dump.IsOk, dump.ToString());
        dump.Value.Dispose();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> callback = await solver.DumpAsync(
            model: meta,
            rcb: null,
            dcb: _ => Results.OkInstance);
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(callback).Code);
        var configured = new ScipLinearSolver(new SolverConfig { DumpMechanismModelConcurrent = true });
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(await configured.DumpAsync(
            model: meta,
            rcb: null,
            dcb: null)).Code);
        var columns = new ScipColumnGenerationSolver();
        var benders = new ScipBendersDecompositionSolver();
        // LP 与子问题入口已改为真实求解，只有模型日志导出与空固定映射在触碰原生库之前被拒绝。
        // The LP and sub-problem entries now solve for real; only model logging and a null fixed map are rejected
        // before the native library is touched.
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<FrameworkColumns.LpResult, ErrorCode, Error<ErrorCode>>>(await columns.SolveLPAsync(
            name: "lp",
            metaModel: meta,
            toLogModel: true)).Code);
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<FrameworkBenders.LinearSubResult, ErrorCode, Error<ErrorCode>>>(await benders.SolveSubAsync(
            name: "sub",
            metaModel: meta,
            objectVariable: null!,
            fixedVariables: new Dictionary<IVariableItem, Flt64>(),
            toLogModel: true)).Code);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<FrameworkBenders.LinearSubResult, ErrorCode, Error<ErrorCode>>>(await benders.SolveSubAsync(
            name: "sub",
            metaModel: meta,
            objectVariable: null!,
            fixedVariables: null!)).Code);
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(await columns.SolveMILPAsync(
            name: "milp",
            metaModel: meta,
            toLogModel: true)).Code);
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(await columns.SolveMILPAsync(
            name: "milp",
            metaModel: meta,
            amount: 2,
            toLogModel: true)).Code);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(await columns.SolveMILPAsync(
            name: "milp",
            metaModel: meta,
            amount: (ulong)int.MaxValue + 1)).Code);
        Assert.Equal(ErrorCode.Other, Assert.IsType<Failed<SolverOutput, ErrorCode, Error<ErrorCode>>>(await benders.SolveMasterAsync(
            name: "master",
            metaModel: meta,
            toLogModel: true)).Code);
    }

    private static LinearTriadModel Model(
        ModelViewVariable variable,
        SparseVector row,
        ConstraintRelation relation,
        Flt64 right,
        LinearObjectiveCell[] objective,
        ObjectCategory category,
        double constant) => new(
            variables: [variable],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([row]),
                signs: [relation],
                rhs: [right],
                names: ["row"],
                sources: [ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(
                category: category,
                cells: objective,
                constant: new Flt64(constant)),
            name: "contract");

    private static async Task AssertIllegal(
        LinearTriadModel model,
        SolverConfig config) =>
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(
                await new ScipLinearSolver(config).InvokeReportAsync(model)).Code);

    private sealed class CancellingView(LinearTriadModel model, CancellationTokenSource cancellation) : ILinearTriadModelView {
        private int _variableReads;

        public IReadOnlyList<ModelViewVariable> Variables {
            get {
                if (++_variableReads == 2) {
                    cancellation.Cancel();
                }
                return model.Variables;
            }
        }

        public ModelConstraint<LinearConstraintCell> Constraints => model.Constraints;
        public Objective<LinearObjectiveCell> Objective => model.Objective;
        public string Name => model.Name;
        public Result<Success, ErrorCode, Error<ErrorCode>> ExportLP(StreamWriter? writer) => model.ExportLP(writer);
        public void Dispose() { }
    }
}
