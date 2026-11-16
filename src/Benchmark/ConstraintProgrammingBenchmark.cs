#nullable enable

using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// CP 构建、求解与可移植恢复基准 / CP build, solve, and portable restore benchmarks.
/// </summary>
/// <remarks>
/// **刻意使用 Fake 求解器**，使其不依赖原生库即可执行；SCIP 集成耗时由插件集成套件单独采集。
///
/// **Deliberately uses the Fake solver** so it runs without a native library; SCIP integration timings are collected by the plugin integration suite
/// separately.
/// </remarks>
[MemoryDiagnoser]
public class ConstraintProgrammingBenchmark {
    /// <summary>夹具规模 / Fixture size.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    /// <summary>夹具的固定配置指纹 / The fixture's fixed configuration fingerprint.</summary>
    private const string BenchmarkConfigurationFingerprint =
        "benchmark-configuration-fingerprint";

    /// <summary>
    /// 可求解夹具的作业数 / Job count of the solvable fixtures.
    /// </summary>
    /// <remarks>
    /// **固定 4，不随 `Dataset` 放大**。Fake 求解器是**纯枚举、无传播**：`n` 个作业各 `n+2` 个起点就是
    /// `(n+2)^n` 次赋值。实测 `large`（8 作业）单次排程求解 **357 秒**，而 `small`（4 作业）24 毫秒——
    /// 把它挂到 `Dataset` 上会让 `large` 这一档的基准跑不完，**而 BenchmarkDotNet 不会因此报错，只会一直跑**。
    /// 降阶基准不受此限（降阶是多项式复杂度），所以它们仍然随 `Dataset` 放大。
    ///
    /// **Fixed at 4 and not scaled by `Dataset`.** The Fake solver is **pure enumeration with no propagation**: `n` jobs with `n+2` start values each is
    /// `(n+2)^n` assignments. Measured: `large` (8 jobs) takes **357 seconds** per scheduling solve while `small` (4 jobs) takes 24 ms — tying it to
    /// `Dataset` makes the `large` tier impossible to finish, **and BenchmarkDotNet would not report an error, it would simply keep running**. The lowering
    /// benchmarks are unaffected (lowering is polynomial), so they still scale with `Dataset`.
    /// </remarks>
    private const int SolvableJobCount = 4;

    /// <summary>直接夹具（供冒烟测试断言约束入模）/ The direct fixture (so smoke tests can assert constraints landed).</summary>
    internal ConstraintProgrammingModel DirectModel => _directModel;

    /// <summary>排程降阶夹具 / The scheduling lowering fixture.</summary>
    internal ConstraintProgrammingModel SchedulingModel => _schedulingModel;

    private ConstraintProgrammingModel _directModel = null!;
    private ConstraintProgrammingModel _schedulingModel = null!;
    private ConstraintProgrammingModel _optionalSchedulingModel = null!;
    private ConstraintProgrammingModel _variableDurationSchedulingModel = null!;
    private ConstraintProgrammingModel _solvableSchedulingModel = null!;
    private ConstraintProgrammingModel _solvableOptionalSchedulingModel = null!;
    private ConstraintProgrammingModel _solvableVariableDurationSchedulingModel = null!;
    private LinearMetaModel<Flt64> _bendersMasterModel = null!;
    private ConstraintProgrammingModel _bendersSubproblemModel = null!;
    private LogicBasedBendersEngine _bendersEngine = null!;
    private string _checkpointJson = string.Empty;
    private FakeConstraintProgrammingSolver _solver = null!;

