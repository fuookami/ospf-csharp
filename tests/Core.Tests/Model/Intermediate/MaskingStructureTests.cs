#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 掩码结构："我用到了"与"这个列归我" / "I use it" and "it is mine" differ.
/// </summary>
/// <remarks>
/// 对齐基线 `MaskingStructure.kt` 的 `MaskingStructure`。
///
/// **mask 列可能共享，而共享决定了谁保留它。**
///
/// **The mask column may be shared, and sharing decides who retains it.**
/// </remarks>
public class MaskingStructureTests
{
    /// <summary>
    /// **只在定义 mask 时才保留它** / The mask is retained only when defined here.
    /// </summary>
    /// <remarks>
    /// **这是这里的核心判据。**
    ///
    /// **共享 mask 由别的结构定义，这里再登记一次会让同一个列被登记两遍。**
    /// **"登记两遍"在多数调用方那里表现为重复行，不是错误——求解照常。**
    ///
    /// **判据两侧都测：给了定义则保留两个列，没给则只保留结果列。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **A shared mask is defined elsewhere, and registering it here registers one column twice.**
    /// **"Twice" shows up as duplicate rows rather than an error — solving proceeds.**
    ///
    /// **Both sides are tested: with a definition two columns are retained, without it only the result.**
    /// </remarks>
    [Fact]
    public void TheMask_ShouldBeRetainedOnlyWhenDefinedHere()
    {
        MaskingStructure shared = Structure();

        shared.RetainedResultVariables.Should().HaveCount(1);
        shared.RetainedResultVariables[0].Should().BeSameAs(shared.ResultVariable);
        shared.MaskDefinition.Should().BeNull();

        MaskingStructure owned = Structure() with { MaskDefinition = Definition() };

        owned.RetainedResultVariables.Should().HaveCount(2);
        owned.RetainedResultVariables[0].Should().BeSameAs(owned.ResultVariable);
        owned.RetainedResultVariables[1].Should().BeSameAs(Mask);
    }

    /// <summary>**共享时只加条件值的行，不加定义行** / A shared mask adds no definition row.</summary>
    [Fact]
    public void TheSharedMask_ShouldAddNoDefinitionRow()
    {
        IReadOnlyList<LinearInequality<Flt64>> shared =
            Structure().GenerateConstraints().Value;

        shared.Should().HaveCount(4, "only the conditional value's four rows");
        foreach (LinearInequality<Flt64> row in shared) {
            row.Name.Should().NotContain("_mask_eq");
        }
    }

    /// <summary>
    /// **定义行是 `mask − definition = 0`，且排在条件值的行之前** /
    /// The definition row is `mask − definition = 0` and comes first.
    /// </summary>
    /// <remarks>
    /// **顺序本身不是语义（等式在哪都一样），但如果有人按下标切行、
    /// 或者按"前 N 行是本结构的定义"做假设，顺序就成了契约。**
    ///
    /// **`x = 0` 那一行代入 `mask = 0` 应恰好为 0；代入 `mask = 1` 应为 1——
    /// 这样就固定了"系数是 1、右侧是 0"这两件事。**
    ///
    /// **The order is not itself semantics, but it becomes a contract for anyone slicing by index or assuming "the first
    /// N rows are this structure's definition".**
    ///
    /// **Substituting `mask = 0` into the `x = 0` row must give exactly 0 and `mask = 1` must give 1 — that pins both "the
    /// coefficient is one" and "the right-hand side is zero".**
    /// </remarks>
    [Fact]
    public void TheDefinitionRow_ShouldComeFirstAndPinTheMask()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows =
            (Structure() with { MaskDefinition = Definition() }).GenerateConstraints().Value;

        rows.Should().HaveCount(5, "one definition row plus the conditional value's four");
        rows[0].Name.Should().Be("cv_mask_eq");
        rows[0].Comparison.Should().Be(Comparison.EQ);

