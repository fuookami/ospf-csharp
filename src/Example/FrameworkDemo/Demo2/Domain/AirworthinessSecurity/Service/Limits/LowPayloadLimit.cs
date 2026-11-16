// 低甲板载荷下界约束 / The lower bound on the low-deck payload.

using System;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
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
/// 低甲板载荷下界 / The lower bound on the low-deck payload.
/// </summary>
/// <remarks>
/// 逐条对齐：一行 `lower_estimate_payload ≥ min_low_payload`。
///
/// 这是**下界**，不是上界。下舱装得太少会让重心太靠前，所以"至少要装多少"是安全要求。写成上界能编译、能求解，
/// 而且大多数算例里恰好在最优解上都不起作用——那正是这种错最危险的地方：它不报错，只是把一条安全约束变成了一条容量约束。
///
/// one row `lower_estimate_payload ≥ min_low_payload`. It is a **lower** bound: too little in the lower hold puts the centre of gravity too far forward,
/// so "at least this much" is a safety requirement. Writing it as an upper bound compiles, solves, and in most instances happens not to bind at the optimum — exactly what
/// makes the mistake dangerous: nothing reports it, a safety constraint just became a capacity one.
/// </remarks>
internal sealed class LowPayloadLimit
{
    private readonly Payload _payload;
    private readonly MinLowPayload _minLowPayload;
    private readonly string _name;

    /// <summary>
    /// 构造低甲板载荷下界 / Construct the lower bound on the low-deck payload.
    /// </summary>
    /// <param name="payload">业载模型 / The payload model.</param>
    /// <param name="minLowPayload">最小低甲板载荷 / The minimum low-deck payload.</param>
    /// <param name="name">约束名 / The constraint name.</param>
    public LowPayloadLimit(
        Payload payload,
        MinLowPayload minLowPayload,
        string name = "low_payload_limit") {
        _payload = payload;
        _minLowPayload = minLowPayload;
        _name = name;
    }

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_payload.LowEstimatePayload is not { } lowPayload) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "下舱推荐业载符号未注册 / the lower-hold recommended payload symbol is not registered"));
        }
        if (_minLowPayload.MinLowPayloadSymbol is not { } minimum) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "最小低甲板载荷符号未注册 / the minimum low-deck payload symbol is not registered"));
        }

        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(lowPayload.Polynomial.Monomials, lowPayload.Polynomial.Constant),
            new LinearPolynomial<Flt64>(minimum.Polynomial.Monomials, minimum.Polynomial.Constant),
            Comparison.GE,
            _name);
        return model.AddConstraint(row, group: null, name: _name);
    }
}
