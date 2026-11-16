#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 编译后的问题：选择、索引、资源、行 / The compiled problem: choices, indices, resources and rows.
/// </summary>
/// <remarks>
/// 与基线 `CompiledActivityProblem` 对齐。**索引表与取消表是两条不同的映射**：
/// <see cref="Indices"/> 只收**已有排程**的选择（键是那个排程），<see cref="Cancellations"/> 只收**没有排程**的活动
/// （键是活动）。一个选择恰好落在其中一边，不会两边都有。
///
/// Aligned with the baseline's `CompiledActivityProblem`. **The index table and the cancellation table are two different mappings**: <see cref="Indices"/> takes only choices that **already have a
/// placement** (keyed by that placement) while <see cref="Cancellations"/> takes only activities that **have none** (keyed by the activity). Each choice lands on exactly one side.
/// </remarks>
public sealed class CompiledActivityProblem<V>
    where V : struct, IFloatingNumber<V>
{
    /// <summary>
    /// 构造 / Construct.
    /// </summary>
    /// <param name="problem">原问题 / The source problem.</param>
    /// <param name="choices">编译后的选择 / The compiled choices.</param>
    /// <param name="resources">执行者产能 / The executor capacities.</param>
    /// <param name="fixedReservations">固定占用 / The fixed reservations.</param>
    /// <param name="extraRows">附加行 / The extra rows.</param>
    /// <param name="materials">物料 / The materials.</param>
    /// <param name="materialEvents">物料事件 / The material events.</param>
    public CompiledActivityProblem(
        ActivitySchedulingProblem<V> problem,
        IReadOnlyList<CompiledActivityChoice<V>> choices,
        IReadOnlyDictionary<string, CompiledActivityResource<V>> resources,
        IReadOnlyList<CompiledActivityReservation> fixedReservations,
        IReadOnlyList<CompiledActivityRow> extraRows,
        IReadOnlyDictionary<string, CompiledActivityMaterial<V>> materials,
        IReadOnlyList<CompiledActivityMaterialEvent> materialEvents) {
        Problem = problem;
        Choices = choices;
        Resources = resources;
        FixedReservations = fixedReservations;
        ExtraRows = extraRows;
        Materials = materials;
        MaterialEvents = materialEvents;

        var indices = new Dictionary<ActivityChoiceKey, int>();
        var cancellations = new Dictionary<string, int>();
        for (int index = 0; index < choices.Count; index++) {
            CompiledActivityChoice<V> choice = choices[index];
            if (choice.Placement is { } placement) {
                indices[placement.Key] = index;
            } else {
                cancellations[choice.Task] = index;
            }
        }
        Indices = indices;
        Cancellations = cancellations;
    }

    /// <summary>原问题 / The source problem</summary>
    public ActivitySchedulingProblem<V> Problem { get; }

    /// <summary>编译后的选择 / The compiled choices</summary>
    public IReadOnlyList<CompiledActivityChoice<V>> Choices { get; }

    /// <summary>执行者产能 / The executor capacities</summary>
    public IReadOnlyDictionary<string, CompiledActivityResource<V>> Resources { get; }

    /// <summary>固定占用 / The fixed reservations</summary>
    public IReadOnlyList<CompiledActivityReservation> FixedReservations { get; }

    /// <summary>附加行 / The extra rows</summary>
    public IReadOnlyList<CompiledActivityRow> ExtraRows { get; }

    /// <summary>物料 / The materials</summary>
    public IReadOnlyDictionary<string, CompiledActivityMaterial<V>> Materials { get; }

    /// <summary>物料事件 / The material events</summary>
    public IReadOnlyList<CompiledActivityMaterialEvent> MaterialEvents { get; }

    /// <summary>已有排程的选择索引 / The choice indices that already carry a placement</summary>
    public IReadOnlyDictionary<ActivityChoiceKey, int> Indices { get; }

    /// <summary>没有排程的活动的取消索引 / The cancellation indices for activities without a placement</summary>
    public IReadOnlyDictionary<string, int> Cancellations { get; }

    /// <summary>
    /// 取某端点的时刻 / Read the instant of one endpoint.
    /// </summary>
    /// <param name="time">时段 / The range.</param>
    /// <param name="endpoint">端点 / The endpoint.</param>
    /// <returns>时刻 / The instant.</returns>
    public static DateTimeOffset Endpoint(TimeRange time, ActivityEndpoint endpoint) =>
        endpoint == ActivityEndpoint.Start ? time.Start : time.End;

    /// <summary>
    /// 前后两个排程是否满足依赖 / Whether two placements satisfy a dependency.
    /// </summary>
    /// <param name="dependency">依赖 / The dependency.</param>
    /// <param name="predecessor">前驱时段 / The predecessor's range.</param>
    /// <param name="successor">后继时段 / The successor's range.</param>
    /// <returns>是否满足 / Whether it is satisfied.</returns>
    /// <remarks>
    /// **端点由依赖自己指定**：`From` / `To` 决定量的是"完成到开始"还是别的组合，
    /// 写死成完成到开始会让其它组合的约束凭空消失。**`Maximum` 为空表示没有上界**，不是零。
    ///
    /// **The endpoints come from the dependency itself**: `From` and `To` decide whether the lag is measured finish-to-start or some other combination, and hard-coding finish-to-start would silently drop
    /// the others. **A null `Maximum` means no upper bound**, not zero.
    /// </remarks>
    public static bool Compatible(ActivityDependency dependency, TimeRange predecessor, TimeRange successor) {
        ArgumentNullException.ThrowIfNull(dependency);
        TimeSpan lag = Endpoint(successor, dependency.To) - Endpoint(predecessor, dependency.From);
        return lag >= dependency.MinimumLag
            && (dependency.Maximum is not { } maximum || lag <= maximum);
    }
}

