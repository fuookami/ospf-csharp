#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>稳定 interval 标识 / Stable interval identifier.</summary>
/// <param name="Value">标识文本 / Identifier text.</param>
public readonly record struct IntervalId(string Value);

/// <summary>interval 求值结果 / Evaluated interval value.</summary>
/// <param name="Start">起始时间 / Start time.</param>
/// <param name="Size">持续时间 / Duration.</param>
/// <param name="End">结束时间 / End time.</param>
/// <param name="Present">是否存在 / Whether the interval is present.</param>
public sealed record IntervalValue(
    Int64 Start,
    Int64 Size,
    Int64 End,
    bool Present = true) {
    /// <summary>持续时间语义别名 / Duration semantic alias.</summary>
    public Int64 Duration => Size;
}

/// <summary>
/// CP interval 变量，满足 `end = start + size`；presence 非空时为可选 interval。
/// CP interval variable satisfying `end = start + size`; a non-null presence makes it optional.
/// </summary>
public sealed record IntervalVariable {
    private IntervalVariable(
        IntervalId id,
        ConstraintProgrammingExpression start,
        ConstraintProgrammingExpression size,
        ConstraintProgrammingExpression end,
        BooleanLiteral? presence,
        ModelElementScope scope,
        string? origin,
        IReadOnlyList<ModelElementOrigin> identityProvenance) {
        Id = id;
        Start = start;
        Size = size;
        End = end;
        Presence = presence;
        Scope = scope;
        Origin = origin;
        IdentityProvenance = identityProvenance;
    }

    /// <summary>稳定 interval 标识 / Stable interval identifier.</summary>
    public IntervalId Id { get; }

    /// <summary>起始时间表达式 / Start-time expression.</summary>
    public ConstraintProgrammingExpression Start { get; }

    /// <summary>持续时间表达式 / Duration expression.</summary>
    public ConstraintProgrammingExpression Size { get; }

    /// <summary>结束时间表达式 / End-time expression.</summary>
    public ConstraintProgrammingExpression End { get; }

    /// <summary>可选存在文字 / Optional presence literal.</summary>
    public BooleanLiteral? Presence { get; }

    /// <summary>身份作用域 / Identity scope.</summary>
    public ModelElementScope Scope { get; }

    /// <summary>兼容来源字段 / Compatibility origin field.</summary>
    public string? Origin { get; }

    /// <summary>完整身份来源 / Complete identity provenance.</summary>
    public IReadOnlyList<ModelElementOrigin> IdentityProvenance { get; }

    /// <summary>持续时间语义别名 / Duration semantic alias.</summary>
    public ConstraintProgrammingExpression Duration => Size;

    /// <summary>是否为可选 interval / Whether this is an optional interval.</summary>
    public bool IsOptional => Presence is not null;

    /// <summary>interval 引用的稳定变量 ID / Stable variable IDs referenced by the interval.</summary>
    public IReadOnlySet<VariableId> Variables {
        get {
            var variables = new HashSet<VariableId>();
            variables.UnionWith(Start.Variables);
            variables.UnionWith(Size.Variables);
            variables.UnionWith(End.Variables);
            if (Presence?.VariableId is { } presence) {
                variables.Add(presence);
            }
            return variables.ToFrozenSet();
        }
    }

    /// <summary>创建 interval 变量 / Create an interval variable.</summary>
    /// <param name="id">稳定 interval ID / Stable interval ID.</param>
    /// <param name="start">起始时间表达式 / Start-time expression.</param>
    /// <param name="size">持续时间表达式 / Duration expression.</param>
    /// <param name="end">结束时间表达式 / End-time expression.</param>
    /// <param name="presence">可选存在文字 / Optional presence literal.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>interval 或结构化错误 / Interval or a structured error.</returns>
    public static Result<IntervalVariable, ErrorCode, Error<ErrorCode>> Create(
        string id,
        ConstraintProgrammingExpression start,
        ConstraintProgrammingExpression size,
        ConstraintProgrammingExpression end,
        BooleanLiteral? presence = null,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(end);
        Result<ConstraintProgrammingIdentityMetadata, ErrorCode, Error<ErrorCode>> identity =
            ConstraintProgrammingIdentity.Canonicalize(
                id: id,
                scope: scope,
                origin: origin,
                provenance: identityProvenance,
                identityNamespace: "cp",
                identitySchemaVersion: "1.0");
        if (identity.IsFailed) {
            return identity.Map(_ => default(IntervalVariable)!);
        }
        return Results.Ok(new IntervalVariable(
            id: new IntervalId(id),
            start: start,
            size: size,
            end: end,
            presence: presence,
            scope: identity.Value.Scope,
            origin: origin,
            identityProvenance: identity.Value.Provenance));
    }

