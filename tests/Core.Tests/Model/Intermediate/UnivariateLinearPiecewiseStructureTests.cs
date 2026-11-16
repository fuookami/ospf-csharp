#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 分段线性：区域末端的排他索引，与"显式 M 不需要范围证明" /
/// The region's exclusive end, and "an explicit M needs no range proof".
/// </summary>
/// <remarks>
/// B1191 按基线 `FunctionExpansion.kt` 的 `DeferredFunctionConstraintRegion`、
/// `UnivariateLinearPiecewiseStructure`（限 `ValidateFallbackInputBounds`）与
/// `DeferredFunctionFallback.kt` 的 `capturePwlInputBounds` 补。
///
/// adds
/// `UnivariateLinearPiecewiseStructure`, and `capturePwlInputBounds`.
/// </remarks>
public class UnivariateLinearPiecewiseStructureTests
{
    /// <summary>
    /// **区域末端是排他索引** / The region end is exclusive.
    /// </summary>
    /// <remarks>
    /// **判据钉住不变量 `Last − First == Count`，而不是某个具体数值**：
    /// **数值会随 `First` 一起变，于是错误的公式看起来仍然成立。**
    ///
    /// **The criterion pins `Last − First == Count` rather than a concrete number**: **a number moves with `First`, so a
    /// wrong formula would still look right.**
    /// </remarks>
    [Fact]
    public void TheRegionEnd_ShouldBeExclusive()
    {
        var region = new DeferredFunctionConstraintRegion(Structure(), 2, 3);

        region.FirstConstraintIndex.Should().Be(2);
        region.LastConstraintIndex.Should().Be(5);
        (region.LastConstraintIndex - region.FirstConstraintIndex).Should().Be(
            region.ConstraintCount, "the end is exclusive, so the difference is the count");
        (region.LastConstraintIndex - 1).Should().Be(4, "the final row sits one before the end");

        // **空区域：末端等于起点。**
        // **An empty region: the end equals the start.**
        var empty = new DeferredFunctionConstraintRegion(Structure(), 7, 0);
        empty.LastConstraintIndex.Should().Be(
            7, "an empty region ends where it starts, not one before");
    }