/// <summary>
/// 把编译后的问题展开成线性行 / Expand a compiled problem into linear rows.
/// </summary>
/// <remarks>
/// 与基线 `ActivitySchedulingRows.rows` 对齐，覆盖**选择行 / 冻结行 / 存在性行 / 依赖行 / 附加行**五类。
/// 产能行与物料行分别由 `ActivityCapacitySweep` 与 `ActivityMaterialRows` 提供（两者都已有独立判据），
/// 由聚合层拼接——这里不重复实现。
///
/// Aligned with the baseline's `ActivitySchedulingRows.rows`, covering **selection, frozen, presence, dependency and extra** rows. Capacity and material rows come from `ActivityCapacitySweep` and
/// `ActivityMaterialRows` (both already carrying their own criteria) and are stitched together by the aggregation layer, so they are not re-implemented here.
/// </remarks>
public static class ActivitySchedulingRowBuilder {
    /// <summary>
    /// 展开全部行 / Expand every row.
    /// </summary>
    /// <param name="data">编译后的问题 / The compiled problem.</param>
    /// <returns>行列表 / The rows.</returns>
    /// <remarks>
    /// 三处照抄：
    ///
    /// 1. **选择行上下界都是 1**——每个活动恰好选一个模式，写成"至多 1"会允许一个都不选。
    /// 2. **冻结行只在 `Frozen` 时产生**，且上下界都是 1（钉死那一个选择）。
    /// 3. **固定项进常数项而不是系数**：已有排程的时段没有决策列，写进系数表会引用一个不存在的列。
    ///
    /// Three things copied: **a selection row has both bounds at one**, since each activity picks exactly one mode and "at most 1" would allow picking none; **a frozen row appears only when
    /// `Frozen`**, pinning that one choice with both bounds at one; and **fixed terms go into the constant rather than the coefficients**, because a pre-existing placement has no decision column and
    /// writing it into the coefficient table would reference a column that does not exist.
    /// </remarks>
    public static IReadOnlyList<CompiledActivityRow> Rows(CompiledActivityProblem<Flt64> data) {
        ArgumentNullException.ThrowIfNull(data);

        var rows = new List<CompiledActivityRow>();

        for (int index = 0; index < data.Problem.Activities.Count; index++) {
            Activity<Flt64> activity = data.Problem.Activities[index];
            var coefficients = new Dictionary<int, Flt64>();
            for (int choice = 0; choice < data.Choices.Count; choice++) {
                if (string.Equals(data.Choices[choice].Task, activity.Id, StringComparison.Ordinal)) {
                    coefficients[choice] = new Flt64(1.0);
                }
            }
            rows.Add(new CompiledActivityRow(
                $"selection_{index}", coefficients, Lower: new Flt64(1.0), Upper: new Flt64(1.0)));
        }

        for (int index = 0; index < data.Problem.BaselinesValue.Count; index++) {
            ActivityBaseline<Flt64> baseline = data.Problem.BaselinesValue[index];
            if (!baseline.Frozen) {
                continue;
            }
            if (!data.Indices.TryGetValue(baseline.Choice, out int choiceIndex)) {
                continue;
            }
            rows.Add(new CompiledActivityRow(
                $"frozen_{index}",
                new Dictionary<int, Flt64> { [choiceIndex] = new Flt64(1.0) },
                Lower: new Flt64(1.0),
                Upper: new Flt64(1.0)));
        }

        var facts = new Dictionary<string, TimeRange>(StringComparer.Ordinal);
        foreach (ActivityExecution<Flt64> execution in data.Problem.ExecutionsValue) {
            facts[execution.Placement.Key.Task] = execution.Placement.Time;
        }

        for (int dependencyIndex = 0; dependencyIndex < data.Problem.DependenciesValue.Count; dependencyIndex++) {
            ActivityDependency dependency = data.Problem.DependenciesValue[dependencyIndex];
            List<(int? Index, TimeRange Time)> predecessors = Choices(data, facts, dependency.Predecessor);
            List<(int? Index, TimeRange Time)> successors = Choices(data, facts, dependency.Successor);

            if (dependency.Presence != ActivityDependencyPresence.WhenBothPresent) {
                rows.Add(Presence($"presence_{dependencyIndex}_predecessor", successors, predecessors));
            }
            if (dependency.Presence == ActivityDependencyPresence.RequireBoth) {
                rows.Add(Presence($"presence_{dependencyIndex}_successor", predecessors, successors));
            }

            int pair = 0;
            foreach ((int? fromIndex, TimeRange predecessor) in predecessors) {
                foreach ((int? toIndex, TimeRange successor) in successors) {
                    if (CompiledActivityProblem<Flt64>.Compatible(dependency, predecessor, successor)) {
                        continue;
                    }
                    var coefficients = new Dictionary<int, Flt64>();
                    int constants = 0;
                    foreach (int? index in new[] { fromIndex, toIndex }) {
                        if (index is { } value) {
                            coefficients[value] = coefficients.TryGetValue(value, out Flt64 known)
                                ? known + new Flt64(1.0)
                                : new Flt64(1.0);
                        } else {
                            constants++;
                        }
                    }
                    rows.Add(new CompiledActivityRow(
                        $"dependency_{dependencyIndex}_{pair++}",
                        coefficients,
                        Upper: new Flt64(1.0 - constants)));
                }
            }
        }

        for (int index = 0; index < data.ExtraRows.Count; index++) {
            rows.Add(data.ExtraRows[index] with { Id = $"extra_{index}" });
        }
        return rows;
    }