    /// <summary>创建固定 duration 的 interval / Create a fixed-duration interval.</summary>
    /// <param name="id">稳定 interval ID / Stable interval ID.</param>
    /// <param name="start">起始时间表达式 / Start-time expression.</param>
    /// <param name="size">固定持续时间 / Fixed duration.</param>
    /// <param name="end">结束时间表达式 / End-time expression.</param>
    /// <param name="presence">可选存在文字 / Optional presence literal.</param>
    /// <param name="scope">身份作用域 / Identity scope.</param>
    /// <param name="origin">兼容来源字段 / Compatibility origin field.</param>
    /// <param name="identityProvenance">完整身份来源 / Complete identity provenance.</param>
    /// <returns>interval 或结构化错误 / Interval or a structured error.</returns>
    public static Result<IntervalVariable, ErrorCode, Error<ErrorCode>> CreateFixed(
        string id,
        ConstraintProgrammingExpression start,
        Int64 size,
        ConstraintProgrammingExpression end,
        BooleanLiteral? presence = null,
        ModelElementScope scope = ModelElementScope.ModelLocal,
        string? origin = null,
        IReadOnlyList<ModelElementOrigin>? identityProvenance = null) {
        if (size < Int64.Zero) {
            return Failure(
                $"interval duration 不得为负：{size} / Interval size must not be negative: {size}");
        }
        return Create(
            id: id,
            start: start,
            size: ConstraintProgrammingExpression.CreateConstant(size),
            end: end,
            presence: presence,
            scope: scope,
            origin: origin,
            identityProvenance: identityProvenance);
    }

    /// <summary>求值并校验 interval 一致性 / Evaluate and validate interval consistency.</summary>
    /// <param name="values">整数变量赋值 / Integer variable assignments.</param>
    /// <returns>interval 值或结构化错误 / Interval value or a structured error.</returns>
    public Result<IntervalValue, ErrorCode, Error<ErrorCode>> Evaluate(
        IReadOnlyDictionary<VariableId, Int64> values) {
        if (Presence is not null) {
            Result<bool, ErrorCode, Error<ErrorCode>> present = Presence.EvaluateInteger(values);
            if (present.IsFailed) {
                return present.Map(_ => default(IntervalValue)!);
            }
            if (!present.Value) {
                return Results.Ok(new IntervalValue(
                    Start: Int64.Zero,
                    Size: Int64.Zero,
                    End: Int64.Zero,
                    Present: false));
            }
        }

        Result<Int64, ErrorCode, Error<ErrorCode>> start = Start.Evaluate(values);
        if (start.IsFailed) {
            return start.Map(_ => default(IntervalValue)!);
        }
        Result<Int64, ErrorCode, Error<ErrorCode>> size = Size.Evaluate(values);
        if (size.IsFailed) {
            return size.Map(_ => default(IntervalValue)!);
        }
        Result<Int64, ErrorCode, Error<ErrorCode>> end = End.Evaluate(values);
        if (end.IsFailed) {
            return end.Map(_ => default(IntervalValue)!);
        }
        if (size.Value < Int64.Zero) {
            return FailureValue(
                $"interval duration 不得为负：{size.Value} / Interval size must not be negative: {size.Value}");
        }

        BigInteger expectedEnd =
            new BigInteger(start.Value.ToLong()) + new BigInteger(size.Value.ToLong());
        if (expectedEnd < long.MinValue || expectedEnd > long.MaxValue) {
            return FailureValue(
                "interval 起止时间溢出 Int64 / Interval start-plus-size overflows Int64");
        }
        if (end.Value.ToLong() != (long)expectedEnd) {
            return FailureValue(
                "interval 必须满足 end = start + size / Interval must satisfy end = start + size");
        }
        return Results.Ok(new IntervalValue(
            Start: start.Value,
            Size: size.Value,
            End: end.Value));
    }