    /// <summary>每个基准状态构建一次夹具 / Build the fixtures once per benchmark state.</summary>
    [GlobalSetup]
    public void Setup() {
        int count = Dataset switch {
            "small" => 4,
            "medium" => 6,
            "large" => 8,
            _ => 4,
        };
        _directModel = BuildDirectModel(count);
        _schedulingModel = BuildSchedulingModel(count);
        _optionalSchedulingModel = BuildOptionalSchedulingModel(count);
        _variableDurationSchedulingModel = BuildVariableDurationSchedulingModel(count);
        // **三份可求解夹具的作业数也必须来自 `Dataset`**：此前写死 4 / 3 / 3，
        // 于是三个排程求解基准三档测同一条路径。
        //
        // **但不能直接用 `count`**：`FakeConstraintProgrammingSolver` 是**穷举**的，作业数一上去组合数就爆
        // （实测 `count=8` 时单个测试跑了 **5 分 37 秒**）。所以这三份走**更缓的进阶** `count / 2 + 1` = 3 / 4 / 5：
        // 规模确实进到被测路径，穷举仍然跑得完。**基准要能反复跑，比"规模更大"重要。**
        //
        // **The three solvable fixtures must take their job count from `Dataset`**: they were hard-coded to 4 / 3 / 3, so the three scheduling solves measured one
        // path. **But not `count` directly**: `FakeConstraintProgrammingSolver` **enumerates**, and the combination count explodes as jobs grow (measured: `count=8` took
        // **5 minutes 37 seconds** for a single test). These three therefore use a **gentler** progression `count / 2 + 1` = 3 / 4 / 5 — the scale reaches the measured path
        // and the enumeration still finishes. **A benchmark that runs repeatedly matters more than a bigger scale.**
        int solvableJobs = (count / 2) + 1;
        _solvableSchedulingModel = BuildSolvableSchedulingModel(
            solvableJobs,
            "benchmark-solvable-scheduling");
        _solvableOptionalSchedulingModel = BuildSolvableSchedulingModel(
            solvableJobs,
            "benchmark-solvable-optional-scheduling");
        _solvableVariableDurationSchedulingModel = BuildSolvableSchedulingModel(
            solvableJobs,
            "benchmark-solvable-variable-duration");
        _solver = new FakeConstraintProgrammingSolver();
        (_bendersMasterModel, _bendersSubproblemModel, _bendersEngine) = BuildBenders(count);
        _checkpointJson = CaptureCheckpoint();
    }

    /// <summary>释放夹具 / Release the fixtures.</summary>
    [GlobalCleanup]
    public void Cleanup() {
        _directModel.Dispose();
        _schedulingModel.Dispose();
        _optionalSchedulingModel.Dispose();
        _variableDurationSchedulingModel.Dispose();
        _solvableSchedulingModel.Dispose();
        _solvableOptionalSchedulingModel.Dispose();
        _solvableVariableDurationSchedulingModel.Dispose();
        _bendersMasterModel.Dispose();
        _bendersSubproblemModel.Dispose();
    }

    /// <summary>测量 CP snapshot 编码 / Measure CP snapshot encoding.</summary>
    /// <returns>编码后的 snapshot 长度 / Encoded snapshot length.</returns>
    [Benchmark]
    public int SnapshotEncode() =>
        ConstraintProgrammingSnapshotCodec.Encode(_directModel.Snapshot().Value).Value.Length;

    /// <summary>测量直接 CP 枚举求解 / Measure direct CP enumeration solve.</summary>
    /// <returns>求解结果的字符串表示 / String representation of the solver result.</returns>
    [Benchmark]
    public string DirectSolve() =>
        _solver.SolveAsync(_directModel).GetAwaiter().GetResult().Value?.ToString()
        ?? string.Empty;

    /// <summary>测量固定时长排程求解 / Measure fixed-duration scheduling solve.</summary>
    /// <returns>求解结果的字符串表示 / String representation of the solver result.</returns>
    [Benchmark]
    public string SchedulingSolve() =>
        _solver.SolveAsync(_solvableSchedulingModel).GetAwaiter().GetResult().Value?.ToString()
        ?? string.Empty;

    /// <summary>测量可选固定时长排程求解 / Measure optional fixed-duration scheduling solve.</summary>
    /// <returns>求解结果的字符串表示 / String representation of the solver result.</returns>
    [Benchmark]
    public string OptionalSchedulingSolve() =>
        _solver.SolveAsync(_solvableOptionalSchedulingModel).GetAwaiter().GetResult().Value
            ?.ToString()
        ?? string.Empty;

