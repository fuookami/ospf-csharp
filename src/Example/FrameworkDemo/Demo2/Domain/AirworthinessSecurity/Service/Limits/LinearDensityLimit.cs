// 线性密度上限：按切面约束 / The upper bound on linear density, per cross-section.

using System;
using System.Collections.Generic;
using System.Globalization;
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
/// 线性密度上限 / The upper bound on linear density, per cross-section.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `service/limits/LinearDensityLimit.kt`：对**每条限制线**加一行
/// `Σ 该线上舱位的线性密度 ≤ 该线所属区域的上限`，名为 `{name}_{区域名}_{力臂}`。
///
/// 与表面密度上限的区别不只是"面积换成长度"，还有**求和的范围**：表面密度是逐舱位各一行，线性密度是**一条横截面一行**，
/// 而一条线通常覆盖多个舱位。所以这里的左端是多舱位之和——把某个舱位漏掉，那条横截面就少算一截重量，而约束看起来依然成立。
///
/// 三处细节：
///
/// 1. **整条线全部不可用才跳过**。基线判 `line.positions.none { it.status.available }`，是**整条线**的守卫，不是逐舱位过滤。
///    一条线上只要还有一个可用舱位，这条约束就照加，不可用舱位的密度仍被算进和里。这与表面密度上限**逐舱位**跳不可用不同，
///    是基线的选择，照抄（下方 `ponytail:` 记下了它的后果）。
/// 2. **下标从限制线直接取**。基线用 `positions.indexOf(position)` 按**值相等**回查下标；两个完全相同的舱位会查错。
///    C# 的 `LinearDensityLimitLine.PositionIndices` 在切面时就把下标带上了，这一整类错误不存在。
/// 3. **名字带力臂数值**。同名约束在求解器里分不清是哪一条横截面，`linear_density_limit_Z1_100` 能直接读出位置。
///
/// Mirrors the baseline: one row per limit line, `Σ linear density of the line's positions ≤ the zone's limit`, named `{name}_{zone}_{arm}`. The difference from the surface density
/// cap is not just area-versus-length but **what is summed**: surface density is one row per position, linear density is **one row per cross-section**, and a line usually covers
/// several positions — dropping one understates the weight on that section while the constraint still looks satisfied. Three details: (1) **skip only when the whole line is
/// unavailable** (`none { available }` is a whole-line guard, not a per-position filter), so an unavailable position's density still joins the sum when a neighbour on the same
/// line is available — different from the surface cap, and copied as-is with its consequence recorded below; (2) **the indices come straight from the line** — the baseline
/// back-looks them up by **value equality**, which picks the wrong one for two identical positions, a whole class of error C# avoids by carrying indices from the cut;
/// (3) **the name carries the arm value**, otherwise every row is indistinguishable in the solver log.
/// </remarks>
internal sealed class LinearDensityLimit
{
    private readonly LinearDensity _linearDensity;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly string _name;

    /// <summary>
    /// 构造线性密度上限 / Construct the upper bound on linear density.
    /// </summary>
    /// <param name="linearDensity">线性密度模型 / The linear density model.</param>
    /// <param name="positions">舱位（含可用性）/ The positions, carrying availability.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public LinearDensityLimit(
        LinearDensity linearDensity,
        IReadOnlyList<StowagePosition> positions,
        string name = "linear_density_limit") {
        _linearDensity = linearDensity;
        _positions = positions;
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
        foreach (LinearDensityLimitLine line in _linearDensity.LimitLines) {
            bool anyAvailable = false;
            foreach (int j in line.PositionIndices) {
                if (_positions[j].Status.Available) {
                    anyAvailable = true;
                    break;
                }
            }
            if (!anyAvailable) {
                continue;
            }

            // ponytail: 整条线守卫的后果——线与线共享舱位时，某个不可用舱位的线性密度仍会被算进这条线的和里。基线就是这么写的，
            // ponytail: 本批照抄。若将来"不可用舱位不该贡献重量"成为硬要求，就把这里改成逐舱位过滤，并在判据里加"不可用舱位不贡献"一条。
            // ponytail: Consequence of the whole-line guard: when lines share positions, an unavailable position's linear density still enters this line's sum. That is what the
            // ponytail: baseline does and it is copied here. If "an unavailable position must contribute no weight" ever becomes a hard requirement, filter per position here and add
            // ponytail: a criterion for it.
            var polynomial = new MutableLinearPolynomial<Flt64>();
            foreach (int j in line.PositionIndices) {
                if (_linearDensity.LinearDensitySymbol(j) is not { } density) {
                    // 线上有舱位却没有线性密度符号，说明 `LinearDensity.Register` 没跑或跑失败。静默跳过会让那条横截面少算一截重量，
                    // 而约束照样"成立"——那是最坏的失败方式。
                    // A position on the line without a linear density symbol means `LinearDensity.Register` never ran or failed. Skipping silently understates that cross-section's
                    // weight while the constraint still "holds" — the worst failure mode.
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "限制线上的舱位缺少线性密度符号 / a position on a limit line has no linear density symbol: "
                        + _positions[j].Name));
                }
                foreach (LinearMonomial<Flt64> monomial in density.Polynomial.Monomials) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
                }
                polynomial.AddConstant(density.Polynomial.Constant);
            }

            string constraintName = _name + "_" + line.Zone.Name + "_" + line.Arm.ToString(CultureInfo.InvariantCulture);
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
                new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(line.Zone.MaxLinearDensity)),
                Comparison.LE,
                constraintName);
            Try result = model.AddConstraint(row, group: null, name: constraintName);
            if (!result.IsOk) {
                return result;
            }
            added.Add(constraintName);
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
