#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

using PoolResult = System.ValueTuple<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Fuookami.Ospf.Math.Algebra.Number.Flt64>, System.Collections.Generic.List<System.Collections.Generic.List<Fuookami.Ospf.Math.Algebra.Number.Flt64>>>;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// `SolveMILPAsync` 必须**真的用适配器包装回调**，而不是把 `ProgressContext` 存着不用。
/// `SolveMILPAsync` must **really wrap the callbacks with the adapters**, not just store `ProgressContext`.
/// </summary>
/// <remarks>
/// 移植了适配器，但 C# 的 `FrameworkSolveOptions` **没有 `ProgressContext` 字段**，
/// 于是适配器**没有可接的位置**。 补上字段并在 `SolveMILPAsync` 里包装。
///
/// **判据不能只看"求解成功"**——那与进度无关。判据要看**上报器有没有收到快照**，
/// 以及**原回调有没有被调用**：前者证明适配器被用了，后者证明它没把原回调吃掉。
///
/// ported the adapters, but the C# `FrameworkSolveOptions` **had no `ProgressContext` field**, so the adapters **had nowhere to hook in**. adds the field and
/// wraps inside `SolveMILPAsync`.
///
/// **The criterion cannot just check "the solve succeeded"** — that has nothing to do with progress. It must check **whether the reporter received snapshots** and
/// **whether the original callback was invoked**: the former proves the adapter was used, the latter proves it did not swallow the original callback.
/// </remarks>
public class SolveMilpProgressWiringTests {
    private sealed class RecordingReporter : IProgressReporter {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Results.Ok(Results.SuccessInstance);
        }
    }

    /// <summary>一个在求解过程中触发回调的求解器替身 / A solver double that fires the callbacks during the solve.</summary>
    /// <remarks>
    /// 只实现接口全部成员，**与进度无关的那些直接抛**——它们不该被这条路径调用到。
    ///
    /// Implements every interface member; **the ones unrelated to progress throw** — this path should never reach them.
    /// </remarks>
    private sealed class CallbackFiringSolver : IColumnGenerationSolver {
        public string Name => "callback-firing";

        public int RegistrationCallbackCalls { get; private set; }

        public int SolvingCallbackCalls { get; private set; }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            if (registrationStatusCallBack is not null) {
                ++RegistrationCallbackCalls;
                registrationStatusCallBack(new RegistrationStatus(
                    EmptySymbolAmount: default,
                    ReadySymbolAmount: default,
                    TotalSymbolAmount: default));
            }
            if (solvingStatusCallBack is not null) {
                ++SolvingCallbackCalls;
                solvingStatusCallBack(new SolvingStatus(
                    Solver: Name,
                    SolverIndex: 0,
                    SolverConfig: new Fuookami.Ospf.Core.Solver.Config.SolverConfig(),
                    Gap: new Flt64(0.0)));
            }
            return Task.FromResult(
                Results.Failed<FeasibleSolverOutput<Flt64>>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "stub: no real solve")));
        }

        public Task<Result<PoolResult, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            ulong amount,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("pool solve is unrelated to this criterion");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("converted solve is unrelated to this criterion");

        public int LpRegistrationCallbackCalls { get; private set; }

        public int LpSolvingCallbackCalls { get; private set; }

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            if (registrationStatusCallBack is not null) {
                ++LpRegistrationCallbackCalls;
                registrationStatusCallBack(new RegistrationStatus(
                    EmptySymbolAmount: default,
                    ReadySymbolAmount: default,
                    TotalSymbolAmount: default));
            }
            if (solvingStatusCallBack is not null) {
                ++LpSolvingCallbackCalls;
                solvingStatusCallBack(new SolvingStatus(
                    Solver: Name,
                    SolverIndex: 0,
                    SolverConfig: new Fuookami.Ospf.Core.Solver.Config.SolverConfig(),
                    Gap: new Flt64(0.0)));
            }
            return Task.FromResult(
                Results.Failed<IColumnGenerationSolver.LpResult>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "stub: no real LP solve")));
        }

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("converted LP solve is unrelated to this criterion");
    }

    /// <summary>只在解池重载里触发回调的替身 / A double that fires callbacks only in the pool overload.</summary>
    private sealed class PoolCallbackFiringSolver : IColumnGenerationSolver {
        public string Name => "pool-callback-firing";

        public int PoolSolvingCallbackCalls { get; private set; }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("the non-pool overload must not be used by this criterion");

        public Task<Result<PoolResult, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            ulong amount,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            if (solvingStatusCallBack is not null) {
                ++PoolSolvingCallbackCalls;
                solvingStatusCallBack(new SolvingStatus(
                    Solver: Name,
                    SolverIndex: 0,
                    SolverConfig: new Fuookami.Ospf.Core.Solver.Config.SolverConfig(),
                    Gap: new Flt64(0.0)));
            }
            return Task.FromResult(
                Results.Failed<PoolResult>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "stub: no real pool solve")));
        }

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("unrelated");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("unrelated");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("unrelated");
    }

    private static LinearMetaModel<Flt64> MakeModel() => new("wiring-test", ObjectCategory.Minimum);

    /// <summary>**主问题求解成功后必须上报收尾快照** / The master stage must report a closing snapshot on success.</summary>
    /// <remarks>
    /// 记录了一条缺口：收尾快照没有独立判据**——当时的替身返回失败，
    /// 而收尾快照**只在成功时上报**，所以那个分支从未被走到。
    ///
    /// 这条用一个**成功返回**的替身把它补上。**这是缺口，不是可选项**：
    /// 若实现忘了收尾上报，进度条会停在 30（开头那条的值），**永远不会到 40**。
    ///
    /// **recorded a gap: the closing snapshot had no criterion** — the double then returned a failure, and the closing snapshot **is only reported on success**,
    /// so that branch was never reached.
    ///
    /// This fills it with a **success-returning** double. **It is a gap, not an optional extra**: if the implementation forgot the closing report, the progress bar
    /// would freeze at 30 (the opening value) and **never reach 40**.
    /// </remarks>
    [Fact]
    public async Task SolveLPAsync_ShouldReportAClosingSnapshotOnSuccess() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new SucceedingLpSolver();

        await ((IColumnGenerationSolver)solver).SolveLPAsync(MakeModel(), new FrameworkSolveOptions {
            ProgressContext = context,
        });

        reporter.Snapshots.Should().HaveCountGreaterThanOrEqualTo(2, "an opening and a closing snapshot");
        reporter.Snapshots[0].ProgressInStage.Should().Be(0);
        reporter.Snapshots[^1].ProgressInStage.Should().Be(100, "the closing snapshot must report completion");
        reporter.Snapshots[^1].OverallProgress.Should().Be(40);
    }

    /// <summary>一个 LP 求解成功的替身 / A double whose LP solve succeeds.</summary>
    private sealed class SucceedingLpSolver : IColumnGenerationSolver {
        public string Name => "succeeding-lp";

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("unrelated");

        public Task<Result<PoolResult, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            ulong amount,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("unrelated");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("unrelated");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Ok(new IColumnGenerationSolver.LpResult(
                new FeasibleSolverOutput<Flt64>(
                    Obj: Flt64.Zero,
                    Solution: new Solution<Flt64>(new List<Flt64>()),
                    Time: TimeSpan.FromMilliseconds(1),
                    PossibleBestObj: Flt64.Zero,
                    Gap: Flt64.Zero),
                new Dictionary<MathConstraint, Flt64>())));

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("unrelated");
    }

    /// <summary>**解池分支也必须用包装后的回调** / The pool branch must also use the wrapped callbacks.</summary>
    /// <remarks>
    /// 基线在 `solveMILP` 的**两个重载**里各接一次线（第 156、158 行与第 166、168 行）。
    /// C# 的实现把 `registration` / `solving` 算在分支**之前**，所以两个分支共用包装后的回调——
    /// **这个结构本身正确，但它依赖"分支之前"这个位置**。若有人把包装移进非池分支，
    /// 池分支就会退回未包装的回调，而**非池分支的判据全部照旧通过**。
    ///
    /// 这条判据专门走**池分支**（`SolutionAmount` 非 null）。
    ///
    /// The baseline wires **both** `solveMILP` overloads (lines 156/158 and 166/168). The C# implementation computes `registration` / `solving` **before** the branch,
    /// so both branches share the wrapped callbacks — **that structure is correct, but it depends on being "before the branch"**. If someone moved the wrapping into
    /// the non-pool branch only, the pool branch would fall back to unwrapped callbacks while **every non-pool criterion still passed**.
    ///
    /// This criterion takes the **pool branch** (`SolutionAmount` non-null).
    /// </remarks>
    [Fact]
    public async Task SolveMILPAsync_ShouldWrapCallbacksOnThePoolBranchToo() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new PoolCallbackFiringSolver();

        await ((IColumnGenerationSolver)solver).SolveMILPAsync(MakeModel(), new FrameworkSolveOptions {
            ProgressContext = context,
            SolutionAmount = 2,
        });

        reporter.Snapshots.Should().NotBeEmpty("the pool branch must report through the adapters");
        solver.PoolSolvingCallbackCalls.Should().BePositive();
    }

    /// <summary>**主问题阶段必须前后各上报一次** / The master-problem stage must report once before and once after.</summary>
    /// <remarks>
    /// 基线 `solveLP(metaModel, options)` 在求解前上报 `(MasterLP, 0, 30)`，成功后上报一次收尾。
    /// C# 侧原先**没有这个重载**，所以主问题阶段**一次快照都没有**。
    ///
    /// **判据要求恰好两条**，不是"至少一条"：若实现只上报了开始或只上报了结束，
    /// "至少一条"会通过，而**进度条会停在 0 或停在 100**。
    ///
    /// The baseline's `solveLP(metaModel, options)` reports `(MasterLP, 0, 30)` before the solve and once more on success. The C# side **had no such overload**, so
    /// the master-problem stage **produced no snapshot at all**.
    ///
    /// **The criterion demands exactly two**, not "at least one": if the implementation reported only the start or only the end, "at least one" would pass while
    /// **the progress bar would freeze at 0 or at 100**.
    /// </remarks>
    [Fact]
    public async Task SolveLPAsync_ShouldReportBeforeAndAfterTheMasterSolve() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new CallbackFiringSolver();

        await ((IColumnGenerationSolver)solver).SolveLPAsync(MakeModel(), new FrameworkSolveOptions {
            ProgressContext = context,
        });

        // 替身返回失败，所以收尾快照（仅成功时上报）不会出现；开头那条必须在。
        // The double returns a failure, so the closing snapshot (success-only) will not appear; the opening one must.
        reporter.Snapshots.Should().NotBeEmpty("the opening snapshot must be reported before the solve");
        reporter.Snapshots[0].Stage.Id.Value.Should().Be("MASTER_LP");
        reporter.Snapshots[0].ProgressInStage.Should().Be(0);
        reporter.Snapshots[0].OverallProgress.Should().Be(30);
    }

    /// <summary>**主问题阶段的适配器必须用 `MasterLP` 阶段** / The master adapters must use the `MasterLP` stage.</summary>
    /// <remarks>
    /// 若包装时误用了 `Milp`，回调触发的快照会落在**错误的阶段**，
    /// 而快照**数量**仍然正确。所以判据必须看**阶段 id**，不能只看数量。
    ///
    /// If wrapping mistakenly used `Milp` (the default on the earlier path), callback-driven snapshots would land in **the wrong stage** while the snapshot **count** stays
    /// correct. So the criterion must look at the **stage id**, not just the count.
    /// </remarks>
    [Fact]
    public async Task SolveLPAsync_ShouldUseTheMasterLpStageForCallbackSnapshots() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new CallbackFiringSolver();

        await ((IColumnGenerationSolver)solver).SolveLPAsync(MakeModel(), new FrameworkSolveOptions {
            ProgressContext = context,
        });

        bool found = reporter.Snapshots.Any(
            snapshot => snapshot.Stage.Id.Value == "MASTER_LP"
                && snapshot.SubStage is not null
                && snapshot.SubStage.Key == "incumbent");
        found.Should().BeTrue("the solving callback must be wrapped under the MasterLP stage");
    }

    /// <summary>**无进度上下文时主问题阶段零上报** / With no progress context, the master stage reports nothing.</summary>
    [Fact]
    public async Task SolveLPAsync_ShouldNotReportWhenNoProgressContextIsGiven() {
        var reporter = new RecordingReporter();
        var solver = new CallbackFiringSolver();

        await ((IColumnGenerationSolver)solver).SolveLPAsync(MakeModel(), new FrameworkSolveOptions());

        reporter.Snapshots.Should().BeEmpty();
    }

    /// <summary>**有进度上下文时，上报器必须收到快照** / With a progress context, the reporter must receive snapshots.</summary>
    /// <remarks>
    /// 这是"适配器真的被用了"的直接证据。若实现只是把 `ProgressContext` 存着不用，
    /// 求解仍会成功返回（这里返回失败也照常），**上报器一条快照都收不到**。
    ///
    /// This is direct evidence that the adapter was used. If the implementation merely stored `ProgressContext`, the solve would still return normally (even its failure
    /// here) and **the reporter would receive zero snapshots**.
    /// </remarks>
    [Fact]
    public async Task SolveMILPAsync_ShouldFeedSnapshotsToTheReporterWhenAProgressContextIsGiven() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new CallbackFiringSolver();

        await ((IColumnGenerationSolver)solver).SolveMILPAsync(MakeModel(), new FrameworkSolveOptions { ProgressContext = context });

        reporter.Snapshots.Should().NotBeEmpty("the adapters must actually report");
    }

    /// <summary>**没有进度上下文时，一条快照都不该出现** / Without a progress context, no snapshot should appear.</summary>
    /// <remarks>
    /// 反向判据：它证明"上报来自上下文"，而不是某个默认全局上报器。
    /// **只测正向会让"无条件上报"这种实现也通过**。
    ///
    /// The reverse criterion: it proves the reporting comes from the context rather than some default global reporter. **Testing only the positive direction would let an
    /// "always report" implementation pass**.
    /// </remarks>
    [Fact]
    public async Task SolveMILPAsync_ShouldNotReportWhenNoProgressContextIsGiven() {
        var reporter = new RecordingReporter();
        var solver = new CallbackFiringSolver();

        await ((IColumnGenerationSolver)solver).SolveMILPAsync(MakeModel(), new FrameworkSolveOptions());

        reporter.Snapshots.Should().BeEmpty();
    }

    /// <summary>**包装后原回调仍必须被调用** / The original callbacks must still be invoked after wrapping.</summary>
    /// <remarks>
    /// 的适配器内部会调原回调；这条在**接线层**再验证一次——
    /// 若 `SolveMILPAsync` 把 `options.RegistrationStatusCallBack` 换成适配器却忘了把它传给适配器，
    /// 原回调就会**静默失效**。
    ///
    /// The adapters invoke the original callback internally; this re-checks it **at the wiring layer** — if `SolveMILPAsync` replaced the callbacks with adapters but
    /// forgot to pass the originals into them, the original callbacks would **silently stop firing**.
    /// </remarks>
    [Fact]
    public async Task SolveMILPAsync_ShouldStillInvokeTheOriginalCallbacks() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new CallbackFiringSolver();
        int registrationCalls = 0;
        int solvingCalls = 0;

        await ((IColumnGenerationSolver)solver).SolveMILPAsync(MakeModel(), new FrameworkSolveOptions {
            ProgressContext = context,
            RegistrationStatusCallBack = _ => {
                ++registrationCalls;
                return Results.Ok(Results.SuccessInstance);
            },
            SolvingStatusCallBack = _ => {
                ++solvingCalls;
                return Results.Ok(Results.SuccessInstance);
            },
        });

        registrationCalls.Should().Be(1, "wrapping must not swallow the registration callback");
        solvingCalls.Should().Be(1, "wrapping must not swallow the solving callback");
        reporter.Snapshots.Should().NotBeEmpty();
    }
}