    /// <summary>测量可变时长排程求解 / Measure variable-duration scheduling solve.</summary>
    /// <returns>求解结果的字符串表示 / String representation of the solver result.</returns>
    [Benchmark]
    public string VariableDurationSchedulingSolve() =>
        _solver.SolveAsync(_solvableVariableDurationSchedulingModel)
            .GetAwaiter().GetResult().Value?.ToString()
        ?? string.Empty;

    /// <summary>测量固定时长 exact MIP 分解构建 / Measure fixed-duration exact MIP decomposition build.</summary>
    /// <returns>生成的关系约束数量 / Number of generated relation constraints.</returns>
    [Benchmark]
    public int FixedDurationMipLowering() => LowerAndCountConstraints(_schedulingModel);

    /// <summary>测量可选固定时长 exact MIP 分解构建 / Measure optional fixed-duration exact MIP decomposition build.</summary>
    /// <returns>生成的关系约束数量 / Number of generated relation constraints.</returns>
    [Benchmark]
    public int OptionalDurationMipLowering() =>
        LowerAndCountConstraints(_optionalSchedulingModel);

    /// <summary>测量可变时长 exact MIP 分解构建 / Measure variable-duration exact MIP decomposition build.</summary>
    /// <returns>生成的关系约束数量 / Number of generated relation constraints.</returns>
    [Benchmark]
    public int VariableDurationMipLowering() =>
        LowerAndCountConstraints(_variableDurationSchedulingModel);

    /// <summary>主问题变量数 / Master variable count.</summary>
    /// <remarks>
    /// 给规模有效性判据读的：`BendersSolve` 的返回值是结果字符串，**看不出规模**，所以判据需要这个数字。
    /// 它必须由 `Setup` 按 `Dataset` 填出来——**否则三档测的还是同一条路径**。
    ///
    /// Read by the scale-validity criterion: `BendersSolve` returns the result string, which **shows no scale**, so the criterion needs this number. `Setup` must fill
    /// it from `Dataset` — **otherwise all three tiers measure one path** .
    /// </remarks>
    public int MasterVariableCount => _bendersMasterModel.Tokens.Tokens.Count;

    /// <summary>可求解排程夹具的变量数 / Variable count of the solvable scheduling fixture.</summary>
    /// <remarks>
    /// 给规模有效性判据读的。`SchedulingSolve` 返回的是**结果的 `ToString`**——那个字符串长度**与作业数无关**
    /// （三档都是 2910 字符），所以判据必须读这个数字。
    /// **它量的是求解器真正消费的那个模型**，不是"配置里写了多少"。
    ///
    /// Read by the scale-validity criterion. `SchedulingSolve` returns the **result's `ToString`**, whose length **does not depend on the job count** (measured at
    /// 2910 characters for all three tiers), so the criterion must read this number. **It measures the model the solver actually consumes**, not what the
    /// configuration says.
    /// </remarks>
    public int SolvableSchedulingVariableCount =>
        _solvableSchedulingModel.Snapshot().Value.Variables.Count;

    /// <summary>测量一次 Fake CP 逻辑 Benders 求解 / Measure one Fake CP logic-based Benders run.</summary>
    /// <returns>求解结果的字符串表示 / String representation of the solver result.</returns>
    [Benchmark]
    public string BendersSolve() =>
        _bendersEngine
            .SolveAsync(_bendersMasterModel, _bendersSubproblemModel)
            .GetAwaiter()
            .GetResult()
            .Value?.ToString()
        ?? string.Empty;

    /// <summary>测量可移植 checkpoint 捕获与摘要复验 / Measure portable checkpoint capture and digest verification.</summary>
    /// <returns>编码后的 checkpoint JSON / Encoded checkpoint JSON.</returns>
    [Benchmark]
    public string CheckpointCapture() => CaptureCheckpoint();

