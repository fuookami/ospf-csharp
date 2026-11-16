#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 任务束调度时间 / Bunch scheduling task time
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchSchedulingTaskTime<B, T, E, A> : TaskSchedulingTaskTime<T, E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly TimeSpan? _redundancyRange;

    /// <summary>
    /// 任务束调度时间构造 / Bunch scheduling task time constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="redundancyRange">冗余范围 / Redundancy range</param>
    public BunchSchedulingTaskTime(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        BunchCompilation<B, T, E, A> compilation,
        TimeSpan? redundancyRange = null)
        : base(timeWindow, tasks, compilation,
            advanceEnabled: true, delayEnabled: true,
            delayLastEndTimeEnabled: true, advanceEarliestEndTimeEnabled: true,
            overMaxDelayEnabled: true, overMaxAdvanceEnabled: true) {
        _redundancyRange = redundancyRange;
        Compilation = compilation;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="redundancyRange">冗余范围 / Redundancy range</param>
    public BunchSchedulingTaskTime(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        BunchCompilation<B, T, E, A> compilation,
        TimeSpan? redundancyRange = null)
        : base(timeBoundary, tasks, compilation,
            advanceEnabled: true, delayEnabled: true,
            delayLastEndTimeEnabled: true, advanceEarliestEndTimeEnabled: true,
            overMaxDelayEnabled: true, overMaxAdvanceEnabled: true) {
        _redundancyRange = redundancyRange;
        Compilation = compilation;
    }

    /// <summary>任务束编译结果 / Bunch compilation result</summary>
    public BunchCompilation<B, T, E, A> Compilation { get; }

    /// <summary>是否有冗余 / Whether redundancy is present</summary>
    public bool WithRedundancy => _redundancyRange.HasValue;

    /// <summary>
    /// 冗余量（`est_redundancy`）的映射 / The redundancy-amount map (`est_redundancy`).
    /// </summary>
    /// <remarks>没有给冗余范围时为 `null`——那时"冗余"这个量不存在，`est` 与 `estEnd` 都是常量零。
    ///
    /// Null when no redundancy range is given — the amount does not exist then, and both `est` and `estEnd` are constant zero.</remarks>
    public IReadOnlyDictionary<T, object>? EstRedundancy { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// **任务束的 `est` 是一个束共用的位移**，不是每任务一个变量——那正是"束"的意思：束内成员同起同止。
    /// 给了冗余范围时这个位移就是冗余量 `est_redundancy`（范围 ±冗余值；任务给了时间时，不许提前的钉 `≥ 0`、不许延迟的钉 `≤ 0`）；
    /// 没给冗余范围时它就是常量零。`estEnd` 与 `est` **相同**（这一支不加工期）。
    ///
    /// 本类因此只重写这一个方法，八张表（偏移、超最大偏移、准时、结束时间偏移）全部由 `TaskSchedulingTaskTime` 提供：
    /// 那些表只依赖"每个任务的 `est` 是什么"，与 `est` 从哪来无关。之前本类自己实现了四张表、缺了另外四张——
    /// 那种"看起来有段时间表达"的状态比明确失败更难发现。
    ///
    /// 把两者混起来（也给每个任务建独立变量）会让"束内任务必须同起同止"这条隐含约束消失，解依旧可行、目标依旧好看。
    ///
    /// **A bunch's `est` is one displacement shared by the whole bunch**, not a variable per task — that is what "bunch" means: members start and end together. With a redundancy
    /// range that displacement is the redundancy amount (bounded by ± the value; with per-task clamps when the task has a time); without one it is constant zero. `estEnd` is
    /// **identical** to `est` (the code does not add the duration in this branch). This class therefore overrides only this method, and all eight tables (deviation, over-max,
    /// on-time, end-time deviation) come from `TaskSchedulingTaskTime`: they depend only on "what is each task's `est`", not on where it comes from. Previously this class built
    /// four of the eight itself — a state that looks like "some time expressions exist", which is harder to notice than an outright failure. Mixing the two schemes (creating a
    /// variable per task here too) dissolves the implicit "members of a bunch start and end together" constraint while the solution stays feasible and the objective looks fine.
    /// </remarks>
    protected override Try BuildEstimatePolynomials(
        Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear,
        out Dictionary<T, LinearPolynomial<Flt64>> starts,
        out Dictionary<T, LinearPolynomial<Flt64>> ends) {
        ArgumentNullException.ThrowIfNull(linear);

        var redundancy = new Dictionary<T, object>();
        if (_redundancyRange is { } range) {
            Flt64 value = TimeBoundary.ValueOf(range);
            foreach (T task in Tasks) {
                var variable = new Fuookami.Ospf.Core.Variable.RealVar("est_redundancy_" + task.Id);
                variable.Range.Geq(value.Negate());
                variable.Range.Leq(value);
                if (task.Time is not null) {
                    if (!task.AdvanceEnabled) {
                        variable.Range.Geq(Flt64.Zero);
                    }
                    if (!task.DelayEnabled) {
                        variable.Range.Leq(Flt64.Zero);
                    }
                }
                Try addedVariable = linear.Add(variable);
                if (!addedVariable.IsOk) {
                    starts = new Dictionary<T, LinearPolynomial<Flt64>>();
                    ends = new Dictionary<T, LinearPolynomial<Flt64>>();
                    return addedVariable;
                }
                var symbol = new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, variable) },
                        Flt64.Zero),
                    name: "est_redundancy_" + task.Id);
                Try addedSymbol = linear.Add(symbol);
                if (!addedSymbol.IsOk) {
                    starts = new Dictionary<T, LinearPolynomial<Flt64>>();
                    ends = new Dictionary<T, LinearPolynomial<Flt64>>();
                    return addedSymbol;
                }
                redundancy[task] = symbol;
            }
            EstRedundancy = redundancy;
        }

        starts = new Dictionary<T, LinearPolynomial<Flt64>>();
        ends = new Dictionary<T, LinearPolynomial<Flt64>>();
        foreach (T task in Tasks) {
            LinearPolynomial<Flt64> shift = redundancy.TryGetValue(task, out object? redundancySymbol)
                ? new LinearPolynomial<Flt64>(
                    System.Linq.Enumerable.ToArray(
                        ((Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)redundancySymbol).Polynomial.Monomials),
                    Flt64.Zero)
                : new LinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(),
                    Flt64.Zero);
            starts[task] = shift;
            ends[task] = shift;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 添加任务束的迭代列 / Add the per-iteration bunch columns.
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="bunches">任务束列表 / List of bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    /// <remarks>
    /// 把每个束的 `valueOf(束开始) × x` 累加到它覆盖的任务的 `est` 上，`valueOf(束结束) × x` 累加到 `estEnd` 上——
    /// 这就是"选了这一列，这些任务就从束给的时刻开始/结束"。列变量 `x` 由编译结果的 `AddColumns` 建出来，
    /// 这里**按编译结果记的列下标**取，而不是按入参顺序数：去重会改变顺序，数错就把偏移加到了别人的列上。
    ///
    /// `est` / `estEnd` 已经在模型里，内容是**原地**改的（`LinearExpressionSymbol` 按需展开）。再 `Add` 一次只会撞重名。
    ///
    /// 之前这里明确失败（"列变量那一层还没有"）；现在那一层有了，失败换成真的累加。仍然会失败的是：
    /// 模型不是线性模型、或者这一轮还没加过列（调用方顺序错了）。
    ///
    /// Accumulates each bunch's `valueOf(bunch start) × x` onto the `est` of the tasks it covers, and `valueOf(bunch end) × x` onto `estEnd` — that is "select this column and these
    /// tasks start/end at the instant the bunch gives". The column variable `x` is created by the compilation's `AddColumns`, and is looked up here **by the column index the
    /// compilation recorded**, not by argument order: deduplication reorders, and miscounting puts the deviation on someone else's column. `est` / `estEnd` are already in the model
    /// and modified **in place** (lazily flattened symbols); adding again would only trip a repeated name. This used to fail loudly ("the column layer is missing"); that layer now
    /// exists  and the failure is replaced by real accumulation. What still fails: a non-linear model, or a round whose columns were never added (caller order is wrong).
    /// </remarks>
    public virtual Try AddColumns(ulong iteration, IReadOnlyList<B> bunches, object model) {
        ArgumentNullException.ThrowIfNull(bunches);
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务束的迭代列只能进线性元模型 / bunch iteration columns can only go into a linear meta model"));
        }
        if (Compilation.Columns(iteration) is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "这一轮还没有加过列，无法给任务时间加偏移（迭代 " + iteration
                + " / this round has no columns yet, so the time deviations cannot be added (iteration " + iteration + ")"));
        }
        if (EstimateStartTime is null || EstimateEndTime is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务时间尚未注册，无法加列 / the task time is not registered yet, so columns cannot be added"));
        }

        var covered = new Dictionary<T, List<B>>();
        foreach (T task in Tasks) {
            covered[task] = new List<B>();
        }
        foreach (B bunch in bunches) {
            if (Compilation.ColumnIndex(iteration, bunch) is null) {
                continue;
            }
            foreach (T task in Tasks) {
                if (bunch.Keys.ContainsKey(task.Key)) {
                    covered[task].Add(bunch);
                }
            }
        }

        BinVariable1 columns = Compilation.Columns(iteration)!;
        foreach (T task in Tasks) {
            List<B> list = covered[task];
            if (list.Count == 0) {
                continue;
            }
            var start = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)EstimateStartTime[task];
            var end = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)EstimateEndTime[task];
            foreach (B bunch in list) {
                int columnIndex = Compilation.ColumnIndex(iteration, bunch)!.Value;
                start.AsMutable().AddMonomial(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                    TimeBoundary.ValueOf(bunch.Time.Start), columns[columnIndex]));
                end.AsMutable().AddMonomial(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                    TimeBoundary.ValueOf(bunch.Time.End), columns[columnIndex]));
            }
        }

        _ = linear;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}

