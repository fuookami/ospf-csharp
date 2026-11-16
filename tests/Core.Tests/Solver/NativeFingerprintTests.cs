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
/// 全种类指纹：快路径与阶梯 / The fast path and the ladder.
/// </summary>
/// <remarks>
/// 对齐的 `nativeFingerprint`。
///
/// Adds </remarks>
public class NativeFingerprintTests
{
    /// <summary>
    /// **快路径：只有 PWL 时交给 `piecewiseFingerprint`，schema 是 `-pwl-1`** /
    /// The fast path: PWL only delegates, and the schema is `-pwl-1`.
    /// </summary>
    /// <remarks>
    /// **判据直接和"手工调用 `piecewiseFingerprint`"的结果比，而不是和某个常量比。**
    /// **这样它测的是"确实走了那条路"，而不是"恰好等于某个哈希"。**
    ///
    /// **The criterion compares against a manual `piecewiseFingerprint` call rather than a constant, so it tests "it really
    /// took that path" instead of "it happens to equal some hash".**
    /// </remarks>
    [Fact]
    public void TheFastPath_ShouldDelegateToPiecewiseFingerprint() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [R.Key] = 1 };
        NormalizedPiecewise structure =
            Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });

        AuditFingerprint fast = NativeFingerprint.Compute(
            previous, columns, new[] { structure },
            Array.Empty<NormalizedAbs>(), Array.Empty<NativeMaxData>(),
            Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(), "gurobi").Value;

        AuditFingerprint manual = NativePiecewiseFingerprint
            .Compute(previous, columns, new[] { structure }, "gurobi").Value;

        fast.Value.Should().Be(manual.Value);
        fast.SchemaVersion.Should().Be(
            "gurobi-pwl-1", "the fast path reports the PWL schema, not a cross-kind one");

        // **慢路径的最低档是 `-abs-1` / `-pwl-abs-1`，所以同一份 PWL 数据走两条路 schema 不同。**
        // **The slow path's lowest is `-abs-1` / `-pwl-abs-1`, so the same PWL data gets a different schema.**
        NativeFingerprint.Schema(
                new[] { structure }, Array.Empty<NativeMaxData>(),
                Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(), "gurobi")
            .Should().Be("gurobi-pwl-abs-1");
    }

    /// <summary>
    /// **阶梯：相邻两类同时非空时，<b>靠前的那一类赢</b>** /
    /// The ladder: when two neighbours are both non-empty, the <b>earlier</b> one wins.
    /// </summary>
    /// <remarks>
    /// **这是这里最有内容的一条。**
    ///
    /// **阶梯的十三个分支里这里可达五个：** **`binary-logic-1` → `semi-1` → `max-1` →
    /// `abs-1` / `pwl-abs-1`。**
    ///
    /// **要测的不是"某一个分支对不对"，而是<b>相邻两个同时非空时谁赢</b>——
    /// 因为一个只按优先级表写出来的实现，最可能的错是把顺序写反，
    /// 而单类算例（只有一类非空）对顺序完全不敏感。**
    ///
    /// **所以判据逐对叠加，断言每一个都被前面那个压住。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **Five of its thirteen branches are reachable here: `binary-logic-1` → `semi-1` → `max-1` →
    /// `abs-1` / `pwl-abs-1`.**
    ///
    /// **What needs testing is <b>who wins when two neighbours are both non-empty</b>, because the most likely mistake in a
    /// priority table is getting the order backwards — and single-kind cases are completely insensitive to order.**
    ///
    /// **So the criterion accumulates one kind at a time and asserts each is dominated by the one before it.**
    /// </remarks>
    [Fact]
    public void TheLadder_ShouldLetTheEarlierKindWin() {
        NormalizedPiecewise pwl = Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });
        SemiStructure semi = SemiOf(lower: 3.0);
        NativeMaxData max = MaxOf();
        BinaryLogicStructure logic = LogicOf();

        Empty().Should().Be("gurobi-abs-1", "no structures at all is the ABS schema");
        Only(piecewise: new[] { pwl })
            .Should().Be("gurobi-pwl-abs-1", "PWL present with no MAX lifts the schema");
        Only(piecewise: new[] { pwl }, maxima: new[] { max })
            .Should().Be("gurobi-functions-max-1", "MAX dominates PWL+ABS");
        Only(piecewise: new[] { pwl }, maxima: new[] { max }, semis: new[] { semi })
            .Should().Be("gurobi-functions-semi-1", "semi dominates MAX");
        Only(piecewise: new[] { pwl }, maxima: new[] { max }, semis: new[] { semi },
                logic: new[] { logic })
            .Should().Be(
                "gurobi-functions-binary-logic-1", "binary logic dominates everything below it");
    }

    /// <summary>
    /// **慢路径的 PWL 行多一个 `kind=pwl|` 前缀，所以与快路径连行内容都不同** /
    /// The slow path's PWL line carries a `kind=pwl|` prefix, so the line content differs too.
    /// </summary>
    /// <remarks>
    /// **这一点很容易被忽略：** **同一份 PWL 数据，快路径和慢路径给出不同的 schema，
    /// <b>而且</b>不同的行文本。**
    ///
    /// **判据让"只有 PWL"与"PWL + ABS"两个指纹不同，并单独断言 schema 的差别——
    /// 因为"指纹不同"可能是行不同造成的，也可能是 schema 不同造成的，两者要分开看。**
    ///
    /// **Easy to miss: the same PWL data differs between the two paths in the schema <b>and</b> in the line text.**
    ///
    /// **The criterion asserts the two fingerprints differ and the schemas differ separately — a differing fingerprint
    /// could come from either, and they should be examined apart.**
    /// </remarks>
    [Fact]
    public void TheSlowPath_ShouldAddAKindPrefix() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");
        var columns = new Dictionary<VariableItemKey, int> {
            [A.Key] = 0, [R.Key] = 1,
        };
        NormalizedPiecewise structure =
            Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });

        AuditFingerprint fast = NativeFingerprint.Compute(
            previous, columns, new[] { structure },
            Array.Empty<NormalizedAbs>(), Array.Empty<NativeMaxData>(),
            Array.Empty<SemiStructure>(), Array.Empty<BinaryLogicStructure>(), "gurobi").Value;

        // **加一个 ABS 结构，把 pwl 结构留在原地——于是走慢路径。**
        // **Add an ABS structure and keep the PWL one, forcing the slow path.**
        var slowColumns = new Dictionary<VariableItemKey, int>(columns) {
            [AbsInput.Key] = 2, [AbsResult.Key] = 3,
        };
        AuditFingerprint slow = NativeFingerprint.Compute(
            previous, slowColumns, new[] { structure },
            new[] { new NormalizedAbs(AbsInput.Key, AbsResult.Key, P.Key, N.Key, S.Key) },
            Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
            Array.Empty<BinaryLogicStructure>(), "gurobi").Value;

        slow.Value.Should().NotBe(fast.Value);
        slow.SchemaVersion.Should().Be("gurobi-pwl-abs-1");
        fast.SchemaVersion.Should().Be("gurobi-pwl-1");

        // **再让"纯 ABS"与"PWL + ABS"分开：两者 schema 不同，因为前者是 `-abs-1`。**
        // **And ABS-only differs from PWL+ABS in schema, since the former is `-abs-1`.**
        AuditFingerprint absOnly = NativeFingerprint.Compute(
            previous,
            new Dictionary<VariableItemKey, int> { [AbsInput.Key] = 0, [AbsResult.Key] = 1 },
            Array.Empty<NormalizedPiecewise>(),
            new[] { new NormalizedAbs(AbsInput.Key, AbsResult.Key, P.Key, N.Key, S.Key) },
            Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
            Array.Empty<BinaryLogicStructure>(), "gurobi").Value;

        absOnly.SchemaVersion.Should().Be("gurobi-abs-1");
    }

    /// <summary>
    /// **慢路径的完整文本被一个基准哈希钉住** / The slow path's full text is pinned by a golden hash.
    /// </summary>
    /// <remarks>
    /// **这条判据是被一次"没抓住"的变异逼出来的。**
    ///
    /// **我原来的判据只断言"快路径指纹 ≠ 慢路径指纹"。把慢路径的 `kind=pwl` 改成
    /// `kind=pwl-mutated`，判据<b>全绿</b>——因为两条路径本来就不同，
    /// 前缀改不改都不影响"不同"。**
    ///
    /// **也就是说那条判据测的是"两条路径不同"，不是"慢路径的行长什么样"。**
    ///
    /// **基准哈希是这一类函数的自然选择：** **指纹本来就该是确定性的，
    /// 而"确定性"最强的表述就是"它等于这个值"。** **文本里任何一个字符变了，哈希就变。**
    ///
    /// **代价是这条判据需要在新平台上核对一次**（如果 `"R"` 格式的浮点渲染
    /// 在不同运行时上有差异）——而这个代价可以接受，因为
    /// **"渲染格式变了"恰恰是这条判据最该报警的事。**
    ///
    /// **This criterion was forced by a mutation that was not caught.**
    ///
    /// **The old criterion only asserted "fast fingerprint != slow fingerprint". Renaming the slow path's `kind=pwl` to
    /// `kind=pwl-mutated` left it <b>green</b> — the two paths already differ, so the prefix cannot change that.**
    ///
    /// **So it tested "the two paths differ", not "the slow path's line looks like this".**
    ///
    /// **A golden hash is the natural choice for such a function: a fingerprint is supposed to be deterministic, and the
    /// strongest statement of determinism is "it equals this value". Any character in the text moves the hash.**
    ///
    /// **The cost is re-verifying on a new platform if the `"R"` float rendering ever differs between runtimes — acceptable,
    /// because "the rendering changed" is exactly what this criterion most needs to report.**
    /// </remarks>
    [Fact]
    public void TheSlowPath_ShouldMatchItsGoldenHash() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");
        var columns = new Dictionary<VariableItemKey, int> {
            [A.Key] = 0,
            [R.Key] = 1,
            [AbsInput.Key] = 2,
            [AbsResult.Key] = 3,
        };

        AuditFingerprint slow = NativeFingerprint.Compute(
            previous, columns,
            new[] { Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) },
            new[] { Abs() },
            Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
            Array.Empty<BinaryLogicStructure>(), "gurobi").Value;

        slow.SchemaVersion.Should().Be("gurobi-pwl-abs-1");
        slow.Value.Should().Be(GOLDEN);
    }

    /// <summary>
    /// **半连续边界必须<b>严格为正</b>，`0` 被拒** / Semi-continuous bounds must be <b>strictly positive</b>; zero is rejected.
    /// </summary>
    /// <remarks>
    /// **`lower <= 0` 被拒，不是 `< 0`：** **半连续的下界必须为正，
    /// 否则 `0` 与"未启用"分不开。**
    ///
    /// **判据测 `0`（被拒）、负数（被拒）、`lower > upper`（被拒）与合法值（通过）。**
    ///
    /// **`lower <= 0` is rejected, not `< 0`: the lower bound must be positive, otherwise zero is indistinguishable from
    /// "not enabled".**
    ///
    /// **The criterion covers `0`, a negative value, `lower > upper`, and a valid pair.**
    /// </remarks>
    [Fact]
    public void SemiBounds_ShouldBeStrictlyPositive() {
        foreach (double lower in new[] { 0.0, -1.0 }) {
            ComputeWithSemi(SemiOf(lower: lower, upper: 10.0))
                .Should().BeOfType<Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>>(
                    $"a lower bound of {lower} is not usable");
        }

        ComputeWithSemi(SemiOf(lower: 5.0, upper: 3.0))
            .Should().BeOfType<Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>>(
                "the lower bound must not exceed the upper one");

        ComputeWithSemi(SemiOf(lower: 1.0, upper: 3.0))
            .Should().BeOfType<Ok<AuditFingerprint, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **MAX 的输入在渲染前先按列号排序，所以项的顺序不影响指纹** /
    /// MAX terms are sorted by column before rendering, so term order does not matter.
    /// </summary>
    [Fact]
    public void MaxTermOrder_ShouldNotAffectTheFingerprint() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");
        var columns = new Dictionary<VariableItemKey, int> {
            [A.Key] = 0, [B.Key] = 2, [MaxResult.Key] = 1,
            [AbsInput.Key] = 3, [AbsResult.Key] = 4,
        };

        string first = NativeFingerprint.Compute(
            previous, columns, Array.Empty<NormalizedPiecewise>(),
            new[] { Abs() },
            new[] { MaxOf(reverse: false) }, Array.Empty<SemiStructure>(),
            Array.Empty<BinaryLogicStructure>(), "gurobi").Value.Value;

        string second = NativeFingerprint.Compute(
            previous, columns, Array.Empty<NormalizedPiecewise>(),
            new[] { Abs() },
            new[] { MaxOf(reverse: true) }, Array.Empty<SemiStructure>(),
            Array.Empty<BinaryLogicStructure>(), "gurobi").Value.Value;

        first.Should().Be(second, "the terms are sorted by column before rendering");
    }

    /// <summary>
    /// **缺列时被拒，且按种类给出不同的消息** / Missing columns are rejected with per-kind messages.
    /// </summary>
    [Fact]
    public void MissingColumns_ShouldBeReportedPerKind() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");

        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativeFingerprint.Compute(
                previous, new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedPiecewise>(), new[] { Abs() },
                Array.Empty<NativeMaxData>(), Array.Empty<SemiStructure>(),
                Array.Empty<BinaryLogicStructure>(), "gurobi"))
            .Error.Message.Should().Contain("ABS input column is missing");

        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativeFingerprint.Compute(
                previous, new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedPiecewise>(), Array.Empty<NormalizedAbs>(),
                new[] { MaxOf() }, Array.Empty<SemiStructure>(),
                Array.Empty<BinaryLogicStructure>(), "gurobi"))
            .Error.Message.Should().Contain("MAX result column is missing");

        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativeFingerprint.Compute(
                previous, new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedPiecewise>(), Array.Empty<NormalizedAbs>(),
                Array.Empty<NativeMaxData>(), new[] { SemiOf(lower: 1.0, upper: 3.0) },
                Array.Empty<BinaryLogicStructure>(), "gurobi"))
            .Error.Message.Should().Contain("Semi-continuous result column is missing");
    }

    /// <summary>
    /// **指示器与遮盖<b>不在本签名里</b>，所以它们的 schema 分支也不在** /
    /// Indicators and maskings are <b>absent from this signature</b>, so their schema branches are absent too.
    /// </summary>
    /// <remarks>
    /// **这是一条<b>缺口判据</b>：它断言的是一个"不存在"。**
    ///
    /// **阶梯有十三个分支，这里只有五个可达。另外八个
    /// （`masking-1`、`masking-2`、`indicator-2..8`）的前提是指示器或遮盖非空，
    /// 而这两个列表在本签名下不存在。**
    ///
    /// **所以那八个分支是<b>缺的</b>，不是被并进别的分支。**
    /// **判据枚举本实现能产出的全部 schema，断言里面<b>没有</b>任何指示器或遮盖字样——**
    /// **这样将来如果把某个指示器结构错并进 `max-1` 之类，判据会红。**
    ///
    /// **A <b>gap criterion</b>: it asserts an absence.**
    ///
 /// **The implementation ladder has thirteen branches; five are reachable here.** **The other eight presuppose indicators or
    /// maskings, which this signature does not carry.**
    ///
    /// **So those eight are <b>missing</b>, not folded in.** **The criterion enumerates every schema this implementation can
    /// produce and asserts none mentions indicators or masking — so if a later change quietly folds one into `max-1` or the
    /// like, it fails.**
    /// </remarks>
    [Fact]
    public void TheUnportedSchemaBranches_ShouldBeAbsentNotFolded() {
        var produced = new List<string> {
            Empty(),
            Only(piecewise: new[] { Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) }),
            Only(piecewise: new[] { Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) },
                maxima: new[] { MaxOf() }),
            Only(piecewise: new[] { Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) },
                semis: new[] { SemiOf(lower: 1.0, upper: 3.0) }),
            Only(piecewise: new[] { Piecewise(A, R, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) },
                logic: new[] { LogicOf() }),
        };

        produced.Should().OnlyHaveUniqueItems("each combination picks exactly one branch");
        produced.Should().NotContain(
            schema => schema.Contains("indicator", StringComparison.Ordinal)
                || schema.Contains("masking", StringComparison.Ordinal),
            "those two kinds are not ported, so their branches must not appear at all");

        produced.Should().BeEquivalentTo(new[] {
            "gurobi-abs-1",
            "gurobi-pwl-abs-1",
            "gurobi-functions-max-1",
            "gurobi-functions-semi-1",
            "gurobi-functions-binary-logic-1",
        }, "exactly the five reachable branches, with the other eight missing");
    }

    private static string Empty() => Only();

    private static string Only(
        NormalizedPiecewise[]? piecewise = null,
        NativeMaxData[]? maxima = null,
        SemiStructure[]? semis = null,
        BinaryLogicStructure[]? logic = null) =>
        NativeFingerprint.Schema(
            piecewise ?? Array.Empty<NormalizedPiecewise>(),
            maxima ?? Array.Empty<NativeMaxData>(),
            semis ?? Array.Empty<SemiStructure>(),
            logic ?? Array.Empty<BinaryLogicStructure>(),
            "gurobi");

    private static Result<AuditFingerprint, ErrorCode, Error<ErrorCode>> ComputeWithSemi(
        SemiStructure semi) =>
        NativeFingerprint.Compute(
            new AuditFingerprint("1.0", "sha256", "seed"),
            new Dictionary<VariableItemKey, int> {
                [SemiResult.Key] = 0,
                [SemiIndicator.Key] = 1,
                [A.Key] = 2,
                [AbsInput.Key] = 3,
                [AbsResult.Key] = 4,
            },
            Array.Empty<NormalizedPiecewise>(),
            new[] { Abs() },
            Array.Empty<NativeMaxData>(),
            new[] { semi },
            Array.Empty<BinaryLogicStructure>(),
            "gurobi");

    private static SemiStructure SemiOf(double lower, double upper = 10.0) =>
        new(LowerBound: new Flt64(lower), UpperBound: new Flt64(upper),
            ResultVariable: SemiResult, IndicatorVariable: SemiIndicator,
            Converter: IIntoValue<Flt64>.Identity, Name: "semi");

    private static NativeMaxData MaxOf(bool reverse = false) {
        var left = new Dictionary<VariableItemKey, double> { [A.Key] = 1.0 };
        var right = new Dictionary<VariableItemKey, double> { [B.Key] = 2.0 };

        return new NativeMaxData(
            Inputs: reverse
                ? new[] {
                    new NativeMaxInputData(right, 0.0, 0.0, 10.0),
                    new NativeMaxInputData(left, 1.0, 0.0, 10.0),
                }
                : new[] {
                    new NativeMaxInputData(left, 1.0, 0.0, 10.0),
                    new NativeMaxInputData(right, 0.0, 0.0, 10.0),
                },
            ResultKey: MaxResult.Key,
            SelectorKeys: new[] { MaxSel1.Key, MaxSel2.Key },
            BigMValues: new[] { 100.0, 100.0 },
            Name: "max");
    }

    private static BinaryLogicStructure LogicOf() =>
        new(Operation: BinaryLogicOperation.And,
            Inputs: new IVariableItem[] { A, B },
            ResultVariable: LogicResult,
            Converter: IIntoValue<Flt64>.Identity,
            Name: "logic");

    private static NormalizedAbs Abs() =>
        new(AbsInput.Key, AbsResult.Key, P.Key, N.Key, S.Key);

    private static NormalizedPiecewise Piecewise(
        IVariableItem input, IVariableItem result, double[] breakpoints, double[] values) {
        var points = new Flt64[breakpoints.Length];
        for (int index = 0; index < breakpoints.Length; ++index) {
            points[index] = new Flt64(breakpoints[index]);
        }

        var ys = new Flt64[values.Length];
        for (int index = 0; index < values.Length; ++index) {
            ys[index] = new Flt64(values[index]);
        }

        return new NormalizedPiecewise(
            InputKey: input.Key, ResultKey: result.Key,
            SelectorKeys: new[] { Sel.Key },
            Breakpoints: points, Values: ys);
    }

    private const string GOLDEN =
        "1dc5d89d1acce00a0ced8b610d3eeabbbeaa669a0b3aa9f145582fe40a2df2f6";

    private static readonly RealVar A = new("nf-a");

    private static readonly RealVar B = new("nf-b");

    private static readonly RealVar R = new("nf-r");

    private static readonly RealVar AbsInput = new("nf-ai");

    private static readonly RealVar AbsResult = new("nf-ar");

    private static readonly RealVar P = new("nf-p");

    private static readonly RealVar N = new("nf-n");

    private static readonly BinVar S = new("nf-s");

    private static readonly BinVar Sel = new("nf-sel");

    private static readonly RealVar MaxResult = new("nf-maxr");

    private static readonly BinVar MaxSel1 = new("nf-msel1");

    private static readonly BinVar MaxSel2 = new("nf-msel2");

    private static readonly RealVar SemiResult = new("nf-semir");

    private static readonly BinVar SemiIndicator = new("nf-semiind");

    private static readonly BinVar LogicResult = new("nf-logicr");
}
