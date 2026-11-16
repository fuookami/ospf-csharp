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
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 归一化：跨结构与结构内的两层唯一性 / Two levels of uniqueness, within and across structures.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `normalizePiecewiseStructures` 与 `normalizeAbsStructures`。
///
/// Adds </remarks>
public class NativePrimitiveNormalizerTests
{
    /// <summary>
    /// **合格的一组结构被归一化** / A valid list normalizes.
    /// </summary>
    [Fact]
    public void AValidList_ShouldNormalize() {
        Result<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>> result =
            NativePrimitiveNormalizer.NormalizePiecewise(new[] { Piecewise(Result1) });

        result.Should().BeOfType<
            Ok<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>>();
        result.Value.Should().HaveCount(1);
        result.Value[0].ResultKey.Should().Be(Result1.Key);
        result.Value[0].SelectorKeys.Should().HaveCount(2);
        result.Value[0].Breakpoints.Should().HaveCount(3);
        result.Value[0].Values.Should().HaveCount(3);
        result.Value[0].Values[0].ToDouble().Should().Be(0.0);
        result.Value[0].Values[2].ToDouble().Should().Be(5.0);
    }

    /// <summary>
    /// **零个选择变量<b>能</b>通过归一化** / Zero selectors <b>passes</b> normalization.
    /// </summary>
    /// <remarks>
    /// **这是这里最值得记的一处：归一化的数量检查是有条件的
    /// （`selectorKeys.isNotEmpty &&...`），所以零个选择的快照<b>放行</b>。**
    ///
    /// **而行生成里 `SelectorVariables.Count != segments` 直接拒绝。**
    ///
    /// **两层严格程度不同，而宽松的那一层在前**：**一个零选择的快照会走到很后面才被拒。**
    ///
    /// **判据把两层的实际行为<b>并排断言</b>，让这个差别成为一个被记录的事实——
    /// 而不是一个"某天有人发现归一化太松"的惊喜。**
    ///
    /// **This is the batch's most notable point: the count check is conditional
    /// (`selectorKeys.isNotEmpty &&...`), so a zero-selector snapshot <b>passes</b>.**
    ///
    /// **the earlier row generation rejects `SelectorVariables.Count != segments` outright.**
    ///
    /// **The two layers differ in strictness and the lenient one comes first**: **a zero-selector snapshot travels a long
    /// way before being rejected.**
    ///
    /// **The criterion asserts both layers' actual behaviour <b>side by side</b>, making the difference a recorded fact
    /// rather than a future surprise.**
    /// </remarks>
    [Fact]
    public void ZeroSelectors_ShouldPassNormalizationButFailRowGeneration() {
        var noSelectors = new UnivariateLinearPiecewiseStructure(
            Input: Poly(1.0, 0.0),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: Result1, SelectorVariables: Array.Empty<IVariableItem>()) {
            Converter = IIntoValue<Flt64>.Identity,
            ExplicitM = new Flt64(100.0),
            Name = "pwl",
        };

        NativePrimitiveNormalizer.NormalizePiecewise(new[] { noSelectors }).Should()
            .BeOfType<Ok<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>>(
                "the count check only fires when selectors are present");

        noSelectors.GenerateConstraints().Should()
            .BeOfType<Failed<
                IReadOnlyList<Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>>,
                ErrorCode, Error<ErrorCode>>>( "row generation requires one selector per segment");
    }

    /// <summary>
    /// **选择变量数量不符会被拒** / A wrong selector count is rejected.
    /// </summary>
    [Fact]
    public void AWrongSelectorCount_ShouldBeRejected() {
        var withOne = new UnivariateLinearPiecewiseStructure(
            Input: Poly(1.0, 0.0),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: Result1, SelectorVariables: new IVariableItem[] { Bit1 }) {
            Converter = IIntoValue<Flt64>.Identity,
            Name = "pwl",
        };

        Result<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>> result =
            NativePrimitiveNormalizer.NormalizePiecewise(new[] { withOne });

        result.Should().BeOfType<
            Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("selector count is inconsistent");
    }

    /// <summary>
    /// **同一个结构里选择变量重复会被拒** / Duplicate selectors within one structure are rejected.
    /// </summary>
    [Fact]
    public void DuplicateSelectors_ShouldBeRejected() {
        var duplicated = new UnivariateLinearPiecewiseStructure(
            Input: Poly(1.0, 0.0),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: Result1,
            SelectorVariables: new IVariableItem[] { Bit1, Bit1 }) {
            Converter = IIntoValue<Flt64>.Identity,
            Name = "pwl",
        };

        ((Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>)
            NativePrimitiveNormalizer.NormalizePiecewise(new[] { duplicated }))
            .Error.Message.Should().Contain("selector keys are duplicated");
    }

    /// <summary>
    /// **跨结构的结果键重复会被拒** / A duplicated result key across structures is rejected.
    /// </summary>
    /// <remarks>
    /// **这一层是 primitive 层查不到的：两个结构各自五键互不相同，却抢同一个结果列。**
    ///
    /// **The primitive layer cannot see this: both structures are internally consistent yet fight for the same result
    /// column.**
    /// </remarks>
    [Fact]
    public void ADuplicatedResultKeyAcrossStructures_ShouldBeRejected() {
        UnivariateLinearPiecewiseStructure first = Piecewise(Result1);
        UnivariateLinearPiecewiseStructure second =
            Piecewise(Result1, selectors: new IVariableItem[] { Bit3, Bit4 });

        Result<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>> result =
            NativePrimitiveNormalizer.NormalizePiecewise(new[] { first, second });

        result.Should().BeOfType<
            Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("result keys are duplicated");
    }

