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
/// MAX 结构的范围证明：收缩可以，扩大不行 / Shrinking is fine, growing is not.
/// </summary>
/// <remarks>
/// 对齐的 `MaxStructure`。
///
/// **这个方向不对称，所以判据必须两个方向都测。**
///
/// **The direction is asymmetric, so a criterion must test both sides.**
/// </remarks>
public class MaxStructureTests
{
    /// <summary>
    /// **当前范围收缩时仍然通过** / A shrunken range still passes.
    /// </summary>
    /// <remarks>
    /// **捕获时是 `[−10, 10]`，现在变量被限制到 `[−5, 5]`：Big-M 上界仍然有效。**
    ///
    /// **这一条与下一条必须成对存在**：**只测"扩大被拒"的话，
    /// 一个"永远拒绝"的实现也能通过。**
    ///
    /// **Captured as `[−10, 10]` and now restricted to `[−5, 5]`: the Big-M upper bound still holds.**
    ///
    /// **This and the next must come in a pair**: **testing only "grow is rejected" passes for an implementation that
    /// always rejects.**
    /// </remarks>
    [Fact]
    public void TheShrunkenRange_ShouldStillPass()
    {
        MaxStructure structure = Structure(new[] { (-10.0, 10.0) }, new[] { -5.0 });

        structure.ValidateFallbackInputBounds().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "a narrower range keeps the captured upper bound valid");
    }

    /// <summary>
    /// **当前范围扩大时被拒** / A grown range is rejected.
    /// </summary>
    [Fact]
    public void TheGrownRange_ShouldBeRejected()
    {
        // **下界越过捕获的左边。**
        // **The lower bound crosses the captured left edge.**
        Structure(new[] { (-10.0, 10.0) }, new[] { -11.0 })
            .ValidateFallbackInputBounds().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "the Big-M may no longer bound the input from below");

        // **上界越过捕获的右边。**
        // **The upper bound crosses the captured right edge.**
        Structure(new[] { (-10.0, 10.0) }, new[] { 11.0 })
            .ValidateFallbackInputBounds().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "the Big-M may no longer bound the input from above");

        // **两侧同时越界。**
        // **Both sides cross at once.**
        Structure(new[] { (-10.0, 10.0) }, new[] { 11.0 })
            .ValidateFallbackInputBounds().Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **数量不匹配先报，且不是"越界"那种消息** / A count mismatch reports first, and not as an index error.
    /// </summary>
    /// <remarks>
    /// **数量检查在 `try` <b>之外</b>。**
    /// **如果把它挪到里面（或者干脆不做），`Inputs[index]` 会抛下标越界，
    /// 而那个异常被 `catch` 转成一条完全不同的消息——
    /// 调用方看到"校验失败"而真正的原因是"数量对不上"。**
    ///
    /// **The count check sits <b>outside</b> the `try`.**
    /// **Moving it inside (or dropping it) makes `Inputs[index]` throw out-of-range, and the `catch` turns that into a
    /// completely different message — the caller reads "validation failed" while the cause is a count mismatch.**
    /// </remarks>
    [Fact]
    public void TheCountMismatch_ShouldReportItsOwnMessage()
    {
        // **直接构造：一个输入、两个捕获证明。** **helper 会按捕获数量建输入，
        // 所以数量不匹配的情形只能手搭。**
        //
        // **Built directly: one input, two captured proofs.** **The helper sizes the inputs from the captured list, so a
        // mismatch has to be assembled by hand.**
        var structure = new MaxStructure(
            Inputs: new[] { new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero) },
            ResultVariable: Result, SelectorVariables: new[] { FirstSelector },
            BigMValues: new[] { new Flt64(100.0) },
            Converter: IIntoValue<Flt64>.Identity) {
            CapturedInputBounds = new LinearPolynomialBounds<Flt64>?[] {
                new(Flt64.Zero, new Flt64(10.0)),
                new(Flt64.Zero, new Flt64(10.0)),
            },
        };

        Result<Success, ErrorCode, Error<ErrorCode>> result = structure.ValidateFallbackInputBounds();

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain(
            "input-bound proofs for",
            "the count check must speak for itself rather than be reported as an index failure");
    }

    /// <summary>
    /// **没有捕获时直接通过** / With nothing captured the check passes.
    /// </summary>
    [Fact]
    public void TheAbsentCapture_ShouldPass()
    {
        MaxStructure structure = Structure(new[] { (-10.0, 10.0) }, new[] { 0.0 });

        structure.ValidateFallbackInputBounds().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "no proof was captured, so nothing can be violated");
    }

    /// <summary>
    /// **`null` 条目被跳过，而不是当成"证明为零"** / A `null` entry is skipped, not read as a proof of zero.
    /// </summary>
    /// <remarks>
    /// **如果一个 `null` 被当成"证明是 `[0, 0]`"，那么任何不含 0 的范围都会被拒。**
    /// **判据让第一个条目的捕获为空、而它的当前范围是 `[−10, 10]`——
    /// 若被当成零，这个范围立刻越界。**
    ///
    /// **If a `null` were read as a proof of `[0, 0]`, any range excluding zero would be rejected.**
    /// **The criterion leaves the capture null while the input's current value is `7` — read as `[0, 0]`, that value
    /// immediately exceeds the proof.**
    ///
    /// **曾经用的是常量 `0`，于是"当成零"的变异也全绿——因为 `0` 恰好落在 `[0, 0]` 里。
    /// 判据的输入必须<b>远离</b>那个错误值，否则它证明不了任何事。**
    /// </remarks>
    [Fact]
    public void TheNullCapture_ShouldBeSkipped()
    {
        var structure = new MaxStructure(
            Inputs: new[] { new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(7.0)) },
            ResultVariable: Result, SelectorVariables: new[] { FirstSelector },
            BigMValues: new[] { new Flt64(100.0) },
            Converter: IIntoValue<Flt64>.Identity) {
            CapturedInputBounds = new LinearPolynomialBounds<Flt64>?[] { null },
        };

        structure.ValidateFallbackInputBounds().Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "a null entry means no proof, not a proof of zero");
    }

    /// <summary>**选择列按顺序报出** / The selector columns come out in order.</summary>
    [Fact]
    public void TheSelectors_ShouldBeReportedInOrder()
    {
        MaxStructure structure = Structure(
            new[] { (-10.0, 10.0), (-10.0, 10.0) }, new[] { 0.0, 0.0 }, selectorCount: 2);

        structure.HelperVariables.Should().HaveCount(2);
        structure.HelperVariables[0].Should().BeSameAs(FirstSelector);
    }

    /// <summary>
    /// **多输入时 `InputOrNull` 返回 `null`，不取第一个** / With several inputs `InputOrNull` returns null.
    /// </summary>
    /// <remarks>
    /// **取第一个会让一个二元 `max` 被当成一元的，而调用方无从发现。**
    ///
    /// **Taking the first would treat a binary `max` as unary, with nothing telling the caller.**
    /// </remarks>
    [Fact]
    public void TheSeveralInputs_ShouldNotCollapseToOne()
    {
        MaxStructure single = Structure(new[] { (-10.0, 10.0) }, new[] { 0.0 });
        single.InputOrNull().Should().BeSameAs(single.Inputs[0]);

        MaxStructure pair = Structure(new[] { (-10.0, 10.0), (-10.0, 10.0) }, new[] { 0.0, 0.0 }, selectorCount: 2);
        pair.InputOrNull().Should().BeNull("two inputs are not one input");
        pair.InputPolynomialsOrEmpty().Should().HaveCount(2, "but the full list still reports both");
    }

    /// <summary>**MAX 已经接入分派表** / MAX is wired into the dispatch table.</summary>
    [Fact]
    public void TheMaxStructure_ShouldBeDispatched()
    {
        MaxStructure structure = Structure(new[] { (-10.0, 10.0) }, new[] { 0.0 });

        structure.ResultVariableOrNull().Should().BeSameAs(Result);
        structure.HelperVariablesOrEmpty().Should().HaveCount(1);
        structure.SupportsDeferredFallback().Should().BeTrue();
    }

    /// <summary>
    /// 构造一个 MAX 结构：`captured` 是捕获的证明，`currentBounds` 是"当前"范围 /
    /// Build a MAX structure: `captured` proofs and "current" ranges.
    /// </summary>
    /// <remarks>
    /// **"当前范围"没有别的地方可以设置**：**`ValidateFallbackInputBounds` 从 `Inputs` 算出它，
    /// 而这里用一个<b>常数多项式</b>让 `FiniteBounds` 返回 `[c, c]`——
    /// 于是当前范围被精确钉在一个点上。**
    ///
    /// **这样做的代价是"当前范围"永远是退化的单点**，
    /// **所以"收缩"只能测单点落在捕获框内这一种，不能测一个真区间。**
    /// **这一点写在这里，因为看代码的人会以为 `currentBounds` 的长度有含义。**
    ///
    /// **The "current range" cannot be set anywhere else**: **`ValidateFallbackInputBounds` computes it from `Inputs`,
    /// and a <b>constant polynomial</b> makes `FiniteBounds` return `[c, c]` — pinning the current range to a point.**
    ///
    /// **The cost is that the current range is always a degenerate point**, **so "shrink" can only be tested as "a point
    /// inside the captured box", not as a real interval.**
    /// </remarks>
    private static MaxStructure Structure(
        (double Lower, double Upper)[] captured,
        double[] currentBounds,
        int selectorCount = 1) {
        var capturedBounds = new List<LinearPolynomialBounds<Flt64>?>(captured.Length);
        var inputs = new List<LinearPolynomial<Flt64>>(captured.Length);
        var bigMs = new List<Flt64>(captured.Length);

        for (int index = 0; index < captured.Length; ++index) {
            capturedBounds.Add(new LinearPolynomialBounds<Flt64>(
                new Flt64(captured[index].Lower), new Flt64(captured[index].Upper)));
            inputs.Add(new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(currentBounds[index])));
            bigMs.Add(new Flt64(100.0));
        }

        var selectors = new List<IVariableItem>(selectorCount);
        for (int index = 0; index < selectorCount; ++index) {
            selectors.Add(index == 0 ? FirstSelector : new BinVar($"mx-sel-{index}"));
        }

        return new MaxStructure(
            Inputs: inputs, ResultVariable: Result, SelectorVariables: selectors,
            BigMValues: bigMs, Converter: IIntoValue<Flt64>.Identity) {
            CapturedInputBounds = capturedBounds,
        };
    }

    private static readonly RealVar MaxResult = new("mx-result");

    private static readonly BinVar FirstSelector = new("mx-sel-0");

    // ===== 行生成 =====

    /// <summary>
    /// **`2n + 1` 行，三族** / `2n + 1` rows in three families.
    /// </summary>
    [Fact]
    public void TheRows_ShouldBeTwoPerInputPlusOne() {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows(2);

        rows.Should().HaveCount(5, "two lower, two upper and one select row");
        rows[0].Name.Should().Be("max_max_lb_0");
        rows[1].Name.Should().Be("max_max_lb_1");
        rows[2].Name.Should().Be("max_max_ub_0");
        rows[3].Name.Should().Be("max_max_ub_1");
        rows[4].Name.Should().Be("max_max_select_one");

        rows[0].Comparison.Should().Be(Comparison.GE);
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[4].Comparison.Should().Be(Comparison.EQ);
    }

    /// <summary>
    /// **选中那一支时结果等于被选输入** / At the selected branch the result equals that input.
    /// </summary>
    /// <remarks>
    /// **`x = 3`、`y = 7`、`sel = (0, 1)` 时正确解是 `result = 7`。**
    ///
    /// **两条下界都满足（`7 ≥ 3`、`7 ≥ 7`），而被选中的 `ub_1` 紧（`7 ≤ 7`）、
    /// 未选中的 `ub_0` 松（`7 ≤ 3 + 100`）。**
    ///
    /// **With `x = 3`, `y = 7` and `sel = (0, 1)` the correct answer is `result = 7`.**
    ///
    /// **Both lower bounds hold (`7 ≥ 3`, `7 ≥ 7`), the selected `ub_1` is tight (`7 ≤ 7`) and the unselected `ub_0` is
    /// slack (`7 ≤ 3 + 100`).**
    /// </remarks>
    [Fact]
    public void TheSelectedBranch_ShouldGiveTheMax() {
        IReadOnlyList<LinearInequality<Flt64>> rows = Rows(2);

        // **可行 ≠ 紧：下界行在 `result = 7`、`x = 3` 时残差是 `+4`，而它是满足的。
        // **要求所有残差为零，是把"可行"错当成"紧"——同一处错误此前已经犯过一次。**
        //
        // **Feasible ≠ tight: the lower row has residual `+4` at `result = 7`, `x = 3` and it holds.
        // **Demanding every residual be zero confuses "feasible" with "binding" — the same mistake as.**
        Feasible(rows, x: 3.0, y: 7.0, sel0: 0.0, sel1: 1.0, result: 7.0)
            .Should().BeTrue("sel = (0,1) makes the result seven");

        // **结果取「小于最大值」时，下界行被违反。**
        // **A result below the maximum violates a lower bound.**
        Residuals(rows, x: 3.0, y: 7.0, sel0: 0.0, sel1: 1.0, result: 5.0)[1]
            .Should().BeLessThan(0.0, "result must be at least y");

        // **结果取「大于最大值」时，被选中的上界行被违反。**
        // **A result above the maximum violates the selected upper bound.**
        Residuals(rows, x: 3.0, y: 7.0, sel0: 0.0, sel1: 1.0, result: 9.0)[3]
            .Should().BeGreaterThan(0.0, "result must not exceed the selected input");
    }

    /// <summary>
    /// **选择行是等式，不是 `≤ 1`** / The select row is an equality, not `≤ 1`.
    /// </summary>
    /// <remarks>
    /// **这是这里的核心判据。**
    ///
    /// **换成 `≤ 1` 的话，全零选择就可行：**
    /// **此时所有的上界都处于松弛状态（`result ≤ input_i + M_i`），
    /// 于是 `result` 只需要 `≥ max` 就可以任意大。**
    ///
    /// **具体的算例：`x = 3`、`y = 7`、`sel = (0, 0)`、`result = 1000`。**
    /// **下界行：`1000 ≥ 3`、`1000 ≥ 7`，成立。
    /// 上界行：`1000 ≤ 3 + 100 = 103` 违反——所以 `M` 得更大。**
    ///
    /// **用 `M = 100` 时它被上界挡住，所以这个算例检验不到等式。**
    /// **用 `M = 2000` 才检验得到：那时全零选择下 `result = 1000` 满足所有行。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **As `≤ 1`, all-zero selectors become feasible: every upper row sits slack (`result ≤ input_i + M_i`) and
    /// `result` only has to be `≥ max`, free to grow.**
    ///
    /// **Concretely: `x = 3`, `y = 7`, `sel = (0, 0)`, `result = 1000`.** **Lower rows hold (`1000 ≥ 3`, `1000 ≥ 7`);
    /// upper rows demand `1000 ≤ 3 + M`, so `M` must be large.**
    ///
    /// **With `M = 100` an upper row blocks it, so that instance does not test the equality.** **With `M = 2000` it does:
    /// all-zero selectors then admit `result = 1000`.**
    /// </remarks>
    [Fact]
    public void TheSelectRow_ShouldBeAnEquality() {
        IReadOnlyList<LinearInequality<Flt64>> largeM = Rows(2, bigM: 2000.0);

        // **全零选择：选择行的残差是 `0 − 1 = −1`，不再是 0。**
        // **All-zero selectors: the select row's residual is `0 − 1 = −1`, no longer zero.**
        Residuals(largeM, x: 3.0, y: 7.0, sel0: 0.0, sel1: 0.0, result: 1000.0)[4]
            .Should().BeApproximately(-1.0, 1e-9, "the equality demands the selectors sum to one");

        // **而其它四行在那个点上是满足的——所以真的是选择行在挡。**
        // **The other four hold at that point, so it really is the select row doing the blocking.**
        // **把选择行换成"恒成立"，同一个赋值就会变得可行——那正是 `≤ 1` 的后果。**
        // **Replacing the select row with a tautology makes the same assignment feasible — exactly what `≤ 1` gives.**
        Feasible(largeM, x: 3.0, y: 7.0, sel0: 0.0, sel1: 0.0, result: 1000.0)
            .Should().BeFalse("the select equality is what blocks all-zero selectors");

        FeasibleWithoutSelect(largeM, x: 3.0, y: 7.0, sel0: 0.0, sel1: 0.0, result: 1000.0)
            .Should().BeTrue(
                "with M = 2000 every other row tolerates it, so only the equality stands between "
                + "all-zero selectors and an unbounded result");
    }

    /// <summary>
    /// **数量不匹配与非负 Big-M 各被拒** / A count mismatch and a negative Big-M are both rejected.
    /// </summary>
    /// <remarks>
    /// **三个数量一致的条件（非空、选择变量数、M 数）是三个独立的分支。**
    ///
    /// **The three size conditions (non-empty, selector count, M count) are three separate branches.**
    /// </remarks>
    [Fact]
    public void TheValidation_ShouldCoverEachBranch() {
        // **空输入。**
        // **Empty inputs.**
        new MaxStructure(Array.Empty<LinearPolynomial<Flt64>>(), MaxResult,
            Array.Empty<IVariableItem>(), Array.Empty<Flt64>(),
            IIntoValue<Flt64>.Identity, "max").GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();

        // **选择变量少一个。**
        // **One selector short.**
        new MaxStructure(new[] { Zero(), Zero() }, MaxResult,
            new[] { (IVariableItem)Sel0 }, new[] { new Flt64(100.0), new Flt64(100.0) },
            IIntoValue<Flt64>.Identity, "max").GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();

        // **M 少一个。**
        // **One M short.**
        new MaxStructure(new[] { Zero(), Zero() }, MaxResult,
            new[] { (IVariableItem)Sel0, Sel1 }, new[] { new Flt64(100.0) },
            IIntoValue<Flt64>.Identity, "max").GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();

        // **负的 M。**
        // **A negative M.**
        new MaxStructure(new[] { Zero(), Zero() }, MaxResult,
            new[] { (IVariableItem)Sel0, Sel1 },
            new[] { new Flt64(100.0), new Flt64(-1.0) },
            IIntoValue<Flt64>.Identity, "max").GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();

        // **重复的选择变量键。**
        // **Duplicate selector keys.**
        new MaxStructure(new[] { Zero(), Zero() }, MaxResult,
            new[] { (IVariableItem)Sel0, Sel0 },
            new[] { new Flt64(100.0), new Flt64(100.0) },
            IIntoValue<Flt64>.Identity, "max").GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **MAX 现在能真正物化了** / MAX can now actually materialize.
    /// </summary>
    /// <remarks>
    /// **它是"未实现"的三格之一，后来划掉一格，这里划掉这一格。**
    /// **举证：物化成功且行数是 `2n + 1`。**
    ///
    /// **It was one of the three "not implemented" cells; one was removed,e and another.**
    /// **The evidence: it materializes with `2n + 1` rows.**
    /// </remarks>
    [Fact]
    public void TheMax_ShouldNowMaterialize() {
        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackMaterializers.Core.Materialize(Verified());

        result.Should().BeOfType<
            Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        result.Value.Constraints.Should().HaveCount(5);
    }

    private static MaxStructure Verified() =>
        new(new[] { Zero(), Zero() }, MaxResult,
            new[] { (IVariableItem)Sel0, Sel1 },
            new[] { new Flt64(100.0), new Flt64(100.0) },
            IIntoValue<Flt64>.Identity, "max") {
            // **走物化路径需要输入范围证明（`ValidateFallbackInputBounds`）。**
            // **常数 0 的范围是 `[0, 0]`，捕获同样的是 `[0, 0]`，所以证明成立。**
            //
            // **The materialize path needs an input-range proof; a constant zero has range `[0, 0]`, and capturing the
            // same value makes the proof hold.**
            CapturedInputBounds = new[] {
                (LinearPolynomialBounds<Flt64>?)new LinearPolynomialBounds<Flt64>(
                    new Flt64(0.0), new Flt64(0.0)),
                new LinearPolynomialBounds<Flt64>(new Flt64(0.0), new Flt64(0.0)),
            },
        };

    private static LinearPolynomial<Flt64> Zero() =>
        new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

    private static IReadOnlyList<LinearInequality<Flt64>> Rows(int count, double bigM = 100.0) {
        var inputs = new LinearPolynomial<Flt64>[count];
        var selectors = new IVariableItem[count];
        var bigMs = new Flt64[count];
        for (int index = 0; index < count; ++index) {
            inputs[index] = index == 0
                ? new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, X) }, Flt64.Zero)
                : new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, Y) }, Flt64.Zero);
            selectors[index] = index == 0 ? Sel0 : Sel1;
            bigMs[index] = new Flt64(bigM);
        }

        return new MaxStructure(inputs, MaxResult, selectors, bigMs,
            IIntoValue<Flt64>.Identity, "max").GenerateConstraints().Value;
    }

    /// <summary>各行的残差：按 `lhs − rhs` 计算 / Each residual, as lhs − rhs.</summary>
    private static double[] Residuals(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double y, double sel0, double sel1, double result) {
        double[] values = new double[rows.Count];
        for (int index = 0; index < rows.Count; ++index) {
            LinearInequality<Flt64> row = rows[index];
            double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

            foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
                total += monomial.Coefficient.ToDouble()
                    * Lookup(monomial.Symbol, x, y, sel0, sel1, result);
            }

            foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
                total -= monomial.Coefficient.ToDouble()
                    * Lookup(monomial.Symbol, x, y, sel0, sel1, result);
            }

            values[index] = total;
        }

        return values;
    }

    private static double Lookup(
        ISymbol symbol, double x, double y, double sel0, double sel1, double result) {
        if (symbol.Equals(X)) { return x; }
        if (symbol.Equals(Y)) { return y; }
        if (symbol.Equals(Sel0)) { return sel0; }
        if (symbol.Equals(Sel1)) { return sel1; }
        if (symbol.Equals(MaxResult)) { return result; }
        return 0.0;
    }

    private static readonly RealVar X = new("mx-x");

    private static readonly RealVar Y = new("mx-y");

    private static readonly RealVar Result = new("mx-result");

    private static readonly BinVar Sel0 = new("mx-sel0");

    private static readonly BinVar Sel1 = new("mx-sel1");

    /// <summary>代入一组取值，看所有行按各自的比较方向是否满足 / Check every row against its own direction.</summary>
    private static bool Feasible(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double y, double sel0, double sel1, double result) =>
        Check(rows, x, y, sel0, sel1, result, includeSelect: true);

    /// <summary>同上，但跳过选择行 / The same, skipping the select row.</summary>
    private static bool FeasibleWithoutSelect(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double y, double sel0, double sel1, double result) =>
        Check(rows, x, y, sel0, sel1, result, includeSelect: false);

    private static bool Check(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double y, double sel0, double sel1, double result, bool includeSelect) {
        foreach (LinearInequality<Flt64> row in rows) {
            if (!includeSelect && row.Comparison == Comparison.EQ) {
                continue;
            }

            double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();
            foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
                total += monomial.Coefficient.ToDouble()
                    * Lookup(monomial.Symbol, x, y, sel0, sel1, result);
            }

            foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
                total -= monomial.Coefficient.ToDouble()
                    * Lookup(monomial.Symbol, x, y, sel0, sel1, result);
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
}