    internal IntervalVariable WithIdentity(
        ModelElementScope scope,
        IReadOnlyList<ModelElementOrigin> provenance) =>
        new(
            id: Id,
            start: Start,
            size: Size,
            end: End,
            presence: Presence,
            scope: scope,
            origin: Origin,
            identityProvenance: provenance);

    internal IntervalVariable WithExpressions(
        ConstraintProgrammingExpression start,
        ConstraintProgrammingExpression size,
        ConstraintProgrammingExpression end,
        BooleanLiteral? presence) =>
        new(
            id: Id,
            start: start,
            size: size,
            end: end,
            presence: presence,
            scope: Scope,
            origin: Origin,
            identityProvenance: IdentityProvenance);

    private static Result<IntervalVariable, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<IntervalVariable>(
            new Err<ErrorCode>(
                code: ErrorCode.IllegalArgument,
                message: message));

    private static Result<IntervalValue, ErrorCode, Error<ErrorCode>> FailureValue(string message) =>
        Results.Failed<IntervalValue>(
            new Err<ErrorCode>(
                code: ErrorCode.IllegalArgument,
                message: message));
}

/// <summary>NoOverlap 排程约束 / No-overlap scheduling constraint.</summary>
public sealed record NoOverlap : ConstraintProgrammingConstraint {
    private NoOverlap(IReadOnlyList<IntervalVariable> intervals) {
        Intervals = intervals;
    }

    /// <summary>参与排程的 interval / Scheduled intervals.</summary>
    public IReadOnlyList<IntervalVariable> Intervals { get; }

    /// <inheritdoc/>
    public override IReadOnlySet<VariableId> Variables =>
        Intervals.SelectMany(interval => interval.Variables).ToFrozenSet();

    /// <summary>创建 NoOverlap 约束 / Create a NoOverlap constraint.</summary>
    /// <param name="intervals">参与排程的 interval / Scheduled intervals.</param>
    /// <returns>约束或结构化错误 / Constraint or a structured error.</returns>
    public static Result<NoOverlap, ErrorCode, Error<ErrorCode>> Create(
        IEnumerable<IntervalVariable> intervals) {
        ArgumentNullException.ThrowIfNull(intervals);
        var list = intervals.ToList();
        if (list.Count == 0) {
            return Results.Failed<NoOverlap>(
                new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: "NoOverlap interval 集合不能为空 / NoOverlap interval collection must not be empty"));
        }
        return Results.Ok(new NoOverlap(new ReadOnlyCollection<IntervalVariable>(list)));
    }

    /// <inheritdoc/>
    public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(
        IReadOnlyDictionary<VariableId, Int64> values) {
        var evaluated = new List<IntervalValue>();
        foreach (IntervalVariable interval in Intervals) {
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> result = interval.Evaluate(values);
            if (result.IsFailed) {
                return result.Map(_ => false);
            }
            if (result.Value.Present) {
                evaluated.Add(result.Value);
            }
        }
        for (int left = 0; left < evaluated.Count; ++left) {
            for (int right = left + 1; right < evaluated.Count; ++right) {
                if (evaluated[left].Start < evaluated[right].End &&
                    evaluated[right].Start < evaluated[left].End) {
                    return Results.Ok(false);
                }
            }
        }
        return Results.Ok(true);
    }
}

/// <summary>Cumulative 容量约束 / Cumulative capacity constraint.</summary>
public sealed record Cumulative : ConstraintProgrammingConstraint {
    private Cumulative(
        IReadOnlyList<IntervalVariable> intervals,
        IReadOnlyList<ConstraintProgrammingExpression> demands,
        ConstraintProgrammingExpression capacity) {
        Intervals = intervals;
        Demands = demands;
        Capacity = capacity;
    }

    /// <summary>参与容量约束的 interval / Intervals consuming capacity.</summary>
    public IReadOnlyList<IntervalVariable> Intervals { get; }