    /// <summary>测量可移植 checkpoint 解码与恢复 / Measure portable checkpoint decode and restore.</summary>
    /// <returns>恢复出的 checkpoint 标识 / The restored checkpoint identifier.</returns>
    [Benchmark]
    public string CheckpointRestore() {
        Result<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> decoded =
            ConstraintProgrammingCheckpointCodec.DecodeCompatible(_checkpointJson);
        if (decoded is not Ok<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>> envelope) {
            return string.Empty;
        }
        Result<
            ConstraintProgrammingCheckpointRestore,
            ErrorCode,
            Error<ErrorCode>> restored = ConstraintProgrammingCheckpointCodec.Restore(
                envelope: envelope.Value,
                snapshot: _directModel.Snapshot().Value,
                expectedConfigurationFingerprint: envelope.Value.ConfigurationFingerprint,
                expectedSolverFingerprint: envelope.Value.SolverFingerprint);
        return restored is Ok<
                ConstraintProgrammingCheckpointRestore,
                ErrorCode,
                Error<ErrorCode>> restoreOk
            ? restoreOk.Value.Envelope.CheckpointId
            : string.Empty;
    }

    private static ConstraintProgrammingModel BuildDirectModel(int count) {
        var model = new ConstraintProgrammingModel(
            name: "benchmark-direct",
            objectCategory: ObjectCategory.Minimum);
        var variables = new List<IntVar>(count);
        for (int index = 0; index < count; ++index) {
            var variable = new IntVar($"direct-{index}");
            variables.Add(variable);
            model.RegisterVariable(variable, domain: IntegerDomain.Boolean);
            model.AddConstraint(
                ConstraintProgrammingConstraint.CreateLessOrEqual(
                    ConstraintProgrammingExpression.CreateVariable(variable).Value,
                    Int64.One).Value,
                id: new ConstraintId($"direct-bound-{index}"));
        }
        model.Minimize(
            ConstraintProgrammingExpression.CreateVariable(variables[0]).Value);
        return model;
    }

    /// <summary>
    /// 固定时长排程夹具（给降阶基准）/ The fixed-duration scheduling fixture (for the lowering benchmarks).
    /// </summary>
    /// <remarks>
    /// **降阶与求解对 `end` 的要求互相冲突，所以夹具必须分开两份**：
    /// 降阶器要求"interval 的 start/end 必须是标量变量"（`Unsupported: interval start/end 必须是标量变量`），
    /// 而 Fake 求解器要求候选赋值满足 `end = start + size`——**`end` 一旦是自由变量，
    /// 枚举到第一个不满足的赋值就整次求解失败**（`recordEvaluationFailure` → `stopped = true`）。
    /// 因此这一份保留自由 `end` 变量只喂降阶基准，求解基准走 `BuildSolvableSchedulingModel`。
    ///
    /// 有一份夹具，于是条 `schedulingSolve` 基准**测的是一次失败路径**，从未真正枚举过排程。
    ///
    /// **Lowering and solving disagree about `end`, so the fixture must exist twice.** The lowerer requires "interval start/end must be scalar variables"
    /// (`Unsupported: interval start/end must be scalar variables`), while the Fake solver requires candidate assignments to satisfy `end = start + size` —
    /// **once `end` is a free variable, the first assignment that violates it fails the whole solve** (`recordEvaluationFailure` → `stopped = true`). This
    /// copy keeps the free `end` variable and only feeds the lowering benchmarks; the solve benchmarks use `BuildSolvableSchedulingModel`.
    ///
    /// The code has a single fixture, so its `schedulingSolve` benchmark **measures a failure path** and never really enumerates a schedule.
    /// </remarks>
    private static ConstraintProgrammingModel BuildSchedulingModel(int count) {
        var model = new ConstraintProgrammingModel(
            name: "benchmark-scheduling",
            objectCategory: ObjectCategory.Minimum);
        var intervals = new List<IntervalVariable>(count);
        for (int index = 0; index < count; ++index) {
            var start = new IntVar($"start-{index}");
            var end = new IntVar($"end-{index}");
            model.RegisterVariable(
                start,
                domain: IntegerDomain.CreateInterval(
                    new Int64(0),
                    new Int64(count + 1)).Value);
            model.RegisterVariable(
                end,
                domain: IntegerDomain.CreateInterval(
                    new Int64(1),
                    new Int64(count * 2)).Value);
            IntervalVariable interval = IntervalVariable.CreateFixed(
                id: $"job-{index}",
                start: ConstraintProgrammingExpression.CreateVariable(start).Value,
                size: Int64.One,
                end: ConstraintProgrammingExpression.CreateVariable(end).Value).Value;
            model.RegisterInterval(interval);
            intervals.Add(interval);
        }
        model.AddConstraint(
            NoOverlap.Create(intervals).Value,
            id: new ConstraintId("schedule-no-overlap"));
        return model;
    }

