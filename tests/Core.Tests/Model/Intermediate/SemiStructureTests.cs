#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 半连续：两条行只在成对时才强制 0 / Two rows force zero only as a pair.
/// </summary>
/// <remarks>
/// B1190 按基线 `FunctionExpansion.kt` 的 `SemiStructure` 与 `DeferredFunctionFallback.kt` 的
/// `DeferredFunctionFallbackConstraints` 补。
///
/// **指示 = 0 时，上界行只说"不超过 0"、下界行只说"不小于 0"——单独任何一行都只是半边。**
///
/// B1190 adds the baseline's `SemiStructure` and `DeferredFunctionFallbackConstraints`.
///
/// **At indicator 0 the upper row says only "≤ 0" and the lower only "≥ 0" — either alone is half.**
/// </remarks>
public class SemiStructureTests
{
    /// <summary>
    /// **指示 = 0 时两行一起把结果钉在 0 上** / As a pair the rows pin the result to zero.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **判据分三步断言，缺一步都证明不了"成对"：**
    ///
    /// 1. **`_semi_upper` 在 `result = 0` 处紧**（`0 ≤ 0`）
    /// 2. **`_semi_lower` 在 `result = 0` 处紧**（`0 ≥ 0`）
    /// 3. **`result = 1` 同时违反上界行、`result = −1` 同时违反下界行**
    ///
    /// **第 1、2 步只说"0 可行"；第 3 步才说"0 以外不可行"。**
    /// **只做前两步的话，一个"两行都恒成立"的实现也能通过。**
    ///
    /// **This is the batch's core criterion, and it needs all three steps:**
    ///
    /// 1. **`_semi_upper` is tight at `result = 0`**
    /// 2. **`_semi_lower` is tight at `result = 0`**
    /// 3. **`result = 1` violates the upper row and `result = −1` violates the lower**
    ///
    /// **Steps 1 and 2 only say "zero is feasible"; step 3 says "nothing else is".**
    /// **Without step 3 an implementation whose rows always hold would pass.**
    /// </remarks>
    [Fact]
    public void TheIndicatorZero_ShouldPinTheResultToZero()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        rows.Should().HaveCount(2);
        // **名字是 `Name + "_semi_upper"`，而 `Name` 的默认值是 `"semi"`——所以重复一次。**
        // **The name is `Name + "_semi_upper"` with `Name` defaulting to `"semi"`, hence the repeat.**
        rows[0].Name.Should().Be("semi_semi_upper");
        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[1].Name.Should().Be("semi_semi_lower");
        rows[1].Comparison.Should().Be(Comparison.GE);

        // **两步：0 可行。**
        // **Two steps: zero is feasible.**
        Residual(rows[0], result: 0.0, indicator: 0.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[1], result: 0.0, indicator: 0.0).Should().BeApproximately(0.0, 1e-9);

