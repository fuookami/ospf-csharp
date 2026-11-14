// 业载上限与下限 / The payload upper and lower bounds.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 业载上下限 / The payload upper and lower bounds.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `service/limits/PayloadLimit.kt`：两行，`estimate_payload ≤ maxPayload`（名字 `{name}`）与
/// `estimate_payload ≥ minPayload`（名字 `{name}_minimum`）。
///
/// 两点容易做错：
///
/// 1. **上限那行不带后缀**，下限才带 `_minimum`。看起来应当对称，但基线就是这样；改名会让既有的报告与日志对不上。
/// 2. **`minPayload` 是构造参数，不是 `Payload` 的字段**。`Payload` 只有 `PlannedPayload` / `MaxPayload` / `ComputedPayload`，
///    下限来自运行期参数（调度给的最低业载）。混用会把"计划业载"当成"最低业载"。
///
/// Mirrors the baseline: two rows, `estimate_payload ≤ maxPayload` (named `{name}`) and `estimate_payload ≥ minPayload` (named `{name}_minimum`). Two easy mistakes:
/// (1) **only the lower row carries a suffix** — it looks like it should be symmetric, but this is what the baseline does and renaming breaks existing reports;
/// (2) **`minPayload` is a constructor argument, not a `Payload` field** — `Payload` holds `PlannedPayload` / `MaxPayload` / `ComputedPayload` only, and the minimum comes from
/// a runtime parameter; conflating them turns "planned payload" into "minimum payload".
/// </remarks>
internal sealed class PayloadLimit
{
    private readonly Payload _payload;
    private readonly double _minPayload;
    private readonly string _name;

    /// <summary>
    /// 构造业载上下限 / Construct the payload bounds.
    /// </summary>
    /// <param name="payload">业载模型 / The payload model.</param>
    /// <param name="minPayload">最低业载 / The minimum payload.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public PayloadLimit(Payload payload, double minPayload, string name = "payload_limit") {
        _payload = payload;
        _minPayload = minPayload;
        _name = name;
    }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_payload.EstimatePayload is not { } estimate) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "业载估计符号未注册 / the estimate payload symbol is not registered"));
        }

        Try upper = Add(model, estimate, _payload.MaxPayload, Comparison.LE, _name);
        if (!upper.IsOk) {
            return upper;
        }
        string minimumName = _name + "_minimum";
        Try lower = Add(model, estimate, _minPayload, Comparison.GE, minimumName);
        if (!lower.IsOk) {
            return lower;
        }
        LastAddedNames = new[] { _name, minimumName };
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static Try Add(
        LinearMetaModel<Flt64> model,
        LinearExpressionSymbol estimate,
        double bound,
        Comparison comparison,
        string name) {
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(estimate.Polynomial.Monomials, estimate.Polynomial.Constant),
            new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(bound)),
            comparison,
            name);
        return model.AddConstraint(row, group: null, name: name);
    }
}