    /// <summary>对应资源需求 / Corresponding resource demands.</summary>
    public IReadOnlyList<ConstraintProgrammingExpression> Demands { get; }

    /// <summary>容量表达式 / Capacity expression.</summary>
    public ConstraintProgrammingExpression Capacity { get; }

    /// <inheritdoc/>
    public override IReadOnlySet<VariableId> Variables {
        get {
            var variables = new HashSet<VariableId>();
            foreach (IntervalVariable interval in Intervals) {
                variables.UnionWith(interval.Variables);
            }
            foreach (ConstraintProgrammingExpression demand in Demands) {
                variables.UnionWith(demand.Variables);
            }
            variables.UnionWith(Capacity.Variables);
            return variables.ToFrozenSet();
        }
    }

    /// <summary>创建 Cumulative 约束 / Create a Cumulative constraint.</summary>
    /// <param name="intervals">参与容量约束的 interval / Intervals consuming capacity.</param>
    /// <param name="demands">对应资源需求 / Corresponding resource demands.</param>
    /// <param name="capacity">容量表达式 / Capacity expression.</param>
    /// <returns>约束或结构化错误 / Constraint or a structured error.</returns>
    public static Result<Cumulative, ErrorCode, Error<ErrorCode>> Create(
        IEnumerable<IntervalVariable> intervals,
        IEnumerable<ConstraintProgrammingExpression> demands,
        ConstraintProgrammingExpression capacity) {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(capacity);
        var intervalList = intervals.ToList();
        var demandList = demands.ToList();
        if (intervalList.Count == 0) {
            return Results.Failed<Cumulative>(
                new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: "Cumulative interval 集合不能为空 / Cumulative interval collection must not be empty"));
        }
        if (intervalList.Count != demandList.Count) {
            return Results.Failed<Cumulative>(
                new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: "Cumulative interval 与 demand 数量必须一致 / Cumulative interval and demand counts must match"));
        }
        return Results.Ok(new Cumulative(
            intervals: new ReadOnlyCollection<IntervalVariable>(intervalList),
            demands: new ReadOnlyCollection<ConstraintProgrammingExpression>(demandList),
            capacity: capacity));
    }

    /// <inheritdoc/>
    public override Result<bool, ErrorCode, Error<ErrorCode>> IsSatisfied(
        IReadOnlyDictionary<VariableId, Int64> values) {
        var evaluated = new List<(IntervalValue Interval, Int64 Demand)>();
        for (int index = 0; index < Intervals.Count; ++index) {
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> interval = Intervals[index].Evaluate(values);
            if (interval.IsFailed) {
                return interval.Map(_ => false);
            }
            if (!interval.Value.Present) {
                continue;
            }
            Result<Int64, ErrorCode, Error<ErrorCode>> demand = Demands[index].Evaluate(values);
            if (demand.IsFailed) {
                return demand.Map(_ => false);
            }
            if (demand.Value < Int64.Zero) {
                return Results.Failed<bool>(
                    new Err<ErrorCode>(
                        code: ErrorCode.IllegalArgument,
                        message: $"cumulative demand 不得为负：{demand.Value} / Cumulative demand must not be negative: {demand.Value}"));
            }
            evaluated.Add((interval.Value, demand.Value));
        }

        Result<Int64, ErrorCode, Error<ErrorCode>> capacity = Capacity.Evaluate(values);
        if (capacity.IsFailed) {
            return capacity.Map(_ => false);
        }
        if (capacity.Value < Int64.Zero) {
            return Results.Failed<bool>(
                new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: $"cumulative capacity 不得为负：{capacity.Value} / Cumulative capacity must not be negative: {capacity.Value}"));
        }

        foreach (Int64 point in evaluated
                     .SelectMany(entry => new[] { entry.Interval.Start, entry.Interval.End })
                     .Distinct()) {
            BigInteger load = BigInteger.Zero;
            foreach ((IntervalValue interval, Int64 demand) in evaluated) {
                if (interval.Start <= point && point < interval.End) {
                    load += new BigInteger(demand.ToLong());
                }
            }
            if (load > new BigInteger(capacity.Value.ToLong())) {
                return Results.Ok(false);
            }
        }
        return Results.Ok(true);
    }
}
