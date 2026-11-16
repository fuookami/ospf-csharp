// 网络结构建设费用目标 / Network-structure construction-cost objective.
// 与固定 Kotlin （1053 字节）对应。

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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Service;

/// <summary>
/// 注册建设费用最小化目标 / Register the construction-cost minimisation objective.
/// </summary>
/// <remarks>
/// **费用表达式必须惰性求值**。建设费用表达式只有在建设变量组件注册进模型之后才存在——构造目标的那一刻它还是空的。若在构造期
/// 就把多项式抓下来，注册进模型的是一个空目标：模型照样求解成功，但返回的是任意一个可行解而不是最优解，且没有任何提示。
/// ` -&gt; LinearPolynomial` 而不是 `LinearPolynomial` 正是这个原因。
/// **The cost expression must be evaluated lazily.** The construction-cost expression only exists after the construction variable
/// component has been registered into the model — at objective-construction time it is still empty. Capturing the polynomial eagerly
/// would register an empty objective: the model still solves successfully but returns an arbitrary feasible solution instead of the
/// optimum, with nothing reported. That is exactly why the code takes a ` -&gt; LinearPolynomial` supplier rather than a
/// polynomial.
/// </remarks>
public sealed class NetworkStructureConstructionCostObjective : IPipeline<LinearMetaModel<Flt64>> {
    private readonly Func<LinearPolynomial<Flt64>> _cost;

    /// <summary>
    /// 构造建设费用目标 / Construct the construction-cost objective.
    /// </summary>
    /// <param name="cost">建设费用表达式供应器 / The construction-cost expression supplier.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    public NetworkStructureConstructionCostObjective(Func<LinearPolynomial<Flt64>> cost, string name) {
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
    /// 将建设费用设为元模型目标 / Set construction cost as the meta-model objective.
    /// </summary>
    /// <param name="model">目标元模型 / Target meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return model.AddObject(ObjectCategory.Minimum, _cost(), Name, Name);
    }
}
