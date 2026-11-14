// 网络流约束管线 / Flow constraint pipelines.
// 与固定 Kotlin 基线 `service/limits/CapacityBoundConstraint.kt`（1216 字节）、`service/limits/FlowConservationConstraint.kt`（1071 字节）对应。
// Mirrors the pinned baseline's `CapacityBoundConstraint.kt` and `FlowConservationConstraint.kt`.
//
// 两条约束都是「把一棵已注册的表达式树变成一行约束」，本身不含业务判断：容量上下界与流量守恒的正确性由调用方保证，这里只负责
// 忠实注册。因此它们都能直接对着 `IAbstractLinearMetaModel<Flt64>` 实现，不需要具体求解器。
// Both constraints only "turn an already-registered expression tree into one row"; they carry no business logic of their own — the
// correctness of the capacity bounds and the flow balance is the caller's responsibility and this layer just registers them
// faithfully. That is why both can target `IAbstractLinearMetaModel<Flt64>` directly without any concrete solver.

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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Service.Limits;

/// <summary>
/// 资源容量上下界 / Resource capacity bounds.
/// </summary>
/// <remarks>
/// **上下界注册成两条独立约束，而不是合成一条区间约束**。求解器与对偶都按单行理解约束；合成区间后，上界与下界共用一份对偶，
/// 而在任意一个最优解上通常只有一侧是紧的——共用对偶会让定价侧同时"看到"两侧的边际价值，算出的 reduced cost 对不上任何真实行。
/// **The lower and upper bounds are registered as two independent constraints, not merged into one interval constraint.** Solvers
/// and duals reason about single rows; a merged interval shares one dual between both sides while at any optimum usually only one
/// side is tight — a shared dual would make pricing "see" marginal value on both sides and produce reduced costs matching no real
/// row.
/// </remarks>
public sealed class CapacityBoundConstraint : IPipeline<IAbstractLinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _expression;
    private readonly Flt64 _lower;
    private readonly Flt64 _upper;

    /// <summary>
    /// 构造容量上下界约束 / Construct the capacity-bound constraint.
    /// </summary>
    /// <param name="expression">被约束的资源表达式 / The constrained resource expression.</param>
    /// <param name="lower">下界 / Lower bound.</param>
    /// <param name="upper">上界 / Upper bound.</param>
    /// <param name="name">约束名称 / Constraint name.</param>
    public CapacityBoundConstraint(LinearPolynomial<Flt64> expression, Flt64 lower, Flt64 upper, string name) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(name);
        _expression = expression;
        _lower = lower;
        _upper = upper;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册资源容量上下界 / Register the resource capacity bounds.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try lowerResult = model.AddConstraint(
            _expression.Ge(Scalar(_lower)), this, name: $"{Name}_lower");
        if (lowerResult is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
            return Results.Failed<Success>(lowerFailure.Error);
        }
        return model.AddConstraint(_expression.Le(Scalar(_upper)), this, name: $"{Name}_upper");
    }

    /// <summary>
    /// 把标量包装成常数多项式 / Wrap a scalar as a constant polynomial.
    /// </summary>
    /// <param name="value">标量 / The scalar.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Scalar(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}

/// <summary>
/// 节点净流量守恒 / Node net-flow conservation.
/// </summary>
public sealed class FlowConservationConstraint : IPipeline<IAbstractLinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _balance;
    private readonly Flt64 _net;

    /// <summary>
    /// 构造节点流量守恒约束 / Construct the node flow-conservation constraint.
    /// </summary>
    /// <param name="balance">节点净流量表达式 / The node net-flow expression.</param>
    /// <param name="net">声明的净供需 / The declared net balance.</param>
    /// <param name="name">约束名称 / Constraint name.</param>
    public FlowConservationConstraint(LinearPolynomial<Flt64> balance, Flt64 net, string name) {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(name);
        _balance = balance;
        _net = net;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册节点流量守恒约束 / Register the node flow-conservation constraint.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return model.AddConstraint(
            _balance.Eq(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), _net)),
            this,
            name: Name);
    }
}
