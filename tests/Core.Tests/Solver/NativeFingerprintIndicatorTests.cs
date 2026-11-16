#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Report;
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
/// 指示器与遮盖接进指纹 / Indicators and maskings wired into the fingerprint.
/// </summary>
/// <remarks>
/// 把 `NativeIndicatorData` 与 `NativeMaskingData` 接进 `NativeFingerprint`，
/// 补齐 schema 阶梯里此前缺席的九个分支。
///
/// Wires the indicator and masking types into the fingerprint, completing the nine previously absent schema rungs.
/// </remarks>
public class NativeFingerprintIndicatorTests
{
    /// <summary>
    /// **指示器阶梯从 `-indicator-8` 到 `-indicator-2` 逐级下降** /
    /// The indicator ladder descends from `-indicator-8` to `-indicator-2`.
    /// </summary>
    /// <remarks>
    /// **顺序是：** **差值(8) → 合取(7) → 零带(6) → 蕴含(5) → 条件数值(4) →
    /// 等价结果(3) → 普通指示(2)。**
    ///
    /// **判据逐级叠加同一份指示数据，断言每一级都压过下一级。**
    /// **只测单级的话，一个顺序写反的实现也能全绿——单级算例对顺序完全不敏感。**
    ///
    /// **The code order is: difference(8), conjunction(7), zero band(6), implied(5), conditional value(4),
    /// equivalents(3), plain(2).**
    ///
    /// **The criterion accumulates features onto one indicator and asserts each rung dominates the next.** **Testing only
    /// single rungs would leave an order-reversed implementation green — single-feature cases are insensitive to
    /// order.**
    /// </remarks>
    [Fact]
    public void TheIndicatorLadder_ShouldDescendFromEightToTwo() {
        Rung(Plain()).Should().Be("gurobi-functions-indicator-2");
        Rung(Plain(equivalents: new[] { Eq.Key })).Should().Be("gurobi-functions-indicator-3");
        Rung(Plain(conditional: true)).Should().Be("gurobi-functions-indicator-4");
        Rung(Plain(implied: true)).Should().Be("gurobi-functions-indicator-5");
        Rung(Plain(banded: true)).Should().Be("gurobi-functions-indicator-6");
        Rung(Plain(conjunction: true)).Should().Be("gurobi-functions-indicator-7");
        Rung(Plain(difference: true)).Should().Be("gurobi-functions-indicator-8");

        // **关键的一步：单特性算例对<b>顺序</b>完全不敏感。**
        // **把 7 与 8 两档对调，上面七条<b>全绿</b>——因为每次只有一个特性打开，
        // 谁先谁后都选到同一个分支。**
        //
        // **所以必须再补"两者同时打开"的算例，逐个相邻对叠加。**
        //
        // **The crucial step: single-feature cases are completely insensitive to <b>order</b>.**
        // **Swapping rungs 7 and 8 leaves all seven above <b>green</b> — with only one feature on at a time, either order
        // picks the same branch.**
        //
        // **So "both on at once" cases are added, one adjacent pair at a time.**
        Rung(Plain(equivalents: new[] { Eq.Key }, conditional: true))
            .Should().Be("gurobi-functions-indicator-4", "4 outranks 3");
        Rung(Plain(conditional: true, implied: true))
            .Should().Be("gurobi-functions-indicator-5", "5 outranks 4");
        Rung(Plain(implied: true, banded: true))
            .Should().Be("gurobi-functions-indicator-6", "6 outranks 5");
        Rung(Plain(banded: true, conjunction: true))
            .Should().Be("gurobi-functions-indicator-7", "7 outranks 6");
        Rung(Plain(conjunction: true, difference: true))
            .Should().Be("gurobi-functions-indicator-8", "8 outranks 7");
    }

    /// <summary>
    /// **遮盖压在二值逻辑与指示器之上，且"有定义"升到 `-masking-2`** /
    /// Masking outranks binary logic and indicators, and a definition lifts it to `-masking-2`.
    /// </summary>
    [Fact]
    public void TheMaskingRungs_ShouldOutrankEverythingBelow() {
        NativeFingerprint.Schema(
                Array.Empty<NormalizedPiecewise>(), Array.Empty<NativeMaxData>(),
                Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(),
                "gurobi",
                indicators: new[] { Plain(difference: true) },
                maskings: new[] { Masking(definition: false) })
            .Should().Be(
                "gurobi-functions-masking-1",
                "masking outranks even the highest indicator rung");

        NativeFingerprint.Schema(
                Array.Empty<NormalizedPiecewise>(), Array.Empty<NativeMaxData>(),
                Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(),
                "gurobi",
                indicators: Array.Empty<NativeIndicatorData>(),
                maskings: new[] { Masking(definition: true) })
            .Should().Be(
                "gurobi-functions-masking-2",
                "a mask definition lifts the schema one rung");
    }