    /// <summary>
    /// 可求解的排程夹具 / A solvable scheduling fixture.
    /// </summary>
    /// <remarks>
    /// `end` 写成派生表达式 `start + 1`：Fake 求解器逐赋值求值 interval，派生式保证恒等式成立。
    /// 代价是这份夹具**不能降阶**（降阶器只接受标量 start/end 变量），这正是两份夹具并存的原因。
    ///
    /// `end` is written as the derived expression `start + 1`: the Fake solver evaluates the interval per assignment, and a derived expression keeps the
    /// identity true. The cost is that this fixture **cannot be lowered** (the lowerer only accepts scalar start/end variables), which is exactly why two
    /// fixtures coexist.
    /// </remarks>
    /// <param name="count">作业数量 / Job count.</param>
    /// <param name="name">模型名 / Model name.</param>
    /// <returns>可求解夹具 / The solvable fixture.</returns>
    private static ConstraintProgrammingModel BuildSolvableSchedulingModel(
        int count,
        string name) {
        var model = new ConstraintProgrammingModel(
            name: name,
            objectCategory: ObjectCategory.Minimum);
        var intervals = new List<IntervalVariable>(count);
        for (int index = 0; index < count; ++index) {
            var start = new IntVar($"solvable-start-{index}");
            model.RegisterVariable(
                start,
                domain: IntegerDomain.CreateInterval(
                    new Int64(0),
                    new Int64(count + 1)).Value);
            IntervalVariable interval = IntervalVariable.CreateFixed(
                id: $"solvable-job-{index}",
                start: ConstraintProgrammingExpression.CreateVariable(start).Value,
                size: Int64.One,
                end: ConstraintProgrammingExpression.Add(
                    ConstraintProgrammingExpression.CreateVariable(start).Value,
                    ConstraintProgrammingExpression.CreateConstant(1)).Value).Value;
            model.RegisterInterval(interval);
            intervals.Add(interval);
        }
        model.AddConstraint(
            NoOverlap.Create(intervals).Value,
            id: new ConstraintId("solvable-no-overlap"));
        return model;
    }

    private static ConstraintProgrammingModel BuildOptionalSchedulingModel(int count) {
        var model = new ConstraintProgrammingModel(
            name: "benchmark-optional-scheduling",
            objectCategory: ObjectCategory.Minimum);
        var intervals = new List<IntervalVariable>(count);
        for (int index = 0; index < count; ++index) {
            var start = new IntVar($"optional-start-{index}");
            var presence = new BinVar($"optional-presence-{index}");
            model.RegisterVariable(
                start,
                domain: IntegerDomain.CreateInterval(new Int64(0), new Int64(6)).Value);
            model.RegisterVariable(presence);
            var end = new IntVar($"optional-end-{index}");
            model.RegisterVariable(
                end,
                domain: IntegerDomain.CreateInterval(new Int64(0), new Int64(8)).Value);
            IntervalVariable interval = IntervalVariable.CreateFixed(
                id: $"optional-job-{index}",
                start: ConstraintProgrammingExpression.CreateVariable(start).Value,
                size: Int64.One,
                end: ConstraintProgrammingExpression.CreateVariable(end).Value,
                presence: new BooleanLiteral.VariableLiteral(presence)).Value;
            model.RegisterInterval(interval);
            intervals.Add(interval);
        }
        model.AddConstraint(
            NoOverlap.Create(intervals).Value,
            id: new ConstraintId("optional-no-overlap"));
        return model;
    }

