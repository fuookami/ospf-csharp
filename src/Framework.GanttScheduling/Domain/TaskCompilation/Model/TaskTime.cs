#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 任务时间抽象基类 / Task time abstract base class
/// </summary>
public abstract class TaskTime {
    /// <summary>是否启用提前 / Whether advance is enabled</summary>
    public abstract bool AdvanceEnabled { get; }

    /// <summary>是否启用延迟 / Whether delay is enabled</summary>
    public abstract bool DelayEnabled { get; }

    /// <summary>是否启用提前最早结束时间 / Whether advance earliest end time is enabled</summary>
    public virtual bool AdvanceEarliestEndTimeEnabled => false;

    /// <summary>
    /// 读取某个任务的预估开始时间符号 / Read a task's estimated start-time symbol.
    /// </summary>
    /// <param name="task">任务 / The task.</param>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <returns>是否取到 / Whether it was found.</returns>
    /// <remarks>
    /// **为什么要有这个非泛型入口**：约束类拿到的任务列表是 `IAbstractTask&lt;E, A&gt;`，而 `IEstimateStartTime&lt;T&gt;`
    /// 里的字典是**不协变**的——`TaskSchedulingTaskTime&lt;TestTask, …&gt;` 并不是 `IEstimateStartTime&lt;IAbstractTask&lt;…&gt;&gt;`。
    /// 若 `T` 是声明处协变就能直接写；C# 只能让实现方自己按键查一次。
    ///
    /// **Why a non-generic entry point**: the constraint classes hold `IAbstractTask&lt;E, A&gt;` while the dictionary inside `IEstimateStartTime&lt;T&gt;` is **invariant** —
    /// `TaskSchedulingTaskTime&lt;TestTask, …&gt;` is not an `IEstimateStartTime&lt;IAbstractTask&lt;…&gt;&gt;`. A declaration-site covariant `T` would allow it directly;
    /// in C# the implementation does the lookup itself.
    /// </remarks>
    public virtual bool TryEstimateStart(object task, out object? symbol) {
        symbol = null;
        return false;
    }

    /// <summary>
    /// 读任务的延迟或提前偏移量符号 / Read a task's delay or advance symbol.
    /// </summary>
    /// <param name="task">任务 / The task.</param>
    /// <param name="delay">`true` 读延迟，`false` 读提前 / `true` reads the delay, `false` the advance.</param>
    /// <param name="symbol">偏移量符号 / The deviation symbol.</param>
    /// <returns>是否有这一项 / Whether the entry exists.</returns>
    /// <remarks>
    /// 非泛型入口的理由与 `TryEstimateStart` 相同：C# 字典是不变的，而约束类只知道自己那对 `{E, A}`。
    ///
    /// Same reason as `TryEstimateStart`: C# dictionaries are invariant, while the constraint classes only know their `{E, A}` pair.
    /// </remarks>
    public virtual bool TryReadDeviation(object task, bool delay, out object? symbol) {
        symbol = null;
        return false;
    }

    /// <summary>
    /// 读任务的预估结束时间符号 / Read a task's estimated end-time symbol.
    /// </summary>
    /// <param name="task">任务 / The task.</param>
    /// <param name="symbol">预估结束时间符号 / The estimated end-time symbol.</param>
    /// <returns>是否有这一项 / Whether the entry exists.</returns>
    /// <remarks>
    /// 非泛型入口的理由与 `TryEstimateStart` 相同：C# 字典是不变的，而约束类只知道自己那对 `{E, A}`。
    ///
    /// Same reason as `TryEstimateStart`: C# dictionaries are invariant, while the constraint classes only know their `{E, A}` pair.
    /// </remarks>
    public virtual bool TryEstimateEnd(object task, out object? symbol) {
        symbol = null;
        return false;
    }

    /// <summary>是否启用延迟最晚结束时间 / Whether delay last end time is enabled</summary>
    public virtual bool DelayLastEndTimeEnabled => false;

    /// <summary>是否启用超最大提前 / Whether over-max advance is enabled</summary>
    public virtual bool OverMaxAdvanceEnabled => false;

    /// <summary>是否启用超最大延迟 / Whether over-max delay is enabled</summary>
    public virtual bool OverMaxDelayEnabled => false;

    /// <summary>
    /// solver 时间窗口边界；`null` 表示该实现不提供 / The solver time-window boundary; null means this implementation does not expose one.
    /// </summary>
    /// <remarks>
    /// 完工时间的极值函数需要一个 **Big-M 上界**。`LinearExpressionSymbol` 的 `Range` 每次访问都是**新建对象**，
    /// 写进去的值谁也读不到——于是极值函数拿不到有限上下界，直接以
    /// `极值函数 Big-M 不可推导或无效` 失败（第一版就是这么炸的）。
    /// 时间窗结束值天然就是完工时间的上界，拿它当 Big-M 既正确又不用假装能跟踪范围。
    ///
    /// The extremum function behind the makespan needs a **Big-M upper bound**. `LinearExpressionSymbol.Range` returns a **fresh object** on every access, so whatever is written
    /// into it is read by nobody — the extremum then finds no finite bounds and fails with `Extremum Big-M is unavailable or invalid` (exactly what the first version did). The
    /// window end value is inherently an upper bound on the makespan, so using it as the Big-M is both correct and honest about not tracking ranges.
    /// </remarks>
    public virtual SolverTimeWindowBoundary? TimeBoundary => null;

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public abstract Try Register(object model);
}

/// <summary>
/// 提供预估结束时间表的任务时间 / A task time that exposes its estimated end times.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <remarks>
/// **没有结束时间就没有完工时间。** 每个任务的结束时间做成符号（`estimateEndTime`），
/// 最大完工时间再对这些符号取最大值。少了这张表，`Makespan.Register` 只能返回 `Ok` 装作已经注册，
/// 而模型里根本没有 `makespan` 这个量——之后任何"最小化完工时间"都不是在最小化任何东西。
///
/// **No end times, no makespan.** The implementation turns each task's end time into a symbol (`estimateEndTime`) and takes the maximum over them. Without that table
/// `Makespan.Register` can only return `Ok` and pretend, while the model holds no `makespan` at all — any later "minimise the makespan" then minimises nothing.
/// </remarks>
public interface IEstimateEndTime<T> {
    /// <summary>
    /// 任务到预估结束时间符号的映射；`null` 表示尚未注册 / The map from task to its estimated end-time symbol; null means not registered yet.
    /// </summary>
    IReadOnlyDictionary<T, object>? EstimateEndTime { get; }
}

/// <summary>
/// 提供预估开始时间符号的对象 / An object that exposes its estimated start-time symbols.
/// </summary>
/// <remarks>
/// 开始时间和结束时间**不能互换着用**：结束时间是 `est + duration`，用它来比较前后顺序等于把每个任务的时长都算进
/// 排序判定里——两个任务衔接时"前一个的结束 ≤ 后一个的开始"是对的，但"前一个的开始 ≤ 后一个的开始"才是换型判据要问的。
///
/// Start and end times are **not interchangeable**: the end time is `est + duration`, so ordering by it folds every duration into the ordering test — when two tasks abut,
/// "the earlier one's end precedes the later one's start" holds, but the switch criterion asks "does the earlier one *start* before the later one".
/// </remarks>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
public interface IEstimateStartTime<T> {
    /// <summary>任务到预估开始时间符号的映射 / The map from task to its estimated start-time symbol.</summary>
    IReadOnlyDictionary<T, object>? EstimateStartTime { get; }
}

/// <summary>
/// 提供延迟/提前时间符号的对象 / An object that exposes its delay and advance time symbols.
/// </summary>
/// <remarks>
/// 两个映射**不是同一个东西**：`DelayTime` 是松弛的**正部**（开始比计划晚），`AdvanceTime` 是**负部**（开始比计划早）。
/// 只有一方的映射里出现某个任务，说明那个任务只允许那个方向的偏移——把两者混用会让计费方向整个反过来。
///
/// The two maps are **not the same thing**: `DelayTime` is the slack's **positive** part (started later than planned), `AdvanceTime` the **negative** part (started earlier). A task
/// appearing in only one of them means it allows only that direction — swapping the two inverts the whole billing direction.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
public interface ITimeDeviation<T> {
    /// <summary>延迟时间映射 / The delay-time map.</summary>
    IReadOnlyDictionary<T, object>? DelayTime { get; }