        // `x = 0` 时残差为 0；`x = 1` 时残差为 1。
        // At x = 0 the residual is 0; at x = 1 it is 1.
        Residual(rows[0], mask: 0.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], mask: 1.0).Should().BeApproximately(
            1.0, 1e-9, "the mask coefficient is one, so the residual tracks the mask");
    }

    /// <summary>
    /// **定义不为零时，等式会把它抵消** / A non-zero definition shifts the equality to cancel it.
    /// </summary>
    /// <remarks>
    /// **`mask − (mask_eq + 3) = 0` 在 `mask_eq + 3` 处残差为 0。**
    /// **若实现漏掉常数项（只取 monomials），紧点会落在 `mask = 0` 而不是 `3`。**
    ///
    /// **With the definition `mask_eq + 3`, the residual is zero where the mask equals `mask_eq + 3`.**
    /// **An implementation that drops the constant would put the tight point at `mask = 0` instead of `3`.**
    /// </remarks>
    [Fact]
    public void TheDefinitionConstant_ShouldBeCarried()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows =
            (Structure() with { MaskDefinition = Definition(3.0) }).GenerateConstraints().Value;

        Residual(rows[0], mask: 3.0).Should().BeApproximately(
            0.0, 1e-9, "the constant three must be part of the difference");
        Residual(rows[0], mask: 0.0).Should().BeApproximately(
            -3.0, 1e-9);
    }

    /// <summary>**mask 多项式是 `1·mask`** / The mask polynomial is `1·mask`.</summary>
    [Fact]
    public void TheMaskPolynomial_ShouldBeUnitCoefficient()
    {
        LinearPolynomial<Flt64> polynomial = Structure().MaskPolynomial;

        polynomial.Monomials.Should().HaveCount(1);
        polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
        polynomial.Monomials[0].Symbol.Should().BeSameAs(Mask);
        polynomial.Constant.ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **输入清单要含 mask 多项式与定义** / The input list carries the mask polynomial and the definition.
    /// </summary>
    /// <remarks>
    /// **mask 的定义是一个输入多项式，必须报出来——否则它的变量范围没人收集，
    /// 自动 Big-M 会偏小，于是行会切掉合法解。**
    ///
    /// **The mask definition is an input polynomial and must be reported — otherwise its variable ranges go uncollected,
    /// the automatic Big-M comes out too small, and rows cut off legal solutions.**
    /// </remarks>
    [Fact]
    public void TheInputList_ShouldCarryTheMaskPolynomial()
    {
        MaskingStructure shared = Structure();
        shared.InputPolynomialsOrEmpty().Should().HaveCount(2, "the value input plus the mask polynomial");

        MaskingStructure owned = Structure() with { MaskDefinition = Definition() };
        owned.InputPolynomialsOrEmpty().Should().HaveCount(3, "and the definition as well");
    }

    /// <summary>**MASKING 已经接入分派表** / MASKING is wired into the dispatch table.</summary>
    [Fact]
    public void TheMaskingStructure_ShouldBeDispatched()
    {
        MaskingStructure structure = Structure();

        structure.ResultVariableOrNull().Should().BeSameAs(structure.ResultVariable);
        structure.InputOrNull().Should().BeSameAs(structure.Value.Input);
        structure.HelperVariablesOrEmpty().Should().BeEmpty(
            "the mask is a retained column, not a helper");
        structure.SupportsDeferredFallback().Should().BeTrue();
    }

    private static MaskingStructure Structure() =>
        new(Value: new ConditionalValue(
                Input: ValueInput, ResultVariable: ValueResult,
                Bounds: new ConditionBounds<Flt64>(new Flt64(-1.0), new Flt64(1.0)),
                Name: "cv"),
            Mask: Mask, Converter: IIntoValue<Flt64>.Identity);

    private static LinearPolynomial<Flt64> Definition(double constant = 0.0) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(constant));

    private static double Residual(LinearInequality<Flt64> row, double mask) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            if (monomial.Symbol.Equals(Mask)) {
                total += monomial.Coefficient.ToDouble() * mask;
            }
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            if (monomial.Symbol.Equals(Mask)) {
                total -= monomial.Coefficient.ToDouble() * mask;
            }
        }

        return total;
    }

    private static readonly LinearPolynomial<Flt64> ValueInput =
        new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

    private static readonly RealVar ValueResult = new("ms-value");

    private static readonly BinVar Mask = new("ms-mask");
}