    /// <summary>
    /// **两个 ABS 共用同一输入列是允许的** / Two ABS structures may share an input column.
    /// </summary>
    /// <remarks>
    /// **跨结构唯一性只登记 `resultKey`/`positive`/`negative`/`sign` 四个，<b>输入列不在其中</b>。**
    ///
    /// **所以 `abs(x)` 可以合法地出现两次（结果列不同）——这是一个真实需求：
    /// 同一个量的绝对值可能被两个维度分别引用。**
    ///
    /// **判据把"共用输入列"与"共用结果列"并排：前者必须通过，后者必须被拒。**
    ///
    /// **Cross-structure uniqueness registers `resultKey`/`positive`/`negative`/`sign` but <b>not</b> the input column.**
    ///
    /// **So `abs(x)` may legitimately appear twice with different result columns — a real need, since two dimensions may
    /// each reference the absolute value of the same quantity.**
    ///
    /// **The criterion places "shared input" and "shared result" side by side: the former must pass, the latter must be
    /// rejected.**
    /// </remarks>
    [Fact]
    public void SharedInputColumns_ShouldBeAllowedButSharedResultsNot() {
        AbsStructure[] sharedInput = { Abs(Result1, Pos1, Neg1, Sign1), Abs(Result2, Pos2, Neg2, Sign2) };

        NativePrimitiveNormalizer.NormalizeAbs(sharedInput).Should()
            .BeOfType<Ok<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>>(
                "both structures read the same input but write different columns");

        AbsStructure[] sharedResult = { Abs(Result1, Pos1, Neg1, Sign1), Abs(Result1, Pos2, Neg2, Sign2) };

        Result<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>> rejected =
            NativePrimitiveNormalizer.NormalizeAbs(sharedResult);
        rejected.Should().BeOfType<Failed<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>)rejected)
            .Error.Message.Should().Contain("ABS keys are duplicated");
    }

    /// <summary>
    /// **primitive 层的错误被原样转发** / A primitive-layer error is forwarded verbatim.
    /// </summary>
    /// <remarks>
    /// **归一化不重新包装下层消息**：**一个"断点非递增"的结构，最终拿到的仍然是
    /// `breakpoints must be strictly increasing`，而不是一句笼统的"归一化失败"。**
    /// **丢掉下层消息等于把排查起点往上挪了一层。**
    ///
    /// **Normalization does not re-wrap the lower layer's message**: **a structure with increasing-order problems still
    /// reports `breakpoints must be strictly increasing` rather than a vague "normalization failed".**
    /// **Dropping the lower message moves the start of any investigation up one level.**
    /// </remarks>
    [Fact]
    public void APrimitiveError_ShouldBeForwardedVerbatim() {
        var shuffled = new UnivariateLinearPiecewiseStructure(
            Input: Poly(1.0, 0.0),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(10.0), new Flt64(5.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: Result1,
            SelectorVariables: new IVariableItem[] { Bit1, Bit2 }) {
            Converter = IIntoValue<Flt64>.Identity,
            Name = "pwl",
        };

        ((Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>)
            NativePrimitiveNormalizer.NormalizePiecewise(new[] { shuffled }))
            .Error.Message.Should().Contain("strictly increasing");
    }

    /// <summary>
    /// **空列表是一个成功的空归一化** / An empty list normalizes to nothing, successfully.
    /// </summary>
    [Fact]
    public void AnEmptyList_ShouldSucceed() {
        NativePrimitiveNormalizer.NormalizePiecewise(
            Array.Empty<UnivariateLinearPiecewiseStructure>()).Value.Should().BeEmpty();
        NativePrimitiveNormalizer.NormalizeAbs(Array.Empty<AbsStructure>()).Value.Should().BeEmpty();
    }

    private static UnivariateLinearPiecewiseStructure Piecewise(
        IVariableItem result,
        IVariableItem[]? selectors = null) =>
        new(Input: Poly(1.0, 0.0),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: result,
            SelectorVariables: selectors ?? new IVariableItem[] { Bit1, Bit2 }) {
            Converter = IIntoValue<Flt64>.Identity,
            Name = "pwl",
        };

    private static AbsStructure Abs(
        IVariableItem result, IVariableItem positive, IVariableItem negative, IVariableItem sign) =>
        new(Input: Poly(1.0, 0.0), ResultVariable: result, PositiveVariable: positive,
            NegativeVariable: negative, SignVariable: sign,
            PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

    private static LinearPolynomial<Flt64> Poly(double coefficient, double constant) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), X) },
            new Flt64(constant));

    private static readonly RealVar X = new("nz-x");

    private static readonly RealVar Result1 = new("nz-r1");

    private static readonly RealVar Result2 = new("nz-r2");

    private static readonly RealVar Pos1 = new("nz-p1");

    private static readonly RealVar Pos2 = new("nz-p2");

    private static readonly RealVar Neg1 = new("nz-n1");

    private static readonly RealVar Neg2 = new("nz-n2");

    private static readonly BinVar Sign1 = new("nz-s1");

    private static readonly BinVar Sign2 = new("nz-s2");

    private static readonly BinVar Bit1 = new("nz-b1");

    private static readonly BinVar Bit2 = new("nz-b2");

    private static readonly BinVar Bit3 = new("nz-b3");

    private static readonly BinVar Bit4 = new("nz-b4");
}