    /// <summary>提前时间映射 / The advance-time map.</summary>
    IReadOnlyDictionary<T, object>? AdvanceTime { get; }
}

/// <summary>
/// 提供"相对目标结束时间的偏移量"的对象 / An object that exposes the end-time deviations.
/// </summary>
/// <remarks>
/// 两个映射都是**单边正部**，且方向相反：
/// <list type="bullet">
/// <item>`DelayLastEndTime = max(预估结束 − 最晚结束, 0)`——拖后了多少；</item>
/// <item>`AdvanceEarliestEndTime = max(最早结束 − 预估结束, 0)`——提前了多少。</item>
/// </list>
/// 两者都相对**结束时间**，与 `DelayTime` / `AdvanceTime`（相对开始时间）不是一回事：同一任务可以开始准时、结束却迟早了
/// （工期被拉长或压缩）。
///
/// Both maps are **one-sided positive parts**, in opposite directions: `DelayLastEndTime = max(estimated end − last end, 0)` and
/// `AdvanceEarliestEndTime = max(earliest end − estimated end, 0)`. Both are relative to the **end** time, unlike `DelayTime` / `AdvanceTime` (relative to the start): the same
/// task can start on time and still finish early or late when its duration stretches or shrinks.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
public interface IEndDeviation<T> {
    /// <summary>相对最晚结束时间的拖后量 / The delay past the last allowed end.</summary>
    IReadOnlyDictionary<T, object>? DelayLastEndTime { get; }

    /// <summary>相对最早结束时间的提前量 / The advance before the earliest allowed end.</summary>
    IReadOnlyDictionary<T, object>? AdvanceEarliestEndTime { get; }
}

/// <summary>
/// 提供超最大偏移量的对象 / An object that exposes the over-max deviation amounts.
/// </summary>
/// <remarks>
/// 两个映射都是**正部**：`over_max_delay_time = max(延迟 − 最大允许延迟, 0)`，`over_max_advance_time = max(提前 − 最大允许提前, 0)`。
/// 用 `max(·)` 而不是 `1 − …` 是为了让"超过一小时"和"超过十小时"在计费上**真的不同**——
/// 二值化会把两者都收成一次超限，而超限成本通常是按超出的量计的。
///
/// Both maps are **positive parts**: `over_max_delay_time = max(delay − maxDelay, 0)` and `over_max_advance_time = max(advance − maxAdvance, 0)`. The `max(·)` rather than a
/// 0/1 flag is what makes "an hour over" and "ten hours over" charge differently — a binary flag would fold both into one violation, while over-max cost is normally per unit.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
public interface IOverMaxDeviation<T> {
    /// <summary>超最大延迟映射 / The over-max delay map.</summary>
    IReadOnlyDictionary<T, object>? OverMaxDelayTime { get; }

    /// <summary>超最大提前映射 / The over-max advance map.</summary>
    IReadOnlyDictionary<T, object>? OverMaxAdvanceTime { get; }
}

/// <summary>
/// 提供准时/不准时指示的对象 / An object that exposes its on-time and not-on-time indicators.
/// </summary>
/// <remarks>
/// 两个映射**不是互补的**：
/// <list type="bullet">
/// <item>`OnTime` 是"完工不晚于最晚结束时间" + "不早于最早结束时间"两条指示的**和**；</item>
/// <item>`NotOnTime` 是"晚了整整一个时间窗" + "早了整整一个时间窗"两条指示的**和**——它带一个**死区**，
///   不是 `1 − OnTime`。所以一间任务可以既不在 `OnTime` 里也不在 `NotOnTime` 里（刚好在死区）。</item>
/// </list>
/// 把 `NotOnTime` 当成 `1 − OnTime` 会让"晚了一点点"和"晚了整整一天"等价——而两者在计费上完全不同。
///
/// The two maps are **not complements**: `OnTime` sums "finished no later than the last allowed end" and "no earlier than the earliest allowed end", while `NotOnTime` sums
/// "late by a whole window" and "early by a whole window" — it carries a **dead band** and is not `1 − OnTime`. A task can therefore be in neither map (right inside the dead
/// band). Treating `NotOnTime` as `1 − OnTime` makes "a little late" and "a whole day late" equivalent, which they are not when charged.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type.</typeparam>
public interface IOnTime<T> {
    /// <summary>准时指示映射 / The on-time indicator map.</summary>
    IReadOnlyDictionary<T, object>? OnTime { get; }

    /// <summary>不准时指示映射 / The not-on-time indicator map.</summary>
    IReadOnlyDictionary<T, object>? NotOnTime { get; }
}

/// <summary>
/// 预估结束时间表的构造 / Building the estimated end-time table.
/// </summary>
/// <remarks>
/// 每任务的 `est` 变量（开始时间选择）加时长算出结束时间；本实现**还没有** `est` 变量，
/// 所以只能取任务自己声明的时间窗结束点，作为一个**常量**多项式。这条限制必须显式记着：
/// 现在的完工时间只反映"任务计划什么时候结束"，**不反映排程把任务挪到哪里**。
///
/// The implementation derives each end time from a per-task `est` variable (a start-time choice) plus the duration; this implementation **has no** `est` variable yet, so it can
/// only take the end of the task's own declared window as a **constant** polynomial. That limit matters: today's makespan reflects "when the plan says the tasks end",
/// **not where the schedule moves them**.
/// </remarks>
public static class EstimateEndTimeTable {
    /// <summary>结束时间符号名前缀 / End-time symbol name prefix.</summary>
    public const string Prefix = "estimate_end_time_";

    /// <summary>开始时间符号名前缀 / Start-time symbol name prefix.</summary>
    public const string StartPrefix = "estimate_start_time_";

    /// <summary>
    /// 判断任务是否可估 / Whether a task can be estimated.
    /// </summary>
    /// <param name="time">任务时间窗 / The task's time window.</param>
    /// <returns>是否可估 / Whether it can be estimated.</returns>
    /// <remarks>
    /// **没有时间窗就没有结束时间。** 跳过它会让完工时间偏小——而且偏得很自然，
    /// 因为剩下的任务本来就结束得更早。这类偏差在结果里读不出来。
    ///
    /// **No window, no end time.** Skipping it makes the makespan smaller, and naturally so — the remaining tasks do end earlier. That bias is invisible in the result.
    /// </remarks>
    public static bool CanEstimate(TimeRange? time) => time is not null;
}