    /// <summary>
    /// **给了显式 M 时，缺转换器与缺证明都不算错** / With an explicit M, a missing converter and proof are fine.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **自动 M 从输入范围推出，所以它必须证明"范围没有被后续约束放宽"。**
    /// **显式 M 是调用方给的数，与输入范围无关，也就不需要这份证明。**
    ///
    /// **"不需要"不等于"跳过检查"：它意味着后面三步的前提都不成立，
    /// 所以那个分岔必须在<b>最前面</b>。**
    ///
    /// **判据构造一个既没有转换器、也没有证明、但给了显式 M 的快照——它必须通过。**
    /// **把分岔挪到后面，这一例立刻变红。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **The automatic M is derived from the input range and must prove the range was not widened later; an explicit M
    /// is independent of it and needs no such proof.**
    ///
    /// **"Not needed" is not "skipped": it means the premises of the next three steps do not hold, so the fork has to
    /// come <b>first</b>.**
    ///
    /// **The criterion builds a snapshot with no converter, no proof, and an explicit M — it must pass.** **Moving the
    /// fork later turns this case red immediately.**
    /// </remarks>
    [Fact]
    public void TheExplicitM_ShouldNeedNeitherConverterNorProof()
    {
        var explicitOnly = new UnivariateLinearPiecewiseStructure(
            Input: Input(0.0, 10.0), Breakpoints: new[] { new Flt64(5.0) },
            Slopes: new[] { Flt64.One }, Intercepts: new[] { Flt64.Zero },
            ResultVariable: Result, SelectorVariables: new[] { Selector }) {
            ExplicitM = new Flt64(1000.0),
            Converter = null,
            CapturedInputBounds = null,
        };

        explicitOnly.ValidateFallbackInputBounds().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "an explicit M makes the whole range proof unnecessary");
    }

    /// <summary>**缺转换器的消息与缺证明的消息不同** / The two absences report different messages.</summary>
    /// <remarks>
    /// **两条都是 `IllegalArgument`。如果它们共用一条消息，
    /// 调用方就分不出"我忘了传转换器"与"我没捕获证明"。**
    ///
    /// **Both are `IllegalArgument`. A shared message would leave the caller unable to tell "I forgot the converter" from
    /// "I did not capture the proof".**
    /// </remarks>
    [Fact]
    public void TheTwoAbsences_ShouldSpeakForThemselves()
    {
        UnivariateLinearPiecewiseStructure noConverter = Bare() with {
            Converter = null, CapturedInputBounds = Captured(0.0, 10.0),
        };

        Result<Success, ErrorCode, Error<ErrorCode>> first =
            noConverter.ValidateFallbackInputBounds();
        first.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)first).Error.Message.Should().Contain(
            "no value converter");

        UnivariateLinearPiecewiseStructure noProof = Bare() with {
            Converter = IIntoValue<Flt64>.Identity, CapturedInputBounds = null,
        };

        Result<Success, ErrorCode, Error<ErrorCode>> second =
            noProof.ValidateFallbackInputBounds();
        second.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)second).Error.Message.Should().Contain(
            "no input bounds proof");
    }

    /// <summary>
    /// **捕获本身失败时，那条错误被原样传出** / A failed capture forwards its own error.
    /// </summary>
    /// <remarks>
    /// **"捕获失败"与"没有捕获"是两件事：** **前者说明当初的推导就出错了，
    /// 把它折成"没有证明"会让原因消失。**
    ///
    /// **"The capture failed" and "no capture" are two things**: **the former says the original derivation already
    /// failed, and folding it into "no proof" loses the cause.**
    /// </remarks>
    [Fact]
    public void TheFailedCapture_ShouldForwardItsMessage()
    {
        UnivariateLinearPiecewiseStructure structure = Bare() with {
            Converter = IIntoValue<Flt64>.Identity,
            CapturedInputBounds =
                new Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument, "the original capture already failed"),
        };

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            structure.ValidateFallbackInputBounds();

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain(
            "the original capture already failed");
    }

    /// <summary>**收缩可以、扩大不行** / Shrinking passes, growing fails.</summary>
    [Fact]
    public void TheBoundsDirection_ShouldMatchTheMaxStructure()
    {
        UnivariateLinearPiecewiseStructure shrunk = Bare() with {
            Converter = IIntoValue<Flt64>.Identity, CapturedInputBounds = Captured(-10.0, 10.0),
        };

        // **输入常量为 5，范围是 `[5, 5]`，落在 `[−10, 10]` 内。**
        // **The input is the constant five, so its range is `[5, 5]` inside `[−10, 10]`.**
        shrunk.ValidateFallbackInputBounds().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        UnivariateLinearPiecewiseStructure grown = Bare() with {
            Input = Input(11.0, 0.0),
            Converter = IIntoValue<Flt64>.Identity, CapturedInputBounds = Captured(0.0, 10.0),
        };

        // **常量 `11` 越过了捕获的上界 `10`。**
        // **我这第一版写的是"常量 −5 越过下界"，而 `Bare()` 的输入常量其实是 `0`——
        // 于是它落在 `[0, 10]` 内、判据变红。红的原因是我的输入选错。**
        //
        // **The constant `11` crosses the captured edge at `10`.**
        // **My first version claimed "the constant −5 crosses the lower edge" while `Bare()`'s constant is actually `0`
        // — inside `[0, 10]`, so the criterion reddened. Over my input again.**
        grown.ValidateFallbackInputBounds().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "eleven is above the captured upper bound");
    }

    /// <summary>
    /// **捕获助手的第一层可以打出，第二层经 `FiniteBounds` 不可达** /
    /// The first capture check is reachable; the second is not, through `FiniteBounds`.
    /// </summary>
    /// <remarks>
    /// **这是本方向第四条"不可测"发现，记在这里而不是假装覆盖了。**
    ///
    /// **两层检查：**
    ///
    /// 1. **范围<b>存在</b>**（`FiniteBounds` 返回 `null`）
    /// 2. **范围<b>可用</b>**（两端都是有限可编码值）
    ///
    /// **第 1 层可达**：**一个无穷常量、或者一个会让乘积溢出的系数，都让 `FiniteBounds` 返回 `null`。**
    ///
    /// **第 2 层不可达**：**我试了 `1e15` 到 `1e300` 的常量、以及 `1e100` 到 `1e308` 的系数，
    /// 凡是 `FiniteBounds` 返回非 `null` 的，`IsUsable` 都判为可用；凡是不可以的，`FiniteBounds` 自己就返回了 `null`。**
    ///
    /// **也就是说 `FiniteBounds` 已经把非有限值过滤掉了，第 2 层在当前实现下是<b>防御性</b>的。**
    /// **保留它是对的（`FiniteBounds` 的行为不是本函数的契约），但判据不该假装能测到它。**
    ///
    /// **This is the direction's fourth "untestable" finding, recorded rather than papered over.**
    ///
    /// **Layer 1 is reachable**: **an infinite constant, or a coefficient whose product overflows, makes
    /// `FiniteBounds` return `null`.**
    ///
    /// **Layer 2 is not**: **I tried constants from `1e15` to `1e300` and coefficients from `1e100` to `1e308`; whenever
    /// `FiniteBounds` returned non-null, `IsUsable` accepted it, and whenever it would not, `FiniteBounds` itself
    /// returned null.**
    ///
    /// **So `FiniteBounds` already filters non-finite values and layer 2 is <b>defensive</b> under the current
    /// implementation.** **Keeping it is right (`FiniteBounds`'s behaviour is not this function's contract), but a
    /// criterion must not pretend to reach it.**
    /// </remarks>
    [Fact]
    public void TheCapture_ShouldReachTheFirstCheckOnly()
    {
        PiecewiseInputBounds.Capture(
            Input(0.0, 10.0), IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>>();

        // **无穷常量：`FiniteBounds` 返回 `null`。**
        // **An infinite constant: `FiniteBounds` returns null.**
        Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> infinite =
            PiecewiseInputBounds.Capture(
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), new Flt64(double.PositiveInfinity)),
                IIntoValue<Flt64>.Identity);

        infinite.Should().BeOfType<
            Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>)infinite)
            .Error.Message.Should().Contain("no provable finite input range");

        // **溢出系数：同样落到第一层。**
        // **An overflowing coefficient: the same first layer.**
        PiecewiseInputBounds.Capture(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1e300), FreeVariable) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>>();

        // **而自由变量本身有默认界（约 `±4.5e15`），所以它是"有范围且可用"的一例。**
        // **A free variable has default bounds (about `±4.5e15`), so it is an example of "present and usable".**
        PiecewiseInputBounds.Capture(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, FreeVariable) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity).Should()
            .BeOfType<Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**PWL 已经接入分派表** / PWL is wired into the dispatch table.</summary>
    [Fact]
    public void ThePiecewise_ShouldBeDispatched()
    {
        UnivariateLinearPiecewiseStructure structure = Structure();

        structure.ResultVariableOrNull().Should().BeSameAs(Result);
        structure.InputOrNull().Should().BeSameAs(structure.Input);
        structure.InputPolynomialsOrEmpty().Should().HaveCount(1);
        structure.HelperVariablesOrEmpty().Should().HaveCount(1);
        structure.SupportsDeferredFallback().Should().BeTrue();
    }

    private static UnivariateLinearPiecewiseStructure Bare() =>
        new(Input: Input(0.0, 5.0), Breakpoints: new[] { new Flt64(5.0) },
            Slopes: new[] { Flt64.One }, Intercepts: new[] { Flt64.Zero },
            ResultVariable: Result, SelectorVariables: new[] { Selector });

    private static UnivariateLinearPiecewiseStructure Structure() =>
        Bare() with {
            Converter = IIntoValue<Flt64>.Identity, CapturedInputBounds = Captured(0.0, 10.0),
        };

    private static Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> Captured(
        double lower, double upper) =>
        new Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(
            new LinearPolynomialBounds<Flt64>(new Flt64(lower), new Flt64(upper)));

    /// <summary>一个常数多项式 / A constant polynomial.</summary>
    private static LinearPolynomial<Flt64> Input(double value, double ignored) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    private static readonly RealVar Result = new("pwl-result");

    private static readonly BinVar Selector = new("pwl-sel");

    private static readonly RealVar FreeVariable = new("pwl-free");

    // ===== B1199：行生成 =====

    /// <summary>
    /// **`1 + 4n` 行，名字与族** / `1 + 4n` rows, names and families.
    /// </summary>
    [Fact]
    public void TheRows_ShouldBeOnePlusFourPerSegment() {
        IReadOnlyList<LinearInequality<Flt64>> rows = Generated(segments: 2);

        rows.Should().HaveCount(9, "one select row plus four per segment");
        rows[0].Name.Should().Be("piecewise_select_one");
        rows[0].Comparison.Should().Be(Comparison.EQ);

        rows[1].Name.Should().Be("piecewise_seg_0_lb");
        rows[2].Name.Should().Be("piecewise_seg_0_ub");
        rows[3].Name.Should().Be("piecewise_seg_0_eq_ub");
        rows[4].Name.Should().Be("piecewise_seg_0_eq_lb");
        rows[5].Name.Should().Be("piecewise_seg_1_lb");

        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[3].Comparison.Should().Be(Comparison.LE);
        rows[4].Comparison.Should().Be(Comparison.GE);
    }

    /// <summary>
    /// **被选中的那一段给出正确的值，未选中的段被松弛推开** /
    /// The selected segment gives the right value; unselected ones are relaxed away.
    /// </summary>
    /// <remarks>
    /// **两段：`[0, 5]` 上 `slope 1, intercept 0`，`[5, 10]` 上 `slope 0, intercept 5`。
    /// 所以 `x = 7` 时正确值是 `5`。**
    ///
    /// **`sel = (0, 1)`（选中第二段）、`x = 7`、`result = 5` 必须可行。**
    /// **而 `x = 7` 落在第二段外（第一段）时有 `sel = (1, 0)` 必须<b>不可行</b>。**
    ///
    /// **Two segments: slope one over `[0, 5]`, then slope zero at five over `[5, 10]`, so `x = 7` gives `5`.**
    ///
    /// **With `sel = (0, 1)`, `x = 7` and `result = 5` it must be feasible; with `sel = (1, 0)` the x-value falls outside
    /// the selected segment and it must not be.**
    /// </remarks>
    [Fact]
    public void TheSelectedSegment_ShouldGiveTheValue() {
        IReadOnlyList<LinearInequality<Flt64>> rows = Generated(segments: 2);

        Feasible(rows, x: 7.0, sel0: 0.0, sel1: 1.0, result: 5.0)
            .Should().BeTrue("segment two covers x = 7 and its line is the constant five");

        Feasible(rows, x: 7.0, sel0: 1.0, sel1: 0.0, result: 5.0)
            .Should().BeFalse("x = 7 lies outside segment one, so sel = (1,0) cannot hold");

        // **结果偏离该段直线时被等式行挡住——而<b>两侧都要测</b>：**
        // **`6` 违反上界行、`4` 违反下界行。**
        // **只测一侧的话，把某一侧的松弛方向写反，判据仍然全绿。**
        //
        // **A result off the segment's line is blocked by the equation rows — and <b>both sides must be tested</b>:**
        // **`6` violates the upper row and `4` violates the lower one.**
        // **Testing only one side leaves a flipped relaxation on the other side completely unobserved.**
        Feasible(rows, x: 7.0, sel0: 0.0, sel1: 1.0, result: 6.0)
            .Should().BeFalse("the selected segment's line is the constant five, so six is above it");
        Feasible(rows, x: 7.0, sel0: 0.0, sel1: 1.0, result: 4.0)
            .Should().BeFalse("and four is below it");
    }

    /// <summary>
    /// **选择行是等式，不是 `≤ 1`** / The select row is an equality, not `≤ 1`.
    /// </summary>
    /// <remarks>
    /// **与 MAX 同一个教训：换成 `≤ 1` 时全零选择可行，
    /// 所有段便同时处于松弛状态，于是 `result` 不受任何段的直线约束。**
    ///
    /// **判据用"跳过等式行之后同一个点可行"来把责任锁在等式上。**
    ///
    /// **The same lesson as MAX: as `≤ 1`, all-zero selectors become feasible, every segment sits slack, and `result`
    /// is tied to no segment's line.**
    ///
    /// **The criterion uses "the same point holds once the equality is skipped" to pin the responsibility on the
    /// equality.**
    /// </remarks>
    [Fact]
    public void TheSelectRow_ShouldBeAnEquality() {
        IReadOnlyList<LinearInequality<Flt64>> rows = Generated(segments: 2);

        // **`result` 必须落在松弛后的等式行之内：`M = 100` 时上界约为 `105`，
        // 所以取 `100` 而不是 `999`——`999` 被等式行自己挡住了，
        // 那样断言的就变成"`M` 够不够大"而不是"等式在不在"（MAX 那批的同一个教训）。**
        //
        // **The result must sit inside the relaxed equation rows: with `M = 100` the cap is about `105`, so use `100`
        // rather than `999` — `999` is blocked by the equation rows themselves, which would test "is M large enough"
        // instead of "is the equality there" (the same lesson as the MAX batch).**
        Feasible(rows, x: 7.0, sel0: 0.0, sel1: 0.0, result: 100.0)
            .Should().BeFalse("all-zero selectors are excluded by the equality");
        FeasibleWithoutSelect(rows, x: 7.0, sel0: 0.0, sel1: 0.0, result: 100.0)
            .Should().BeTrue("without the equality every other row tolerates that point");
    }

    /// <summary>
    /// **显式 M 必须为正** / An explicit M must be positive.
    /// </summary>
    /// <remarks>
    /// **与 `AbsStructure` 的非负要求不同：这里的 M 是一个松弛量，
    /// 为零的话未选中的段仍然成立，模型会错。**
    ///
    /// **Different from `AbsStructure`'s nonnegative rule: here M is a relaxation, and zero would let unselected
    /// segments bind.**
    /// </remarks>
    [Fact]
    public void TheExplicitM_ShouldBePositive() {
        PiecewiseStructure(explicitM: 0.0).GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        PiecewiseStructure(explicitM: -5.0).GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        PiecewiseStructure(explicitM: 100.0).GenerateConstraints().Should()
            .BeOfType<Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **自动 M 的地板挡住 `M = 0`** / The automatic M's floor stops `M = 0`.
    /// </summary>
    /// <remarks>
    /// **四个逐段松弛量有可能<b>全部为零</b>：** **输入域恰好落在段内（两个 `xRelax` 为零）、
    /// 全局输出界恰好贴着直线端点（两个 `eqRelax` 为零）。**
    ///
    /// **那时 `M = 0`，"松弛"其实不松弛，未选中的段会照常生效——模型不可行。**
    ///
    /// **地板（输入域绝对值的较大者）挡住的就是这件事。**
    ///
    /// **判据构造一个恰好满足"四个都为零"的快照，并断言跑出来的 M 仍然是正的
    /// （通过检查生成的约束里存在非零的 `M` 系数列项来判断）。**
    ///
    /// **All four per-segment relaxations can be <b>zero</b>: the input domain sits inside the segment and the global
    /// output bounds touch the line's endpoints.**
    ///
    /// **Then `M = 0`, a relaxation of zero does not relax, unselected segments bind, and the model is infeasible.**
    ///
    /// **The floor — the larger absolute input bound — is what stops it.**
    ///
    /// **The criterion builds a snapshot that satisfies "all four are zero" and asserts the resulting M is still
    /// positive, by checking that a nonzero M coefficient appears on the selector.**
    /// </remarks>
    [Fact]
    public void TheFloor_ShouldRaiseTheAutomaticMToTheDomainMagnitude() {
        // **四个逐段松弛量有可能<b>全部为零</b>：** **输入域恰好落在段内（两个 `xRelax` 为零）、
        // 全局输出界恰好贴着直线端点（两个 `eqRelax` 为零）。**
        //
        // **判据必须用一个<b>大输入域</b>才区分得出来：**
        // **`Resolve(0, explicit: false)` 本来就会把 0 修复成 `Minimum = 1.0`，
        // 所以在 `[0, 1]` 这样的域上，"地板"与"修复"给出的值恰好都是 1——
        // 那两件事就不可区分了。**
        //
        // **用 `[-1000, 1000]`：地板给出 1000，而单靠修复只给出 1。**
        //
        // **All four per-segment relaxations can be <b>zero</b>: the domain sits inside the segment and the output bounds
        // touch the line's ends.**
        //
        // **The criterion needs a <b>wide domain</b> to tell the difference: `Resolve(0, explicit: false)` already repairs
        // zero to `Minimum = 1.0`, so on a domain like `[0, 1]` the floor and the repair both yield one and the two are
        // indistinguishable.**
        //
        // **With `[-1000, 1000]` the floor yields 1000 while the repair alone yields one.**
        var wide = new UnivariateLinearPiecewiseStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero),
            Breakpoints: new[] { new Flt64(-1000.0), new Flt64(1000.0) },
            Slopes: new[] { Flt64.Zero }, Intercepts: new[] { Flt64.Zero },
            ResultVariable: Result, SelectorVariables: new[] { Selector }) {
            Converter = IIntoValue<Flt64>.Identity,
            CapturedInputBounds = new Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(
                new LinearPolynomialBounds<Flt64>(new Flt64(-1000.0), new Flt64(1000.0))),
        };

        IReadOnlyList<LinearInequality<Flt64>> rows = wide.GenerateConstraints().Value;
        rows.Should().HaveCount(5);

        // **`_seg_0_lb` 把选择列乘上 `−M`，所以它的系数就是 `−M`。**
        // **`_seg_0_lb` multiplies the selector by `−M`, so its coefficient is `−M`.**
        double selectorCoefficient = 0.0;
        foreach (LinearMonomial<Flt64> monomial in rows[1].Lhs.Monomials) {
            if (monomial.Symbol.Equals(Selector)) {
                selectorCoefficient = monomial.Coefficient.ToDouble();
            }
        }

        global::System.Math.Abs(selectorCoefficient).Should().BeApproximately(
            1000.0, 1e-9,
            "the floor is the domain magnitude, not the repaired Minimum of one");
    }

    /// <summary>
    /// **缺转换器与缺输入范围给不同的消息** / A missing converter and a missing range say different things.
    /// </summary>
    [Fact]
    public void TheTwoAbsences_ShouldDifferForGenerationToo() {
        // **缺转换器。**
        // **No converter.**
        // **维度检查排在转换器检查之前，所以这里必须用一个<b>结构上合法</b>的快照——
        // 否则先命中的是"数量不匹配"，测不到"缺转换器"。**
        //
        // **The dimension check runs before the converter check, so this needs a <b>structurally valid</b> snapshot —
        // otherwise "inconsistent counts" fires first and "no converter" is never reached.**
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> noConverter =
            new UnivariateLinearPiecewiseStructure(
                Input: Input(0.0, 0.0),
                Breakpoints: new[] { new Flt64(0.0), new Flt64(1.0) },
                Slopes: new[] { Flt64.One }, Intercepts: new[] { Flt64.Zero },
                ResultVariable: Result, SelectorVariables: new[] { Selector }).GenerateConstraints();
        noConverter.Should().BeOfType<
            Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)noConverter)
            .Error.Message.Should().Contain("no value converter");

        // **有转换器、无显式 M、无捕获范围：**
        // **——无穷常量会被<b>更早</b>的"输入含非有限值"检查拦住，测不到范围这一支。**
        // **——自由变量有默认界（约 `±4.5e15`），所以它本来就是能过的。**
        // **——真正打到"缺范围"的是<b>系数溢出</b>：系数 `1e300` 本身可用，
        // 但乘上变量的默认界就溢出，`FiniteBounds` 于是返回 `null`。**
        //
        // **With a converter, no explicit M and no capture: an infinite constant is caught by the <b>earlier</b>
        // non-finite-input check, a free variable has default bounds and passes, and what actually reaches "no range" is
        // an <b>overflowing coefficient</b>: `1e300` is itself usable, but multiplying it by the variable's default bounds
        // overflows and `FiniteBounds` returns null.**
        var infinite = new UnivariateLinearPiecewiseStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(1e300), X) }, Flt64.Zero),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(1.0) },
            Slopes: new[] { Flt64.Zero }, Intercepts: new[] { Flt64.Zero },
            ResultVariable: Result, SelectorVariables: new[] { Selector }) {
            Converter = IIntoValue<Flt64>.Identity,
        };

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> noRange =
            infinite.GenerateConstraints();
        noRange.Should().BeOfType<
            Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)noRange)
            .Error.Message.Should().Contain("no provable finite input range");
    }

    /// <summary>
    /// **PWL 现在能真正物化了——物化器七格全满** / PWL materializes; all seven cells are full.
    /// </summary>
    [Fact]
    public void ThePiecewise_ShouldNowMaterialize() {
        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackMaterializers.Core.Materialize(
                PiecewiseStructure(explicitM: 100.0));

        result.Should().BeOfType<
            Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        result.Value.Constraints.Should().HaveCount(9);
    }

    private static UnivariateLinearPiecewiseStructure PiecewiseStructure(double explicitM) =>
        new(Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: Result, SelectorVariables: new[] { Selector, Selector2 }) {
            ExplicitM = new Flt64(explicitM),
            Converter = IIntoValue<Flt64>.Identity,
        };

    private static IReadOnlyList<LinearInequality<Flt64>> Generated(int segments) {
        var breakpoints = new Flt64[segments + 1];
        var slopes = new Flt64[segments];
        var intercepts = new Flt64[segments];
        var selectors = new IVariableItem[segments];
        for (int index = 0; index < segments; ++index) {
            breakpoints[index] = new Flt64(index * 5.0);
            slopes[index] = index == 0 ? Flt64.One : Flt64.Zero;
            intercepts[index] = new Flt64(index * 5.0);
            selectors[index] = index == 0 ? Selector : Selector2;
        }

        breakpoints[segments] = new Flt64(segments * 5.0);

        return new UnivariateLinearPiecewiseStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero),
            Breakpoints: breakpoints, Slopes: slopes, Intercepts: intercepts,
            ResultVariable: Result, SelectorVariables: selectors) {
            ExplicitM = new Flt64(100.0),
            Converter = IIntoValue<Flt64>.Identity,
        }.GenerateConstraints().Value;
    }

    private static bool Feasible(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double sel0, double sel1, double result) =>
        Check(rows, x, sel0, sel1, result, includeSelect: true);

    private static bool FeasibleWithoutSelect(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double sel0, double sel1, double result) =>
        Check(rows, x, sel0, sel1, result, includeSelect: false);

    private static bool Check(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double sel0, double sel1, double result, bool includeSelect) {
        foreach (LinearInequality<Flt64> row in rows) {
            if (!includeSelect && row.Comparison == Comparison.EQ) {
                continue;
            }

            double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();
            foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
                total += monomial.Coefficient.ToDouble() * Lookup(monomial.Symbol, x, sel0, sel1, result);
            }

            foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
                total -= monomial.Coefficient.ToDouble() * Lookup(monomial.Symbol, x, sel0, sel1, result);
            }

            bool holds = row.Comparison switch {
                Comparison.LE => total <= 1e-9,
                Comparison.GE => total >= -1e-9,
                Comparison.EQ => global::System.Math.Abs(total) <= 1e-9,
                _ => false,
            };

            if (!holds) {
                return false;
            }
        }

        return true;
    }

    private static double Lookup(ISymbol symbol, double x, double sel0, double sel1, double result) {
        if (symbol.Equals(X)) { return x; }
        if (symbol.Equals(Selector)) { return sel0; }
        if (symbol.Equals(Selector2)) { return sel1; }
        if (symbol.Equals(Result)) { return result; }
        return 0.0;
    }

    private static readonly RealVar X = new("pwl-x");

    private static readonly BinVar Selector2 = new("pwl-sel2");
}
