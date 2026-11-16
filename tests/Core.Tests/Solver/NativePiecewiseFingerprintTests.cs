#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// PWL 指纹：描述的是集合，不是列表 / It describes a set, not a list.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `piecewiseFingerprint` 与 `numberListText`。
///
/// Adds </remarks>
public class NativePiecewiseFingerprintTests
{
    /// <summary>
    /// **顺序不影响指纹** / Order does not affect the fingerprint.
    /// </summary>
    /// <remarks>
    /// **这是这里最有内容的一条。**
    ///
    /// **行在拼之前被排序，所以同一个结构集合无论以什么顺序给出，指纹相同。**
    ///
    /// **这不是"顺手排一下"——它是"指纹描述的是<b>集合</b>而不是<b>列表</b>"这个语义的直接体现。**
    /// **不排序的话，一个仅仅顺序不同的模型会得到不同的指纹，
    /// 而审计指纹的用途恰恰是"这两个模型是不是同一个"。**
    ///
    /// **判据用两个只差顺序的结构列表，断言指纹相同。**
    /// **如果实现漏掉排序，两次的中间文本不同，指纹必然不同。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **Lines are sorted before joining, so the same set of structures fingerprints identically in any order.**
    ///
    /// **That is not an incidental sort — it is the semantics "the fingerprint describes a <b>set</b>, not a <b>list</b>".**
    /// **Without it, a model differing only in order fingerprints differently, and the whole point of an audit fingerprint
    /// is "are these two models the same one".**
    ///
    /// **The criterion uses two lists differing only in order and asserts the fingerprints match.** **Without the sort the
    /// intermediate text differs and so must the fingerprint.**
    /// </remarks>
    [Fact]
    public void Order_ShouldNotAffectTheFingerprint() {
        NormalizedPiecewise first = Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });
        NormalizedPiecewise second = Piecewise(B, R2, new[] { 0.0, 4.0 }, new[] { 1.0, 9.0 });

        var columns = new Dictionary<VariableItemKey, int> {
            [A.Key] = 0, [R1.Key] = 1, [B.Key] = 2, [R2.Key] = 3,
        };

        string forward = Compute(new[] { first, second }, columns);
        string backward = Compute(new[] { second, first }, columns);

        forward.Should().Be(backward, "the fingerprint describes a set, so order must not matter");
    }

    /// <summary>
    /// **空列表不加换行，所以它就是上一阶段指纹的哈希** /
    /// An empty list adds no newline, so it is the hash of the previous fingerprint alone.
    /// </summary>
    /// <remarks>
    /// **基线的 `append(fingerprint.value)` 之后是 `if (structureLines.isNotEmpty) { append('\n');... }`。**
    ///
    /// **所以"没有 PWL 结构"时的内容就是上一阶段的指纹本身，而不是"上一阶段指纹 + 一个换行"。**
    ///
    /// **判据直接把这个等式写出来：** **空列表的结果必须<b>等于</b>
    /// `Sha256(previous.Value, "{backend}-pwl-1")`。**
    ///
    /// **如果实现无条件加一个换行，两者不同，判据变红。**
    ///
    /// **The baseline does `append(fingerprint.value)` then `if (structureLines.isNotEmpty)`.** **So "no PWL structures"
    /// hashes the previous fingerprint itself, not "that plus a newline".**
    ///
    /// **The criterion writes that identity out: an empty list must equal `Sha256(previous.Value, "{backend}-pwl-1")`.**
    /// **An unconditional newline makes the two differ and the criterion fails.**
    /// </remarks>
    [Fact]
    public void AnEmptyList_ShouldHashTheBareFingerprint() {
        var previous = new AuditFingerprint("1.0", "sha256", "abc123");
        var columns = new Dictionary<VariableItemKey, int>();

        AuditFingerprint empty = NativePiecewiseFingerprint
            .Compute(previous, columns, Array.Empty<NormalizedPiecewise>(), "gurobi").Value;

        AuditFingerprint expected = SolveFingerprinting.Sha256(previous.Value, "gurobi-pwl-1");

        empty.Value.Should().Be(
            expected.Value, "an empty list contributes nothing, not even a newline");
        empty.SchemaVersion.Should().Be("gurobi-pwl-1");
    }

    /// <summary>
    /// **不同的断点或值给出不同的指纹** / Different breakpoints or values give different fingerprints.
    /// </summary>
    [Fact]
    public void DifferentContents_ShouldFingerprintDifferently() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [R1.Key] = 1 };

        string baseline = Compute(
            new[] { Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) }, columns, previous);
        string otherValues = Compute(
            new[] { Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 21.0 }) }, columns, previous);
        string otherBreakpoints = Compute(
            new[] { Piecewise(A, R1, new[] { 0.0, 11.0 }, new[] { 0.0, 20.0 }) }, columns, previous);

        baseline.Should().NotBe(otherValues, "a different value list is a different model");
        baseline.Should().NotBe(otherBreakpoints, "different breakpoints are a different model");
    }

    /// <summary>
    /// **上一阶段的指纹参与哈希，所以链条是连起来的** /
    /// The previous fingerprint is hashed in, so the chain is linked.
    /// </summary>
    [Fact]
    public void ThePreviousFingerprint_ShouldBePartOfTheHash() {
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [R1.Key] = 1 };
        NormalizedPiecewise structure =
            Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });

        string first = Compute(
            new[] { structure }, columns, new AuditFingerprint("1.0", "sha256", "one"));
        string second = Compute(
            new[] { structure }, columns, new AuditFingerprint("1.0", "sha256", "two"));

        first.Should().NotBe(second, "the same structures on different seeds must differ");
    }

    /// <summary>
    /// **非有限值被拒** / A non-finite value is rejected.
    /// </summary>
    /// <remarks>
    /// **一个 `NaN` 或无穷会让"同一个模型"的指纹取决于它被创建时的具体数值，
    /// 而那是不可复现的——而指纹的全部意义就是可复现。**
    ///
    /// **A `NaN` or infinity would make the fingerprint depend on a value at creation time, which is unreproducible — and
    /// reproducibility is the entire point of a fingerprint.**
    /// </remarks>
    [Fact]
    public void NonFiniteValues_ShouldBeRejected() {
        Result<string, ErrorCode, Error<ErrorCode>> result =
            NativePiecewiseFingerprint.NumberListText(
                new[] { new Flt64(1.0), new Flt64(double.NaN) },
                "native.pwl.fingerprint.values");

        result.Should().BeOfType<Failed<string, ErrorCode, Error<ErrorCode>>>();
        ((Failed<string, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .Contain("must be finite");
        ((Failed<string, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .Contain("[1]", "the message must name which element");
    }

    /// <summary>
    /// **渲染与当前区域设置无关** / Rendering is independent of the current culture.
    /// </summary>
    /// <remarks>
    /// **这是一条<b>必须</b>有的判据：** **在小数点用逗号的文化下，
    /// 渲染出来的列表会与逗号分隔符混淆，指纹也就随机器而变。**
    ///
    /// **判据在 `de-DE` 下跑一遍，断言结果与不变量文化下相同。**
    ///
    /// **A <b>necessary</b> criterion**: **under a comma-decimal culture the rendered list would clash with the comma
    /// separator, making the fingerprint machine-dependent.**
    ///
    /// **The criterion runs under `de-DE` and asserts the result matches the invariant culture.**
    /// </remarks>
    [Fact]
    public void Rendering_ShouldBeCultureInvariant() {
        string invariant = NativePiecewiseFingerprint.NumberListText(
            new[] { new Flt64(1.5), new Flt64(2.25) }, "field").Value;

        CultureInfo previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string german = NativePiecewiseFingerprint.NumberListText(
                new[] { new Flt64(1.5), new Flt64(2.25) }, "field").Value;

            german.Should().Be(
                invariant, "a comma-decimal culture must not change the rendered fingerprint text");
            german.Should().Be("1.5,2.25");
        }
        finally {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// **缺列时被拒** / A missing column is rejected.
    /// </summary>
    [Fact]
    public void MissingColumns_ShouldBeRejected() {
        var previous = new AuditFingerprint("1.0", "sha256", "seed");
        NormalizedPiecewise structure =
            Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });

        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativePiecewiseFingerprint.Compute(
                previous,
                new Dictionary<VariableItemKey, int> { [R1.Key] = 1 },
                new[] { structure }, "gurobi"))
            .Error.Message.Should().Contain("input column is missing");

        ((Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>)
            NativePiecewiseFingerprint.Compute(
                previous,
                new Dictionary<VariableItemKey, int> { [A.Key] = 0 },
                new[] { structure }, "gurobi"))
            .Error.Message.Should().Contain("result column is missing");
    }

    /// <summary>
    /// **后端名只进 schema 版本，<b>不进摘要</b>** / The backend name enters the schema version but <b>not</b> the digest.
    /// </summary>
    /// <remarks>
    /// **我原来的判据是"不同后端给出不同指纹"，一跑就红了。**
    ///
    /// **查下来：`SolveFingerprinting.Sha256` 只哈希<b>内容</b>，
    /// `schemaVersion` 只是随返回值带出去的元数据。**
    ///
    /// **所以 `gurobi` 与 `cplex` 在同样的结构上给出相同的 `Value`，但 `SchemaVersion` 是
    /// `gurobi-pwl-1` 与 `cplex-pwl-1`。**
    ///
    /// **这是一条值得写下来的事实，因为它决定"怎么比较两个指纹"：**
    /// **只比 `Value` 的调用方会认为两个后端的指纹相同——
    /// 而它们在"同一个模型"这个意义上确实相同，区别只在于"哪个后端算的"。**
    ///
    /// **所以这不是缺陷，而是"摘要回答模型是否相同，schema 版本回答是谁算的"这个分工。
    /// 判据把两件事分开断言。**
    ///
    /// **My original criterion said "different backends give different fingerprints" and it failed.**
    ///
    /// **What it found: `SolveFingerprinting.Sha256` hashes <b>content</b> only; `schemaVersion` is metadata carried along
    /// with the result.**
    ///
    /// **So `gurobi` and `cplex` give the same `Value` on the same structures while their `SchemaVersion`s are
    /// `gurobi-pwl-1` and `cplex-pwl-1`.**
    ///
    /// **Worth recording because it decides how to compare two fingerprints: a caller comparing only `Value` concludes the
    /// two backends match — and they do in the "same model" sense; the difference is only "who computed it".**
    ///
    /// **Not a defect but a division of labour: the digest answers whether the models match, the schema version answers who
    /// computed it. The criterion asserts the two facts separately.**
    /// </remarks>
    [Fact]
    public void TheBackendName_ShouldEnterOnlyTheSchemaVersion() {
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [R1.Key] = 1 };
        NormalizedPiecewise structure =
            Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });
        var previous = new AuditFingerprint("1.0", "sha256", "seed");

        AuditFingerprint gurobi = NativePiecewiseFingerprint
            .Compute(previous, columns, new[] { structure }, "gurobi").Value;
        AuditFingerprint cplex = NativePiecewiseFingerprint
            .Compute(previous, columns, new[] { structure }, "cplex").Value;

        gurobi.Value.Should().Be(
            cplex.Value, "the schema version is not part of the hashed content");
        gurobi.SchemaVersion.Should().Be("gurobi-pwl-1");
        cplex.SchemaVersion.Should().Be("cplex-pwl-1");
    }

    /// <summary>
    /// **本实现<b>不</b>声称与 Kotlin 的指纹字节一致** /
    /// This implementation does <b>not</b> claim byte-identical fingerprints with Kotlin.
    /// </summary>
    /// <remarks>
    /// **这是一条<b>反声明</b>，写在这里是为了防止将来有人把它当成既成事实。**
    ///
    /// **指纹的文本是 `Double.toString`（Kotlin）与 `"R"` 格式（.NET）之间的差距：**
    /// **两边都是"最短可往返表示"，但指数写法与末尾零的处理<b>不保证</b>逐字符相同。**
    ///
    /// **所以"同一个模型在两侧得到同一个指纹"这件事<b>没有被测量过</b>，
    /// 而本方向的原则是不宣称未实测的跨语言互通。**
    ///
    /// **判据只断言"本实现自洽"：同样的输入在同一侧给出同样的指纹。
    /// 跨语言的字节一致需要一次真实的两侧对照实验，而那不是这里做的事。**
    ///
    /// **A <b>counter-claim</b>, written so that nobody later treats it as established fact.**
    ///
    /// **The fingerprint text differs between `Double.toString` (Kotlin) and the `"R"` format (.NET): both are shortest
    /// round-trip, but exponent formatting and trailing zeros are <b>not</b> guaranteed to match character by character.**
    ///
    /// **So "the same model fingerprints identically on both sides" has <b>not been measured</b>, and this direction does not
    /// claim unmeasured cross-language interop.**
    ///
    /// **The criterion only asserts self-consistency on this side; byte equality across languages would need a real
    /// two-sided experiment, which not do.**
    /// </remarks>
    [Fact]
    public void Fingerprints_ShouldBeSelfConsistentButNotClaimedCrossLanguage() {
        var columns = new Dictionary<VariableItemKey, int> { [A.Key] = 0, [R1.Key] = 1 };
        NormalizedPiecewise structure =
            Piecewise(A, R1, new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 });

        string once = Compute(new[] { structure }, columns);
        string twice = Compute(new[] { structure }, columns);

        once.Should().Be(twice, "the same input on this side always gives the same fingerprint");
        once.Should().MatchRegex(
            "^[0-9a-f]+$", "it is a hex digest; its agreement with another language's digest is a separate experiment");
    }

    private static string Compute(
        NormalizedPiecewise[] structures,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        AuditFingerprint? previous = null,
        string backend = "gurobi") =>
        NativePiecewiseFingerprint
            .Compute(
                previous ?? new AuditFingerprint("1.0", "sha256", "seed"),
                columns, structures, backend)
            .Value.Value;

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
            SelectorKeys: new[] { Selector1.Key },
            Breakpoints: points, Values: ys);
    }

    private static readonly RealVar A = new("fp-a");

    private static readonly RealVar B = new("fp-b");

    private static readonly RealVar R1 = new("fp-r1");

    private static readonly RealVar R2 = new("fp-r2");

    private static readonly BinVar Selector1 = new("fp-sel1");
}