/// <summary>
/// 任务调度任务时间 / Task scheduling task time
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskSchedulingTaskTime<T, E, A> : TaskTime, IEstimateEndTime<T>, IEstimateStartTime<T>, ITimeDeviation<T>, IOnTime<T>, IOverMaxDeviation<T>, IEndDeviation<T>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly bool _delayLastEndTimeEnabled;
    private readonly bool _overMaxDelayEnabled;
    private readonly bool _overMaxAdvanceEnabled;
    private readonly bool _advanceEarliestEndTimeEnabled;
    private readonly ICompilation _compilation;
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly bool _advanceEnabled;
    private readonly bool _delayEnabled;

    /// <summary>
    /// 任务调度任务时间构造 / Task scheduling task time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="advanceEnabled">是否启用提前 / Whether advance is enabled</param>
    /// <param name="delayEnabled">是否启用延迟 / Whether delay is enabled</param>
    /// <param name="delayLastEndTimeEnabled">是否启用"最晚结束时间"判定 / Whether the last-end-time criterion is enabled</param>
    /// <param name="advanceEarliestEndTimeEnabled">是否启用"最早结束时间"判定 / Whether the earliest-end-time criterion is enabled</param>
    /// <param name="overMaxDelayEnabled">是否启用超最大延迟 / Whether over-max delay is enabled</param>
    /// <param name="overMaxAdvanceEnabled">是否启用超最大提前 / Whether over-max advance is enabled</param>
    public TaskSchedulingTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        ICompilation compilation,
        bool advanceEnabled = false,
        bool delayEnabled = false,
        bool delayLastEndTimeEnabled = false,
        bool advanceEarliestEndTimeEnabled = false,
        bool overMaxDelayEnabled = false,
        bool overMaxAdvanceEnabled = false) {
        _tasks = tasks;
        _compilation = compilation;
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _advanceEnabled = advanceEnabled;
        _delayEnabled = delayEnabled;
        _delayLastEndTimeEnabled = delayLastEndTimeEnabled;
        _advanceEarliestEndTimeEnabled = advanceEarliestEndTimeEnabled;
        _overMaxDelayEnabled = overMaxDelayEnabled;
        _overMaxAdvanceEnabled = overMaxAdvanceEnabled;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="advanceEnabled">是否启用提前 / Whether advance is enabled</param>
    /// <param name="delayEnabled">是否启用延迟 / Whether delay is enabled</param>
    /// <param name="delayLastEndTimeEnabled">是否启用"最晚结束时间"判定 / Whether the last-end-time criterion is enabled</param>
    /// <param name="advanceEarliestEndTimeEnabled">是否启用"最早结束时间"判定 / Whether the earliest-end-time criterion is enabled</param>
    /// <param name="overMaxDelayEnabled">是否启用超最大延迟 / Whether over-max delay is enabled</param>
    /// <param name="overMaxAdvanceEnabled">是否启用超最大提前 / Whether over-max advance is enabled</param>
    public TaskSchedulingTaskTime(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        ICompilation compilation,
        bool advanceEnabled = false,
        bool delayEnabled = false,
        bool delayLastEndTimeEnabled = false,
        bool advanceEarliestEndTimeEnabled = false,
        bool overMaxDelayEnabled = false,
        bool overMaxAdvanceEnabled = false) {
        _tasks = tasks;
        _compilation = compilation;
        _timeBoundary = timeBoundary;
        _advanceEnabled = advanceEnabled;
        _delayEnabled = delayEnabled;
        _delayLastEndTimeEnabled = delayLastEndTimeEnabled;
        _advanceEarliestEndTimeEnabled = advanceEarliestEndTimeEnabled;
        _overMaxDelayEnabled = overMaxDelayEnabled;
        _overMaxAdvanceEnabled = overMaxAdvanceEnabled;
    }

    /// <inheritdoc/>
    public override bool AdvanceEnabled => _advanceEnabled;

    /// <inheritdoc/>
    public override bool DelayEnabled => _delayEnabled;

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <inheritdoc/>
    public override SolverTimeWindowBoundary TimeBoundary => _timeBoundary;

    /// <inheritdoc/>
    public IReadOnlyDictionary<T, object>? EstimateEndTime { get; private set; }

    /// <summary>
    /// 任务到开始时间松弛符号的映射；`null` 表示尚未注册 / The map from task to its start-time slack symbol; null means not registered yet.
    /// </summary>
    /// <remarks>
    /// 只在"允许提前或延迟"的任务上有真符号；其余任务得到的是**常量零**（不是 `null`）——
    /// `LinearIntermediateSymbol.empty` 就是这个语义，读它的人会拿到 0 而不是空引用。
    ///
    /// Only tasks that may advance or be delayed get a real symbol; the rest get a **constant zero** (not `null`) — its `LinearIntermediateSymbol.empty` means exactly
    /// that, so a reader gets 0 rather than a null reference.
    /// </remarks>
    public IReadOnlyDictionary<T, object>? EstimateSlack { get; private set; }

    /// <summary>
    /// 任务到延迟时间符号的映射；只含**真的允许延迟**且有正松弛的任务 / The map from task to its delay-time symbol; only tasks that may really be delayed and have a positive part.
    /// </summary>
    public IReadOnlyDictionary<T, object>? DelayTime { get; private set; }

    /// <summary>
    /// 任务到提前时间符号的映射；只含**真的允许提前**且有负松弛的任务 / The map from task to its advance-time symbol; only tasks that may really advance and have a negative part.
    /// </summary>
    public IReadOnlyDictionary<T, object>? AdvanceTime { get; private set; }

    /// <summary>
    /// 任务到预估开始时间符号的映射；`null` 表示尚未注册 / The map from task to its estimated start-time symbol; null means not registered yet.
    /// </summary>
    public IReadOnlyDictionary<T, object>? EstimateStartTime { get; private set; }

    /// <inheritdoc/>
    /// <summary>
    /// 任务到准时指示的映射 / The map from task to its on-time indicator.
    /// </summary>
    /// <remarks>
    /// 只在 `DelayLastEndTimeEnabled` 或 `AdvanceEarliestEndTimeEnabled` 时存在——两个开关都关着时"准时"没有定义。
    ///
    /// Only exists when `DelayLastEndTimeEnabled` or `AdvanceEarliestEndTimeEnabled`; with both off, "on time" is undefined.
    /// </remarks>
    public IReadOnlyDictionary<T, object>? OnTime { get; private set; }

    /// <summary>
    /// 任务到不准时指示的映射 / The map from task to its not-on-time indicator.
    /// </summary>
    public IReadOnlyDictionary<T, object>? NotOnTime { get; private set; }

    /// <summary>任务到超最大延迟量的映射 / The map from task to its over-max delay amount.</summary>
    public IReadOnlyDictionary<T, object>? OverMaxDelayTime { get; private set; }

    /// <summary>任务到超最大提前量的映射 / The map from task to its over-max advance amount.</summary>
    public IReadOnlyDictionary<T, object>? OverMaxAdvanceTime { get; private set; }

    /// <summary>任务到"拖后结束"量的映射 / The map from task to its late-finish amount.</summary>
    public IReadOnlyDictionary<T, object>? DelayLastEndTime { get; private set; }

    /// <summary>任务到"提前结束"量的映射 / The map from task to its early-finish amount.</summary>
    public IReadOnlyDictionary<T, object>? AdvanceEarliestEndTime { get; private set; }

    /// <inheritdoc/>
    public override bool DelayLastEndTimeEnabled => _delayLastEndTimeEnabled;

    /// <inheritdoc/>
    public override bool AdvanceEarliestEndTimeEnabled => _advanceEarliestEndTimeEnabled;

    /// <inheritdoc/>
    public override bool OverMaxDelayEnabled => _overMaxDelayEnabled;

    /// <inheritdoc/>
    public override bool OverMaxAdvanceEnabled => _overMaxAdvanceEnabled;

    public override bool TryEstimateStart(object task, out object? symbol) {
        symbol = null;
        if (EstimateStartTime is null || task is not T typed) {
            return false;
        }
        return EstimateStartTime.TryGetValue(typed, out symbol);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 见基类 `TryReadDeviation` 的注释：这里只是按 `T` 查一次键。
    ///
    /// See the base `TryReadDeviation` remarks: this only performs the lookup by `T`.
    /// </remarks>
    public override bool TryReadDeviation(object task, bool delay, out object? symbol) {
        symbol = null;
        if (task is not T typed) {
            return false;
        }
        IReadOnlyDictionary<T, object>? source = delay ? DelayTime : AdvanceTime;
        return source is not null && source.TryGetValue(typed, out symbol);
    }

    /// <summary>注册到模型 / Register to model</summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 每个任务一条 `estimate_end_time_{Id}`，值是**本任务时间窗结束点的常量多项式**（见 <see cref="EstimateEndTimeTable"/> 的说明）。
    /// 任务没有时间窗时**失败**而不是跳过：跳过会让完工时间偏小而看不出来。
    ///
    /// One `estimate_end_time_{Id}` per task, valued as a **constant polynomial at the task's own window end** (see <see cref="EstimateEndTimeTable"/>). A task without a window
    /// **fails** instead of being skipped: skipping shrinks the makespan invisibly.
    /// </remarks>
    public override Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务时间只能注册到线性元模型 / task time can only be registered into a linear meta model"));
        }
        if (EstimateEndTime is not null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        // 开始时间变量：`est`，连续窗口用连续变量。
        // 三条边界规则沿用，且**后写的覆盖先写的**：
        //   ① 上界 = 窗口结束；
        //   ② 不启用提前（全局或本任务）→ 下界 = 本任务时间窗开始；
        //   ③ 不启用延迟（全局或本任务）→ 上界 = 本任务时间窗开始。
        // ②③ 同时成立时 `est` 被钉在时间窗开始——这是正确的：不能挪就只能按计划开始。
        //
 // Start-time variable: `est`, continuous on a continuous window. Its three bound rules are copied verbatim, **later writes overriding earlier ones**:
        // (1) upper bound = window end; (2) advance not enabled (globally or for this task) → lower bound = this task's window start; (3) delay not enabled (globally or for
        // this task) → upper bound = this task's window start. With both (2) and (3) `est` is pinned to the window start — correct: cannot move, so it starts as planned.
        // 的是 **`URealVariable1`（无符号连续）**，不是 `RealVariable1`：开始时间不可能是负的。
        // 缺省下界因此是 0 而不是 `-1e308`——差别只在"允许提前"那一支：下界不写死成任务时间窗开始后，
        // 有符号变量会一路降到负无穷，倒推出一个负的完工时间。
        //
 // This implementation uses **`URealVariable1` (unsigned continuous)**, not `RealVariable1`: a start time cannot be negative. Its default lower bound is therefore 0 rather than
        // `-1e308` — and that only matters in the "advance allowed" branch: with the lower bound not pinned to the task's window start, a signed variable would sink to negative
        // infinity and yield a negative makespan.
        Try builtEstimate = BuildEstimatePolynomials(
            linear,
            out Dictionary<T, LinearPolynomial<Flt64>> estimateStartPolynomials,
            out Dictionary<T, LinearPolynomial<Flt64>> estimateEndPolynomials);
        if (!builtEstimate.IsOk) {
            return builtEstimate;
        }

        var table = new Dictionary<T, object>();
        var starts = new Dictionary<T, object>();
        var slacks = new Dictionary<T, object>();
        var delays = new Dictionary<T, object>();
        var advances = new Dictionary<T, object>();
        var symbols = new List<IIntermediateSymbol>();
        for (int index = 0; index < _tasks.Count; ++index) {
            T task = _tasks[index];
            Flt64 duration = _timeBoundary.ValueOf(task.Duration ?? TimeSpan.Zero);
            LinearPolynomial<Flt64> estPolynomial = estimateStartPolynomials[task];
            LinearPolynomial<Flt64> estEndPolynomial = estimateEndPolynomials[task];
            var startSymbol = new LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                    System.Linq.Enumerable.ToArray(estPolynomial.Monomials),
                    estPolynomial.Constant),
                name: EstimateEndTimeTable.StartPrefix + task.Id);
            starts[task] = startSymbol;
            table[task] = new LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                    System.Linq.Enumerable.ToArray(estEndPolynomial.Monomials),
                    estEndPolynomial.Constant),
                name: EstimateEndTimeTable.Prefix + task.Id);
            symbols.Add(startSymbol);
            symbols.Add((IIntermediateSymbol)table[task]);

            // `est_slack`：**开始时间偏离计划开始多少**。在"允许提前或延迟"时建它——不能挪的任务
            // 偏离恒为零，建一个恒零的符号只会让后面读它的人以为读到了什么。
            // 两个方向都开时走 `SlackFunction` 的四行绝对值公式；只开一个方向时走一行等式/单边行。
            //
 // `est_slack`: **how far the start deviates from the planned start**. The implementation only builds it when advance or delay is allowed — a task that cannot move has zero
            // deviation always, and building a constant-zero symbol would make whoever reads it believe they got something. Both directions open goes through `SlackFunction`'s
            // four-row absolute-value formulation; one direction goes through a single equality / one-sided row.
            // 没有时间窗的任务：`est` / `estEnd` 照建（它们来自钩子，与时间窗无关），但**没有"偏离计划"可言**——
            // 计划开始未知，偏移、超最大偏移、准时、结束时间偏移全会指向一个不存在的东西。给常量零并在符号名上说清楚。
            // 这道坎原来写成了硬失败：`TaskTimeImpl.register` 对 `time == null` 是直接跳过，而任务束/迭代那两支的
            // 任务本来就可能没有时间窗。要求"必须有时间窗"是本实现在抽取钩子时**多加的**限制。
            //
            // A task without a time window still gets `est` / `estEnd` (they come from the hook and do not depend on the window), but there is **no "deviation from plan"** — the
            // planned start is unknown, so deviation, over-max, on-time and end-time deviation would all point at something that does not exist. A constant zero is built and
            // named as such. This used to be a hard failure: `register` can simply skip `time == null`, and the bunch/iterative branches legitimately
            // carry tasks without a window. Demanding a window was a restriction **added** here when the hook was extracted.
            if (task.Time is null) {
                var zeroSlack = new LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                        Flt64.Zero),
                    name: "est_slack_" + task.Id);
                // **不要**在这里单独 `linear.Add`：循环结束后 `symbols` 会整体加一次，单独加就是重名。
                // **Do not** `linear.Add` here: `symbols` is added in bulk after the loop, and adding here repeats the name.
                slacks[task] = zeroSlack;
                symbols.Add(zeroSlack);
                continue;
            }

            bool advance = _advanceEnabled && task.AdvanceEnabled;
            bool delay = _delayEnabled && task.DelayEnabled;
            if (!advance && !delay) {
                slacks[task] = new LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
                    name: "est_slack_" + task.Id);
                continue;
            }
            var slack = new Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>(
                x: estPolynomial,
                y: new LinearPolynomial<Flt64>(Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), _timeBoundary.ValueOf(task.Time!.Start)),
                converter: Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity,
                withNegative: advance,
                withPositive: delay,
                name: "est_slack_" + task.Id);
            var adapter = new Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>(slack);
            Try addedSlack = linear.Add(adapter);
            if (!addedSlack.IsOk) {
                return addedSlack;
            }
            slacks[task] = adapter;

            // **延迟/提前时间就是松弛的正部/负部**，不是另算一个量。取消开着时再套一层掩码：
            // 被取消掉的任务不该累计偏移量，所以 `delay_time = pos × taskCompilation`。
            //
            // **Delay/advance time is just the slack's positive/negative part**, not a separate quantity. When cancellation is on, a mask goes on top: a cancelled task must not
            // accrue deviation, so `delay_time = pos × taskCompilation`.
            Try masked = MaskOf(task, out ILinearIntermediateSymbol<Flt64>? mask);
            if (!masked.IsOk) {
                return masked;
            }

            if (delay && slack.PosVar is not null) {
                Try built = BuildDeviation(
                    linear,
                    new LinearPolynomial<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, slack.PosVar) },
                        Flt64.Zero),
                    mask,
                    "delay_time_" + task.Id,
                    out object symbol);
                if (!built.IsOk) {
                    return built;
                }
                delays[task] = symbol;
            }
            if (advance && slack.NegVar is not null) {
                Try built = BuildDeviation(
                    linear,
                    new LinearPolynomial<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, slack.NegVar) },
                        Flt64.Zero),
                    mask,
                    "advance_time_" + task.Id,
                    out object symbol);
                if (!built.IsOk) {
                    return built;
                }
                advances[task] = symbol;
            }
        }

        Try added = linear.Add(symbols);
        if (!added.IsOk) {
            return added;
        }
        EstimateStartTime = starts;
        EstimateEndTime = table;
        EstimateSlack = slacks;
        DelayTime = delays;
        AdvanceTime = advances;

        // 准时 / 不准时：**两张表不是互补的**，见 `IOnTime` 的注释。
        // On time / not on time: **the two tables are not complements**, see the remarks on `IOnTime`.
        if (DelayLastEndTimeEnabled || AdvanceEarliestEndTimeEnabled) {
            var onTime = new Dictionary<T, object>();
            var notOnTime = new Dictionary<T, object>();
            var onSymbols = new List<IIntermediateSymbol>();
            foreach (T task in _tasks) {
                Try built = BuildOnTimePair(linear, task, onTime, notOnTime, out object on, out object notOn);
                if (!built.IsOk) {
                    return built;
                }
                onSymbols.Add((IIntermediateSymbol)on);
                onSymbols.Add((IIntermediateSymbol)notOn);
            }
            Try addedOnTime = linear.Add(onSymbols);
            if (!addedOnTime.IsOk) {
                return addedOnTime;
            }
            OnTime = onTime;
            NotOnTime = notOnTime;
        }

        if (OverMaxDelayEnabled) {
            Try built = BuildOverMax(linear, delays, static task => task.DelayEnabled, static task => task.MaxDelay,
                "over_max_delay_time", out Dictionary<T, object> map);
            if (!built.IsOk) {
                return built;
            }
            OverMaxDelayTime = map;
        }
        if (OverMaxAdvanceEnabled) {
            Try built = BuildOverMax(linear, advances, static task => task.AdvanceEnabled, static task => task.MaxAdvance,
                "over_max_advance_time", out Dictionary<T, object> map);
            if (!built.IsOk) {
                return built;
            }
            OverMaxAdvanceTime = map;
        }

        if (DelayLastEndTimeEnabled) {
            Try built = BuildEndDeviation(linear, delaySide: true, static task => task.LastEndTime,
                static task => task.DelayEnabled, "delay_last_end_time", out Dictionary<T, object> map);
            if (!built.IsOk) {
                return built;
            }
            DelayLastEndTime = map;
        }
        if (AdvanceEarliestEndTimeEnabled) {
            Try built = BuildEndDeviation(linear, delaySide: false, static task => task.EarliestEndTime,
                static task => task.AdvanceEnabled, "advance_earliest_end_time", out Dictionary<T, object> map);
            if (!built.IsOk) {
                return built;
            }
            AdvanceEarliestEndTime = map;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造一整张"相对目标结束时间的偏移量"表 / Build a whole end-time deviation table.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="delaySide">`true` 取拖后（正部），`false` 取提前（负部）/ `true` for the late side (positive part), `false` for the early side (negative part).</param>
    /// <param name="target">任务的目标结束时间 / The task's target end time.</param>
    /// <param name="mayMove">任务在该方向是否允许移动 / Whether the task may move that way.</param>
    /// <param name="prefix">符号名前缀 / The symbol-name prefix.</param>
    /// <param name="table">建出的表 / The created table.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 三种情况：任务不允许移动 → 常量零；任务没给目标结束时间 → 常量零；否则 `SlackFunction(estEnd, 目标, ...)` 的**单边部**
    /// （拖后取正部、提前取负部），取消开着时再套掩码。
    ///
    /// **提前为什么取负部**：`SlackFunction` 的负部是 `max(y − x, 0)`，把 `y` 放目标结束时间、`x` 放 `estEnd`，正好是"早了多少"。
    /// 取错边会让"提前两小时"算成"拖后两小时"，而两个数值的**量纲相同**、都在 0 附近，从数值上看不出错。
    ///
    /// Three cases: the task may not move → constant zero; no target given → constant zero; otherwise the **one-sided part** of
    /// `SlackFunction(estEnd, target, ...)` — positive for the late side, negative for the early side — masked when cancellation is on. **Why the negative part for advance**:
    /// `SlackFunction`'s negative part is `max(y − x, 0)`; putting the target in `y` and `estEnd` in `x` gives "how early". Taking the wrong side turns "two hours early" into
    /// "two hours late", and since both are the same unit and both near zero, nothing looks wrong numerically.
    /// </remarks>
    private Try BuildEndDeviation(
        IAbstractLinearMetaModel<Flt64> linear,
        bool delaySide,
        Func<T, DateTimeOffset?> target,
        Func<T, bool> mayMove,
        string prefix,
        out Dictionary<T, object> table) {
        table = new Dictionary<T, object>();
        var symbols = new List<IIntermediateSymbol>();
        foreach (T task in _tasks) {
            string name = prefix + "_" + task.Id;
            Try maskedLookup = MaskOf(task, out ILinearIntermediateSymbol<Flt64>? mask);
            if (!maskedLookup.IsOk) {
                return maskedLookup;
            }
            if (!mayMove(task) || target(task) is not { } targetEnd
                || !TryEstimateEnd(task, out object? endSymbol)
                || endSymbol is not ILinearIntermediateSymbol<Flt64> end) {
                LinearExpressionSymbol zero = ConstantSymbol(name);
                symbols.Add(zero);
                table[task] = zero;
                continue;
            }

            var slack = new Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>(
                x: end.Polynomial,
                y: new LinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                    _timeBoundary.ValueOf(targetEnd)),
                converter: Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity,
                withNegative: !delaySide,
                withPositive: delaySide,
                name: name + "_slack");
            var adapter = new Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>(slack);
            Try added = linear.Add(adapter);
            if (!added.IsOk) {
                return added;
            }
            Fuookami.Ospf.Core.Variable.URealVar? part = delaySide ? slack.PosVar : slack.NegVar;
            if (part is null) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "结束时间偏移的松弛没有需要的单边部：" + task.Id
                    + " / the end-time slack is missing the required one-sided part: " + task.Id));
            }
            Try built = BuildDeviation(
                linear,
                new LinearPolynomial<Flt64>(
                    new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, part) },
                    Flt64.Zero),
                mask,
                name,
                out object symbol);
            if (!built.IsOk) {
                return built;
            }
            symbols.Add((IIntermediateSymbol)symbol);
            table[task] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造每个任务的预估开始与结束时间多项式 / Build the per-task estimated start and end polynomials.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="starts">任务到预估开始时间多项式的映射 / The map from task to its estimated-start polynomial.</param>
    /// <param name="ends">任务到预估结束时间多项式的映射 / The map from task to its estimated-end polynomial.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 抽成虚方法的理由：后续八张表（偏移、超最大偏移、准时、结束时间偏移）都只依赖"每个任务的 est 是什么"，
    /// 与 est 从哪来无关。任务调度时间从**己方的开始时间变量**取 est；任务束与迭代任务调度时间那边
    /// est 是一个**共用的位移**或**常量零**（一个束只有一个位移，迭代任务没有独立开始变量）。
    /// 不抽出来的话，那两个类要么复制这八张表，要么永远只有一个静默 `Ok`。
    ///
    /// **默认实现**：连续窗口上一变量一任务（`est`），三条边界规则照此，且**后写的覆盖先写的**——
    /// ① 上界 = 窗口结束；② 不启用提前（全局或本任务）→ 下界 = 本任务时间窗开始；③ 不启用延迟（全局或本任务）→ 上界 = 本任务时间窗开始。
    /// ②③ 同时成立时 `est` 被钉在时间窗开始——这是正确的：不能挪就只能按计划开始。
    ///
    /// **为什么占位实现是失败而不是常量零**：常量零会让"开始时间"永远是 0，八张表照样建得出来、解照样可行，
    /// 而计划时间悄悄从窗口开始塌到 0。
    ///
    /// Extracted as a virtual method because the eight tables that follow (deviation, over-max, on-time, end-time deviation) depend only on "what is each task's `est`", not on where
    /// it comes from. Task scheduling time takes `est` from its **own start-time variables**; bunch and iterative scheduling time have a **shared displacement** or a **constant
    /// zero** instead (a bunch has one displacement, and an iterative task has no independent start variable). Without the hook those two classes either duplicate the eight
    /// tables or stay a silent `Ok` forever. **Default implementation**: one variable per task on a continuous window, with its three bound rules copied verbatim and
    /// **later writes overriding earlier ones** — (1) upper bound = window end; (2) advance not enabled (globally or for this task) → lower bound = this task's window start;
    /// (3) delay not enabled (globally or for this task) → upper bound = this task's window start. With (2) and (3) together `est` is pinned to the window start, which is
    /// correct: cannot move, so starts as planned. **Why the placeholder fails instead of returning zeros**: a constant zero would make every start time 0 while the eight tables
    /// still build and the solution stays feasible, silently collapsing the plan from the window start to 0.
    /// </remarks>
    protected virtual Try BuildEstimatePolynomials(
        IAbstractLinearMetaModel<Flt64> linear,
        out Dictionary<T, LinearPolynomial<Flt64>> starts,
        out Dictionary<T, LinearPolynomial<Flt64>> ends) {
        // 离散窗口的守卫**放在这里**，因为它是"建开始时间变量"这一支的限制：离散窗口上用
        // `UIntVariable1`（`UInt64` 承载），而本仓**还没有**整数开始时间变量——`UIntVar` 的缺省上界是
        // `ulong.MaxValue`，换成 double 是 1.8e19，超过 binary64 能精确表示的整数上界 2^53，求解器在注册阶段就会拒。
        //
        // 放在共用循环里就错了：任务束的 `est` 是一个**连续位移**（`est_redundancy`），与"整数开始时刻"无关，
        // 而 Demo4 的束时间窗恰恰是离散的——共用守卫会把那条本来能注册的路径挡住。
        //
 // The discrete-window guard belongs **here**, because it is a restriction of "building start-time variables": this implementation uses `UIntVariable1` (backed by `UInt64`) on
        // discrete windows, while this repository has **no** integer start-time variable yet — `UIntVar`'s default upper bound is `ulong.MaxValue` (1.8e19 as a double), beyond
        // binary64's exact-integer limit of 2^53, so the solver rejects it at registration. Putting it in the shared loop was wrong: a bunch's `est` is a **continuous
        // displacement** (`est_redundancy`) unrelated to integer start instants, and Demo4's bunch window is discrete — the shared guard blocked a path that does register.
        if (!_timeBoundary.Continues) {
            starts = new Dictionary<T, LinearPolynomial<Flt64>>();
            ends = new Dictionary<T, LinearPolynomial<Flt64>>();
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "离散时间窗的开始时间变量尚未接线（需要先解决 UInt64 边界换算） / the discrete-window start-time variable is not wired yet (the UInt64 bound conversion comes first)"));
        }

        var est = new URealVariable1("est", _tasks.Count);
        for (int index = 0; index < _tasks.Count; ++index) {
            T task = _tasks[index];
            est[index].Name = "est_" + task.Id;
            est[index].Range.Leq(_timeBoundary.EndValue);
            if (task.Time is not { } time) {
                continue;
            }
            if (!_advanceEnabled || !task.AdvanceEnabled) {
                est[index].Range.Geq(_timeBoundary.ValueOf(time.Start));
            }
            if (!_delayEnabled || !task.DelayEnabled) {
                est[index].Range.Leq(_timeBoundary.ValueOf(time.Start));
            }
        }
        Try addedEst = linear.Add(est.Items);
        if (!addedEst.IsOk) {
            starts = new Dictionary<T, LinearPolynomial<Flt64>>();
            ends = new Dictionary<T, LinearPolynomial<Flt64>>();
            return addedEst;
        }

        starts = new Dictionary<T, LinearPolynomial<Flt64>>();
        ends = new Dictionary<T, LinearPolynomial<Flt64>>();
        for (int index = 0; index < _tasks.Count; ++index) {
            T task = _tasks[index];
            // 默认的结束时间是"开始 + 工期"，所以**这一支**要求有工期；别的 `est` 来源（任务束、迭代任务）不加工期，
            // 也就不该被这条要求挡住。要求放在用它的人身上，而不是放在共用循环里。
            // The default end time is "start + duration", so **this branch** requires a duration; other `est` sources (bunches, iterative tasks) do not add one and must not be
            // blocked by it. The requirement lives where it is used, not in the shared loop.
            if (task.Duration is not { } duration) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务没有持续时长，无法从开始时间推出结束时间：" + task.Id
                    + " / the task has no duration, so its end time cannot follow from its start: " + task.Id));
            }
            var monomial = new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, est[index]);
            starts[task] = new LinearPolynomial<Flt64>(new[] { monomial }, Flt64.Zero);
            // 结束时间 = 开始时间 + 工期。**这里**加时长；迭代任务调度时间那支不加（见它的重写）。
 // End = start + duration. This implementation adds the duration **here**; the iterative task scheduling time does not (see its override).
            ends[task] = new LinearPolynomial<Flt64>(new[] { monomial }, _timeBoundary.ValueOf(duration));
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造一整张"超最大偏移量"表 / Build a whole over-max deviation table.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="deviation">同方向的偏移量表 / The deviation map of the same direction.</param>
    /// <param name="enabled">任务在该方向是否允许移动 / Whether the task may move in that direction.</param>
    /// <param name="maximum">任务的最大允许偏移 / The task's maximum allowed deviation.</param>
    /// <param name="prefix">符号名前缀 / The symbol-name prefix.</param>
    /// <param name="table">建出的表 / The created table.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 三种情况：
    /// <list type="number">
    /// <item>任务不允许移动 → **常量零**（它不可能超限）；</item>
    /// <item>任务没给最大允许量 → **常量零**（没有上限就没有"超限"）；</item>
    /// <item>否则 → `max(偏移 − 上限, 0)`，取消开着时再套掩码（取消掉的任务不计超限）。</item>
    /// </list>
    /// 第三种用的是 `SlackFunction` 的**正部**（`withNegative: false`），不是二值指示——见 `IOverMaxDeviation` 的注释。
    ///
    /// Three cases: the task may not move → **constant zero** (it cannot exceed); no maximum given → **constant zero** (no limit, no violation); otherwise
    /// `max(deviation − limit, 0)`, masked when cancellation is on (a cancelled task accrues none). The third uses the **positive part** of a `SlackFunction`
    /// (`withNegative: false`), not a 0/1 flag — see the remarks on `IOverMaxDeviation`.
    /// </remarks>
    private Try BuildOverMax(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyDictionary<T, object> deviation,
        Func<T, bool> enabled,
        Func<T, TimeSpan?> maximum,
        string prefix,
        out Dictionary<T, object> table) {
        table = new Dictionary<T, object>();
        var symbols = new List<IIntermediateSymbol>();
        foreach (T task in _tasks) {
            string name = prefix + "_" + task.Id;
            if (!enabled(task) || maximum(task) is not { } limit || !deviation.TryGetValue(task, out object? deviationSymbol)) {
                LinearExpressionSymbol zero = ConstantSymbol(name);
                symbols.Add(zero);
                table[task] = zero;
                continue;
            }
            Try maskedLookup = MaskOf(task, out ILinearIntermediateSymbol<Flt64>? mask);
            if (!maskedLookup.IsOk) {
                return maskedLookup;
            }
            LinearPolynomial<Flt64> x = ((ILinearIntermediateSymbol<Flt64>)deviationSymbol).Polynomial;
            var slack = new Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>(
                x: x,
                y: new LinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                    _timeBoundary.ValueOf(limit)),
                converter: Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity,
                withNegative: false,
                withPositive: true,
                name: name + "_slack");
            var adapter = new Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>(slack);
            Try added = linear.Add(adapter);
            if (!added.IsOk) {
                return added;
            }
            if (slack.PosVar is not { } positive) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "超最大偏移的松弛没有正部：" + task.Id
                    + " / the over-max slack has no positive part: " + task.Id));
            }
            Try masked = BuildDeviation(
                linear,
                new LinearPolynomial<Flt64>(
                    new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, positive) },
                    Flt64.Zero),
                mask,
                name,
                out object symbol);
            if (!masked.IsOk) {
                return masked;
            }
            symbols.Add((IIntermediateSymbol)symbol);
            table[task] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 取一条偏移量该套的掩码列 / Read the mask column a deviation should carry.
    /// </summary>
    /// <param name="task">任务 / The task.</param>
    /// <param name="mask">掩码列；`null` 表示不掩码 / The mask column; null means no masking.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 取消开着时返回该任务的 `task_compilation` 列（`y + Σx`），否则返回 `null`。**取不到就失败**，不静默不掩码：
    /// 不掩码意味着"被取消的任务也在计费偏移"，而解依旧可行、目标依旧是个正常数。
    ///
    /// Returns the task's `task_compilation` column (`y + Σx`) when cancellation is on, otherwise `null`. **Failing when it is unavailable**, rather than silently not masking:
    /// not masking means "cancelled tasks are charged too", while the solution stays feasible and the objective stays a respectable number.
    /// </remarks>
    private Try MaskOf(T task, out ILinearIntermediateSymbol<Flt64>? mask) {
        mask = null;
        if (!_compilation.TaskCancelEnabled) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (_compilation is not ITaskCompilation<E, A> compilation
            || !compilation.TaskCompilationTable.TryGetValue(task, out object? maskSymbol)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "取消开着却取不到任务的编译列，无法给偏移量加掩码：" + task.Id
                + " / cancellation is on but the task's compilation column is unavailable: " + task.Id));
        }
        mask = (ILinearIntermediateSymbol<Flt64>)maskSymbol;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>造一条常量零符号 / Build a constant-zero symbol.</summary>
    private static LinearExpressionSymbol ConstantSymbol(string name) => new(
        new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
            Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
            Flt64.Zero),
        name: name);

    /// <summary>
    /// 造一个任务的准时与不准时指示 / Build one task's on-time and not-on-time indicators.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="task">任务 / The task.</param>
    /// <param name="onTime">准时表 / The on-time map.</param>
    /// <param name="notOnTime">不准时表 / The not-on-time map.</param>
    /// <param name="on">准时符号 / The on-time symbol.</param>
    /// <param name="notOn">不准时符号 / The not-on-time symbol.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 两个方向各自只在"开关开着 + 任务允许移动 + 任务给了目标时间"时才是一个真指示，否则是**常量零**。
    /// 常量零而不是"跳过"：`OnTime` / `NotOnTime` 是**和**，少一项与该项为零在数值上是同一件事，但前者会让
    /// `taskTime.onTime[task]` 在 `OnTime` 表里缺席，目标函数取符号时会炸。
    ///
    /// 死区：`not_on_last_end_time` 只在**整整晚一个时间窗**时才为 1（`afterWindowDurationValue`），
    /// `not_on_earliest_end_time` 只在**整整早一个时间窗**时才为 1（`beforeWindowDurationValue`）。沿用，不改成 `1 − on`。
    ///
    /// Each direction is a real indicator only when the switch is on, the task may move, and the task carries a target time; otherwise it is **constant zero** (not skipped):
    /// `OnTime` / `NotOnTime` are **sums**, so a missing term equals a zero term numerically, but skipping would leave `taskTime.onTime[task]` absent and the objective would throw
    /// when reading the symbol. The dead band is its: `not_on_last_end_time` only fires when late by a **whole window** (`afterWindowDurationValue`) and
    /// `not_on_earliest_end_time` only when early by a **whole window** (`beforeWindowDurationValue`). Copied as-is rather than replaced with `1 − on`.
    /// </remarks>
    private Try BuildOnTimePair(
        IAbstractLinearMetaModel<Flt64> linear,
        T task,
        Dictionary<T, object> onTime,
        Dictionary<T, object> notOnTime,
        out object on,
        out object notOn) {
        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        var notMonomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 notConstant = Flt64.Zero;

        if (DelayLastEndTimeEnabled && task.DelayEnabled && task.LastEndTime is { } lastEndTime) {
            Try built = BuildOnTimeIndicator(
                linear,
                task,
                _timeBoundary.ValueOf(lastEndTime),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
                _timeBoundary.AfterWindowDurationValue(lastEndTime),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
                "on_last_end_time_" + task.Id,
                "not_on_last_end_time_" + task.Id,
                monomials, ref constant, notMonomials, ref notConstant);
            if (!built.IsOk) {
                on = new object();
                notOn = new object();
                return built;
            }
        }
        if (AdvanceEarliestEndTimeEnabled && task.AdvanceEnabled && task.EarliestEndTime is { } earliestEndTime) {
            Try built = BuildOnTimeIndicator(
                linear,
                task,
                _timeBoundary.ValueOf(earliestEndTime),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
                _timeBoundary.BeforeWindowDurationValue(earliestEndTime),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
                "on_earliest_end_time_" + task.Id,
                "not_on_earliest_end_time_" + task.Id,
                monomials, ref constant, notMonomials, ref notConstant);
            if (!built.IsOk) {
                on = new object();
                notOn = new object();
                return built;
            }
        }

        var onSymbol = new LinearExpressionSymbol(
            new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(monomials.ToArray(), constant),
            name: "on_time_" + task.Id);
        var notOnSymbol = new LinearExpressionSymbol(
            new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(notMonomials.ToArray(), notConstant),
            name: "not_on_time_" + task.Id);
        onTime[task] = onSymbol;
        notOnTime[task] = notOnSymbol;
        on = onSymbol;
        notOn = notOnSymbol;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把一条指示（`IfThenFunction` 的结果）并进两个多项式 / Fold one indicator into the two polynomials.
    /// </summary>
    private Try BuildOnTimeIndicator(
        IAbstractLinearMetaModel<Flt64> linear,
        T task,
        Flt64 onValue,
        Fuookami.Ospf.Math.Symbol.Inequality.Comparison onComparison,
        Flt64 notOnValue,
        Fuookami.Ospf.Math.Symbol.Inequality.Comparison notOnComparison,
        string onName,
        string notOnName,
        List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>> monomials,
        ref Flt64 constant,
        List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>> notMonomials,
        ref Flt64 notConstant) {
        if (!TryEstimateEnd(task, out object? endSymbol)
            || endSymbol is not ILinearIntermediateSymbol<Flt64> end) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册该任务的预估结束时间：" + task.Id
                + " / the task time has not registered this task's estimated end: " + task.Id));
        }
        Try onBuilt = BuildIndicator(linear, end.Polynomial, onValue, onComparison, onName);
        if (!onBuilt.IsOk) {
            return onBuilt;
        }
        Try notOnBuilt = BuildIndicator(linear, end.Polynomial, notOnValue, notOnComparison, notOnName);
        if (!notOnBuilt.IsOk) {
            return notOnBuilt;
        }
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in PolynomialOf(onName).Monomials) {
            monomials.Add(monomial);
        }
        constant = constant + PolynomialOf(onName).Constant;
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in PolynomialOf(notOnName).Monomials) {
            notMonomials.Add(monomial);
        }
        notConstant = notConstant + PolynomialOf(notOnName).Constant;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private readonly Dictionary<string, ILinearIntermediateSymbol<Flt64>> _indicators = new();

    /// <summary>
    /// 准时/不准时指示的底层函数符号，按名字索引 / The underlying indicator functions, indexed by name.
    /// </summary>
    /// <remarks>
    /// 暴露它是为了让判据能查**方向**（`IfThenFunction.Relation`）：`on_time` 那条表达式只是 `1 × 指示变量`，
    /// 方向写反了从多项式上看不出来，但会让"准时"变成"迟过"。
    ///
    /// Exposed so criteria can check the **direction** (`IfThenFunction.Relation`): the `on_time` expression is just `1 × indicator variable`, so an inverted direction is
    /// invisible in the polynomial while turning "on time" into "past due".
    /// </remarks>
    public IReadOnlyDictionary<string, ILinearIntermediateSymbol<Flt64>> Indicators => _indicators;

    private Try BuildIndicator(
        IAbstractLinearMetaModel<Flt64> linear,
        LinearPolynomial<Flt64> end,
        Flt64 value,
        Fuookami.Ospf.Math.Symbol.Inequality.Comparison comparison,
        string name) {
        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Model.Mechanism.LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input =
            Fuookami.Ospf.Core.Model.Mechanism.LinearConstraintInput<Flt64>.From(
            new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                end,
                new LinearPolynomial<Flt64>(Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), value),
                comparison,
                name),
            Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Full(),
            Flt64.Zero,
            name);
        if (!input.IsOk) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "造不出准时判定的约束输入：" + name
                + " / the on-time constraint input could not be built: " + name));
        }
        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> function =
            Fuookami.Ospf.Core.Symbol.Function.IfThenFunction<Flt64>.From(
            input.Value,
            Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity,
            OnTimeStrictBoundary,
            name: name);
        if (!function.IsOk) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "造不出准时判定函数：" + name
                + " / the on-time function could not be built: " + name));
        }
        Try added = linear.Add(function.Value);
        if (!added.IsOk) {
            return added;
        }
        _indicators[name] = function.Value;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private LinearPolynomial<Flt64> PolynomialOf(string name) => _indicators[name].Polynomial;

    /// <summary>`IfThenFunction` 的正负分支间隔 / The true/false gap of an `IfThenFunction`.</summary>
    private static readonly Flt64 OnTimeStrictBoundary = new Flt64(1e-9);

    /// <summary>读任务的预估结束时间符号 / Read a task's estimated end-time symbol.</summary>
    /// <inheritdoc/>
    public override bool TryEstimateEnd(object task, out object? symbol) {
        symbol = null;
        if (EstimateEndTime is null || task is not T typed) {
            return false;
        }
        return EstimateEndTime.TryGetValue(typed, out symbol);
    }


    /// <summary>
    /// 造一条偏移量符号，有掩码时乘上掩码 / Build a deviation symbol, multiplying by the mask when there is one.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="polynomial">偏移量多项式 / The deviation polynomial.</param>
    /// <param name="mask">掩码列；`null` 表示不掩码 / The mask column; null means no masking.</param>
    /// <param name="name">符号名称 / Symbol name.</param>
    /// <param name="symbol">建出的符号 / The created symbol.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **自己把符号加进模型**（调用方不要再加一遍——重复添加会撞 `SymbolRepetitive`，而报错里新旧符号同名叫人看不出是同一处重复）。
    ///
    /// **Registers the symbol itself** (the caller must not add it again — a second add trips `SymbolRepetitive` with the same old and new name, hiding that it is one duplicate).
    /// </remarks>
    /// <remarks>
    /// 掩码是**多项式**（`taskCompilation = y + Σx`），所以走 `MaskingWithPolyMaskFunction`：它会自己建一个二值
    /// `m` 并用一条等式把 `m` 定义成这个多项式。直接把多项式塞进 Big-M 行会失去整数性——模型仍可解，
    /// 只是解里 `m = 0.5`，而"取消了一半"读起来像 0 或 1。
    ///
    /// The mask is a **polynomial** (`taskCompilation = y + Σx`), so this goes through `MaskingWithPolyMaskFunction`, which creates its own binary `m` and defines it by an equality.
    /// Stuffing the polynomial straight into the Big-M rows loses integrality — the model still solves, only with `m = 0.5`, and "half cancelled" reads like 0 or 1.
    /// </remarks>
    private static Try BuildDeviation(
        IAbstractLinearMetaModel<Flt64> linear,
        LinearPolynomial<Flt64> polynomial,
        ILinearIntermediateSymbol<Flt64>? mask,
        string name,
        out object symbol) {
        if (mask is null) {
            var plain = new LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                    System.Linq.Enumerable.ToArray(polynomial.Monomials),
                    polynomial.Constant),
                name: name);
            symbol = plain;
            return linear.Add(plain);
        }
        var masked = new Fuookami.Ospf.Core.Symbol.Function.MaskingWithPolyMaskFunction<Flt64>(
            input: polynomial,
            maskPolynomial: mask.Polynomial,
            converter: Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity,
            name: name);
        var adapter = new Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>(masked);
        symbol = adapter;
        return linear.Add(adapter);
    }
}