    /// <summary>
    /// **指示器压在 MAX 之上** / Indicators outrank MAX.
    /// </summary>
    /// <remarks>
    /// **阶梯里 `max-1` 位于指示器之后，所以两者同时非空时由指示器胜出。**
    /// **这一条很容易写反：直觉上"MAX 比指示器复杂"，但阶梯看的是<b>顺序</b>不是复杂度。**
    ///
    /// **In the code ladder `max-1` comes after the indicator rungs, so an indicator wins when both are non-empty.**
    /// **Easy to invert: intuitively MAX is "more complex", but the ladder is about <b>order</b>, not complexity.**
    /// </remarks>
    [Fact]
    public void Indicators_ShouldOutrankMax() {
        NativeFingerprint.Schema(
                Array.Empty<NormalizedPiecewise>(), new[] { MaxOf() },
                Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(),
                "gurobi",
                indicators: new[] { Plain() })
            .Should().Be(
                "gurobi-functions-indicator-2",
                "the indicator rung sits above max-1 in the baseline ladder");
    }

    /// <summary>
    /// **普通指示器渲染成 `positive-indicator`，零带渲染成 `zero-band`** /
    /// A plain indicator renders as `positive-indicator`; a banded one as `zero-band`.
    /// </summary>
    /// <remarks>
    /// **两个行形状的关键差别：** **`positive-indicator` 用 `gap=`，`zero-band` 用
    /// `inside=` 与 `outside=`，且多一个 `side=`，并且布尔字段的名字也不同**
    /// **（`positive-on-zero` 对 `outside-on-zero`）。**
    ///
    /// **这些字段名<b>完全不参与计算</b>，但它们是指纹文本的全部——
    /// 一个改名的字段会让指纹变化而所有行为判据仍绿。**
    ///
    /// **所以判据用基准哈希钉住，而不是逐字段断言文本（后者太长且脆）。**
    ///
    /// **The two row shapes differ in a way that matters: `positive-indicator` uses `gap=`, `zero-band` uses `inside=`
    /// and `outside=`, adds `side=`, and even names the boolean differently (`positive-on-zero` vs
    /// `outside-on-zero`).**
    ///
    /// **Those field names take <b>no part in computation</b> yet are the whole of the fingerprint text — a renamed field
    /// moves the fingerprint while every behavioural criterion stays green.**
    ///
    /// **So the criterion pins a golden hash rather than asserting the text field by field, which would be long and
    /// brittle.**
    /// </remarks>
    [Fact]
    public void TheIndicatorRows_ShouldMatchTheirGoldenHash() {
        PlainIndicatorHash().Should().Be(PLAIN_GOLDEN);
        BandedIndicatorHash().Should().Be(BANDED_GOLDEN);
        PlainIndicatorHash().Should().NotBe(
            BandedIndicatorHash(), "the two row shapes are genuinely different");
    }

