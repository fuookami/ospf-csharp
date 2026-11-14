// 相邻载荷间隙限制 / The bound on the load gap between adjacent positions.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
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
/// 相邻载荷间隙限制 / The bound on the load gap between adjacent positions.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `service/limits/AdjacentGapLimit.kt`：对每对相邻舱位 (p, p+1) 加**两行**——
///
/// | 行 | 关系 | 名称 |
/// | --- | --- | --- |
/// | `load[p] − load[p+1] ≤ 间隙` | `LE` | `{name}_pos_{p}` |
/// | `load[p+1] − load[p] ≤ 间隙` | `LE` | `{name}_neg_{p}` |
///
/// 四处容易做错：
///
/// 1. **两行方向相反**。这是 `|load[p] − load[p+1]| ≤ 间隙` 的两半；只加一行时只限制了单向，重心可以单侧堆到超出间隙。
/// 2. **`pos` 与 `neg` 的后缀**。两行同名时"第 2 条约束不可行"不知道是哪一侧。
/// 3. **相邻指舱位表的顺序**，不是按力臂排序。基线直接用 `positions[p]` / `positions[p+1]`——顺序由调用方给定，本类不改写它。
/// 4. **`positions.Count - 1` 次，不是 `Count` 次**。取到 `Count` 会越界。
///
/// Mirrors the baseline: two rows per adjacent pair, `load[p] − load[p+1] ≤ gap` (`_pos_`) and `load[p+1] − load[p] ≤ gap` (`_neg_`). Four easy mistakes: (1) **the two rows go
/// opposite ways** — together they are `|load[p] − load[p+1]| ≤ gap`, and one alone bounds only one direction, so weight can pile up on one side past the gap; (2) **the `pos` / `neg`
/// suffixes**, or "constraint 2 is infeasible" does not say which side; (3) **adjacent means the position list's order**, not an arm sort — the baseline indexes `positions[p]` and
/// `positions[p+1]` directly and leaves the order to the caller; (4) **`Count - 1` pairs**, not `Count`, or it overruns.
/// </remarks>
internal sealed class AdjacentGapLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly double _maxAdjacentLoadGap;
    private readonly string _name;

    /// <summary>
    /// 构造相邻载荷间隙限制 / Construct the adjacent gap bound.
    /// </summary>
    /// <param name="positions">舱位（顺序即相邻关系）/ The positions; their order defines adjacency.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="maxAdjacentLoadGap">允许的最大载荷间隙 / The maximum allowed load gap.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public AdjacentGapLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        double maxAdjacentLoadGap,
        string name = "adjacent_gap_limit") {
        _positions = positions;
        _load = load;
        _maxAdjacentLoadGap = maxAdjacentLoadGap;
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
        var added = new List<string>();
        for (int p = 0; p < _positions.Count - 1; ++p) {
            if (_load.EstimateLoadWeightSymbol(p) is not { } current) {
                return Missing(p);
            }
            if (_load.EstimateLoadWeightSymbol(p + 1) is not { } next) {
                return Missing(p + 1);
            }

            string positiveName = _name + "_pos_" + p;
            Try positive = Add(model, current.Polynomial, next.Polynomial, positiveName);
            if (!positive.IsOk) {
                return positive;
            }
            added.Add(positiveName);

            string negativeName = _name + "_neg_" + p;
            Try negative = Add(model, next.Polynomial, current.Polynomial, negativeName);
            if (!negative.IsOk) {
                return negative;
            }
            added.Add(negativeName);
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try Missing(int positionIndex) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "舱位缺少估计装载重量符号 / the position has no estimated load weight symbol: "
            + _positions[positionIndex].Name));

    private Try Add(
        LinearMetaModel<Flt64> model,
        LinearPolynomial<Flt64> left,
        LinearPolynomial<Flt64> right,
        string name) {
        var polynomial = new MutableLinearPolynomial<Flt64>();
        foreach (LinearMonomial<Flt64> monomial in left.Monomials) {
            polynomial.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }
        polynomial.AddConstant(left.Constant);
        foreach (LinearMonomial<Flt64> monomial in right.Monomials) {
            polynomial.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
        }
        polynomial.AddConstant(right.Constant.Negate());

        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
            new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(_maxAdjacentLoadGap)),
            Comparison.LE,
            name);
        return model.AddConstraint(row, group: null, name: name);
    }
}