    private static List<(int? Index, TimeRange Time)> Choices(
        CompiledActivityProblem<Flt64> data,
        IReadOnlyDictionary<string, TimeRange> facts,
        string task) {
        var result = new List<(int? Index, TimeRange Time)>();
        if (facts.TryGetValue(task, out TimeRange? fixedTime) && fixedTime is not null) {
            result.Add((null, fixedTime));
            return result;
        }
        for (int index = 0; index < data.Choices.Count; index++) {
            CompiledActivityChoice<Flt64> choice = data.Choices[index];
            if (choice.Placement is { } placement && string.Equals(choice.Task, task, StringComparison.Ordinal)) {
                result.Add((index, placement.Time));
            }
        }
        return result;
    }

    private static CompiledActivityRow Presence(
        string id,
        IReadOnlyList<(int? Index, TimeRange Time)> from,
        IReadOnlyList<(int? Index, TimeRange Time)> to) {
        var coefficients = new Dictionary<int, Flt64>();
        int fixedFrom = 0;
        foreach ((int? index, _) in from) {
            if (index is { } value) {
                coefficients[value] = coefficients.TryGetValue(value, out Flt64 known)
                    ? known + new Flt64(1.0)
                    : new Flt64(1.0);
            } else {
                fixedFrom++;
            }
        }
        int fixedTo = 0;
        foreach ((int? index, _) in to) {
            if (index is { } value) {
                coefficients[value] = coefficients.TryGetValue(value, out Flt64 known)
                    ? known - new Flt64(1.0)
                    : new Flt64(-1.0);
            } else {
                fixedTo++;
            }
        }
        return new CompiledActivityRow(id, coefficients, Upper: new Flt64(-(fixedFrom - fixedTo)));
    }
}

