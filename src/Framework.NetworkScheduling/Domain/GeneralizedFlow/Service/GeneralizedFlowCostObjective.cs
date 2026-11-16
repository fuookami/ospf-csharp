// 广义流费用目标 / Generalized-flow cost objective.

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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Service;

/// <summary>
/// 按弧投入量资费最小化总费用 / Minimize total tariff charged on arc input.
/// </summary>
public sealed class GeneralizedFlowCostObjective : IPipeline<LinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _cost;

    /// <summary>
    /// 构造总费用目标 / Construct the total-cost objective.
    /// </summary>
    /// <param name="cost">已注册的总费用表达式 / The registered total-cost expression.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    public GeneralizedFlowCostObjective(LinearPolynomial<Flt64> cost, string name) {
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
    /// 注册总费用目标 / Register the total-cost objective.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return model.AddObject(ObjectCategory.Minimum, _cost, Name, Name);
    }
}
