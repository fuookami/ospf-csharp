#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;

/// <summary>
/// 枚举限制严重性级别。
/// Enumerates the restriction severity levels.
/// </summary>
public enum RestrictionType {
    /// <summary>弱限制 / Weak.</summary>
    Weak,
    /// <summary>可违反强限制 / Violable strong.</summary>
    ViolableStrong,
    /// <summary>强限制 / Strong.</summary>
    Strong
}

/// <summary>
/// 限制检查结果的基类。
/// Base class for restriction checking results.
/// </summary>
public abstract class RestrictionCheckingResult {
    /// <summary>限制 / Restriction.</summary>
    public abstract IRestriction Restriction { get; }

    /// <summary>类型 / Type.</summary>
    public RestrictionType Type => Restriction.Type;
}

/// <summary>
/// 表示限制不适用的结果。
/// Result indicating the restriction does not apply.
/// </summary>
public sealed class NotMatter(IRestriction restriction) : RestrictionCheckingResult {
    /// <inheritdoc/>
    public override IRestriction Restriction => restriction;
}

/// <summary>
/// 表示强违反的结果。
/// Result indicating a strong violation.
/// </summary>
public sealed class Violate(IRestriction restriction) : RestrictionCheckingResult {
    /// <inheritdoc/>
    public override IRestriction Restriction => restriction;
}

/// <summary>
/// 表示无违反的结果。
/// Result indicating no violation.
/// </summary>
public sealed class NotViolate(IRestriction restriction) : RestrictionCheckingResult {
    /// <inheritdoc/>
    public override IRestriction Restriction => restriction;
}

/// <summary>
/// 表示可违反（软）违反的结果。
/// Result indicating a violable (soft) violation.
/// </summary>
public sealed class ViolableViolate(IRestriction restriction) : RestrictionCheckingResult {
    /// <inheritdoc/>
    public override IRestriction Restriction => restriction;
}

/// <summary>
/// 可以针对航班任务检查的限制的接口。
/// Interface for restrictions that can be checked against flight tasks.
/// </summary>
public interface IRestriction {
    /// <summary>类型 / Type.</summary>
    RestrictionType Type { get; }

    /// <summary>
    /// 检查此限制是否与给定飞机相关。
    /// Checks whether this restriction is related to the given aircraft.
    /// </summary>
    bool Related(Aircraft aircraft);

    /// <summary>
    /// 针对航班任务检查此限制。
    /// Checks this restriction against a flight task.
    /// </summary>
    RestrictionCheckingResult Check(FlightTask task);

    /// <summary>
    /// 针对指定飞机的航班任务检查此限制。
    /// Checks this restriction against a flight task with a specific aircraft.
    /// </summary>
    RestrictionCheckingResult Check(FlightTask task, Aircraft aircraft);

    /// <summary>
    /// 针对具有恢复策略的航班任务检查此限制。
    /// Checks this restriction against a flight task with a recovery policy.
    /// </summary>
    RestrictionCheckingResult Check(FlightTask task, FlightTaskAssignment recoveryPolicy);
}

/// <summary>
/// 枚举关系限制类别。
/// Enumerates the relation restriction categories.
/// </summary>
public enum RelationRestrictionCategory {
    /// <summary>黑名单 / Black list.</summary>
    BlackList,
    /// <summary>白名单 / White list.</summary>
    WhiteList
}

/// <summary>
/// 基于机场对和飞机集关系的限制。
/// A restriction based on airport pair and aircraft set relationships.
/// </summary>
public sealed class RelationRestriction : IRestriction {
    /// <summary>构造函数 / Constructor.</summary>
    public RelationRestriction(
        RestrictionType type,
        RelationRestrictionCategory category,
        Airport dep,
        Airport arr,
        ISet<Aircraft> aircrafts,
        Flt64? weight = null,
        Flt64? cost = null) {
        Type = type;
        Category = category;
        Dep = dep;
        Arr = arr;
        Aircrafts = aircrafts;
        Weight = weight ?? Flt64.One;
        Cost = cost;
    }

    /// <inheritdoc/>
    public RestrictionType Type { get; }

    /// <summary>类别 / Category.</summary>
    public RelationRestrictionCategory Category { get; }

    /// <summary>出发机场 / Departure airport.</summary>
    public Airport Dep { get; }

    /// <summary>到达机场 / Arrival airport.</summary>
    public Airport Arr { get; }

    /// <summary>飞机集合 / Aircraft set.</summary>
    public ISet<Aircraft> Aircrafts { get; }

    /// <summary>权重 / Weight.</summary>
    public Flt64 Weight { get; }

