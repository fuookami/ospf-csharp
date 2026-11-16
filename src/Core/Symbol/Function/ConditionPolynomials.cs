#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 条件多项式与条件行的可用性检查 / Usability checks for condition polynomials and rows.
/// </summary>
/// <remarks>
/// 对齐里的 `hasUsableConditionSolverValues` /
/// `hasUsableConditionFlattenedValues` / `isUsableConditionBound` 三个私有助手补。
///
/// **为什么这三个检查值得单独抽出来**：**它们挡的是同一类问题——
/// 一个"看起来是数、其实是 `NaN` 或 `inf`"的系数。**
/// **这种系数不会让构造失败，只会让约束<b>永远成立或永远不成立</b>，而模型仍然可行。**
///
/// **`NaN` 尤其危险**：**`NaN ≥ 0` 与 `NaN ≤ 0` 都是 `false`，
/// 于是"这个界有效"的判据静默通过，而求解器拿到一条无意义的行。**
///
/// **Why these checks deserve their own home**: **they all stop one class of problem — a coefficient that "looks
/// like a number but is `NaN` or `inf`".** **Such a coefficient does not fail construction; it makes the constraint
/// <b>always true or always false</b>, while the model stays feasible.**
///
/// **`NaN` is the dangerous one**: **`NaN ≥ 0` and `NaN ≤ 0` are both `false`, so an "is this bound usable"
/// criterion passes silently while the solver receives a meaningless row.**
/// </remarks>
internal static class ConditionPolynomials
{
    /// <summary>标量是否可用 / Whether a scalar is usable.</summary>
    public static bool IsUsable(Flt64 value) => BigMValues.IsUsable(value);

    /// <summary>多项式的常数与每个系数是否都可用 / Whether the constant and every coefficient are usable.</summary>
    public static bool IsUsable(LinearPolynomial<Flt64> polynomial) {
        if (!BigMValues.IsUsable(polynomial.Constant)) {
            return false;
        }

        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (!BigMValues.IsUsable(monomial.Coefficient) || monomial.Symbol is null) {
                return false;
            }
        }

        return true;
    }
}