    private static ConstraintProgrammingModel BuildVariableDurationSchedulingModel(int count) {
        var model = new ConstraintProgrammingModel(
            name: "benchmark-variable-duration",
            objectCategory: ObjectCategory.Minimum);
        var intervals = new List<IntervalVariable>(count);
        for (int index = 0; index < count; ++index) {
            var start = new IntVar($"variable-start-{index}");
            var size = new IntVar($"variable-size-{index}");
            model.RegisterVariable(
                start,
                domain: IntegerDomain.CreateInterval(new Int64(0), new Int64(6)).Value);
            model.RegisterVariable(
                size,
                domain: IntegerDomain.CreateInterval(new Int64(1), new Int64(3)).Value);
            var end = new IntVar($"variable-end-{index}");
            model.RegisterVariable(
                end,
                domain: IntegerDomain.CreateInterval(new Int64(1), new Int64(9)).Value);
            IntervalVariable interval = IntervalVariable.Create(
                id: $"variable-job-{index}",
                start: ConstraintProgrammingExpression.CreateVariable(start).Value,
                size: ConstraintProgrammingExpression.CreateVariable(size).Value,
                end: ConstraintProgrammingExpression.CreateVariable(end).Value).Value;
            model.RegisterInterval(interval);
            intervals.Add(interval);
        }
        model.AddConstraint(
            NoOverlap.Create(intervals).Value,
            id: new ConstraintId("variable-no-overlap"));
        return model;
    }

    private (LinearMetaModel<Flt64>, ConstraintProgrammingModel, LogicBasedBendersEngine)
        BuildBenders(int count) {
        // **规模必须真的进到被测路径**：这张夹具此前**丢掉了 `Dataset`**，三档测的是同一条路径
        // （实测 small/medium/large = 45.4/47.1/45.6 µs，分配**完全相同** 129.16 KB）。现在主问题变量、子问题变量、
        // 子问题约束与绑定条数全部按 `count` 生成。
        //
        // **The scale must reach the measured path**: this fixture **dropped `Dataset`**, so all three tiers measured one path (measured
        // small/medium/large = 45.4/47.1/45.6 µs with **identical** 129.16 KB allocations). Master variables, subproblem variables, subproblem constraints and
        // bindings are all generated from `count` now.
        var master = new LinearMetaModel<Flt64>(name: "benchmark-benders-master");
        var subproblem = new ConstraintProgrammingModel(
            name: "benchmark-benders-subproblem");
        var masterVariables = new List<IVariableItem>(count);
        var bindings = new List<BinaryBendersVariable>(count);
        for (int index = 0; index < count; ++index) {
            var masterVariable = new BinVar($"benchmark-benders-master-{index}");
            master.Add(masterVariable);
            masterVariables.Add(masterVariable);
            var subproblemVariable = new BinVar($"benchmark-benders-subproblem-{index}");
            var variableId = new VariableId($"benders:decision:{index}");
            subproblem.RegisterVariable(variableId, subproblemVariable, IntegerDomain.Boolean);
            subproblem.AddConstraint(
                ConstraintProgrammingConstraint.CreateEqual(
                    ConstraintProgrammingExpression.CreateVariable(subproblemVariable).Value,
                    Int64.One).Value,
                id: new ConstraintId($"benders-decision-required-{index}"));
            bindings.Add(new BinaryBendersVariable(
                Key: $"benders{index}",
                MasterVariable: masterVariable,
                SubproblemVariable: subproblemVariable,
                SubproblemVariableId: variableId));
        }
        var binding = new BinaryBendersVariableBinding(bindings);
        var engine = new LogicBasedBendersEngine(
            masterSolver: model => Task.FromResult(Results.Ok(MasterReport(
                model.RelationConstraints.Count == 0 ? Flt64.Zero : Flt64.One, count))),
            subproblemSolver: _solver,
            binding: binding,
            options: new LogicBasedBendersOptions {
                MaxIterations = 4,
                MasterSolutionSource = output => Results.Ok<IConstraintProgrammingValueSource>(
                    MapConstraintProgrammingValueSource.Of(MasterValues(output, masterVariables))),
            });
        return (master, subproblem, engine);
    }