        // **第三步：0 以外不可行。**
        // **Step three: nothing but zero is feasible.**
        Residual(rows[0], result: 1.0, indicator: 0.0).Should().BeGreaterThan(
            0.0, "the upper row alone rules out positive values");
        Residual(rows[1], result: -1.0, indicator: 0.0).Should().BeLessThan(
            0.0, "the lower row alone rules out negative values");
    }

    /// <summary>
    /// **指示 = 1 时放开到 `[L, U]`** / At indicator one the rows relax to `[L, U]`.
    /// </summary>
    [Fact]
    public void TheIndicatorOne_ShouldRelaxToTheInterval()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows();

        // **`U = 10`、`L = 3`：两个端点都紧，两端之外都被排除。**
        // **`U = 10`, `L = 3`: both endpoints are tight and both outside values are excluded.**
        Residual(rows[0], result: 10.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[0], result: 11.0, indicator: 1.0).Should().BeGreaterThan(0.0);

        Residual(rows[1], result: 3.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);
        Residual(rows[1], result: 2.0, indicator: 1.0).Should().BeLessThan(0.0);
    }

    /// <summary>
    /// **两行在指示 = 0 与 = 1 之间<b>交换</b>了"哪一端在起作用"** /
    /// The two rows swap which end binds.
    /// </summary>
    /// <remarks>
    /// **指示 = 0 时两行都收到 0；指示 = 1 时上界行收到 `U`、下界行收到 `L`。**
    ///
    /// **如果 `L` 与 `U` 的系数写反（`_semi_upper` 用 `L`、`_semi_lower` 用 `U`），
    /// 指示 = 0 时仍然正确（两者都是 0），只有指示 = 1 时才看得出——
    /// 而那时模型会接受 `result ∈ [U, L]`，对一个 `L > U` 的情形它是空集，
    /// 也就是<b>不可行</b>；对一个 `L < U` 的情形它整体倒过来。**
    ///
    /// **At indicator 0 both rows move to zero; at indicator 1 the upper lands on `U` and the lower on `L`.**
    ///
    /// **Swapping the coefficients (upper using `L`, lower using `U`) still looks right at indicator 0 — both are zero —
    /// and only shows at indicator 1.**
    /// </remarks>
    [Fact]
    public void TheCoefficients_ShouldNotSwap()
    {
        SemiStructure swapped = new SemiStructure(
            LowerBound: new Flt64(10.0), UpperBound: new Flt64(3.0),
            ResultVariable: Result, IndicatorVariable: Indicator,
            Converter: IIntoValue<Flt64>.Identity, Name: "sw") with { };

        // **正确结构：`[3, 10]`。**
        // **The correct structure: `[3, 10]`.**
        Residual(Rows()[0], result: 10.0, indicator: 1.0).Should().BeApproximately(0.0, 1e-9);

        // **`U = 3`、`L = 10`（即系数互换后的效果）：两个端点的角色反了。**
        // **`U = 3` with `L = 10` (the swapped effect): the endpoints trade roles.**
        IReadOnlyList<LinearInequality<Flt64>> rows =
            swapped.MaterializeFallbackConstraints().Value.Constraints;

        Residual(rows[0], result: 3.0, indicator: 1.0).Should().BeApproximately(
            0.0, 1e-9, "the upper row follows U");
        Residual(rows[0], result: 10.0, indicator: 1.0).Should().BeGreaterThan(
            0.0, "ten is above this structure's U");
    }

    /// <summary>**辅助列只有指示列，不含结果列** / The helper list is the indicator alone.</summary>
    [Fact]
    public void TheHelpers_ShouldBeTheIndicatorOnly()
    {
        SemiStructure structure = Structure();

        structure.HelperVariables.Should().HaveCount(1);
        structure.HelperVariables[0].Should().BeSameAs(Indicator);
        structure.HelperVariables[0].Should().NotBeSameAs(structure.ResultVariable);
    }

    /// <summary>
    /// **fallback 约束带一个包装，而不是裸列表** / The fallback rows come wrapped, not bare.
    /// </summary>
    /// <remarks>
    /// **包装标记"这些行的来源是 fallback 路径"，调用方据此决定要不要做等价列与保留列的登记。**
    ///
    /// **The wrapper marks "these rows came from the fallback path", which the caller uses to decide about registering
    /// equivalents and retained columns.**
    /// </remarks>
    [Fact]
    public void TheFallback_ShouldReturnWrappedConstraints()
    {
        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
            Structure().MaterializeFallbackConstraints();

        result.Should().BeOfType<
            Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        result.Value.Constraints.Should().HaveCount(2);
    }

    /// <summary>**SEMI 已经接入分派表** / SEMI is wired into the dispatch table.</summary>
    [Fact]
    public void TheSemiStructure_ShouldBeDispatched()
    {
        SemiStructure structure = Structure();

        structure.ResultVariableOrNull().Should().BeSameAs(Result);
        structure.HelperVariablesOrEmpty().Should().HaveCount(1);
        structure.SupportsDeferredFallback().Should().BeTrue();

        // **半连续没有输入多项式：它的"输入"就是结果列自己。**
        // **A semi-continuous variable has no input polynomial: its "input" is the result column itself.**
        structure.InputOrNull().Should().BeNull();
        structure.InputPolynomialsOrEmpty().Should().BeEmpty();
    }

    private static SemiStructure Structure() =>
        new(LowerBound: new Flt64(3.0), UpperBound: new Flt64(10.0),
            ResultVariable: Result, IndicatorVariable: Indicator,
            Converter: IIntoValue<Flt64>.Identity, Name: "semi");

    private static IReadOnlyList<LinearInequality<Flt64>> Rows() =>
        Structure().MaterializeFallbackConstraints().Value.Constraints;

    private static double Residual(
        LinearInequality<Flt64> row, double result, double indicator) {
        double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            total += monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, result, indicator);
        }

        foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
            total -= monomial.Coefficient.ToDouble() * ValueOf(monomial.Symbol, result, indicator);
        }

        return total;
    }

    private static double ValueOf(ISymbol symbol, double result, double indicator) =>
        symbol.Equals(Indicator) ? indicator : symbol.Equals(Result) ? result : 0.0;

    private static readonly RealVar Result = new("semi-result");

    private static readonly BinVar Indicator = new("semi-ind");
}
