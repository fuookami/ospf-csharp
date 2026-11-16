// 最大 CLIM 上下界 / The upper and lower CLIM bounds.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 最大 CLIM 上下界 / The upper and lower CLIM bounds.
/// </summary>
/// <remarks>
/// 逐条对齐：两行，
///
/// | 行 | 关系 | 名称 |
/// | --- | --- | --- |
/// | `clim − maxCLIM ≤ 0` | `LE` | `{name}_ub` |
/// | `clim + maxCLIM ≥ 0` | `GE` | `{name}_lb` |
///
/// 三点容易做错：
///
/// 1. **右端是 0，不是 `maxCLIM`**。`maxCLIM` 挪到左端并改变符号，于是两行都是"与零比较"。写成 `clim ≤ maxCLIM` 与
///    `clim ≥ maxCLIM` 会让可行域缩成一个点——而且求解器会报"不可行"，看起来像数据问题。
/// 2. **第二行是 `clim + maxCLIM ≥ 0`，不是 `clim − maxCLIM ≥ 0`**。这是"CLIM 是偏离中心线的裕度，两侧对称"的写法：重心可以偏左
///    `maxCLIM` 也可以偏右 `maxCLIM`。
/// 3. **`maxCLIM` 的符号要先注册**。它是关于起飞总重的线性式，没注册时两行都加不了。
///
/// two rows, `clim − maxCLIM ≤ 0` (`_ub`) and `clim + maxCLIM ≥ 0` (`_lb`), both compared against zero. Three easy mistakes: (1) **the right side is 0, not
/// `maxCLIM`** — the code moves `maxCLIM` to the left and flips its sign; writing `clim ≤ maxCLIM` and `clim ≥ maxCLIM` collapses the feasible region to a point and the solver
/// reports "infeasible", which reads like a data problem; (2) **the second row is `clim + maxCLIM ≥ 0`, not `−`** — CLIM is a margin either side of the centreline, so the centre of
/// gravity may sit `maxCLIM` to the left or to the right; (3) **the `maxCLIM` symbol must be registered first**.
/// </remarks>
internal sealed class CLIMLimit
{
    private readonly Torque _torque;
    private readonly MaxCLIM _maxClim;
    private readonly string _name;

    /// <summary>
    /// 构造最大 CLIM 上下界 / Construct the CLIM bounds.
    /// </summary>
    /// <param name="torque">力矩模型 / The torque model.</param>
    /// <param name="maxClim">最大 CLIM / The maximum CLIM.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public CLIMLimit(Torque torque, MaxCLIM maxClim, string name = "max_clim_limit") {
        _torque = torque;
        _maxClim = maxClim;
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
        if (_torque.ClimSymbol is not { } clim) {
            // 窄体机不注册 CLIM，所以在窄体机上调用这条限制本身就是调用方的错误：没有 CLIM 可限。
            // Narrow bodies register no CLIM, so invoking this limit on one is a caller error: there is no CLIM to bound.
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "CLIM 符号未注册（窄体机没有 CLIM）/ the CLIM symbol is not registered (a narrow body has no CLIM)"));
        }
        if (_maxClim.MaxClimSymbol is not { } maximum) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "最大 CLIM 符号未注册 / the maximum CLIM symbol is not registered"));
        }

        string upperName = _name + "_ub";
        Try upper = Add(model, clim.Polynomial, maximum.Polynomial, Comparison.LE, upperName);
        if (!upper.IsOk) {
            return upper;
        }
        string lowerName = _name + "_lb";
        Try lower = Add(model, clim.Polynomial, maximum.Polynomial, Comparison.GE, lowerName);
        if (!lower.IsOk) {
            return lower;
        }
        LastAddedNames = new[] { upperName, lowerName };
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 加一行：`clim ∓ maxCLIM ≤ / ≥ 0` / Add one row: `clim ∓ maxCLIM ≤ / ≥ 0`.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="clim">CLIM 多项式 / The CLIM polynomial.</param>
    /// <param name="maximum">最大 CLIM 多项式 / The maximum CLIM polynomial.</param>
    /// <param name="comparison">比较符 / The comparison.</param>
    /// <param name="name">约束名 / The constraint name.</param>
    /// <returns>操作结果 / Operation result.</returns>
    private static Try Add(
        LinearMetaModel<Flt64> model,
        LinearPolynomial<Flt64> clim,
        LinearPolynomial<Flt64> maximum,
        Comparison comparison,
        string name) {
        // 上界用 `clim − maxCLIM`，下界用 `clim + maxCLIM`；两者都与零比较。符号由比较符决定，不能写成同一个表达式配两种比较符。
        // The upper bound is `clim − maxCLIM` and the lower is `clim + maxCLIM`, both against zero. The sign follows the comparison and cannot be one expression under both.
        bool subtract = comparison == Comparison.LE;
        var polynomial = new MutableLinearPolynomial<Flt64>();
        foreach (LinearMonomial<Flt64> monomial in clim.Monomials) {
            polynomial.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }
        polynomial.AddConstant(clim.Constant);
        foreach (LinearMonomial<Flt64> monomial in maximum.Monomials) {
            Flt64 coefficient = subtract ? monomial.Coefficient.Negate() : monomial.Coefficient;
            polynomial.AddMonomial(new LinearMonomial<Flt64>(coefficient, monomial.Symbol));
        }
        polynomial.AddConstant(subtract ? maximum.Constant.Negate() : maximum.Constant);

        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
            new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], Flt64.Zero),
            comparison,
            name);
        return model.AddConstraint(row, group: null, name: name);
    }
}
