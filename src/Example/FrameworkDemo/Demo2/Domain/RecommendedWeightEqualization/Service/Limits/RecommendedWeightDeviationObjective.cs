#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.Limits;

/// <summary>
/// 推荐重量偏差目标 / The recommended-weight-deviation objective.
/// </summary>
/// <remarks>
/// 与对齐：把**全部舱位的偏差变量之和**乘负系数后作为**最大化**目标。
///
/// 四处必须沿用：
///
/// 1. **是最大化，且系数为负**。写成"最小化偏差"数值上等价，但`maximize` 与下游的目标计数/名称是一套的。
/// 2. **求和的是 `load.z` 全部舱位**，不筛 `RecommendedWeightNeeded`——偏差变量本身只在需要的舱位上非零。
/// 3. **目标是单个**，不是每舱位一个。
/// 4. **目标名就是 `Name`**（`recommended_weight_deviation_objective`），与另外三条管线的"目标名带空格"不同。基线这里传的是 `name`。
///
/// The sum of **every position's deviation variable**, scaled by a negative coefficient, becomes a **maximisation** objective. Four things are carried over:
/// **it is a maximisation with a negative coefficient** — writing "minimise the deviation" is numerically equivalent, but its `maximize` pairs with downstream objective
/// counting and naming; **the sum covers every `load.z` entry** without filtering on `RecommendedWeightNeeded`, since the deviation variables are already zero where not needed; **it
/// is a single objective**, not one per position; and **the objective name is `Name` itself** (`recommended_weight_deviation_objective`), unlike the other three pipelines whose
/// objective names carry spaces — the code passes `name` here.
/// </remarks>
internal sealed class RecommendedWeightDeviationObjective
{
    private readonly Load _load;
    private readonly Func<Flt64> _coefficient;

    /// <summary>
    /// 构造偏差目标 / Construct the deviation objective.
    /// </summary>
    /// <param name="load">载荷 / The load.</param>
    /// <param name="coefficient">偏差系数 / The deviation coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public RecommendedWeightDeviationObjective(
        Load load,
        Func<Flt64>? coefficient = null,
        string name = "recommended_weight_deviation_objective")
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
        _coefficient = coefficient ?? (() => Flt64.One);
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_load.Z is not { } z)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "推荐装载数量变量未注册 / the recommended-load-count variables are not registered"));
        }

        Flt64 coefficient = _coefficient();
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        // **遍历用 `IVariableItem`**：`z` 的元素类型是 `CombinationVariableItem<T, TType>`，把泛型参数写死会与实际的
        // `UInt64`/`UInteger` 实例化对不上（实测编译失败）。`IVariableItem` 是它们的公共接口，且 `LinearMonomial` 收它。
        // **Iterate as `IVariableItem`**: `z` holds `CombinationVariableItem<T, TType>`, and hard-coding the type arguments does not match the actual `UInt64`/`UInteger`
        // instantiation (measured to fail compilation). `IVariableItem` is their common interface, and `LinearMonomial` accepts it.
        foreach (Fuookami.Ospf.Core.Variable.IVariableItem deviation in z)
        {
            // `−coefficient × deviation`：符号是变量本身，系数为负。
            // `−coefficient × deviation`: the symbol is the variable, the coefficient negative.
            monomials.Add(new LinearMonomial<Flt64>(new Flt64(-1.0) * coefficient, deviation));
        }
        var objective = new LinearPolynomial<Flt64>(monomials, constant);
        return model.AddObject(ObjectCategory.Maximum, objective, Name, displayName: null);
    }
}