    /// <summary>
    /// **缺列时按种类报** / Missing columns are reported per kind.
    /// </summary>
    [Fact]
    public void MissingColumns_ShouldBeReportedPerKind() {
        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativeFingerprint.Compute(
                Seed(), new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedPiecewise>(), Array.Empty<NormalizedAbs>(),
                Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
                Array.Empty<BinaryLogicStructure>(), "gurobi",
                indicators: new[] { Plain() }))
            .Error.Message.Should().Contain("Indicator result column missing");

        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativeFingerprint.Compute(
                Seed(), new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedPiecewise>(), Array.Empty<NormalizedAbs>(),
                Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
                Array.Empty<BinaryLogicStructure>(), "gurobi",
                maskings: new[] { Masking(definition: false) }))
            .Error.Message.Should().Contain("Mask column missing");
    }

    /// <summary>
    /// **指示器的输入项按列号排序，所以字典顺序不影响指纹** /
    /// Indicator terms are sorted by column, so dictionary order does not matter.
    /// </summary>
    [Fact]
    public void IndicatorTermOrder_ShouldNotAffectTheFingerprint() {
        var forward = new Dictionary<VariableItemKey, double> { [A.Key] = 1.0, [B.Key] = 2.0 };
        var backward = new Dictionary<VariableItemKey, double> { [B.Key] = 2.0, [A.Key] = 1.0 };

        HashOf(Plain(terms: forward)).Should().Be(HashOf(Plain(terms: backward)));
    }

    private static string Rung(NativeIndicatorData indicator) =>
        NativeFingerprint.Schema(
            Array.Empty<NormalizedPiecewise>(), Array.Empty<NativeMaxData>(),
            Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(),
            "gurobi", indicators: new[] { indicator });

    private static string PlainIndicatorHash() => HashOf(Plain());

    private static string BandedIndicatorHash() => HashOf(Plain(banded: true));

    private static string HashOf(NativeIndicatorData indicator) =>
        NativeFingerprint.Compute(
            Seed(), Columns(),
            Array.Empty<NormalizedPiecewise>(), Array.Empty<NormalizedAbs>(),
            Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
            Array.Empty<BinaryLogicStructure>(), "gurobi",
            indicators: new[] { indicator }).Value.Value;

    private static Dictionary<VariableItemKey, int> Columns() => new() {
        [A.Key] = 0,
        [B.Key] = 1,
        [Result.Key] = 2,
        [BandSide.Key] = 3,
        [Eq.Key] = 4,
        [Mask.Key] = 5,
        [MaskResult.Key] = 6,
        [SubResult.Key] = 7,
        [ConjResult.Key] = 8,
    };

    private static AuditFingerprint Seed() => new("1.0", "sha256", "seed");

    private static NativeIndicatorData Plain(
        Dictionary<VariableItemKey, double>? terms = null,
        VariableItemKey[]? equivalents = null,
        bool conditional = false,
        bool implied = false,
        bool banded = false,
        bool conjunction = false,
        bool difference = false) =>
        new(
            Terms: terms
                ?? new Dictionary<VariableItemKey, double> { [A.Key] = 2.0 },
            Constant: 0.0,
            Tolerance: 1.0,
            BigM: 100.0,
            ResultKey: Result.Key,
            Name: "indicator",
            PositiveOnZero: false,
            LowerBound: -100.0,
            UpperBound: 100.0,
            EquivalentResultKeys: equivalents ?? Array.Empty<VariableItemKey>(),
            ConditionalValue: conditional
                ? new NativeConditionalValueData(
                    new Dictionary<VariableItemKey, double> { [B.Key] = 1.0 },
                    0.0, SubResult.Key, -10.0, 10.0, "cond")
                : null,
            ImpliedCondition: implied
                ? new NativeConditionalValueData(
                    new Dictionary<VariableItemKey, double> { [B.Key] = 1.0 },
                    0.0, ConjResult.Key, -10.0, 10.0, "implied")
                : null,
            ZeroBand: banded ? new NativeZeroBandData(0.5, BandSide.Key) : null,
            Conjunction: conjunction
                ? new NativeConjoinedIndicatorData(Plain(), ConjResult.Key, "and")
                : null,
            Difference: difference
                ? new NativeDifferenceIndicatorData(Plain(), SubResult.Key, "xor")
                : null);

    private static NativeMaskingData Masking(bool definition) =>
        new(
            MaskKey: Mask.Key,
            Value: new NativeConditionalValueData(
                new Dictionary<VariableItemKey, double> { [A.Key] = 1.0 },
                0.0, MaskResult.Key, -10.0, 10.0, "value"),
            Definition: definition
                ? new NativeMaskDefinitionData(
                    new Dictionary<VariableItemKey, double> { [B.Key] = 2.0 }, 1.0)
                : null);

    private static NativeMaxData MaxOf() =>
        new(
            Inputs: new[] {
                new NativeMaxInputData(
                    new Dictionary<VariableItemKey, double> { [A.Key] = 1.0 }, 0.0, 0.0, 10.0),
            },
            ResultKey: MaskResult.Key,
            SelectorKeys: new[] { BandSide.Key },
            BigMValues: new[] { 100.0 },
            Name: "max");

    private static readonly RealVar A = new("fi-a");

    private static readonly RealVar B = new("fi-b");

    private static readonly BinVar Result = new("fi-result");

    private static readonly BinVar BandSide = new("fi-band");

    private static readonly BinVar Eq = new("fi-eq");

    private static readonly BinVar Mask = new("fi-mask");

    private static readonly RealVar MaskResult = new("fi-maskr");

    private static readonly RealVar SubResult = new("fi-sub");

    private static readonly BinVar ConjResult = new("fi-conj");

    private const string PLAIN_GOLDEN =
        "67a259ab849821ae9b58bb980510a9cac21b839e25fedccb3795e042de3f268b";

    private const string BANDED_GOLDEN =
        "7131d2898ac96905d45b06473dfb602621faa44fd4321cf60ef67920eb793c34";
}
