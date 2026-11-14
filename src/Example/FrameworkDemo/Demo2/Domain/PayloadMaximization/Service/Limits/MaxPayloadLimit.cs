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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service.Limits;

/// <summary>
/// 最大业载目标 / The maximum-payload objective.
/// </summary>
/// <remarks>
/// 与基线 `payload_maximization/service/limits/MaxPayloadLimit.kt` 对齐：把 `payload.estimatePayload` **整体乘以一个系数**后作为**最大化**目标。
///
/// 四处必须照抄：
///
/// 1. **它是最大化目标**，不是约束。写成 `AddConstraint` 会变成"业载必须 ≤ 某值"，语义完全相反。
/// 2. **系数同时乘单项式和常数**。基线写的是 `monomials.map { c * it.coefficient }` **加** `constant = c * polynomial.constant`，
///    只乘单项式会漏掉常数项（估计业载可能有非零常数）。
/// 3. **系数由外部注入**（`parameter.weightRecommendationPayload`），不是常数 1。
/// 4. **`estimatePayload` 未注册时明确失败**。它是配载域 `Payload.Register` 建的符号，缺了说明注册顺序错了；
///    返回一个空目标会让模型"没有目标"，而求解器照样给出一个解。
///
/// Aligned with the baseline: multiply `payload.estimatePayload` **as a whole** by a coefficient and use it as a **maximisation** objective. Four things must be copied: **it is a
/// maximisation objective**, not a constraint (writing `AddConstraint` makes it "payload must be at most some value", the exact opposite); **the coefficient multiplies both the
/// monomials and the constant** — the baseline maps the monomial coefficients **and** sets `constant = c * polynomial.constant`, and multiplying only the monomials drops the
/// constant term (the estimated payload can carry a non-zero one); **the coefficient is injected** (`parameter.weightRecommendationPayload`), not a constant one; and **an
/// unregistered `estimatePayload` is an explicit failure**, since it is a symbol the stowage domain's `Payload.Register` builds and its absence means the registration order is
/// wrong — returning an empty objective would leave the model with no objective while the solver still returns a solution.
/// </remarks>
internal sealed class MaxPayloadLimit
{
    private readonly Payload _payload;
    private readonly Func<Flt64> _coefficient;

    /// <summary>
    /// 构造最大业载目标 / Construct the maximum-payload objective.
    /// </summary>
    /// <param name="payload">业载模型 / The payload model.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public MaxPayloadLimit(Payload payload, Func<Flt64>? coefficient = null, string name = "max_payload_limit")
    {
        ArgumentNullException.ThrowIfNull(payload);
        _payload = payload;
        _coefficient = coefficient ?? (() => Flt64.One);
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (_payload.EstimatePayload is not { } estimate)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "估计业载符号未注册 / the estimated-payload symbol is not registered"));
        }

        Flt64 coefficient = _coefficient();
        LinearPolynomial<Flt64> source = estimate.Polynomial;
        var monomials = new List<LinearMonomial<Flt64>>(source.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in source.Monomials)
        {
            monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
        }
        // **常数项也要乘**：只乘单项式会漏掉它。
        // **The constant is multiplied too**; scaling only the monomials would drop it.
        var objective = new LinearPolynomial<Flt64>(monomials, coefficient * source.Constant);
        return model.AddObject(ObjectCategory.Maximum, objective, Name, displayName: null);
    }
}