    /// <summary>**把主问题解映射成每条绑定键的值** / Map the master solution onto one value per binding key.</summary>
    /// <param name="output">主问题输出 / The master output.</param>
    /// <param name="masterVariables">主问题变量 / The master variables.</param>
    /// <returns>键到值的映射 / Key-to-value map.</returns>
    /// <remarks>
    /// 之前这里只写死一个 `"benders"` 键，于是**绑定条数一变就取不到值**——规模参数能进夹具，却进不了这条映射。
    ///
    /// Before this hard-coded a single `"benders"` key, so **any change in the binding count lost the values** — the scale parameter could reach the fixture
    /// but not this map.
    /// </remarks>
    private static Dictionary<string, Flt64> MasterValues(
        SolveReport<Flt64> output,
        IReadOnlyList<IVariableItem> masterVariables) {
        var values = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        for (int index = 0; index < masterVariables.Count; ++index) {
            values[$"benders{index}"] = output.Solution!.Values[masterVariables[index].Index];
        }
        return values;
    }

    private static SolveReport<Flt64> MasterReport(Flt64 value, int count) =>
        new() {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Optimal,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(Enumerable.Repeat(value, count).ToArray()),
                Objective: value),
            Proof = new SolveProof(ProofStatus.Verified, "benchmark-master"),
            Statistics = new SolveStatistics(
                SolveTime: TimeSpan.Zero,
                BestBound: value),
        };

    private string CaptureCheckpoint() {
        Result<
            ConstraintProgrammingCheckpointEnvelope,
            ErrorCode,
            Error<ErrorCode>> captured =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: _directModel.Snapshot().Value,
                descriptor: _solver.Descriptor,
                checkpointId: "benchmark-checkpoint",
                createdAtEpochMs: 0L,
                // **必须提供配置指纹**：v2 恢复要求当前配置与求解器指纹都非空，
                // 而捕获时不填会让 `configurationFingerprint` 为 `null`——于是恢复被拒，
                // **基准却照样跑完并给出一个"恢复=空串"的耗时**。
                // **The configuration fingerprint is required**: restoring a v2 checkpoint demands non-blank current configuration and solver
                // fingerprints, and omitting it at capture leaves `configurationFingerprint` `null` — so the restore is rejected while
                // **the benchmark still completes and reports a timing for "restore = empty string"**.
                configurationFingerprint: BenchmarkConfigurationFingerprint);
        return captured is Ok<
                ConstraintProgrammingCheckpointEnvelope,
                ErrorCode,
                Error<ErrorCode>> envelope
            ? ConstraintProgrammingCheckpointCodec.Encode(envelope.Value).Value
                ?? string.Empty
            : string.Empty;
    }

    private static int LowerAndCountConstraints(ConstraintProgrammingModel model) {
        Result<
            ConstraintProgrammingLoweredLinearModel,
            ErrorCode,
            Error<ErrorCode>> lowered =
            new ConstraintProgrammingToLinearModelLowerer().Lower(model);
        if (lowered is not Ok<
                ConstraintProgrammingLoweredLinearModel,
                ErrorCode,
                Error<ErrorCode>> loweredOk) {
            return 0;
        }
        using ConstraintProgrammingLoweredLinearModel linear = loweredOk.Value;
        return linear.Model.RelationConstraints.Count;
    }
}