/// <summary>
/// 迭代任务调度任务时间 / Iterative task scheduling task time
/// </summary>
/// <summary>
/// 迭代任务调度时间 / Iterative task scheduling time
/// </summary>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
/// <remarks>
/// **与任务调度时间共用全部八张表**（偏移、超最大偏移、准时、结束时间偏移）：那些表只依赖"每个任务的 `est` 是什么"，
/// 与 `est` 从哪来无关。本类只重写 `est` 的来源——迭代任务**没有独立的开始时间变量**，`est` 恒为零。
///
/// 这一支同样如此：`estimate_start_time` 与 `estimate_end_time` 都是"冗余量为空时的常量零"，且 `estEnd` **不再加工期**
/// （与任务调度时间那支不同）。冗余范围在**直接返回失败**（"暂未实现 withRedundancy 路径"），本类沿用。
///
/// 之前这里是 `Try Register(...) => Results.Ok(...)`：模型里什么都没有，而调用方以为时间已注册。
///
/// **Shares all eight tables** with task scheduling time (deviation, over-max, on-time, end-time deviation): those depend only on "what is each task's `est`", not on where it comes
/// from. This class overrides only the source of `est` — an iterative task has **no independent start-time variable**, so `est` is identically zero. That branch is the
/// same: both `estimate_start_time` and `estimate_end_time` are "constant zero when there is no redundancy", and `estEnd` does **not** add the duration here (unlike task
/// scheduling time). A redundancy range **fails outright** in the implementation ("the withRedundancy path is not implemented"), copied verbatim. This used to be
/// `Try Register(...) => Results.Ok(...)`: nothing in the model while the caller believed the time was registered.
/// </remarks>
public class IterativeTaskSchedulingTaskTime<IT, T, E, A> : TaskSchedulingTaskTime<T, E, A>
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>迭代任务编译结果 / Iterative task compilation result</summary>
    public IterativeTaskCompilation<IT, T, E, A> IterativeCompilation { get; }

    /// <summary>冗余范围 / Redundancy range</summary>
    public TimeSpan? RedundancyRange { get; }

    /// <summary>
    /// 迭代任务调度时间构造 / Iterative task scheduling time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">迭代编译结果 / Iterative compilation result</param>
 /// <param name="redundancyRange">冗余范围；给了就失败（没实现） / Redundancy range; giving one fails (this implementation does not implement it either)</param>
    public IterativeTaskSchedulingTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        IterativeTaskCompilation<IT, T, E, A> compilation,
        TimeSpan? redundancyRange = null)
        : base(timeWindow, tasks, compilation,
            advanceEnabled: true, delayEnabled: true,
            delayLastEndTimeEnabled: true, advanceEarliestEndTimeEnabled: true,
            overMaxDelayEnabled: true, overMaxAdvanceEnabled: true) {
        IterativeCompilation = compilation;
        RedundancyRange = redundancyRange;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from a solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">迭代编译结果 / Iterative compilation result</param>
    /// <param name="redundancyRange">冗余范围；给了就失败 / Redundancy range; giving one fails</param>
    public IterativeTaskSchedulingTaskTime(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        IterativeTaskCompilation<IT, T, E, A> compilation,
        TimeSpan? redundancyRange = null)
        : base(timeBoundary, tasks, compilation,
            advanceEnabled: true, delayEnabled: true,
            delayLastEndTimeEnabled: true, advanceEarliestEndTimeEnabled: true,
            overMaxDelayEnabled: true, overMaxAdvanceEnabled: true) {
        IterativeCompilation = compilation;
        RedundancyRange = redundancyRange;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 冗余范围在没有实现，所以**给了就直接失败**，而且要在动模型之前失败——先加一半符号再报错会留下一个半成品模型。
    /// 没给冗余时 `est` 与 `estEnd` 都是**常量零**，且 `estEnd` 不再加工期（这一支就是这样）。
    ///
 /// The redundancy range is unimplemented in the implementation, so giving one **fails outright** — and before touching the model, since adding half the symbols first would leave a
    /// half-built model. Without redundancy both `est` and `estEnd` are **constant zero**, and `estEnd` does not add the duration (that is its shape in this branch).
    /// </remarks>
    protected override Try BuildEstimatePolynomials(
        IAbstractLinearMetaModel<Flt64> linear,
        out Dictionary<T, LinearPolynomial<Flt64>> starts,
        out Dictionary<T, LinearPolynomial<Flt64>> ends) {
        ArgumentNullException.ThrowIfNull(linear);
        starts = new Dictionary<T, LinearPolynomial<Flt64>>();
        ends = new Dictionary<T, LinearPolynomial<Flt64>>();

        // 迭代任务的 `est` 虽是常量零，但它的 `est_slack` 要用**整数**松弛（离散窗口上走 `flooredValueOf` + `UInteger`）。
        // 共用循环只产出连续松弛，所以离散窗口在这里仍然明确失败，而不是给一个"看得见但不该动"的连续松弛。
 // An iterative task's `est` is constant zero, but its `est_slack` needs an **integer** slack on a discrete window (this implementation uses `flooredValueOf` + `UInteger`). The
        // shared loop only produces continuous slack, so a discrete window still fails loudly here instead of getting a continuous slack that looks usable but must not move.
        if (!TimeBoundary.Continues) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "迭代任务调度时间的离散窗口尚未接线（松弛需要整数类型） / the discrete window for iterative task time is not wired yet (its slack needs an integer type)"));
        }

        if (RedundancyRange is not null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "迭代任务调度时间暂未实现冗余范围路径，请先关闭 redundancyRange"
                + " / the iterative task scheduling time does not implement the redundancy path yet; drop redundancyRange first"));
        }

        LinearPolynomial<Flt64> zero = new(
            Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
            Flt64.Zero);
        foreach (T task in Tasks) {
            starts[task] = zero;
            ends[task] = zero;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
