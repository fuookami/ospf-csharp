#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 离散条件归一化：把关系、范围、格点三件事装配成一份线性化结果 /
/// Normalize a discrete condition into one linearization record.
/// </summary>
/// <remarks>
/// B1174 按基线 `DiscreteCondition.kt` 的 `normalizeDiscreteCondition` 补，并补齐
/// `transformForRelationIndicator`。
///
/// **这一批只做装配**：**归一化范围、折叠判断、格点证明三块都已就位。**
/// **装配本身仍有两处容易错，而两处都不报错**：
///
/// **① 范围的变换必须与多项式变换用<b>同一个</b> `strictBoundary`。**
/// **多项式走 `p + δ`、范围却只平移 `0`，两者就会错开 `δ`——
/// 于是分支存在性判断在一个偏移过的区间上做，结论可能反。**
///
/// **② 折叠结果优先于边界重算。**
/// **`singleBranchFold` 非空时直接用；只有它为空才按变换后的范围重算。**
/// **反过来的话，折叠给出的"这个分支不可能"会被范围重算覆盖掉，
/// 于是一个本可以省掉的指示变量又被生成——不报错，只是白算。**
///
/// B1174, following the baseline's `normalizeDiscreteCondition`, plus `transformForRelationIndicator`.
///
/// **This batch is assembly only**: **range normalization , gap folding and lattice proofs
/// are all in place.**
/// **Two things in the assembly are still easy to get wrong, and neither throws**:
///
/// **① The range transform must use the <b>same</b> `strictBoundary` as the polynomial transform.**
/// **If the polynomial shifts by `δ` while the range does not, the two are off by `δ` — the branch-existence test
/// then runs on a shifted interval and can flip its conclusion.**
///
/// **② The fold takes precedence over recomputing from the bounds.**
/// **Use `singleBranchFold` when present; recompute only when it is absent.**
/// **The other way round, the fold's "this branch is impossible" gets overwritten by the recomputation, so an
/// indicator variable that could have been dropped is generated anyway — silently, only wasted work.**
/// </remarks>
public static class DiscreteConditionNormalization
{
    /// <summary>归一化一条离散条件 / Normalize one discrete condition.</summary>
    public static Result<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>> Normalize(
        LinearPolynomial<Flt64> polynomial,
        Comparison relation,
        ConditionBounds<Flt64> bounds,
        Flt64 delta,
        Flt64 strictBoundary) {
        ArgumentNullException.ThrowIfNull(polynomial);
        ArgumentNullException.ThrowIfNull(bounds);

        if (!BigMValues.IsUsable(delta) || delta.CompareTo(Flt64.Zero) <= 0) {
            return Fail("delta must be finite and positive");
        }

        if (!BigMValues.IsUsable(strictBoundary) || strictBoundary.CompareTo(Flt64.Zero) <= 0) {
            return Fail("strictBoundary must be finite and positive");
        }

        if (!ConditionPolynomials.IsUsable(polynomial)) {
            return Fail("the condition polynomial must have finite, encodable coefficients");
        }

        // **先算折叠**：**它基于<b>原始</b>关系，而不是变换后的关系。**
        // **基线的注释把这一点写明：变换后的多项式可能在 `delta > strictBoundary` 时
        // 把安全假分支移到正半轴，而固定指示变量时不需要再生成变换后的关系约束。**
        //
        // **The fold comes first**: **it is based on the <b>original</b> relation, not the transformed one.**
        // **The baseline's comment says so: with `delta > strictBoundary` the transformed polynomial can move a safe
        // false branch into the positive half-line, and a fixed indicator needs no transformed relation constraints.**
        TruthValue? singleBranchFold = DiscreteConditionGap.AvoidsGap(relation, bounds, strictBoundary);

        // **`delta > strictBoundary` 且折不了时，需要更强的证明**：
        // **`GT`/`LT` 要证严格边界不落在格点间隙里；`GE`/`LE` 要证平移安全。**
        // **`EQ`/`NE` 不需要——它们本来就不支持。**
        //
        // **When `delta > strictBoundary` and the fold failed, a stronger proof is required**:
        // **`GT`/`LT` must show the strict boundary clears the lattice gap; `GE`/`LE` must show the shift is safe.**
        // **`EQ`/`NE` need neither — they are unsupported anyway.**
        bool needsProof = delta.CompareTo(strictBoundary) > 0 && singleBranchFold is null;

        if (needsProof) {
            bool proven = relation switch {
                Comparison.GT or Comparison.LT => DiscreteConditionLattice.HasSafeStrictPositiveProof(
                    polynomial, relation, strictBoundary),
                Comparison.GE or Comparison.LE => DiscreteConditionLattice.HasSafeShiftProof(
                    polynomial, relation, delta),
                _ => false,
            };

            if (!proven) {
                return Fail(
                    $"relation {relation} with delta {delta} above the strict boundary {strictBoundary} " +
                    "has no discrete-lattice proof, so the normalization is not provably safe");
            }
        }

        // **关系变换：`GT` 不变、`GE` 平移、`LT` 取负、`LE` 取负后平移。**
        // **`EQ`/`NE` 直接拒绝，而不是挑一个方向。**
        //
        // **The relation transform: `GT` none, `GE` shift, `LT` negate, `LE` negate then shift.**
        // **`EQ`/`NE` are rejected outright rather than picking a side.**
        if (relation is Comparison.EQ or Comparison.NE) {
            return Fail($"relation {relation} is unsupported by discrete-condition normalization");
        }

        bool negate = relation is Comparison.LT or Comparison.LE;
        LinearPolynomial<Flt64> transformed = negate ? Negate(polynomial) : polynomial;
        if (relation is Comparison.GE or Comparison.LE) {
            transformed = Shift(transformed, strictBoundary);
        }

        if (!ConditionPolynomials.IsUsable(transformed)) {
            return Fail("the transformed polynomial must have finite, encodable coefficients");
        }

        // **范围变换必须与多项式变换用同一个 `strictBoundary`**：
        // **错开 `δ` 会让分支存在性判断在一个偏移过的区间上做，结论可能反。**
        //
        // **The range transform must use the same `strictBoundary` as the polynomial**:
        // **an offset of `δ` runs the branch-existence test on a shifted interval and can flip its conclusion.**
        ConditionBounds<Flt64> transformedBounds = relation switch {
            Comparison.GT => bounds,
            Comparison.GE => new ConditionBounds<Flt64>(
                bounds.Lower.Plus(strictBoundary), bounds.Upper.Plus(strictBoundary)),
            Comparison.LT => new ConditionBounds<Flt64>(
                bounds.Upper.Negate(), bounds.Lower.Negate()),
            _ => new ConditionBounds<Flt64>(
                bounds.Upper.Negate().Plus(strictBoundary),
                bounds.Lower.Negate().Plus(strictBoundary)),
        };

        if (!BigMValues.IsUsable(transformedBounds.Lower) || !BigMValues.IsUsable(transformedBounds.Upper)) {
            return Fail("the range transform produced a non-finite or unrepresentable endpoint");
        }

        // **折叠优先**：**它非空时直接用，不再按范围重算。**
        // **The fold takes precedence**: **use it when present; do not recompute from the bounds.**
        Flt64 zero = strictBoundary.Minus(strictBoundary);
        TruthValue? fixedValue = singleBranchFold ?? (
            transformedBounds.Lower.CompareTo(strictBoundary) >= 0
                ? TruthValue.True
                : transformedBounds.Upper.CompareTo(zero) <= 0
                    ? TruthValue.False
                    : null);

        bool trueBranchPossible;
        bool falseBranchPossible;

        if (fixedValue is { } decided) {
            trueBranchPossible = decided == TruthValue.True;
            falseBranchPossible = decided == TruthValue.False;
        } else {
            // **没有钉死时，范围还得过一遍"归一化后必须合法"的检查**：
            // **否则一个两个分支都不相交的范围会被当成"两个分支都可能"。**
            //
            // **Without a fixed value the range still has to pass the "normalized range must be legal" check**:
            // **otherwise a range meeting neither branch would be read as "both possible".**
            Result<NormalizedCondition, ErrorCode, Error<ErrorCode>> validated =
                ConditionNormalization.Normalize(Comparison.GT, transformedBounds, strictBoundary);
            if (validated is Failed<NormalizedCondition, ErrorCode, Error<ErrorCode>> failure) {
                return new Failed<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>(failure.Error);
            }

            trueBranchPossible = transformedBounds.Upper.CompareTo(strictBoundary) >= 0;
            falseBranchPossible = transformedBounds.Lower.CompareTo(zero) <= 0;
        }

        return new Ok<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>(
            new DiscreteConditionLinearization(
                Polynomial: transformed,
                Bounds: transformedBounds,
                FixedValue: fixedValue,
                TrueBranchPossible: trueBranchPossible,
                FalseBranchPossible: falseBranchPossible));
    }

    private static LinearPolynomial<Flt64> Negate(LinearPolynomial<Flt64> polynomial) {
        var monomials = new LinearMonomial<Flt64>[polynomial.Monomials.Count];
        for (int index = 0; index < polynomial.Monomials.Count; ++index) {
            monomials[index] = new LinearMonomial<Flt64>(
                polynomial.Monomials[index].Coefficient.Negate(), polynomial.Monomials[index].Symbol);
        }

        return new LinearPolynomial<Flt64>(monomials, polynomial.Constant.Negate());
    }

    private static LinearPolynomial<Flt64> Shift(LinearPolynomial<Flt64> polynomial, Flt64 delta) =>
        new(polynomial.Monomials, polynomial.Constant.Plus(delta));

    private static Result<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<DiscreteConditionLinearization, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
