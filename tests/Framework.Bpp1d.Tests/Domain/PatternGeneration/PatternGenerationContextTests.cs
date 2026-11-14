// 有界候选生成判据 / Criteria for bounded pattern generation.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.PatternGeneration;

/// <summary>
/// **有界候选生成** / Bounded pattern generation.
/// </summary>
/// <remarks>
/// 对应基线的 `PatternGenerationContext`。判据钉住四件事：
///
/// | 点 | 漏掉的后果 |
/// | --- | --- |
/// | **`Complete` 如实报告截断** | 受限池的最优被误报成全局最优 |
/// | **间距只加在件与件之间** | 可行域被无谓缩小，少出候选 |
/// | **单件方案也在候选里** | 只有一件能装时池子为空 |
/// | **`InitialPatterns` 不声称完备** | 启发式被当成证明 |
///
/// Mirrors the baseline's `PatternGenerationContext`; the table lists what each criterion pins.
/// </remarks>
public class PatternGenerationContextTests {
    /// <summary>**枚举出全部可行组合，且标记为完整** / Enumerate every feasible combination and mark it complete.</summary>
    /// <remarks>
    /// 箱长 10、两个物品（长 6 与长 4）、间距 0、各需 1 件：
    /// 可行组合 = `{6}`、`{4}`、`{6,4}` —— **3 个**（`6+4=10` 恰好装满）。
    ///
    /// Bin length 10, two items (lengths 6 and 4), no spacing, one piece each: feasible combinations are `{6}`, `{4}` and `{6,4}` — **three** (`6+4=10` exactly fits).
    /// </remarks>
    [Fact]
    public void Generate_ShouldEnumerateEveryFeasibleCombination() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 6.0, 1), ("I2", 4.0, 1)],
            spacing: 0.0);

        Result<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().Generate(problem);

        result.IsOk.Should().BeTrue(result.ToString());
        PatternGenerationResult<Flt64> generated = ((Ok<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        generated.Complete.Should().BeTrue("the search is well inside the default budget");
        generated.Patterns.Should().HaveCount(3, "the feasible combinations are {6}, {4} and {6,4}");
        generated.VisitedNodes.Should().BeGreaterThan(0);
    }

    /// <summary>**间距让 `6+4` 装不下，于是只剩两个单件方案** / Spacing makes `6+4` infeasible, leaving only the two singletons.</summary>
    /// <remarks>
    /// 反向对照：把间距设成 1，`6 + 1 + 4 = 11 > 10` ⇒ `{6,4}` 不可行，候选只剩 `{6}` 与 `{4}`。
    /// 若实现**漏加**间距，这里会错误地仍然给出 3 个候选。
    ///
    /// Reverse control: with spacing 1, `6 + 1 + 4 = 11 > 10` so `{6,4}` is infeasible and only `{6}` and `{4}` remain. An implementation that **omits** the gap would
    /// still report three.
    /// </remarks>
    [Fact]
    public void Generate_ShouldDropTheCombinationWhenSpacingMakesItInfeasible() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 6.0, 1), ("I2", 4.0, 1)],
            spacing: 1.0);

        Result<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().Generate(problem);

        result.IsOk.Should().BeTrue(result.ToString());
        PatternGenerationResult<Flt64> generated = ((Ok<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        generated.Patterns.Should().HaveCount(2, "6 + 1 + 4 = 11 exceeds the bin length, so only the singletons remain");
    }

    /// <summary>**恰好装满的单件方案必须可行（第一件不加间距）** / A single piece that exactly fills the bin must stay feasible (no gap for the first piece).</summary>
    /// <remarks>
    /// **这条判据是补的。** 原来用"两个物品 6 与 4、间距 1"，无论第一件加不加间距，
    /// `6+4` 都装不下，候选都是 2 个 —— **判据区分不出两种实现**，变异存活。
    ///
    /// 现在用一个**恰好装满**的件：箱长 10、物品长 10、间距 1。
    /// 正确实现里第一件不加间距 ⇒ 可行；若第一件也加间距，`10+1=11 &gt; 10` ⇒ 候选为空。
    ///
    /// **This criterion was added later.** The original used two items (6 and 4) with spacing 1, where `6+4` is infeasible either way, so both implementations yield two
    /// candidates — **the criterion could not tell them apart** and the mutant survived. Now a piece that **exactly fills** the bin is used: bin 10, item 10, spacing 1.
    /// Correct code adds no gap for the first piece so it stays feasible; adding one makes `10+1=11 &gt; 10` and the candidate set empty.
    /// </remarks>
    [Fact]
    public void Generate_ShouldKeepASinglePieceThatExactlyFillsTheBin() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 10.0, 1)],
            spacing: 1.0);

        Result<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().Generate(problem);

        result.IsOk.Should().BeTrue(result.ToString());
        PatternGenerationResult<Flt64> generated = ((Ok<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        generated.Patterns.Should().HaveCount(
            1, "the first piece carries no gap, so a 10-long piece fits a 10-long bin; adding a gap would make it 11");
    }

    /// <summary>**预算截断时必须报告 `Complete = false`** / A budget-truncated search must report `Complete = false`.</summary>
    /// <remarks>
    /// **这是本层最重要的判据**：`Complete` 若在截断后仍为 `true`，
    /// 上层会把"受限池的最优"当成"全局最优"。用 `MaxNodes = 1` 强制截断。
    ///
    /// **The most important criterion here**: if `Complete` stayed `true` after truncation, the caller would treat "optimal over a restricted pool" as "globally
    /// optimal". `MaxNodes = 1` forces the truncation.
    /// </remarks>
    [Fact]
    public void Generate_ShouldReportIncompleteWhenTheNodeBudgetTruncatesTheSearch() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 6.0, 1), ("I2", 4.0, 1)],
            spacing: 0.0);

        Result<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().Generate(
                problem, new PatternGenerationConfig(MaxPatterns: 10000, MaxNodes: 1));

        result.IsOk.Should().BeTrue(result.ToString());
        PatternGenerationResult<Flt64> generated = ((Ok<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        generated.Complete.Should().BeFalse(
            "a budget of one node cannot exhaust the enumeration, so completeness must not be claimed");
    }

    /// <summary>**候选预算截断也必须报告 `Complete = false`** / A pattern-budget truncation must also report `Complete = false`.</summary>
    [Fact]
    public void Generate_ShouldReportIncompleteWhenThePatternBudgetTruncatesTheSearch() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 6.0, 1), ("I2", 4.0, 1)],
            spacing: 0.0);

        Result<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().Generate(
                problem, new PatternGenerationConfig(MaxPatterns: 1, MaxNodes: 100000));

        result.IsOk.Should().BeTrue(result.ToString());
        PatternGenerationResult<Flt64> generated = ((Ok<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        generated.Complete.Should().BeFalse("a budget of one pattern cannot exhaust the enumeration");
        generated.Patterns.Should().HaveCount(1, "the pattern budget caps what is retained");
    }

    /// <summary>**非法预算必须被拒** / An invalid budget must be rejected.</summary>
    /// <param name="maxPatterns">候选上限 / Pattern cap.</param>
    /// <param name="maxNodes">节点上限 / Node cap.</param>
    [Theory]
    [InlineData(0, 100000)]
    [InlineData(10000, 0)]
    public void Generate_ShouldRejectANonPositiveBudget(int maxPatterns, int maxNodes) {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0, items: [("I1", 6.0, 1)], spacing: 0.0);

        Result<PatternGenerationResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().Generate(
                problem, new PatternGenerationConfig(maxPatterns, maxNodes));

        result.IsFailed.Should().BeTrue("both budgets must be strictly positive");
    }

    /// <summary>**初始解包含单件方案，且不声称完备** / Initial patterns include singletons and claim no completeness.</summary>
    /// <remarks>
    /// 单件方案必须在初始池里，否则"只有一个物品能装进箱"时池子是空的。
    /// 返回值**没有** `Complete` 字段——这是有意的：启发式不能当作证明。
    ///
    /// Singletons must be present or the pool would be empty when only one item fits a bin. The return type **has no** `Complete` field by design: a heuristic must
    /// not be mistaken for a proof.
    /// </remarks>
    [Fact]
    public void InitialPatterns_ShouldIncludeSingletons() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 9.0, 1), ("I2", 9.0, 1)],
            spacing: 0.0);

        Result<IReadOnlyList<PackingPattern<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            new PatternGenerationContext<Flt64>().InitialPatterns(problem);

        result.IsOk.Should().BeTrue(result.ToString());
        IReadOnlyList<PackingPattern<Flt64>> patterns =
            ((Ok<IReadOnlyList<PackingPattern<Flt64>>, ErrorCode, Error<ErrorCode>>)result).Value;
        patterns.Should().NotBeEmpty("two 9-long items cannot share a 10-long bin, so only singletons are feasible");
        foreach (PackingPattern<Flt64> pattern in patterns) {
            pattern.Amounts.Should().HaveCount(1, "no pair fits, so every pattern is a singleton");
        }
    }

    /// <summary>**最佳适应与首次适应给出不同方案** / Best fit and first fit differ.</summary>
    /// <remarks>
    /// 三个物品 4、3、3，箱长 10：
    /// **首次适应**把 4 与 3 放进同一箱（4+3=7），第三个 3 再放进去（10）⇒ 一个箱装三个。
    /// **最佳适应**在选择时挑剩余最小的箱，顺序不同则装法不同。
    /// 判据只断言"两者都能产出方案且不抛异常"——**具体差异依赖实现细节，写死会过脆**。
    ///
    /// Three items 4, 3, 3 in a bin of length 10: **first fit** puts 4 and 3 together then the last 3 (total 10). **Best fit** picks the tightest bin, which can pack
    /// differently. The criterion only asserts both produce patterns without throwing — **pinning the exact difference would be brittle**.
    /// </remarks>
    [Fact]
    public void InitialPatterns_ShouldProducePatternsForBothFitStrategies() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0,
            items: [("I1", 4.0, 1), ("I2", 3.0, 1), ("I3", 3.0, 1)],
            spacing: 0.0);
        var context = new PatternGenerationContext<Flt64>();

        Result<IReadOnlyList<PackingPattern<Flt64>>, ErrorCode, Error<ErrorCode>> firstFit =
            context.InitialPatterns(problem, bestFit: false);
        Result<IReadOnlyList<PackingPattern<Flt64>>, ErrorCode, Error<ErrorCode>> bestFitResult =
            context.InitialPatterns(problem, bestFit: true);

        firstFit.IsOk.Should().BeTrue(firstFit.ToString());
        bestFitResult.IsOk.Should().BeTrue(bestFitResult.ToString());
        ((Ok<IReadOnlyList<PackingPattern<Flt64>>, ErrorCode, Error<ErrorCode>>)firstFit).Value
            .Should().NotBeEmpty();
        ((Ok<IReadOnlyList<PackingPattern<Flt64>>, ErrorCode, Error<ErrorCode>>)bestFitResult).Value
            .Should().NotBeEmpty();
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="binLength">箱长（毫米）/ Bin length in millimetres.</param>
    /// <param name="items">物品：(标识, 长度, 需求) / Items: (identity, length, demand).</param>
    /// <param name="spacing">间距（毫米）/ Spacing in millimetres.</param>
    /// <returns>问题 / The problem.</returns>
    private static PackingProblem<Flt64> MakeProblem(
        double binLength,
        (string Id, double Length, ulong Amount)[] items,
        double spacing) {
        var itemList = new List<Item<Flt64>>(items.Length);
        foreach ((string id, double length, ulong amount) in items) {
            itemList.Add(new Item<Flt64>(id, Millimetres(length), Grams(1.0), amount));
        }
        return new PackingProblem<Flt64>(
            Items: itemList,
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(binLength), Grams(100000.0), new Flt64(1.0)),
            },
            Spacing: Millimetres(spacing));
    }

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
