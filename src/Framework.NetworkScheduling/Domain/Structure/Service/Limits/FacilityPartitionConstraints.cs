// 设施分区约束 / Facility-partition constraints.

using System;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Service.Limits;

/// <summary>
/// 对单个分区实施必选／可选唯一归属 / Enforce mandatory or optional unique ownership for one partition.
/// </summary>
/// <remarks>
/// **必选分区用等式、可选分区用不等式**。可选分区若也用等式，模型就被迫为每个分区找一个设施——包括那些业务上允许无人负责的分区；
/// 而用 `≤ 1` 则既禁止"一个分区两个设施"，又保留"不归属"的可能。
/// **A required partition uses an equality, an optional one an inequality.** An equality on an optional partition forces the model to
/// find an owner for every partition, including those that may legitimately have none; `≤ 1` both forbids "one partition, two owners"
/// and keeps "no owner" reachable.
/// </remarks>
public sealed class FacilityPartitionAssignmentConstraint : IPipeline<LinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _assignments;
    private readonly bool _required;

    /// <summary>
    /// 构造分区归属约束 / Construct the partition-ownership constraint.
    /// </summary>
    /// <param name="assignments">归属量表达式 / The assignment expression.</param>
    /// <param name="required">是否必须归属 / Whether ownership is mandatory.</param>
    /// <param name="name">约束命名空间 / Constraint namespace.</param>
    public FacilityPartitionAssignmentConstraint(
        LinearPolynomial<Flt64> assignments,
        bool required,
        string name) {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(name);
        _assignments = assignments;
        _required = required;
        Name = name;
    }

    /// <summary>约束名称 / Constraint name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册分区归属约束 / Register the partition-ownership constraint.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return _required
            ? model.AddConstraint(_assignments.Eq(Constant(Flt64.One)), this, name: Name)
            : model.AddConstraint(_assignments.Le(Constant(Flt64.One)), this, name: Name);
    }

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    /// <param name="value">常数值 / The constant.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}

/// <summary>
/// 对设施分区数量实施最小／最大配额 / Enforce a minimum and optional maximum number of assigned partitions.
/// </summary>
/// <remarks>
/// **最大配额缺省时只注册下界，不注册"无穷大上界"**。把缺省上限写成一个大数会让模型多一条几乎不起作用的行，同时把"没有上限"这个
/// 事实藏进一个魔法常数里；留空则模型里根本没有这条约束。
/// **A missing maximum registers no upper row at all rather than an "infinite" one.** Encoding the absence as a large number adds an
/// inert row and hides the fact inside a magic constant; leaving it out means the constraint simply does not exist.
/// </remarks>
public sealed class FacilityPartitionQuotaConstraint : IPipeline<LinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _assignments;
    private readonly int _minimum;
    private readonly int? _maximum;

    /// <summary>
    /// 构造设施分区配额约束 / Construct the facility partition-quota constraint.
    /// </summary>
    /// <param name="assignments">归属量表达式 / The assignment expression.</param>
    /// <param name="minimum">最少归属分区数 / Minimum owned partitions.</param>
    /// <param name="maximum">最多归属分区数，null 表示不限制 / Maximum owned partitions, null for no limit.</param>
    /// <param name="name">约束命名空间 / Constraint namespace.</param>
    public FacilityPartitionQuotaConstraint(
        LinearPolynomial<Flt64> assignments,
        int minimum,
        int? maximum,
        string name) {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(name);
        _assignments = assignments;
        _minimum = minimum;
        _maximum = maximum;
        Name = name;
    }

    /// <summary>约束名称 / Constraint name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册设施分区配额 / Register the facility partition quota.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_minimum > 0) {
            Try lower = model.AddConstraint(
                _assignments.Ge(Constant(new Flt64(_minimum))), this, name: $"{Name}_minimum");
            if (lower is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
                return Results.Failed<Success>(lowerFailure.Error);
            }
        }
        if (_maximum is { } limit) {
            return model.AddConstraint(
                _assignments.Le(Constant(new Flt64(limit))), this, name: $"{Name}_maximum");
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    /// <param name="value">常数值 / The constant.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}
