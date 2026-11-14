#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 值还原：五路查找的顺序就是语义 / The five-way lookup order is the semantics.
/// </summary>
/// <remarks>
/// B1210 按基线 `NativePiecewiseReport.kt` 的 `restoreValues` 与 `maxInputValues` 补。
///
/// B1210 adds the baseline's `restoreValues` and `maxInputValues`.
/// </remarks>
public class NativeValueRestorationTests
{
    /// <summary>
    /// **还原结果按原始 token 顺序排列** / The restored list follows the original token order.
    /// </summary>
    /// <remarks>
    /// **拼错的顺序不会报错，只会让每个变量的值贴到别的变量上。**
    /// **所以判据用一个<b>列顺序与 token 顺序相反</b>的算例：** token 是 `[A, B]`，
    /// 而列号是 `A → 1`、`B → 0`，值向量是 `[B 的值, A 的值]`。
    ///
    /// **正确实现给出 `[A 的值, B 的值]`；按列号顺序贴会给出反的。**
    ///
    /// **A wrong order raises nothing; it just attaches each value to another variable.**
    /// **The criterion uses a case where <b>column order is the reverse of token order</b>: tokens `[A, B]` with columns
    /// `A → 1`, `B → 0` and a value vector of `[B's, A's]`.**
    ///
    /// **The correct implementation yields `[A's, B's]`; attaching by column order yields the reverse.**
    /// </remarks>
    [Fact]
    public void TheResult_ShouldFollowTokenOrder() {
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = Restore(
            values: new[] { new Flt64(20.0), new Flt64(10.0) },
            columns: new Dictionary<VariableItemKey, int> { [A.Key] = 1, [B.Key] = 0 },
            tokens: new[] { TokenOf(A, 0), TokenOf(B, 1) });

        result.Value[0].ToDouble().Should().Be(10.0, "the first token is A");
        result.Value[1].ToDouble().Should().Be(20.0);
    }

