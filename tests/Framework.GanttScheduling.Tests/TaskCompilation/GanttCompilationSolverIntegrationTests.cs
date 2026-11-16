// 真实 SCIP 端到端判据 / Real-SCIP end-to-end criteria.

#if SCIP_INTEGRATION
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **真实 SCIP 端到端**：桩只证明接线，这里证明能解 / Real SCIP end to end: the stubs proved the wiring, this proves it solves.
/// </summary>
/// <remarks>
/// 只在 `-p:RunScipIntegration=true` 时编译并运行（常量在 **build** 阶段定义）。默认全量测试里这些用例**不存在**，
/// 因此默认通过数不含它们——能力文档里两个数分开报。
///
/// 之前所有编译判据都是**看模型内容**（约束条数、多项式成分、变量值域）。那些判据在"约束写对了但模型不可解"时
/// 仍然全绿。这里把模型交给真实 SCIP：**能解出来，且解满足约束**，才算接线完成。
///
/// Compiled and run only with `-p:RunScipIntegration=true` (the constant is defined at **build** time). In the default suite these cases **do not exist**, so the two pass counts are
/// reported separately in the capability document.
///
/// Every earlier compilation criterion inspected the **model content** (constraint counts, polynomial make-up, variable ranges). Those stay green even when "the constraints are written
/// right but the model is unsolvable". hands the model to real SCIP: the wiring counts as done only when it **solves and the solution satisfies the constraints**.
/// </remarks>
public class GanttCompilationSolverIntegrationTests {
    /// <summary>
    /// **普通编译的模型在真实 SCIP 上可解，且每个任务恰好被一个执行器承接** / The plain compilation solves under real SCIP with exactly one executor per task.
    /// </summary>
    /// <remarks>
    /// `taskCompilation[t] == 1` 就是"每个任务必须被完成或明确取消"。写了它却不可解，说明约束之间自相矛盾；
    /// 没写它则解里所有列都是 0——两种都会被这条判据挡下。
    ///
    /// `taskCompilation[t] == 1` is "every task must be done or explicitly cancelled". Written but unsolvable means the constraints contradict each other; not written means every
    /// column is 0 in the solution — both are caught here.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task PlainCompilation_ShouldSolveUnderRealScip() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var tasks = new List<TestTask> { EnabledTask("T1", executors), EnabledTask("T2", executors) };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();

