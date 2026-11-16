#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 原生绝对值 primitive：两层检查与严格上界 / Two layers of checking, one strict bound.
/// </summary>
/// <remarks>
/// 对齐基线 `NativeAbs.kt` 的 `NativeAbsData` 与 `prepareNativeAbs`。
///
/// Adds </remarks>
public class NativeAbsTests
{
    /// <summary>
    /// **合格的结构转换出五个字段** / A valid structure converts to the five fields.
    /// </summary>
    [Fact]
    public void AValidStructure_ShouldConvert() {
        Result<NativeAbsData, ErrorCode, Error<ErrorCode>> result = NativeAbs.Prepare(Structure());

        result.Should().BeOfType<Ok<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
        NativeAbsData data = result.Value;
        data.InputKey.Should().Be(X.Key);
        data.ResultKey.Should().Be(Result.Key);
        data.PositiveBigM.Should().Be(100.0);
        data.NegativeBigM.Should().Be(100.0);
        data.Name.Should().Be("abs");
    }

    /// <summary>
    /// **`maximumMagnitude` 是严格上界** / `maximumMagnitude` is a strict upper bound.
    /// </summary>
    /// <remarks>
    /// **`|M| >= maximumMagnitude` 被拒，而不是 `> maximumMagnitude`。**
    ///
    /// **刚好等于 SDK 上限的值不可用：它没有留出任何余量，
    /// 求解器在加上别的项之后就会溢出。**
    ///
    /// **判据用 `max = 100` 与 `M = 100` 这一对边界：恰好相等必须被拒，
    /// 而 `99.9` 必须通过。** **只测一侧的话，`>` 与 `>=` 就区分不开。**
    ///
    /// **`|M| >= maximumMagnitude` is rejected, not `> maximumMagnitude`.**
    ///
    /// **A value exactly at the SDK limit is unusable: it leaves no headroom and the solver overflows once another term is
    /// Added
    ///
    /// **The criterion uses the pair `max = 100`, `M = 100`: equality must be rejected and `99.9` must pass.**
    /// **Testing one side leaves `>` and `>=` indistinguishable.**
    /// </remarks>
    [Fact]
    public void TheMaximumMagnitude_ShouldBeStrict() {
        NativeAbs.Prepare(Structure(bigM: 100.0), maximumMagnitude: 100.0).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>(
                "a bound exactly at the limit has no headroom");

        NativeAbs.Prepare(Structure(bigM: 99.9), maximumMagnitude: 100.0).Should()
            .BeOfType<Ok<NativeAbsData, ErrorCode, Error<ErrorCode>>>();

        // **默认的 `+∞` 不过滤任何有限值。**
        // **The default `+∞` filters no finite value.**
        NativeAbs.Prepare(Structure(bigM: 1e300)).Should()
            .BeOfType<Ok<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **`maximumMagnitude` 本身必须是正数且不是 `NaN`** /
    /// `maximumMagnitude` itself must be positive and not NaN.
    /// </summary>
    /// <remarks>
    /// **`0` 与负数被拒、`NaN` 被拒。**
    /// **`NaN` 这一支特别要紧：`|M| >= NaN` 恒为假，若不单独拒绝，那么一个 `NaN` 上限
    /// 会让<b>所有</b> Big-M 都通过校验——一个"什么都放行"的配置。**
    ///
    /// **Zero and negatives are rejected, and so is NaN.**
    /// **The NaN branch matters most: `|M| >= NaN` is always false, so without an explicit rejection a NaN limit would let
    /// <b>every</b> Big-M pass — a configuration that admits everything.**
    /// </remarks>
    [Fact]
    public void TheMaximumMagnitudeItself_ShouldBeValidated() {
        NativeAbs.Prepare(Structure(), maximumMagnitude: 0.0).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
        NativeAbs.Prepare(Structure(), maximumMagnitude: -1.0).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
        NativeAbs.Prepare(Structure(), maximumMagnitude: double.NaN).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>(
                "otherwise every bound would pass the magnitude check");
    }

    /// <summary>
    /// **输入必须是 `1·x + 0`** / The input must be `1*x + 0`.
    /// </summary>
    /// <remarks>
    /// **三种偏离各一例：系数不是 1、常数不是 0、不是一个单项。**
    /// **只测一种的话，另外两种可以整段缺失。**
    ///
    /// **Three deviations, one case each: wrong coefficient, nonzero constant, not a single monomial.**
    /// **Testing one would leave the other two deletable.**
    /// </remarks>
    [Fact]
    public void TheInput_ShouldBeExactlyUnitVariable() {
        NativeAbs.Prepare(Structure(coefficient: 2.0)).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
        NativeAbs.Prepare(Structure(constant: 3.0)).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();

        var twoMonomials = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, X),
                new LinearMonomial<Flt64>(Flt64.One, Y),
            }, Flt64.Zero);
        NativeAbs.Prepare(Structure(input: twoMonomials)).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **键的唯一性包含输入列** / Key uniqueness includes the input column.
    /// </summary>
    /// <remarks>
    /// **结果列与输入是同一列时，方程仍然有解，只是那个解不是 `abs`。**
    ///
    /// **A result sharing the input column keeps the equations solvable, just not as `abs`.**
    /// </remarks>
    [Fact]
    public void TheKeys_ShouldIncludeTheInput() {
        var shared = new AbsStructure(
            Input: Poly(1.0, 0.0), ResultVariable: X, PositiveVariable: Positive,
            NegativeVariable: Negative, SignVariable: Sign,
            PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

        Result<NativeAbsData, ErrorCode, Error<ErrorCode>> result = NativeAbs.Prepare(shared);

        result.Should().BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
        ((Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .Contain("distinct keys");
    }

    /// <summary>
    /// **两个 Big-M 的边界：非负、有限** / Both Big-Ms: nonnegative and finite.
    /// </summary>
    [Fact]
    public void TheBigMs_ShouldBeNonnegativeAndFinite() {
        NativeAbs.Prepare(Structure(bigM: -1.0)).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>(
                "the fallback bounds must be nonnegative");

        var infinite = new AbsStructure(
            Input: Poly(1.0, 0.0), ResultVariable: Result, PositiveVariable: Positive,
            NegativeVariable: Negative, SignVariable: Sign,
            PositiveBigM: new Flt64(double.PositiveInfinity),
            NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

        NativeAbs.Prepare(infinite).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>(
                "the fallback bounds must be finite");
    }

    /// <summary>
    /// **失败消息带着具体原因前缀** / Failure messages carry a concrete reason prefix.
    /// </summary>
    /// <remarks>
    /// **`invalid native ABS data:` 这个前缀让调用方能把"这一层的拒绝"
    /// 与"结构层的拒绝"分开**：**后者来自 `AbsStructure.GenerateConstraints`，
    /// 消息完全不同。**
    ///
    /// **The `invalid native ABS data:` prefix lets a caller separate "this layer refused" from "the structure layer
    /// refused"**: **the latter comes from `AbsStructure.GenerateConstraints` with an entirely different message.**
    /// </remarks>
    [Fact]
    public void TheFailures_ShouldBeLabelled() {
        Result<NativeAbsData, ErrorCode, Error<ErrorCode>> result =
            NativeAbs.Prepare(Structure(coefficient: 2.0));

        ((Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .StartWith("invalid native ABS data:");
    }

    /// <summary>
    /// **求解器层的复检在本仓库<b>当前不可测</b>** / The solver-side re-check is <b>not testable</b> here today.
    /// </summary>
    /// <remarks>
    /// **这是本方向第六条"不可测"发现，记在这里而不是假装覆盖了。**
    ///
    /// **求解器层读的是"实际的系数与常数经过转换器之后还是不是 1 与 0"。**
    /// **这个检查只有在转换器<b>不是恒等</b>时才有区分力：**
    ///
    /// | 转换器 | `coefficient == one` | `ToSolverDouble(coefficient)` |
    /// | --- | --- | --- |
    /// | **恒等** | 真 | **`1.0`（所以两层一致）** |
    /// | **带缩放（例如 ×1000）** | 真 | **`1000.0`（两层不一致，复检才有意义）** |
    ///
    /// **而本仓库目前<b>只有一个</b> `IIntoValue<Flt64>` 实现——内部的 `Flt64IdentityConverter`。**
    /// **没有缩放转换器，就构造不出"两层不一致"的输入，这个分支就打不到。**
    ///
 /// **最初把它写成了 `one` 的往返（`FromValue(IntoValue(one))`），那在任何转换器下都等于 `1.0`——
    /// 一个恒等式，什么都检查不到。改成读实际值之后逻辑是对的，但<b>判据仍然打不到它</b>，
    /// 因为恒等转换器让两层永远一致。**
    ///
    /// **变异验证证实了这一点：把实现退回成 `one` 的往返，七条判据全绿。**
    /// **所以这里<b>不声称</b>这个分支被覆盖了。**
    ///
    /// **保留它的理由**：**`IIntoValue` 是一个公开的注入点，未来会出现缩放转换器；
    /// 那时这一层必须已经在按求解器的数值验。删掉它等于把一个未来的正确性押在"转换器永远是恒等"上。**
    ///
    /// **This is the direction's sixth "untestable" finding, recorded rather than papered over.**
    ///
    /// **The solver layer checks whether the <b>actual</b> coefficient and constant, once converted, are still one and
    /// zero.** **That check only discriminates when the converter is <b>not the identity</b>:**
    ///
    /// **The repository currently has <b>one</b> `IIntoValue<Flt64>` implementation — the internal
    /// `Flt64IdentityConverter`.** **Without a scaling converter there is no input where the two layers disagree, so the
    /// branch is unreachable.**
    ///
    /// **I first wrote it as a round-trip of `one`, which equals `1.0` under any converter — a tautology that checks
    /// nothing.** **Reading the actual values is correct, but the criterion <b>still cannot reach it</b> because the
    /// identity converter keeps the two layers in agreement.**
    ///
    /// **The mutation confirms it: reverting the implementation to the round-trip leaves all seven criteria green.**
    /// **So <b>not</b> claim that branch is covered.**
    ///
    /// **Why keep it**: **`IIntoValue` is a public injection point and scaling converters will appear; that layer must
    /// already be checking solver-side numbers.** **Deleting it would stake a future correctness on "the converter is
    /// always the identity".**
    /// </remarks>
    [Fact]
    public void TheSolverSideRecheck_ShouldBeDocumentedAsUnreachable() {
        // **恒等转换器下，两层检查给出同一个答案——这是"不可测"的直接证据。**
        // **Under the identity converter both layers give the same answer, which is the direct evidence.**
        IIntoValue<Flt64> converter = IIntoValue<Flt64>.Identity;

        var coefficient = new Flt64(1.0);
        converter.FromValue(coefficient).ToSolverDouble().Should().Be(1.0);
        converter.FromValue(converter.IntoValue(Flt64.One)).ToSolverDouble().Should().Be(
            1.0,
            "so the round-trip and the actual-value check agree, and the branch cannot be distinguished");

        // **而"系数不是 1"那一支由数值层拦下，与求解器层无关。**
        // **The "coefficient is not one" case is caught by the numeric layer, independent of the solver layer.**
        NativeAbs.Prepare(Structure(coefficient: 2.0)).Should()
            .BeOfType<Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>>();
    }
    private static AbsStructure Structure(
        LinearPolynomial<Flt64>? input = null,
        double coefficient = 1.0,
        double constant = 0.0,
        double bigM = 100.0) =>
        new(Input: input ?? Poly(coefficient, constant), ResultVariable: Result,
            PositiveVariable: Positive, NegativeVariable: Negative, SignVariable: Sign,
            PositiveBigM: new Flt64(bigM), NegativeBigM: new Flt64(bigM),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

    private static LinearPolynomial<Flt64> Poly(double coefficient, double constant) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), X) },
            new Flt64(constant));

    private static readonly RealVar X = new("na-x");

    private static readonly RealVar Y = new("na-y");

    private static readonly RealVar Result = new("na-result");

    private static readonly RealVar Positive = new("na-pos");

    private static readonly RealVar Negative = new("na-neg");

    private static readonly BinVar Sign = new("na-sign");
}