/// <summary>
/// 把一行注册进模型 / Register one row into a model.
/// </summary>
/// <remarks>
/// 与基线 `ActivityRowPipeline` 对齐。三处照抄：
///
/// 1. **系数与边界都要查有限性**——无穷大在建模阶段就该拒，而不是交给求解器。
/// 2. **上下界相等时只注册一条等式**，注册两条不等式会让求解器多一层判断，且浮点下可能自相矛盾。
/// 3. **变量取自 `compilation.Variables[index]`**，索引与变量同源。
///
/// Aligned with the baseline's `ActivityRowPipeline`. Three things copied: **both coefficients and bounds are checked for finiteness**, since infinity belongs rejected at modelling time rather than
/// handed to the solver; **equal bounds register a single equality**, because two inequalities cost the solver an extra decision and can contradict each other in floating point; and **variables come
/// from `compilation.Variables[index]`**, keeping index and variable from one source.
/// </remarks>
public static class ActivityRowPipeline {
    /// <summary>
    /// 注册 / Register.
    /// </summary>
    /// <param name="row">行 / The row.</param>
    /// <param name="model">模型 / The model.</param>
    /// <param name="compilation">编译结果 / The compilation.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public static Try Register(
        CompiledActivityRow row,
        LinearMetaModel<Flt64> model,
        ActivityCompilation compilation,
        string name) {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (KeyValuePair<int, Flt64> coefficient in row.Coefficients) {
            if (!double.IsFinite(coefficient.Value.ToDouble())) {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "row coefficient overflow"));
            }
        }
        if (row.Lower is { } lowerBound && !double.IsFinite(lowerBound.ToDouble())) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "row bound overflow"));
        }
        if (row.Upper is { } upperBound && !double.IsFinite(upperBound.ToDouble())) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "row bound overflow"));
        }

        var monomials = new List<LinearMonomial<Flt64>>(row.Coefficients.Count);
        foreach (KeyValuePair<int, Flt64> coefficient in row.Coefficients) {
            monomials.Add(new LinearMonomial<Flt64>(coefficient.Value, compilation.Variables[coefficient.Key]));
        }
        var expression = new LinearPolynomial<Flt64>(monomials, new Flt64(0.0));

        Flt64? lower = row.Lower;
        Flt64? upper = row.Upper;
        if (lower is { } low && upper is { } high && low == high) {
            return model.AddConstraint(
                new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                    expression,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), low),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ,
                    $"{name}_{row.Id}_eq"),
                group: null,
                name: $"{name}_{row.Id}_eq");
        }

        if (lower is { } lowBound) {
            Try added = model.AddConstraint(
                new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                    expression,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), lowBound),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
                    $"{name}_{row.Id}_lower"),
                group: null,
                name: $"{name}_{row.Id}_lower");
            if (!added.IsOk) {
                return added;
            }
        }
        if (upper is { } highBound) {
            Try added = model.AddConstraint(
                new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                    expression,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), highBound),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
                    $"{name}_{row.Id}_upper"),
                group: null,
                name: $"{name}_{row.Id}_upper");
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