    /// <summary>成本 / Cost.</summary>
    public Flt64? Cost { get; }

    /// <inheritdoc/>
    public bool Related(Aircraft aircraft) => Aircrafts.Contains(aircraft);

    /// <inheritdoc/>
    public RestrictionCheckingResult Check(FlightTask task) => Check(task.Dep, task.Arr, task.Aircraft);

    /// <inheritdoc/>
    public RestrictionCheckingResult Check(FlightTask task, Aircraft aircraft) => Check(task.Dep, task.Arr, aircraft);

    /// <inheritdoc/>
    public RestrictionCheckingResult Check(FlightTask task, FlightTaskAssignment recoveryPolicy) {
        var dep = recoveryPolicy.Route?.Dep ?? task.Dep;
        var arr = recoveryPolicy.Route?.Arr ?? task.Arr;
        var aircraft = recoveryPolicy.Aircraft ?? task.Aircraft;
        return Check(dep, arr, aircraft);
    }

    private RestrictionCheckingResult Check(Airport dep, Airport arr, Aircraft? aircraft) {
        if (dep != Dep || arr != Arr) return new NotMatter(this);
        if (aircraft is null) return new NotMatter(this);
        return Dump(Aircrafts.Contains(aircraft));
    }

    private bool Violated(bool hit) => Category switch {
        RelationRestrictionCategory.BlackList => hit,
        RelationRestrictionCategory.WhiteList => !hit,
        _ => false
    };

    private RestrictionCheckingResult Dump(bool hit) {
        if (!Violated(hit)) return new NotViolate(this);
        return Type == RestrictionType.Strong && Cost is not null
            ? new ViolableViolate(this) : Type == RestrictionType.Strong
            ? new Violate(this) : new ViolableViolate(this);
    }
}

/// <summary>
/// 具有可配置条件和飞机过滤器的通用限制。
/// A general restriction with configurable conditions and aircraft filters.
/// </summary>
public sealed class GeneralRestriction : IRestriction {
    private readonly Func<FlightTask, FlightTaskAssignment?, bool>? _condition;

    /// <summary>构造函数 / Constructor.</summary>
    public GeneralRestriction(
        RestrictionType type,
        Func<FlightTask, FlightTaskAssignment?, bool>? condition = null,
        ISet<Aircraft>? enabledAircrafts = null,
        ISet<Aircraft>? disabledAircrafts = null,
        Flt64? weight = null,
        Flt64? cost = null) {
        Type = type;
        _condition = condition;
        EnabledAircrafts = enabledAircrafts;
        DisabledAircrafts = disabledAircrafts;
        Weight = weight ?? Flt64.One;
        Cost = cost;
    }

    /// <inheritdoc/>
    public RestrictionType Type { get; }

    /// <summary>启用的飞机集合 / Enabled aircraft set.</summary>
    public ISet<Aircraft>? EnabledAircrafts { get; }

    /// <summary>禁用的飞机集合 / Disabled aircraft set.</summary>
    public ISet<Aircraft>? DisabledAircrafts { get; }

    /// <summary>权重 / Weight.</summary>
    public Flt64 Weight { get; }

    /// <summary>成本 / Cost.</summary>
    public Flt64? Cost { get; }

    /// <inheritdoc/>
    public bool Related(Aircraft aircraft)
        => EnabledAircrafts?.Contains(aircraft) == true || DisabledAircrafts?.Contains(aircraft) == true;

    /// <inheritdoc/>
    public RestrictionCheckingResult Check(FlightTask task) => Dump(Matches(task, null));

    /// <inheritdoc/>
    public RestrictionCheckingResult Check(FlightTask task, Aircraft aircraft)
        => Dump(Matches(task, new FlightTaskAssignment(Aircraft: aircraft)));

    /// <inheritdoc/>
    public RestrictionCheckingResult Check(FlightTask task, FlightTaskAssignment recoveryPolicy)
        => Dump(Matches(task, recoveryPolicy));

    private bool Matches(FlightTask task, FlightTaskAssignment? policy) {
        if (_condition is not null && !_condition(task, policy)) return false;
        if (EnabledAircrafts is not null && !EnabledAircrafts.Contains(task.Aircraft!)) return false;
        if (DisabledAircrafts is not null && DisabledAircrafts.Contains(task.Aircraft!)) return false;
        return true;
    }

    private RestrictionCheckingResult Dump(bool violated) {
        if (!violated) return new NotMatter(this);
        return Type == RestrictionType.Strong && Cost is not null
            ? new ViolableViolate(this) : Type == RestrictionType.Strong
            ? new Violate(this) : new ViolableViolate(this);
    }
}
