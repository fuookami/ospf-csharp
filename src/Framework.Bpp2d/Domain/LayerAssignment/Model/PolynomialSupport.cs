// 多项式构造辅助 / Polynomial construction helpers.

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 多项式构造辅助 / Polynomial construction helpers.
/// </summary>
/// <remarks>
/// core 的 `Sum` 在空单项式列表上会抛异常，而列生成在尚未加列时"负载为空"是**正常状态**。
/// 这个差异不是风格问题：把正常状态表示成错误，会迫使每个调用方去处理一个并不存在的失败，
/// 而真正的失败（量纲错、模型不可行）反而淹没在其中。
///
/// The core `Sum` throws on an empty monomial list, while an empty load before any column is added is a **normal state** in column generation. The distinction is not
/// stylistic: representing a normal state as an error forces every caller to handle a failure that does not exist, and genuine failures (dimension errors, infeasible
/// models) get drowned out among them.
/// </remarks>
public static class PolynomialSupport {
    /// <summary>
    /// 由单项式列表构造线性多项式，空列表返回零多项式 / Build a linear polynomial; an empty list yields the zero polynomial.
    /// </summary>
    /// <param name="monomials">单项式列表 / The monomial list.</param>
    /// <returns>线性多项式 / The linear polynomial.</returns>
    /// <remarks>
    /// **空列表给零多项式，不给 `Failed`**：`SumSafe` 已经用 `Failed(DataEmpty)` 表达"没法推断零值"，
    /// 而这里能推断——`FltX` 有确定的零。两者不是重复 API，是两种不同的问题：
    /// "泛型下无法构造零" vs "具体到 `FltX` 时零就是零"。
    ///
    /// **An empty list gives the zero polynomial, not `Failed`**: `SumSafe` already expresses "cannot infer the zero value" as `Failed(DataEmpty)`, whereas here the zero
    /// **can** be inferred because `FltX` has a definite zero. The two are not duplicate APIs but answers to different questions: "a generic type cannot build its zero"
    /// versus "specialised to `FltX`, zero is simply zero".
    /// </remarks>
    public static LinearPolynomial<FltX> SafeLinearPolynomial(
        IReadOnlyList<LinearMonomial<FltX>> monomials) {
        System.ArgumentNullException.ThrowIfNull(monomials);
        return monomials.Count == 0
            ? new LinearPolynomial<FltX>(System.Array.Empty<LinearMonomial<FltX>>(), FltX.Zero)
            : PolynomialDsl.Sum(monomials);
    }

    /// <summary>
    /// 过滤出非零系数的单项式 / Filter out monomials whose coefficient is zero.
    /// </summary>
    /// <param name="monomials">单项式列表 / The monomial list.</param>
    /// <returns>非零系数单项式列表 / The monomial list with non-zero coefficients.</returns>
    /// <remarks>
    /// **零系数单项式会让"空负载"判断失效**：一个只含零系数单项式的列表在 `Count` 上非空，但它代表的负载是零。
    /// 于是"还没加列"与"加了列但没装东西"这两种状态在判断处会走同一条分支——而它们本该走不同的分支。
    /// 过滤掉之后，两种状态都正确归入"空"。
    ///
    /// **A zero-coefficient monomial defeats the empty-load check**: a list holding only zero-coefficient monomials is non-empty by `Count` while representing zero load. So
    /// "no column added yet" and "a column added but nothing loaded" take the same branch — when they should take different ones. After filtering, both correctly read as
    /// empty.
    /// </remarks>
    public static IReadOnlyList<LinearMonomial<FltX>> SafeLinearMonomials(
        IReadOnlyList<LinearMonomial<FltX>> monomials) {
        System.ArgumentNullException.ThrowIfNull(monomials);

        var kept = new List<LinearMonomial<FltX>>(monomials.Count);
        foreach (LinearMonomial<FltX> monomial in monomials) {
            if (monomial.Coefficient != FltX.Zero) {
                kept.Add(monomial);
            }
        }
        return kept;
    }
}
