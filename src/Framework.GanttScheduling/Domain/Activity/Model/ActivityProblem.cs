#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 基线：某个已定选择的对齐参考 / A baseline: the reference a fixed choice is measured against.
/// </summary>
/// <param name="Choice">基线选择 / The code choice.</param>
/// <param name="Frozen">是否冻结 / Whether it is frozen.</param>
/// <param name="StartChangeCost">改开始时刻的成本 / The cost of moving the start.</param>
/// <param name="ModeChangeCost">改模式的成本 / The cost of changing the mode.</param>
/// <remarks>
/// 与基线 `ActivityBaseline` 对齐。**冻结不等于零成本**：`Frozen` 决定"能不能改"，
/// 两个成本决定"改了要付多少"——把冻结当成成本无穷大会让求解器无法用软约束表达。
///
/// Aligned with its `ActivityBaseline`. **Frozen is not the same as costly**: `Frozen` says whether a change is allowed while the two costs say what it would cost, and treating frozen as
/// infinite cost would leave the solver no way to express it softly.
/// </remarks>
public sealed record ActivityBaseline<V>(
    ActivityChoiceKey Choice,
    bool Frozen,
    Quantity<V> StartChangeCost,
    Quantity<V> ModeChangeCost)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 已执行的排程：一个 placement 及其状态 / An already-executed placement and its status.
/// </summary>
/// <param name="Placement">排程 / The placement.</param>
/// <param name="Status">状态 / The status.</param>
public sealed record ActivityExecution<V>(ActivityPlacement<V> Placement, ActivityExecutionStatus Status)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 一行线性约束 / One linear row.
/// </summary>
/// <param name="Id">标识 / The id.</param>
/// <param name="Unit">量纲 / The unit.</param>
/// <param name="Coefficients">按选择的系数 / The coefficients by choice.</param>
/// <param name="CancellationCoefficients">按活动的取消系数 / The cancellation coefficients by activity.</param>
/// <param name="Lower">下界 / The lower bound.</param>
/// <param name="Upper">上界 / The upper bound.</param>
/// <remarks>
/// 与基线 `ActivityRow` 对齐。**上下界都可空**：只给一侧就是单边约束，兜底成 0 会凭空造出一个约束。
///
/// Aligned with its `ActivityRow`. **Both bounds are nullable**: supplying one side makes a one-sided row, and defaulting to zero would invent a constraint.
/// </remarks>
public sealed record ActivityRow<V>(
    string Id,
    PhysicalUnit Unit,
    IReadOnlyDictionary<ActivityChoiceKey, Quantity<V>>? Coefficients = null,
    IReadOnlyDictionary<string, Quantity<V>>? CancellationCoefficients = null,
    Quantity<V>? Lower = null,
    Quantity<V>? Upper = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>系数（未给则空）/ The coefficients, empty when unset</summary>
    public IReadOnlyDictionary<ActivityChoiceKey, Quantity<V>> CoefficientsValue =>
        Coefficients ?? new Dictionary<ActivityChoiceKey, Quantity<V>>();

    /// <summary>取消系数（未给则空）/ The cancellation coefficients, empty when unset</summary>
    public IReadOnlyDictionary<string, Quantity<V>> CancellationCoefficientsValue =>
        CancellationCoefficients ?? new Dictionary<string, Quantity<V>>();
}

/// <summary>
/// 活动排程问题 / The activity scheduling problem.
/// </summary>
/// <param name="Horizon">计划区间 / The planning horizon.</param>
/// <param name="Activities">活动 / The activities.</param>
/// <param name="Resources">执行者 / The executors.</param>
/// <param name="Dependencies">依赖 / The dependencies.</param>
/// <param name="Baselines">基线 / The baselines.</param>
/// <param name="Executions">已执行排程 / The executed placements.</param>
/// <param name="Rows">线性约束行 / The linear rows.</param>
/// <param name="CostUnit">成本量纲 / The cost unit.</param>
/// <param name="AdditionalCost">附加成本，null 表示没有 / The additional cost, null meaning none.</param>
/// <param name="Materials">物料 / The materials.</param>
/// <param name="MaterialEvents">物料事件 / The material events.</param>
/// <remarks>
/// 与基线 `ActivitySchedulingProblem` 对齐。**除区间、活动、执行者、成本量纲外全部可省**：
/// 一个最小问题就是"在某个区间里把活动排给执行者"，其余都是增量信息。
/// **`AdditionalCost` 是委托**，因为附加成本要看到具体的选择才能算；写成静态字典会强迫调用方先枚举所有组合。
///
/// Aligned with its `ActivitySchedulingProblem`. **Everything except the horizon, activities, executors and cost unit is optional**: a minimal problem is just "place activities onto
/// executors within a horizon", and the rest is incremental. **`AdditionalCost` is a delegate** because an additional cost needs to see the concrete choice, whereas a static dictionary would force
/// the caller to enumerate every combination up front.
/// </remarks>
public sealed record ActivitySchedulingProblem<V>(
    TimeRange Horizon,
    IReadOnlyList<Activity<V>> Activities,
    IReadOnlyList<ActivityResource<V>> Resources,
    IReadOnlyList<ActivityDependency>? Dependencies,
    IReadOnlyList<ActivityBaseline<V>>? Baselines,
    IReadOnlyList<ActivityExecution<V>>? Executions,
    IReadOnlyList<ActivityRow<V>>? Rows,
    PhysicalUnit CostUnit,
    Func<ActivityPlacement<V>, Result<Quantity<V>, ErrorCode, Error<ErrorCode>>>? AdditionalCost = null,
    IReadOnlyList<ActivityMaterial<V>>? Materials = null,
    IReadOnlyList<ActivityMaterialEvent<V>>? MaterialEvents = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>依赖（未给则空）/ The dependencies, empty when unset</summary>
    public IReadOnlyList<ActivityDependency> DependenciesValue =>
        Dependencies ?? Array.Empty<ActivityDependency>();

    /// <summary>基线（未给则空）/ The baselines, empty when unset</summary>
    public IReadOnlyList<ActivityBaseline<V>> BaselinesValue =>
        Baselines ?? Array.Empty<ActivityBaseline<V>>();

    /// <summary>已执行排程（未给则空）/ The executed placements, empty when unset</summary>
    public IReadOnlyList<ActivityExecution<V>> ExecutionsValue =>
        Executions ?? Array.Empty<ActivityExecution<V>>();

    /// <summary>线性约束行（未给则空）/ The linear rows, empty when unset</summary>
    public IReadOnlyList<ActivityRow<V>> RowsValue => Rows ?? Array.Empty<ActivityRow<V>>();

    /// <summary>物料（未给则空）/ The materials, empty when unset</summary>
    public IReadOnlyList<ActivityMaterial<V>> MaterialsValue =>
        Materials ?? Array.Empty<ActivityMaterial<V>>();

    /// <summary>物料事件（未给则空）/ The material events, empty when unset</summary>
    public IReadOnlyList<ActivityMaterialEvent<V>> MaterialEventsValue =>
        MaterialEvents ?? Array.Empty<ActivityMaterialEvent<V>>();
}