        // 目标：每列系数 1，最小化选中列数。于是解必然是"每任务恰好一列"。
        // Objective: coefficient 1 per column, minimising the number of selected columns. The solution is then forced to "exactly one column per task".
        LinearMonomial<Flt64>[] monomials = compilation.Assignment.Values
            .Cast<Fuookami.Ospf.Core.Variable.CombinationVariableItem<UInt8, Fuookami.Ospf.Core.Variable.Binary>>()
            .Select(variable => new LinearMonomial<Flt64>(Flt64.One, variable))
            .ToArray();
        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            name: "cost",
            displayName: null).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-plain", model);

        result.IsOk.Should().BeTrue("two tasks on two executors is trivially feasible: " + result);
        double total = compilation.Assignment.Values
            .Cast<Fuookami.Ospf.Core.Variable.CombinationVariableItem<UInt8, Fuookami.Ospf.Core.Variable.Binary>>()
            .Sum(variable => model.Tokens.Find(variable)!.Result!.Value.ToDouble());
        total.Should().BeApproximately(2.0, 1e-6, "exactly one executor per task, two tasks");
    }

    /// <summary>
    /// **被固定为 0 的组合在解里真的是 0** / A combination pinned to 0 really is 0 in the solution.
    /// </summary>
    /// <remarks>
    /// 值域收紧只写进变量的 `Range`。若某条路径把它丢了，模型**仍然可解**——只是求解器会去选那条本该禁止的组合。
    /// 判据让 `T1` 只允许 `E2`，然后看解里 `(T1, E1)` 是不是 0。
    ///
    /// Range tightening only lands on the variable's `Range`. If some path drops it the model **still solves** — the solver merely selects the combination that should be
    /// forbidden. The criterion lets `T1` use only `E2`, then checks whether `(T1, E1)` is 0 in the solution.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task PlainCompilation_ShouldHonourThePinnedRangeUnderRealScip() {
        var executor1 = new TestExecutor("E1", "E1");
        var executor2 = new TestExecutor("E2", "E2");
        // `TestTask.Executor` 一旦设上、且 `ExecutorChangeEnabled` 保持接口默认的 false，
        // 收紧逻辑就会把别的执行器固定为 0、把这个执行器固定为 1。
        // Once `TestTask.Executor` is set and `ExecutorChangeEnabled` keeps the interface default of false, tightening pins the other executors to 0 and this one to 1.
        var task = new TestTask("T1", "T1") { Executor = executor2 };
        foreach (TestExecutor executor in new[] { executor1, executor2 }) {
            task.EnabledExecutorSet.Add(executor);
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { task }, new List<TestExecutor> { executor1, executor2 });
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();

        // 目标让 `E1` **更便宜**：收紧若失效，求解器会毫不犹豫地选它。
        // The objective makes `E1` **cheaper**: if tightening is lost, the solver picks it without hesitating.
        var forbiddenVariable = (Fuookami.Ospf.Core.Variable.CombinationVariableItem<UInt8, Fuookami.Ospf.Core.Variable.Binary>)
            compilation.Assignment[(task, executor1)];
        var requiredVariable = (Fuookami.Ospf.Core.Variable.CombinationVariableItem<UInt8, Fuookami.Ospf.Core.Variable.Binary>)
            compilation.Assignment[(task, executor2)];
        LinearMonomial<Flt64>[] monomials = new[] {
            new LinearMonomial<Flt64>(Flt64.One, forbiddenVariable),
            new LinearMonomial<Flt64>(new Flt64(100.0), requiredVariable),
        };
        model.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            name: "cost",
            displayName: null).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-pinned", model);

        result.IsOk.Should().BeTrue("the task has a valid executor: " + result);
        model.Tokens.Find(forbiddenVariable)!.Result!.Value.ToDouble().Should().BeApproximately(
            0.0, 1e-6, "the task's executor is fixed to E2, so E1 is pinned to 0 even though it is cheaper");
        model.Tokens.Find(requiredVariable)!.Result!.Value.ToDouble().Should().BeApproximately(
            1.0, 1e-6, "the fixed executor must be selected");
    }

    /// <summary>
    /// **代价口径真的驱动选择**：两个执行器成本不同，最小化 `bunch_cost` 必须选便宜的那个 / Different executor costs: minimising `bunch_cost` must pick the cheaper one.
    /// </summary>
    /// <remarks>
    /// 这一条覆盖 接线的整条链路：`AddColumns` 把成本写进 `bunch_cost` → `TaskCostMinimization` 把 `bunch_cost` 加进目标 →
    /// 真实求解器解出来。任何一环错，答案都会不同：
    /// <list type="bullet">
    /// <item>成本加到了错的列上 → 选中的是被写错的那一列；</item>
    /// <item>目标引用的是符号而不是它的多项式 → 目标恒为 0，求解器随便挑；</item>
    /// <item>目标压根没加进模型 → 同上。</item>
    /// </list>
    /// 所以判据同时看**选了谁**和**目标值**，只看目标值会让"两个都选"蒙混过关。
    ///
    /// This covers the whole wiring: `AddColumns` writes the cost into `bunch_cost` → `TaskCostMinimization` puts `bunch_cost` into the objective → a real solver solves it.
    /// Either link being wrong changes the answer, so the criterion checks **who was selected** as well as **the objective value**; the value alone would let "select both" slip through.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task IterativeCostObjective_ShouldPickTheCheaperExecutorUnderRealScip() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var origin = new List<TestTask> { new("T1", "T1") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            origin, executors);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();

        var cheap = new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[1] }, 0);
        var pricey = new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[0] }, 0);
        // E1 贵、E2 便宜；两列都只属于 T1。
        // E1 is expensive, E2 is cheap; both columns belong to T1.
        compilation.AddColumns(0, new[] { pricey, cheap }, model, task => ReferenceEquals(task, pricey) ? new Flt64(9.0) : new Flt64(2.0), (_, _) => false)
            .IsOk.Should().BeTrue();

        // 主人为补上的"恰好选一列"——迭代编译的 `taskCompilation = y` 不含这一条，主问题才带。
        // The "exactly one column" row is supplied by hand here: the iterative compilation's `taskCompilation = y` does not carry it, the master problem does.
        var priceyColumn = (Fuookami.Ospf.Core.Variable.IVariableItem)compilation.Assignment[(pricey, executors[0])];
        var cheapColumn = (Fuookami.Ospf.Core.Variable.IVariableItem)compilation.Assignment[(cheap, executors[1])];
        var choose = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, priceyColumn),
                new LinearMonomial<Flt64>(Flt64.One, cheapColumn),
            },
            Flt64.Zero);
        model.AddConstraint(choose, group: null, name: "choose_one").IsOk.Should().BeTrue();

        var minimization = new TaskCostMinimization<TestExecutor, TestAssignmentPolicy>(compilation);
        minimization.Invoke(model).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-cost", model);

        result.IsOk.Should().BeTrue("one task, two candidate columns: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(2.0, 1e-6, "the cheaper column costs 2");
        model.Tokens.Find(priceyColumn)!.Result!.Value.ToDouble()
            .Should().BeApproximately(0.0, 1e-6, "the expensive column must not be selected");
        model.Tokens.Find(cheapColumn)!.Result!.Value.ToDouble()
            .Should().BeApproximately(1.0, 1e-6, "the cheap column must be selected");
    }

    /// <summary>
    /// **取消成本低到一定程度，求解器就该取消那个任务** / When cancelling is cheap enough, the solver must cancel.
    /// </summary>
    /// <remarks>
    /// `taskCompilation[t] == 1` 只说"完成或取消"，**选哪个由成本决定**。这一条把承接成本设成 100、取消成本设成 1，
    /// 于是最优解必须取消：`y == 1`、所有 `x == 0`。
    ///
    /// 反过来（取消成本 100、承接 1）在 的普通编译判据里已经验过"每个任务恰好一列"。两条合起来才说明
    /// **成本真的进入了决策**，而不是"约束恰好只允许一种解"。
    ///
    /// `taskCompilation[t] == 1` only says "done or cancelled"; **cost decides which**. With an assignment cost of 100 and a cancellation cost of 1 the optimum must cancel:
    /// `y == 1` and every `x == 0`. The converse (cancellation 100, assignment 1) was covered by the earlier plain-compilation criterion, which sees exactly one column per task. Together
    /// they show **cost really enters the decision**, rather than "the constraints happened to allow only one answer".
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task CancellationObjective_ShouldCancelTheTaskUnderRealScip() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        TestTask task = EnabledTask("T1", executors);
        task.CancelEnabled = true;
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { task }, executors, taskCancelEnabled: true);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();

        // 先给"承接"定个高价，再加取消成本 1。
        // First price the assignment highly, then charge 1 for cancelling.
        var assignment = (Fuookami.Ospf.Core.Variable.IVariableItem)compilation.Assignment[(task, executors[0])];
        model.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(100.0), assignment) }, Flt64.Zero),
            "assignment cost",
            displayName: null).IsOk.Should().BeTrue();
        var minimization = new TaskCancelMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { task }, compilation, _ => new Flt64(1.0));
        minimization.Invoke(model).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-cancel", model);

        result.IsOk.Should().BeTrue("one task, done or cancelled: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(1.0, 1e-6, "cancelling costs 1, assigning costs 100");
        model.Tokens.Find(assignment)!.Result!.Value.ToDouble()
            .Should().BeApproximately(0.0, 1e-6, "assigning is ten times more expensive");
        model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)compilation.Cancellation[task])!
            .Result!.Value.ToDouble().Should().BeApproximately(1.0, 1e-6, "the task must be cancelled");
    }

    /// <summary>
    /// **完工时间第一次能被排程推动** / The makespan becomes pushable by the schedule for the first time.
    /// </summary>
    /// <remarks>
    /// 这是 的**唯一真正分辨力所在**。 的结束时间是常量，最小化完工时间等于什么都没做——解里
    /// 目标值永远是"计划最晚结束"，无论排程怎么做。这里结束时间变成 `est + 时长`，允许提前的 `est` 下界是 0，
    /// 于是最小化会把开始时间拉到 0：3 小时开始、持续 2 小时的任务，完工时间从 **18000 秒降到 7200 秒**。
    ///
    /// 只检查"目标是个正数"或"目标等于某个结束时间"都发现不了这件事；只有**两组对照**能：不允许提前时是 18000，
    /// 允许时是 7200。两者都用真实求解，差额正好是时间窗开始那 3 小时。
    ///
    /// This is where the earlier only real discriminating power lives. the earlier end times were constants, so minimising the makespan did nothing — the objective was always "the plan's
 /// latest end", whatever the schedule did. makes the end time `est + duration`, and an advance-allowed `est` has a lower bound of 0, so minimisation pulls the start
    /// to 0: a task starting at 3h and lasting 2h goes from a **18000 s makespan to 7200 s**. Checking "the objective is positive" or "it equals some end time" catches none of
    /// this; only the **pair** does — 18000 without advance, 7200 with it, both under a real solve, differing by exactly the 3h window start.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Makespan_ShouldShrinkWhenAdvanceIsAllowedUnderRealScip() {
        var window = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64>(
            new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true,
            TimeSpan.FromHours(1));

        async System.Threading.Tasks.Task<double> Solve(bool advance) {
            var tasks = new List<TestTask> {
                new("T1", "T1") {
                    Time = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                        window.Start.AddHours(3), window.Start.AddHours(5)),
                    AdvanceEnabled = advance,
                },
            };
            var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
                tasks, new List<TestExecutor> { new("E1", "E1") });
            var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
                window, tasks, compilation, advanceEnabled: advance);
            var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
            using var model = new LinearMetaModel<Flt64>();
            taskTime.Register(model).IsOk.Should().BeTrue();
            makespan.Register(model).IsOk.Should().BeTrue();
            new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(window, makespan)
                .Invoke(model).IsOk.Should().BeTrue();

            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await new ScipColumnGenerationSolver().SolveMILPAsync(
                    advance ? "gantt-makespan-advance" : "gantt-makespan-pinned", model);
            result.IsOk.Should().BeTrue("the model is feasible: " + result);
            return result.Value.Obj.ToDouble();
        }

        double pinned = await Solve(advance: false);
        double advanced = await Solve(advance: true);

        // 先钉住"允许提前"的那一半：如果两条规则都不生效，两个数会相等。
        // Pin the "advance allowed" half first: if neither rule takes effect the two numbers coincide.
        pinned.Should().BeApproximately(5 * 3600.0, 1e-6, "the task cannot move, so it ends at 5h");
        advanced.Should().BeApproximately(2 * 3600.0, 1e-6,
            "the start drops to 0 and the task ends after its 2h duration");
        advanced.Should().BeLessThan(pinned, "the whole point of the start-time variable");
    }

    /// <summary>
    /// **取消开着时掩码后的偏移量仍能求解，且值仍然等于偏移** / With cancellation on, the masked deviation still solves and still equals the deviation.
    /// </summary>
    /// <remarks>
    /// 掩码行是四行 Big-M 加一行定义，最可能的失效是**把模型写不可行**——那种失败报"无解"，看起来像任务排不下。
    /// 这里最大化掩码后的延迟时间：`task_compilation` 被强制为 1，于是掩码等于恒等，值必须仍是 `21 × 3600`。
    ///
    /// **这里的 ceiling 记在这里**：C# 里 `task_compilation == 1` 是强制的（`AddCompilationConstraints`），所以普通编译下
    /// 这层掩码**恒等 1**，与基线一致。它真正起作用的地方是迭代编译（任务可能不在当前批次里），而那条路径还没接线。
    ///
    /// The masking rows are four Big-M rows plus a definition, and the likeliest failure is **making the model infeasible** — reported as "no solution", looking like an
    /// unschedulable plan. The masked delay is maximised here: `task_compilation` is forced to 1, so the mask is the identity and the value must still be `21 × 3600`.
    /// **The ceiling is recorded here**: in C# `task_compilation == 1` is forced (`AddCompilationConstraints`), so under a plain compilation this mask is **identically 1**, as in
    /// the baseline. Where it really matters is iterative compilation (a task may be outside the current batch), and that path is not wired yet.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task MaskedDelayTime_ShouldStillEqualTheDeviationUnderRealScip() {
        var window = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64>(
            new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true,
            TimeSpan.FromHours(1));
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1") {
                Time = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                    window.Start.AddHours(3), window.Start.AddHours(5)),
                ScheduledTime = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                    window.Start.AddHours(3), window.Start.AddHours(5)),
                DelayEnabled = true,
            },
        };
        tasks[0].EnabledExecutorSet.Add(executors[0]);
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, taskCancelEnabled: true);
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, delayEnabled: true);
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        taskTime.Register(model).IsOk.Should().BeTrue();

        var masking = (Fuookami.Ospf.Core.Symbol.Function.MaskingWithPolyMaskFunction<Flt64>)
            ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)taskTime.DelayTime![tasks[0]]).Function;
        model.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(
                new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, masking.ResultVar) },
                Flt64.Zero),
            "masked_delay",
            displayName: null).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-masked-delay", model);

        result.IsOk.Should().BeTrue("the masking rows must keep the model feasible: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(21 * 3600.0, 1e-6,
            "task_compilation is forced to 1, so the mask is the identity");
        model.Tokens.Find(masking.MaskVar)!.Result!.Value.ToDouble().Should().BeApproximately(1.0, 1e-6,
            "the internal binary mask really is 1, not a fraction");
    }

    /// <summary>
    /// **延迟时间在真实求解器上确实等于"开始比计划晚多少"** / Under a real solver the delay time really equals "how much later than planned the start is".
    /// </summary>
    /// <remarks>
    /// 单靠目标里"有没有那一项、系数对不对"看不出松弛是否真的被约束住。这里**最大化**延迟时间：若 `est_slack` 的等式行或四行公式没进模型，
    /// `pos` 可以任意大，目标会跑飞；若方向写反，`pos` 恒为 0，目标恰好是 0——**两个方向都能用一组对照分辨**：
    /// 计划 3 小时、允许延迟、窗口 24 小时 → 最大化得 `21 × 3600`，最小化得 `0`。
    ///
    /// Checking only "the objective has a term with the right coefficient" cannot tell whether the slack is constrained. Here the delay time is **maximised**: if `est_slack`'s
    /// equality rows never entered the model, `pos` is unbounded and the objective runs away; if the direction is inverted, `pos` is always 0 and the objective is exactly 0. **One
    /// pair separates both**: planned at 3h with delay allowed in a 24h window → maximising gives `21 × 3600`, minimising gives `0`.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task DelayTime_ShouldTrackTheDeviationUnderRealScip() {
        var window = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64>(
            new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true,
            TimeSpan.FromHours(1));

        async System.Threading.Tasks.Task<(double Objective, double Slack)> Solve(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory category,
            string name) {
            var tasks = new List<TestTask> {
                new("T1", "T1") {
                    Time = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                        window.Start.AddHours(3), window.Start.AddHours(5)),
                    ScheduledTime = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                        window.Start.AddHours(3), window.Start.AddHours(5)),
                    DelayEnabled = true,
                },
            };
            var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
                tasks, new List<TestExecutor> { new("E1", "E1") });
            var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
                window, tasks, compilation, delayEnabled: true);
            using var model = new LinearMetaModel<Flt64>();
            taskTime.Register(model).IsOk.Should().BeTrue();
            var slack = (Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>)
                ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[0]]).Function;

            // 一侧走**真实的目标管道**，另一侧直接把同一个变量最大化。两条目标同时存在时求解器只会按其中之一优化，
            // 所以一次只留一条。
            // One side goes through the **real objective pipeline**; the other maximises the same variable directly. With two objectives in the model the solver optimises only one,
            // so exactly one is left in place per solve.
            if (category == Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum) {
                new Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits.TaskDelayTimeMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
                    window, tasks, taskTime).Invoke(model).IsOk.Should().BeTrue();
                model.MetaSubObjects.Last().Category.Should().Be(ObjectCategory.Minimum);
                model.MetaSubObjects.Last().Name.Should().Be("task delay time");
            } else {
                model.AddObject(
                    Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum,
                    new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, slack.PosVar!) },
                        Flt64.Zero),
                    "delay_max",
                    displayName: null).IsOk.Should().BeTrue();
            }

            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await new ScipColumnGenerationSolver().SolveMILPAsync(name, model);
            result.IsOk.Should().BeTrue("the model is feasible: " + result);
            return (
                result.Value.Obj.ToDouble(),
                model.Tokens.Find(slack.PosVar!)!.Result!.Value.ToDouble());
        }

        (double Objective, double Slack) minimized = await Solve(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum, "gantt-delay-min");
        (double Objective, double Slack) maximized = await Solve(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum, "gantt-delay-max");

        minimized.Objective.Should().BeApproximately(0.0, 1e-6, "starting as planned costs no delay");
        minimized.Slack.Should().BeApproximately(0.0, 1e-6);
        maximized.Slack.Should().BeApproximately(21 * 3600.0, 1e-6,
            "the start can slide to the window end, 21h past the plan");
        maximized.Objective.Should().BeApproximately(21 * 3600.0, 1e-6,
            "the objective really is the delay variable, not a stand-in");
    }

    /// <summary>
    /// **开始时间松弛在真实求解器上等于"偏离计划"的量** / Under a real solver the start-time slack equals the deviation from plan.
    /// </summary>
    /// <remarks>
    /// 这条判据盯的是**松弛真的被约束住**。目标里放 `+est_slack - M×…` 那种写法会掩盖问题；这里直接把松弛加进目标**最大化**，
    /// 于是求解器必须把 `neg` 顶到 `y - est`。若四行绝对值公式或等式行没写进模型，`neg` 可以任意大，目标会跑飞。
    ///
    /// 任务计划 3 小时开始、允许提前 → 把开始时间下拉到 0，`est_slack` 必须恰好是 10800 秒。
    /// 只检查"松弛非负"发现不了方向写反；只检查"模型可解"发现不了松弛根本没被约束。
    ///
    /// The criterion targets **the slack actually being constrained**. Writing `+est_slack - M×…` into the objective would hide the problem; here the slack is **maximised**, so the
    /// solver must push `neg` up to `y - est`. If the four-row absolute-value rows or the equality row never entered the model, `neg` is unbounded above and the objective runs
    /// away. The task is planned for 3h with advance allowed → pull the start to 0, and `est_slack` must be exactly 10800 s. Checking "the slack is non-negative" catches no
    /// inverted direction; checking "the model solves" catches an unconstrained slack.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task StartTimeSlack_ShouldEqualTheDeviationUnderRealScip() {
        var window = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64>(
            new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true,
            TimeSpan.FromHours(1));
        var tasks = new List<TestTask> {
            new("T1", "T1") {
                Time = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                    window.Start.AddHours(3), window.Start.AddHours(5)),
                AdvanceEnabled = true,
            },
        };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation, advanceEnabled: true);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();

        var slack = (Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>)
            ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)taskTime.EstimateSlack![tasks[0]]).Function;
        slack.NegVar.Should().NotBeNull();
        model.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(
                new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, slack.NegVar!) },
                Flt64.Zero),
            "advance",
            displayName: null).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-est-slack", model);

        result.IsOk.Should().BeTrue("the model is feasible: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6,
            "the start falls to 0 while the plan says 3h, so the slack is exactly 3h");
        model.Tokens.Find(slack.NegVar!)!.Result!.Value.ToDouble().Should().BeApproximately(3 * 3600.0, 1e-6);
    }

    /// <summary>
    /// **完工时间在真实求解器上等于最晚的结束时间** / Under a real solver the makespan equals the latest end time.
    /// </summary>
    /// <remarks>
    /// 单位判据只看得见符号的形状（`makespan_max`）；"取的是最大值"这件事要**解出来才知道**。
    /// 三任务在 3、9、5 小时结束 → 解里 `makespan_max` 必须是 32400 秒。取最小值会是 10800 秒，
    /// 而 10800 同样是个"合理的完工时间"——只有真实求解能分辨。
    ///
    /// The unit criteria only see the symbol's shape (`makespan_max`); "it really is the maximum" only shows **once solved**. Three tasks ending at 3h, 9h and 5h → the solution's
    /// `makespan_max` must be 32400 seconds. Taking the minimum gives 10800, which is also "a plausible makespan" — only a real solve can tell.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task Makespan_ShouldEqualTheLatestEndTimeUnderRealScip() {
        var window = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64>(
            new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            true,
            TimeSpan.FromHours(1));
        var tasks = new List<TestTask>();
        foreach ((string Id, int Hours) entry in new[] { ("T1", 3), ("T2", 9), ("T3", 5) }) {
            tasks.Add(new TestTask(entry.Id, entry.Id) {
                Time = new Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange(
                    window.Start, window.Start.AddHours(entry.Hours)),
            });
        }
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { new("E1", "E1") });
        var taskTime = new TaskSchedulingTaskTime<TestTask, TestExecutor, TestAssignmentPolicy>(
            window, tasks, compilation);
        var makespan = new Makespan<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, taskTime);
        using var model = new LinearMetaModel<Flt64>();
        taskTime.Register(model).IsOk.Should().BeTrue();
        makespan.Register(model).IsOk.Should().BeTrue();

        var minimization = new MakespanMinimization<TestExecutor, TestAssignmentPolicy>(window, makespan);
        minimization.Invoke(model).IsOk.Should().BeTrue();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync("gantt-makespan", model);

        result.IsOk.Should().BeTrue("the makespan is bounded below by three fixed end times: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(9 * 3600.0, 1e-6, "9h is the latest end time");
    }

    /// <summary>造一个所有执行器都可用、且允许取消的任务 / Build a task whose executors are all enabled and which may be cancelled.</summary>
    /// <param name="id">标识 / Identifier.</param>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <returns>任务 / The task.</returns>
    private static TestTask EnabledTask(string id, List<TestExecutor> executors) {
        var task = new TestTask(id, id);
        foreach (TestExecutor executor in executors) {
            task.EnabledExecutorSet.Add(executor);
        }
        return task;
    }
}
#endif
