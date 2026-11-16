// 路径行边界管线 / Path row-bound pipeline.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Service;

/// <summary>
/// 单行边界管线，行键同时用于对偶提取 / A row-bound pipeline whose key is also used for dual extraction.
/// </summary>
/// <remarks>
/// **上下界相等时注册一条等式，而不是两条不等式**。写成 `≥ b` 与 `≤ b` 两条，模型里就有两个重复行，对偶值被摊到两处，定价据此算出的
/// reduced cost 会系统性偏小。等式只有一行、一个对偶。
/// **Equal bounds register one equality, not two inequalities.** Written as `≥ b` and `≤ b`, the model holds two duplicate rows and
/// the dual is split between them, so the reduced cost the pricer computes drifts low. An equality has one row and one dual.
/// **注册的约束必须逐条记账**。`Constraints` 保存本次注册产生的约束，让上层在移除列或诊断时能精确指认它们；漏记账会让后续清理
/// 变成"按名字猜"。
/// **The registered constraints must be recorded one by one.** `Constraints` keeps the constraints this registration produced, so
/// later column removal or diagnostics can name them exactly; without it, cleanup degenerates into guessing by name.
/// </remarks>
public sealed class PathCompilationRows {
    private readonly List<MathConstraint> _constraints = new();

    /// <summary>
    /// 构造单行边界管线 / Construct the row-bound pipeline.
    /// </summary>
    /// <param name="id">稳定行键 / Stable row key.</param>
    /// <param name="symbol">可增量更新的表达式 / The incrementally updatable expression.</param>
    /// <param name="lower">可选下界 / The optional lower bound.</param>
    /// <param name="upper">可选上界 / The optional upper bound.</param>
    public PathCompilationRows(string id, LinearExpressionSymbol symbol, Flt64? lower, Flt64? upper) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(symbol);
        Id = id;
        Symbol = symbol;
        Lower = lower;
        Upper = upper;
    }

    /// <summary>稳定行键 / Stable row key.</summary>
    public string Id { get; }

    /// <summary>可增量更新的表达式 / The incrementally updatable expression.</summary>
    public LinearExpressionSymbol Symbol { get; }

    /// <summary>可选下界 / The optional lower bound.</summary>
    public Flt64? Lower { get; }

    /// <summary>可选上界 / The optional upper bound.</summary>
    public Flt64? Upper { get; }

    /// <summary>本次注册产生的约束 / The constraints produced by this registration.</summary>
    public IReadOnlyList<MathConstraint> Constraints => _constraints;

    /// <summary>
    /// 注册边界 / Register the bounds.
    /// </summary>
    /// <param name="model">当前模型 / The current model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        int start = model.Constraints.Count;
        Try result;
        if (Lower is { } lower && Upper is { } upper && lower.Equals(upper)) {
            result = model.AddConstraint(
                Symbol.Polynomial.Eq(Constant(lower)),
                null,
                name: $"{Symbol.Name}_eq");
        }
        else {
            result = Results.Ok(Results.SuccessInstance);
            if (Lower is { } low) {
                Try lowerResult = model.AddConstraint(
                    Symbol.Polynomial.Ge(Constant(low)),
                    null,
                    name: $"{Symbol.Name}_lower");
                if (lowerResult is Failed<Success, ErrorCode, Error<ErrorCode>> lowerFailure) {
                    return Results.Failed<Success>(lowerFailure.Error);
                }
            }
            if (Upper is { } high) {
                Try upperResult = model.AddConstraint(
                    Symbol.Polynomial.Le(Constant(high)),
                    null,
                    name: $"{Symbol.Name}_upper");
                if (upperResult is Failed<Success, ErrorCode, Error<ErrorCode>> upperFailure) {
                    return Results.Failed<Success>(upperFailure.Error);
                }
            }
        }
        if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        IReadOnlyList<MathConstraint> constraints = model.Constraints;
        for (int index = start; index < constraints.Count; index++) {
            _constraints.Add(constraints[index]);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    /// <param name="value">常数值 / The constant.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}
