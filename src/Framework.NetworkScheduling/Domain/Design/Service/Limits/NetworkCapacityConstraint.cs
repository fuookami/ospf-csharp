// 结构决策启用的容量约束 / Capacity bounds gated by structural decisions.
// 与固定 Kotlin （1560 字节）对应。

using System;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Service.Limits;

/// <summary>
/// 由结构决策启用的处理／服务容量约束族 / Processing/service capacity gated by structural decisions.
/// </summary>
/// <remarks>
/// **上下界都是表达式而非常数**：容量随"建了哪个等级、开着哪个设施"变化，`lower × 启用量 ≤ 占用 ≤ upper × 启用量` 才能在未建设时把
/// 占用压到零。若把界算成常数，未建设的通道仍然可以承载货流，而建设决策就变成了纯装饰。
/// **Both bounds are expressions rather than constants**: capacity changes with which level is built and which facility is open, so
/// only `lower × activation ≤ load ≤ upper × activation` can drive the load to zero when nothing is built. With constant bounds an
/// unbuilt service still carries flow, and the construction decision becomes pure decoration.
/// </remarks>
public sealed class NetworkCapacityConstraint : IPipeline<LinearMetaModel<Flt64>> {
    private readonly LinearPolynomial<Flt64> _load;
    private readonly LinearPolynomial<Flt64> _lower;
    private readonly LinearPolynomial<Flt64> _upper;

    /// <summary>
    /// 构造结构相关容量约束 / Construct the structure-dependent capacity constraint.
    /// </summary>
    /// <param name="load">已归一化资源占用 / Normalised resource load.</param>
    /// <param name="lower">结构相关下界 / Structure-dependent lower bound.</param>
    /// <param name="upper">有限的结构相关上界 / Finite structure-dependent upper bound.</param>
    /// <param name="name">约束命名空间 / Constraint namespace.</param>
    public NetworkCapacityConstraint(
        LinearPolynomial<Flt64> load,
        LinearPolynomial<Flt64> lower,
        LinearPolynomial<Flt64> upper,
        string name) {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);
        ArgumentNullException.ThrowIfNull(name);
        _load = load;
        _lower = lower;
        _upper = upper;
        Name = name;
    }

    /// <summary>约束名称 / Constraint name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册结构相关容量上下界 / Register structure-dependent capacity bounds.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try lower = model.AddConstraint(_load.Ge(_lower), this, name: $"{Name}_lower");
        if (lower is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
            return Results.Failed<Success>(lowerFailure.Error);
        }
        Try upper = model.AddConstraint(_load.Le(_upper), this, name: $"{Name}_upper");
        if (upper is Failed<Success, ErrorCode, Error<ErrorCode>> upperFailure) {
            return Results.Failed<Success>(upperFailure.Error);
        }
        return Results.Ok(Results.SuccessInstance);
    }
}
