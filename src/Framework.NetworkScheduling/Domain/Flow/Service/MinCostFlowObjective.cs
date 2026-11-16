// 目标管线 / Objective pipelines.
// 与固定 Kotlin （938 字节）、
//（998 字节）、
//（1053 字节）对应。
// , and
//.
//
// 三个目标管线形状相同：把一棵**已经注册进模型**的表达式树交给模型当目标。它们不带业务逻辑，因此不需要具体求解器。
// All three objectives share one shape: hand an expression tree that is **already registered in the model** to the model as its
// objective. They carry no business logic and therefore need no concrete solver.

using System;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Service;

/// <summary>
/// 运行费用目标 / Operating cost objective.
/// </summary>
public sealed class MinCostFlowObjective : IPipeline<LinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _cost;

    /// <summary>
    /// 构造运行费用目标 / Construct the operating-cost objective.
    /// </summary>
    /// <param name="cost">已注册的运行费用表达式 / The registered operating-cost expression.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    public MinCostFlowObjective(LinearPolynomial<Flt64> cost, string name) {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(name);
        _cost = cost;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册运行费用目标 / Register the operating-cost objective.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return model.AddObject(ObjectCategory.Minimum, _cost, Name, Name);
    }
}
