// 广义流约束管线 / Generalized-flow constraint pipelines.
//（2032 字节）、
// , and
//.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Service.Limits;

/// <summary>
/// 对弧起点投入量施加硬容量界 / Enforce hard capacity bounds on origin-side arc input.
/// </summary>
/// <remarks>
/// **容量作用在"起点投入"上，不是"终点产出"上**。广义流弧的产出是投入经过单位换算与业务产率的函数，两者量纲与数值都不同；
/// 若把容量加到产出侧，一条产率 0.8 的弧会被允许 1.25 倍的原料投入，容量约束因此系统性失效。
/// **Capacity applies to the origin input, not the destination output.** A generalized-flow arc's output is a function of its
/// input through unit conversion and business yield, with different dimensions and values; applying capacity on the output side
/// would let an arc with yield 0.8 take in 1.25 times the raw material, so the capacity constraint fails systematically.
/// </remarks>
public sealed class GeneralizedFlowInputCapacityPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> _inputs;
    private readonly IReadOnlyDictionary<NetworkArcId, (Flt64 Lower, Flt64 Upper)> _bounds;

    /// <summary>
    /// 构造弧投入容量管线 / Construct the arc-input capacity pipeline.
    /// </summary>
    /// <param name="inputs">弧投入表达式 / Arc input expressions.</param>
    /// <param name="bounds">弧投入上下界 / Arc input bounds.</param>
    /// <param name="name">管线及约束名称前缀 / Pipeline and constraint-name prefix.</param>
    public GeneralizedFlowInputCapacityPipeline(
        IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> inputs,
        IReadOnlyDictionary<NetworkArcId, (Flt64 Lower, Flt64 Upper)> bounds,
        string name) {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(name);
        _inputs = inputs;
        _bounds = bounds;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册每条弧的投入下界和有限上界 / Register input lower and finite upper bounds for every arc.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        foreach ((NetworkArcId arcId, LinearPolynomial<Flt64> expression) in _inputs) {
            if (!_bounds.TryGetValue(arcId, out (Flt64 Lower, Flt64 Upper) bound)) {
                return NetworkSchedulingErrors.TryFailure(
                    $"广义流弧容量缺失：{arcId.Value} / Generalized-flow arc capacity is missing: {arcId.Value}");
            }
            Try lower = model.AddConstraint(
                expression.Ge(Scalar(bound.Lower)), this, name: $"{Name}_{arcId.Value}_input_lower");
            if (lower is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
                return Results.Failed<Success>(lowerFailure.Error);
            }
            Try upper = model.AddConstraint(
                expression.Le(Scalar(bound.Upper)), this, name: $"{Name}_{arcId.Value}_input_upper");
            if (upper is Failed<Success, ErrorCode, Error<ErrorCode>> upperFailure) {
                return Results.Failed<Success>(upperFailure.Error);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>把标量包装成常数多项式 / Wrap a scalar as a constant polynomial.</summary>
    /// <param name="value">标量 / The scalar.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Scalar(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}

/// <summary>
/// 应用"流出投入 − 流入产出 = 节点净供给"的广义平衡约束 / Apply generalized balance: outgoing input minus incoming output equals
/// node net supply.
/// </summary>
/// <remarks>
/// **平衡式的两侧量纲不同也成立**，因为流入侧用的是"到达产出"、流出侧用的是"起点投入"，这正是广义流与普通流的区别。若误用
/// 同一套投入表达式做两侧相减，就把广义流退化成了普通流：产率不为 1 的弧上，节点供需不再守恒，而解看起来仍然可行。
/// **The two sides of the balance need not share a dimension**, because the incoming side uses "arrival output" while the outgoing
/// side uses "origin input" — that is exactly what distinguishes generalized flow from ordinary flow. Subtracting the same input
/// expression on both sides degenerates generalized flow back to ordinary flow: on arcs whose yield is not one, node supply no longer
/// balances while the solution still looks feasible.
/// </remarks>
public sealed class GeneralizedFlowNodeBalancePipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyDictionary<NetworkNodeId, LinearPolynomial<Flt64>> _balances;
    private readonly IReadOnlyDictionary<NetworkNodeId, Flt64> _netSupplies;

    /// <summary>
    /// 构造节点广义平衡管线 / Construct the generalized node-balance pipeline.
    /// </summary>
    /// <param name="balances">节点平衡表达式 / Node balance expressions.</param>
    /// <param name="netSupplies">节点净供给 / Node net supplies.</param>
    /// <param name="name">管线及约束名称前缀 / Pipeline and constraint-name prefix.</param>
    public GeneralizedFlowNodeBalancePipeline(
        IReadOnlyDictionary<NetworkNodeId, LinearPolynomial<Flt64>> balances,
        IReadOnlyDictionary<NetworkNodeId, Flt64> netSupplies,
        string name) {
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(netSupplies);
        ArgumentNullException.ThrowIfNull(name);
        _balances = balances;
        _netSupplies = netSupplies;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册节点广义平衡约束 / Register generalized node-balance constraints.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        foreach ((NetworkNodeId nodeId, LinearPolynomial<Flt64> expression) in _balances) {
            if (!_netSupplies.TryGetValue(nodeId, out Flt64 netSupply)) {
                return NetworkSchedulingErrors.TryFailure(
                    $"广义流节点供需缺失：{nodeId.Value} / Generalized-flow node balance is missing: {nodeId.Value}");
            }
            Try result = model.AddConstraint(
                expression.Eq(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), netSupply)),
                this,
                name: $"{Name}_balance_{nodeId.Value}");
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }
}
