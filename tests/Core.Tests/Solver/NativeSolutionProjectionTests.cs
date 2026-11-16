#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 解向量投影：检查是一道闸门，不是一次转换 / The check is a gate, not a transform.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `valuesByKey`。
///
/// adds
/// </remarks>
public class NativeSolutionProjectionTests
{
    /// <summary>
    /// **值按键摊回，且列号可以任意排列** / Values land on keys, with arbitrary column numbering.
    /// </summary>
    [Fact]
    public void ValuesShouldLandOnKeys_RegardlessOfColumnOrder() {
        var columns = new Dictionary<VariableItemKey, int> {
            [A.Key] = 2,
            [B.Key] = 0,
            [C.Key] = 1,
        };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeSolutionProjection.ValuesByKey(
                new[] { new Flt64(10.0), new Flt64(20.0), new Flt64(30.0) }, columns);

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        result.Value[A.Key].ToDouble().Should().Be(30.0);
        result.Value[B.Key].ToDouble().Should().Be(10.0);
        result.Value[C.Key].ToDouble().Should().Be(20.0);
    }

    /// <summary>
    /// **非有限值被拒** / A non-finite value is rejected.
    /// </summary>
    [Fact]
    public void ANonFiniteValue_ShouldBeRejected() {
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0 };

        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)
            NativeSolutionProjection.ValuesByKey(
                new[] { new Flt64(double.PositiveInfinity) }, columns))
            .Error.Message.Should().Contain("must be finite");

        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)
            NativeSolutionProjection.ValuesByKey(
                new[] { new Flt64(double.NaN) }, columns))
            .Error.Message.Should().Contain("must be finite");
    }

    /// <summary>
    /// **存下来的是<b>原始</b>的 `Flt64`，不是四舍五入后的 `double`** /
    /// What is stored is the <b>original</b> `Flt64`, not the rounded double.
    /// </summary>
    /// <remarks>
    /// **这是本批最有内容的一条。**
    ///
    /// **`finiteSolverDouble` 的返回值被<b>丢掉</b>，存的是原来那个 `Flt64`。**
    ///
    /// **在 `SolveValueConversionPolicy.AllowRounding` 下，转换会四舍五入到 15 位，
    /// 而存下来的没有。**
    ///
    /// **判据用一个四舍五入会变形的值（`1/3` 的十进制展开），
    /// 在 `AllowRounding` 策略下跑一遍，断言拿到的是<b>原来那个数</b>。**
    ///
    /// **这不是在测"实现细节"：** **"报告出来的值就是求解器给的值"是一个不变量。
    /// 如果实现改成存转换后的 `double`，这个不变量就没了，而这个判据会变红。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **`finiteSolverDouble`'s return value is <b>discarded</b>; the original `Flt64` is stored.**
    ///
    /// **Under `SolveValueConversionPolicy.AllowRounding` the conversion rounds to 15 digits while what is stored is not
    /// rounded.**
    ///
    /// **The criterion uses a value that rounding would deform (the decimal expansion of one third), runs under
    /// `AllowRounding`, and asserts the <b>original</b> number comes back.**
    ///
    /// **This is not testing an implementation detail: "the reported value is the solver's value" is an invariant. An
    /// implementation that stored the converted double would lose it, and this criterion would fail.**
    /// </remarks>
    [Fact]
    public void TheStoredValue_ShouldBeTheOriginalNotTheRoundedOne() {
        using (SolveValueConversionContext.SetPolicy(
            SolveValueConversionPolicy.AllowRounding)) {
            var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0 };

            // **`0.1 + 0.2` 的双精度表示不是 `0.3`；四舍五入到 15 位会把它们的差异抹掉。**
            // **The double for `0.1 + 0.2` is not `0.3`; rounding to 15 digits erases the difference.**
            double raw = 0.1 + 0.2;
            double rounded = global::System.Math.Round(raw, 15);

            rounded.Should().NotBe(
                raw, "otherwise this criterion cannot tell rounding apart from not rounding");

            Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
                result = NativeSolutionProjection.ValuesByKey(
                    new[] { new Flt64(raw) }, columns);

            result.Value[A.Key].ToDouble().Should().Be(
                raw, "the stored value is the solver's own number, passed through untouched");
        }
    }

    /// <summary>
    /// **长度不匹配被拒** / A length mismatch is rejected.
    /// </summary>
    /// <remarks>
    /// **长度检查排在列号检查之前**：**两个键配三个值，先报"长度不匹配"；
    /// 只有长度对上了，才可能去问"哪一列缺了"。**
    ///
    /// **The length check precedes the column check: two keys with three values reports "length mismatch" first; only
    /// once lengths agree can "which column is missing" be asked.**
    /// </remarks>
    [Fact]
    public void ALengthMismatch_ShouldBeRejectedFirst() {
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [B.Key] = 1 };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeSolutionProjection.ValuesByKey(
                new[] { new Flt64(1.0), new Flt64(2.0), new Flt64(3.0) }, columns);

        result.Should().BeOfType<
            Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("length does not match");
    }

    /// <summary>
    /// **列号越界被拒，且消息给出列号** / An out-of-range column is rejected and named.
    /// </summary>
    /// <remarks>
    /// **长度检查通过之后，列号仍可能越界：** **两个键、两个值，而列号是 `0` 与 `7`。**
    ///
    /// **所以"长度对"不等于"列号都在范围内"——两道检查不能互相替代。**
    ///
    /// **Once lengths agree a column can still be out of range: two keys, two values, columns `0` and `7`.** **So "lengths
    /// agree" does not imply "columns are in range" — one check cannot replace the other.**
    /// </remarks>
    [Fact]
    public void AnOutOfRangeColumn_ShouldBeRejectedAndNamed() {
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [B.Key] = 7 };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeSolutionProjection.ValuesByKey(
                new[] { new Flt64(1.0), new Flt64(2.0) }, columns);

        result.Should().BeOfType<
            Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("missing column 7");

        // **负列号同样被拒：** **C# 的索引器会把它当成越界，而基线的 `getOrNull` 也会返回 null。**
        // **A negative column is rejected too: the C# indexer treats it as out of range, as does the baseline's
        // `getOrNull`.**
        var negative = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [B.Key] = -1 };
        NativeSolutionProjection.ValuesByKey(
                new[] { new Flt64(1.0), new Flt64(2.0) }, negative)
            .Should().BeOfType<
                Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>(
                "a negative column is never valid");
    }

    /// <summary>
    /// **空输入通过** / Empty input passes.
    /// </summary>
    [Fact]
    public void EmptyInput_ShouldPass() {
        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeSolutionProjection.ValuesByKey(
                Array.Empty<Flt64>(), new Dictionary<VariableItemKey, int>());

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        result.Value.Should().BeEmpty();
    }

    private static readonly RealVar A = new("sp-a");

    private static readonly RealVar B = new("sp-b");

    private static readonly RealVar C = new("sp-c");
}
