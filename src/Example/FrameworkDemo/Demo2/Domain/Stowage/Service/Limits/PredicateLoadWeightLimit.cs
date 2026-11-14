// 谓词装载重量的上界约束 / The upper bound row on the predicate load weight.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 谓词装载重量上界 / Upper bound on the predicate load weight.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/PredicateLoadWeightLimit.kt`：给每个"需要谓词重量"的舱位加一行 `y_j ≤ plw.max`。
///
/// 这行约束存在的原因是 `y` 的正上下界已经由变量范围给到 `mlw`，但**谓词重量还有一个更紧的规格上界**。少了这一行，
/// `y` 会一路顶到 `mlw`，松弛量随之失真，而求解器不会有任何抱怨。
///
/// Mirrors the baseline: add `y_j ≤ plw.max` for every position that needs the predicate weight. The row exists because `y`'s declared range already goes up to `mlw`, but the
/// predicate weight has a **tighter specification bound**. Without the row `y` drifts to `mlw`, the slack becomes meaningless, and the solver never complains.
/// </remarks>
public sealed class PredicateLoadWeightLimit
{
    private readonly IReadOnlyList<Model.StowagePosition> _positions;
    private readonly Model.Load _load;

    /// <summary>
    /// 构造谓词重量上界 / Construct the predicate weight bound.
    /// </summary>
    /// <param name="positions">舱位 / The stowage positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public PredicateLoadWeightLimit(
        IReadOnlyList<Model.StowagePosition> positions,
        Model.Load load,
        string name = "predicate_load_weight_limit") {
        _positions = positions;
        _load = load;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);
        for (int j = 0; j < _positions.Count; ++j) {
            Model.StowagePosition position = _positions[j];
            if (!position.Status.PredicateWeightNeeded) {
                continue;
            }
            if (position.Plw is not { } spec) {
                // 缺少规格是**错误**而不是跳过：跳过会让这个舱位的谓词重量无上界，而报告里看不出少了哪一行。
                // A missing specification is an **error**, not a skip: skipping leaves this position's predicate weight unbounded with nothing in the report to show it.
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "舱位 " + position.Name + " 缺少谓词装载重量规格 / Position " + position.Name +
                    " is missing predicate load weight specification"));
            }
            if (_load.Y is null) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "推荐装载重量变量未注册 / the recommended load weight variables are not registered"));
            }

            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, _load.Y[j]) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(spec.Max)),
                Comparison.LE,
                Name + "_" + position.Name);
            Try added = linear.AddConstraint(row, group: null, name: Name + "_" + position.Name);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