    /// <summary>
    /// **`nativeValues` <b>静默胜过</b> ABS 辅助列** /
    /// <b>`nativeValues` silently wins over</b> ABS helpers.
    /// </summary>
    /// <remarks>
    /// **这是本批最有内容的一条，而它是一条<b>负面</b>的事实。**
    ///
    /// **B1209 发现 `absHelperValues` 不拒绝"解里含有辅助列"。**
    /// **本批的 `??` 链让 `nativeValues` 先查，所以一个解里带着陈旧的 `positive = 999`
    /// 会<b>胜过</b>重建出来的正确值 `5`。**
    ///
    /// **而选择列没有这个问题，因为 `selectorValues` 会先把"解里含选择列"拒掉。**
    ///
    /// **判据把两者并排：** **同一个陈旧的辅助值，作为 ABS 辅助列时胜出，
    /// 作为选择列时被拒绝——差别完全来自上一层的一道检查。**
    ///
    /// **所以"顺序即语义"这句话的分量在于：某一层的宽松会被下一层的顺序放大成错值。**
    ///
    /// **The batch's most substantive criterion, and it records a <b>negative</b> fact.**
    ///
    /// **B1209 found that `absHelperValues` does not reject a solution containing helpers.** **The `??` chain here looks at
    /// `nativeValues` first, so a solution carrying a stale `positive = 999` <b>beats</b> the correctly reconstructed
    /// `5`.**
    ///
    /// **Selectors do not have this problem because `selectorValues` rejects "the solution contains selectors"
    /// first.**
    ///
    /// **The criterion puts them side by side: the same stale helper value wins as an ABS helper and is rejected as a
    /// selector — the difference comes entirely from a check one layer up.**
    ///
    /// **That is the weight of "order is semantics": leniency in one layer gets amplified into a wrong value by the
    /// ordering in the next.**
    /// </remarks>
    [Fact]
    public void AStaleAbsHelper_ShouldWinOverTheReconstructedOne() {
        // **解里带着陈旧的 `positive = 999`，真实输入是 `5`。**
        // **The solution carries a stale `positive = 999` while the real input is `5`.**
        var columns = new Dictionary<VariableItemKey, int> {
            [Input.Key] = 0,
            [AbsResult.Key] = 1,
            [Positive.Key] = 2,
        };

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = Restore(
            values: new[] { new Flt64(5.0), new Flt64(5.0), new Flt64(999.0) },
            columns: columns,
            tokens: new[] { TokenOf(Positive, 2) },
            abs: new[] { Abs() });

        result.Value[0].ToDouble().Should().Be(
            999.0,
            "nativeValues is consulted first, so the stale value wins and the reconstructed five is dropped");

        // **而选择列在解里出现时，上一层直接拒掉——所以这个顺序问题在那一侧不存在。**
        // **A selector in the solution is rejected one layer up, so the ordering problem does not exist there.**
        var selectorColumns = new Dictionary<VariableItemKey, int> {
            [Input.Key] = 0,
            [Result.Key] = 1,
            [Selector1.Key] = 2,
        };

        Restore(
                values: new[] { new Flt64(7.0), new Flt64(5.0), new Flt64(1.0) },
                columns: selectorColumns,
                tokens: new[] { TokenOf(Selector1, 2) },
                piecewise: new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, Selector1, Selector2) })
            .Should().BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "selectors are rejected before the lookup order can matter");
    }

    /// <summary>
    /// **辅助列能被重建出来贴回 token** / Helper columns are reconstructed onto tokens.
    /// </summary>
    [Fact]
    public void HelperColumns_ShouldBeReconstructed() {
        var columns = new Dictionary<VariableItemKey, int> {
            [Input.Key] = 0,
            [Result.Key] = 1,
        };

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> pieced = Restore(
            values: new[] { new Flt64(7.0), new Flt64(5.0) },
            columns: columns,
            tokens: new[] { TokenOf(Selector1, 2), TokenOf(Selector2, 3) },
            piecewise: new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, Selector1, Selector2) });

        pieced.Value[0].ToDouble().Should().Be(0.0);
        pieced.Value[1].ToDouble().Should().Be(1.0, "seven lies in the second segment");

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> absed = Restore(
            values: new[] { new Flt64(-4.0), new Flt64(4.0) },
            columns: new Dictionary<VariableItemKey, int> {
                [Input.Key] = 0,
                [AbsResult.Key] = 1,
            },
            tokens: new[] { TokenOf(Positive, 2), TokenOf(Negative, 3), TokenOf(Sign, 4) },
            abs: new[] { Abs() });

        absed.Value[0].ToDouble().Should().Be(0.0);
        absed.Value[1].ToDouble().Should().Be(4.0);
        absed.Value[2].ToDouble().Should().Be(0.0, "a negative input signs as zero");
    }

    /// <summary>
    /// **MAX 选择列按最大值重建，并列时取第一个** /
    /// MAX selectors follow the largest value, taking the first on a tie.
    /// </summary>
    /// <remarks>
    /// **基线的 `inputs.indexOf(inputs.maxOrNull()!!)` 取的是<b>第一个</b>极值。**
    ///
    /// **并列时选哪一支不影响目标值（两支一样大），但它影响求解器看到的
    /// 选择变量值——而那是可复现性的一部分：同一个解必须重建出同一组选择列。**
    ///
    /// **The baseline's `indexOf(maxOrNull())` takes the <b>first</b> extreme.**
    ///
    /// **On a tie the choice does not change the objective, but it does change the selector values the solver sees — and
    /// that is part of reproducibility: one solution must reconstruct one selector set.**
    /// </remarks>
    [Fact]
    public void MaxSelectors_ShouldPickTheExtremeAndPreferTheFirstOnATie() {
        NativeMaxData max = MaxOf(
            terms: new[] {
                new Dictionary<VariableItemKey, double> { [A.Key] = 1.0 },
                new Dictionary<VariableItemKey, double> { [B.Key] = 1.0 },
            },
            selectors: new[] { MaxSel1, MaxSel2 });

        // **`A = 10`、`B = 3`：选第一支。**
        // **A = 10, B = 3: the first wins.**
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> clear = Restore(
            values: new[] { new Flt64(10.0), new Flt64(3.0) },
            columns: new Dictionary<VariableItemKey, int> { [A.Key] = 0, [B.Key] = 1 },
            tokens: new[] { TokenOf(MaxSel1, 2), TokenOf(MaxSel2, 3) },
            maxima: new[] { max });

        clear.Value[0].ToDouble().Should().Be(1.0);
        clear.Value[1].ToDouble().Should().Be(0.0);

        // **`A = 4`、`B = 4`：并列，仍取第一支。**
        // **A = 4, B = 4: tied, the first still wins.**
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> tied = Restore(
            values: new[] { new Flt64(4.0), new Flt64(4.0) },
            columns: new Dictionary<VariableItemKey, int> { [A.Key] = 0, [B.Key] = 1 },
            tokens: new[] { TokenOf(MaxSel1, 2), TokenOf(MaxSel2, 3) },
            maxima: new[] { max });

        tied.Value[0].ToDouble().Should().Be(1.0, "indexOf takes the first extreme");
        tied.Value[1].ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **半连续指示列按"结果是否够到下界"重建** / The semi indicator follows "does the result reach the lower bound".
    /// </summary>
    [Fact]
    public void SemiIndicators_ShouldFollowTheLowerBound() {
        SemiStructure semi = SemiOf(lower: 3.0);

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> at = Restore(
            values: new[] { new Flt64(3.0) },
            columns: new Dictionary<VariableItemKey, int> { [SemiResult.Key] = 0 },
            tokens: new[] { TokenOf(SemiIndicator, 1) },
            semi: new[] { semi });
        at.Value[0].ToDouble().Should().Be(
            1.0, "exactly the lower bound counts as reached, so the comparison is >=");

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> below = Restore(
            values: new[] { new Flt64(2.999) },
            columns: new Dictionary<VariableItemKey, int> { [SemiResult.Key] = 0 },
            tokens: new[] { TokenOf(SemiIndicator, 1) },
            semi: new[] { semi });
        below.Value[0].ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **找不到值的 token 被点名** / A token with no value is named.
    /// </summary>
    [Fact]
    public void AValueLessToken_ShouldBeNamed() {
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = Restore(
            values: new[] { new Flt64(1.0) },
            columns: new Dictionary<VariableItemKey, int> { [A.Key] = 0 },
            tokens: new[] { TokenOf(A, 0), TokenOf(B, 1) });

        result.Should().BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>();
        string message = ((Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message;
        message.Should().Contain("no native value");
        message.Should().Contain(
            NativeCombinedKeyValidator.KeyText(B.Key),
            "the message must say which token, otherwise the caller has to guess");
    }

    /// <summary>
    /// **`MaxInputValues` 缺项时被拒，非有限时也被拒** /
    /// `MaxInputValues` rejects missing terms and non-finite results.
    /// </summary>
    [Fact]
    public void MaxInputValues_ShouldRejectMissingAndNonFinite() {
        NativeMaxData max = MaxOf(
            terms: new[] { new Dictionary<VariableItemKey, double> { [A.Key] = 1.0 } },
            selectors: new[] { MaxSel1 });

        ((Failed<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>)
            NativeValueRestoration.MaxInputValues(
                new Dictionary<VariableItemKey, Flt64>(), max))
            .Error.Message.Should().Contain("token is missing");

        ((Failed<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>)
            NativeValueRestoration.MaxInputValues(
                new Dictionary<VariableItemKey, Flt64> { [A.Key] = new Flt64(double.NaN) },
                max))
            .Error.Message.Should().Contain("must be finite");
    }

    /// <summary>
    /// **空 token 列表给出空结果** / An empty token list gives an empty result.
    /// </summary>
    [Fact]
    public void EmptyTokens_ShouldGiveEmptyResult() {
        Restore(
                values: Array.Empty<Flt64>(),
                columns: new Dictionary<VariableItemKey, int>(),
                tokens: Array.Empty<Token<Flt64>>())
            .Value.Should().BeEmpty();
    }

    private static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Restore(
        IReadOnlyList<Flt64> values,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        IReadOnlyList<Token<Flt64>> tokens,
        IReadOnlyList<NormalizedPiecewise>? piecewise = null,
        IReadOnlyList<NormalizedAbs>? abs = null,
        IReadOnlyList<NativeMaxData>? maxima = null,
        IReadOnlyList<SemiStructure>? semi = null) =>
        NativeValueRestoration.Restore(
            values, columns, tokens,
            piecewise ?? Array.Empty<NormalizedPiecewise>(),
            abs ?? Array.Empty<NormalizedAbs>(),
            maxima ?? Array.Empty<NativeMaxData>(),
            semi ?? Array.Empty<SemiStructure>());

    private static NativeMaxData MaxOf(
        Dictionary<VariableItemKey, double>[] terms, IVariableItem[] selectors) {
        var inputs = new List<NativeMaxInputData>(terms.Length);
        foreach (Dictionary<VariableItemKey, double> one in terms) {
            inputs.Add(new NativeMaxInputData(one, 0.0, 0.0, 1.0));
        }

        var keys = new VariableItemKey[selectors.Length];
        for (int index = 0; index < selectors.Length; ++index) {
            keys[index] = selectors[index].Key;
        }

        double[] bigMs = new double[selectors.Length];
        Array.Fill(bigMs, 100.0);

        return new NativeMaxData(
            Inputs: inputs, ResultKey: MaxResult.Key, SelectorKeys: keys,
            BigMValues: bigMs, Name: "max");
    }

    private static SemiStructure SemiOf(double lower) =>
        new(LowerBound: new Flt64(lower), UpperBound: new Flt64(10.0),
            ResultVariable: SemiResult, IndicatorVariable: SemiIndicator,
            Converter: IIntoValue<Flt64>.Identity, Name: "semi");

    private static NormalizedPiecewise Piecewise(
        double[] breakpoints, params IVariableItem[] selectors) {
        var keys = new VariableItemKey[selectors.Length];
        for (int index = 0; index < selectors.Length; ++index) {
            keys[index] = selectors[index].Key;
        }

        var points = new Flt64[breakpoints.Length];
        for (int index = 0; index < breakpoints.Length; ++index) {
            points[index] = new Flt64(breakpoints[index]);
        }

        return new NormalizedPiecewise(
            InputKey: Input.Key, ResultKey: Result.Key, SelectorKeys: keys,
            Breakpoints: points, Values: points);
    }

    private static NormalizedAbs Abs() =>
        new(InputKey: Input.Key, ResultKey: AbsResult.Key, PositiveKey: Positive.Key,
            NegativeKey: Negative.Key, SignKey: Sign.Key);

    private static Token<Flt64> TokenOf(IVariableItem variable, int solverIndex) =>
        new(variable, solverIndex, new Dictionary<object, Action<bool>>());

    private static readonly RealVar A = new("vr-a");

    private static readonly RealVar B = new("vr-b");

    private static readonly RealVar Input = new("vr-in");

    private static readonly RealVar Result = new("vr-r");

    private static readonly RealVar AbsResult = new("vr-absr");

    private static readonly RealVar Positive = new("vr-p");

    private static readonly RealVar Negative = new("vr-n");

    private static readonly BinVar Sign = new("vr-s");

    private static readonly BinVar Selector1 = new("vr-sel1");

    private static readonly BinVar Selector2 = new("vr-sel2");

    private static readonly RealVar MaxResult = new("vr-maxr");

    private static readonly BinVar MaxSel1 = new("vr-msel1");

    private static readonly BinVar MaxSel2 = new("vr-msel2");

    private static readonly RealVar SemiResult = new("vr-semir");

    private static readonly BinVar SemiIndicator = new("vr-semiind");
}
