// 方案定价判据 / Criteria for pattern pricing.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.PatternAssignment;

/// <summary>
/// **方案定价** / Pattern pricing.
/// </summary>
/// <remarks>
/// 对应基线 `PatternAssignmentContext` 的定价半边：`cost`、`reducedCost`、`reducedCosts`、
/// `refreshShadowPrice`。判据点出三处要害：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | **非有限影子价格** | 收下 | 之后每个检验数都变 `NaN`，比较恒假 ⇒ **列生成静默停止，报告"已收敛"** |
/// | **批量定价不按对偶行顺序累加** | 按字典序 | 浮点加法不结合 ⇒ 同一候选**两个不同的检验数**，加入与否取决于走哪条路 |
/// | **`inventory:` 键不带箱型** | 前缀匹配 | 所有箱型的库存价都减掉 ⇒ 检验数集体偏低，**该加的列不加** |
///
/// Mirrors the baseline's pricing half; the three failure modes are in the table above.
/// </remarks>
public class PatternAssignmentPricingTests {
    /// <summary>**`BinCount` 口径的系数恒为 1** / Under `BinCount` the coefficient is always 1.</summary>
    [Fact]
    public void Cost_ShouldBeOneUnderBinCount() {
        PatternAssignmentContext<Flt64> context = MakeContext(PackingObjective.BinCount, binCost: 7.0);

        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = context.Cost(Pattern("B1", ("I1", 1)));

        cost.IsOk.Should().BeTrue(cost.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value.ToDouble().Should().BeApproximately(1.0, 1e-12);
    }

    /// <summary>**`ConsumedLength` 口径的系数是箱长换算到米** / Under `ConsumedLength` the coefficient is the bin length in metres.</summary>
    /// <remarks>
    /// 箱长 `2000` 毫米 ⇒ **2** 米。若跳过单位换算，系数会是 `2000`——**目标量级错三个数量级**，
    /// 而求解器不会报错，它只会返回一个"最优"的错解。
    ///
    /// A bin length of `2000` mm is **2** m. Skipping the conversion leaves `2000` — **three orders of magnitude off**, and the solver never complains; it just returns a
    /// confidently wrong optimum.
    /// </remarks>
    [Fact]
    public void Cost_ShouldConvertTheBinLengthToMetres() {
        PatternAssignmentContext<Flt64> context = MakeContext(PackingObjective.ConsumedLength, binCost: 7.0);

        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = context.Cost(Pattern("B1", ("I1", 1)));

        cost.IsOk.Should().BeTrue(cost.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value.ToDouble().Should().BeApproximately(
            2.0, 1e-12, "2000 mm is 2 m");
    }

    /// <summary>**`Cost` 口径的系数是箱单价** / Under `Cost` the coefficient is the unit bin cost.</summary>
    [Fact]
    public void Cost_ShouldBeTheUnitBinCostUnderCost() {
        PatternAssignmentContext<Flt64> context = MakeContext(PackingObjective.Cost, binCost: 7.0);

        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = context.Cost(Pattern("B1", ("I1", 1)));

        cost.IsOk.Should().BeTrue(cost.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value.ToDouble().Should().BeApproximately(7.0, 1e-12);
    }

    /// <summary>**未知箱型返回失败，不是零** / An unknown bin type fails instead of returning zero.</summary>
    /// <remarks>
    /// 返回零会让"箱型写错"看起来像"这个方案不要钱"——那个方案会被**优先**选中。
    ///
    /// Returning zero would make a wrong bin type look like "this pattern is free", so it gets **preferentially** selected.
    /// </remarks>
    [Fact]
    public void Cost_ShouldFailOnAnUnknownBinType() {
        PatternAssignmentContext<Flt64> context = MakeContext(PackingObjective.Cost, binCost: 7.0);

        context.Cost(Pattern("NOPE", ("I1", 1))).IsFailed.Should().BeTrue();
    }

    /// <summary>**非有限的影子价格必须被拒** / Non-finite shadow prices must be rejected.</summary>
    /// <remarks>
    /// **这是这里最重要的一条。** 一个 `NaN` 价格会让之后**每个**检验数都变 `NaN`；
    /// `NaN` 与原目标值比较恒为假 ⇒ **所有候选看起来都"不值得加入"** ⇒ 列生成**静默停止**，
    /// 而报告说的是"已收敛"。**这是最坏的一种失败：它看起来像正常结束。**
    ///
    /// **The most important criterion in.** A `NaN` price turns **every** later reduced cost into `NaN`; every comparison with `NaN` is false ⇒ **every
    /// candidate looks "not worth adding"** ⇒ column generation **stops silently** while the report says "converged". **The worst kind of failure: it looks like a
    /// normal finish.**
    /// </remarks>
    [Fact]
    public void RefreshShadowPrice_ShouldRejectANonFinitePrice() {
        PatternAssignmentContext<Flt64> context = MakeContext(PackingObjective.BinCount, binCost: 7.0);

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            context.RefreshShadowPrice(Dual(("demand:I1", double.NaN), ("inventory:B1", 0.5)));

        result.IsFailed.Should().BeTrue("a NaN dual price must never become the shadow price");
        context.ShadowPrice.Should().BeSameAs(
            MetaDualSolution.Empty, "a rejected update must leave the previous prices untouched");
    }

    /// <summary>**检验数 = 系数 − 需求贡献 − 库存贡献 + 扩展项** / The reduced cost is cost minus demand and inventory contributions plus extensions.</summary>
    /// <remarks>
    /// 系数 `1`（`BinCount`），需求价 `0.3` 且方案含 2 件 `I1` ⇒ 减 `0.6`；
    /// 该箱型库存价 `0.5` ⇒ 再减 `0.5`；扩展项加 `0.25`。
    /// 合计 `1 − 0.6 − 0.5 + 0.25 = 0.15`。
    ///
    /// Coefficient `1` (`BinCount`), demand price `0.3` with two `I1` pieces ⇒ subtract `0.6`; that bin type's inventory price `0.5` ⇒ subtract `0.5`; the extension adds
    /// `0.25`; total `1 − 0.6 − 0.5 + 0.25 = 0.15`.
    /// </remarks>
    [Fact]
    public void ReducedCost_ShouldCombineDemandInventoryAndExtensions() {
        PatternAssignmentContext<Flt64> context = MakeContext(
            PackingObjective.BinCount, binCost: 7.0, extensions: [new ConstantExtension(0.25)]);
        context.RefreshShadowPrice(Dual(("demand:I1", 0.3), ("inventory:B1", 0.5))).IsOk.Should().BeTrue();

        Flt64 reduced = context.ReducedCost(Pattern("B1", ("I1", 2)));

        reduced.ToDouble().Should().BeApproximately(0.15, 1e-12, "1 - 0.6 - 0.5 + 0.25");
    }

    /// <summary>**`inventory:` 只减该方案自己的箱型** / The `inventory:` price is subtracted for the pattern's own bin type only.</summary>
    /// <remarks>
    /// 两个箱型都有库存价（`B1` 是 `0.5`、`B2` 是 `0.2`）。方案用 `B1` ⇒ 只减 `0.5`。
    /// 若按键前缀匹配，会把两者都减掉 ⇒ 检验数偏 `0.2`，**该加的列不加**。
    ///
    /// Both bin types carry an inventory price (`B1` is `0.5`, `B2` is `0.2`); a pattern on `B1` subtracts only `0.5`. Prefix matching would subtract both, shifting the
    /// reduced cost by `0.2` and **dropping columns that should be added**.
    /// </remarks>
    [Fact]
    public void ReducedCost_ShouldSubtractOnlyThePatternOwnBinTypeInventory() {
        PatternAssignmentContext<Flt64> context =
            MakeContext(PackingObjective.BinCount, binCost: 7.0, secondBinCost: 5.0);
        context.RefreshShadowPrice(Dual(("inventory:B1", 0.5), ("inventory:B2", 0.2))).IsOk.Should().BeTrue();

        Flt64 reduced = context.ReducedCost(Pattern("B1", ("I1", 1)));

        reduced.ToDouble().Should().BeApproximately(0.5, 1e-12, "1 - 0.5, and B2's price must not apply");
    }

    /// <summary>**批量定价必须与单件重载逐位一致** / Batched pricing must match the single-item overload bit for bit.</summary>
    /// <remarks>
    /// **这条判据的全部意义在浮点加法不满足结合律**：
    /// `(0.1 + 0.2) + 0.3 = 0.6000000000000001`，而 `(0.3 + 0.2) + 0.1 = 0.6`。
    /// 若批量版按 `pattern.amounts` 的字典序累加，同一个候选会得到**与单件版不同的值**——
    /// 于是**加入与否取决于走哪条路**。
    ///
    /// 夹具用**同一个货物的三条需求行**（价格 `0.1`、`0.2`、`0.3`），并让方案里货物键的顺序
    /// （`I2` 在前）与对偶行顺序（`I1` 的三条在前）**不同**，这样"按字典序累加"会真的算错。
    ///
    /// **The whole point is that floating-point addition is not associative**: `(0.1 + 0.2) + 0.3 = 0.6000000000000001` while `(0.3 + 0.2) + 0.1 = 0.6`.
    /// Accumulating in `pattern.amounts` dictionary order gives a candidate **a different value from the single-item path**, so **whether it is added depends on the path
    /// taken**. The fixture uses **three demand rows for one item** (prices `0.1`, `0.2`, `0.3`) with the pattern's item keys ordered (`I2` first) **differently from the dual
    /// rows** (`I1`'s three rows first), so dictionary-order accumulation genuinely miscomputes.
    /// </remarks>
    [Fact]
    public void ReducedCosts_ShouldMatchTheSingleItemOverloadBitForBit() {
        PatternAssignmentContext<Flt64> context = MakeContext(
            PackingObjective.BinCount, binCost: 7.0, extensions: [new ConstantExtension(0.25)]);
        // **价格必须让"顺序"真的改变结果。** `1e16` 与 `-1e16` 之间的 `1.0` 会被吃掉：
        // 先减 `1e16` 再减 `-1e16` 最后减 `1.0` 得 `-1`；反过来得 `0`。
        // 第一版用 `0.1/0.2/0.3`，两种顺序在 double 下**恰好同值**，判据无从区分。
        //
        // **The prices must make order actually change the result.** A `1.0` sitting between `1e16` and `-1e16` is absorbed: subtracting `1e16`, then `-1e16`, then `1.0`
        // gives `-1`, while the reverse order gives `0`. The first version used `0.1/0.2/0.3`, where both orders **happen to land on the same double**.
        context.RefreshShadowPrice(Dual(
            ("demand:I1", 1e16),
            ("demand:I1", -1e16),
            ("demand:I2", 1.0))).IsOk.Should().BeTrue();

        // **货物键顺序 `I2` 在 `I1` 之前**，与对偶行顺序相反。
        // **The pattern lists `I2` before `I1`**, opposite to the dual-row order.
        PackingPattern<Flt64> pattern = PatternWithAmounts("B1", ("I2", 1), ("I1", 2));
        var patterns = new List<PackingPattern<Flt64>> { pattern };

        IReadOnlyList<Flt64> batched = context.ReducedCosts(patterns);

        batched.Should().HaveCount(1);
        batched[0].ToDouble().Should().Be(
            context.ReducedCost(pattern).ToDouble(),
            "the batched and single-item paths must agree bit for bit, or the same candidate is priced two ways");
    }

    /// <summary>构造上下文 / Build a context.</summary>
    /// <param name="objective">目标口径 / Objective metric.</param>
    /// <param name="binCost">箱单价 / Unit bin cost.</param>
    /// <param name="secondBinCost">第二箱型单价 / Second bin type cost.</param>
    /// <param name="extensions">扩展 / Extensions.</param>
    /// <returns>上下文 / The context.</returns>
    private static PatternAssignmentContext<Flt64> MakeContext(
        PackingObjective objective,
        double binCost,
        double? secondBinCost = null,
        IReadOnlyList<IPatternModelingExtension<Flt64>>? extensions = null) {
        var binTypes = new List<BinType<Flt64>> {
            new("B1", Millimetres(2000.0), Kilograms(10.0), new Flt64(binCost)),
        };
        if (secondBinCost is { } cost) {
            binTypes.Add(new BinType<Flt64>("B2", Millimetres(1500.0), Kilograms(10.0), new Flt64(cost)));
        }
        var problem = new PackingProblem<Flt64>(
            Items: new List<Item<Flt64>> { new("I1", Millimetres(100.0), Kilograms(1.0), 5UL) },
            BinTypes: binTypes,
            Objective: objective,
            Spacing: Millimetres(0.0));
        return new PatternAssignmentContext<Flt64>(problem, Flt64PackingValueAdapter.Instance, extensions);
    }

    /// <summary>构造对偶解 / Build a dual solution.</summary>
    /// <param name="prices">约束参数到价格 / Constraint args to prices.</param>
    /// <returns>对偶解 / The dual solution.</returns>
    /// <remarks>
    /// **`name` 必须逐行唯一。** `LinearInequalityConstraint` 是 `record`，**按值相等**——
    /// 三行参数相同、内容相同的 `demand:I1` 会**塌缩成一个字典键**，于是夹具里"三条需求行"
    /// 实际只有一条，**按对偶行顺序累加**那条判据也就无从区分。
    /// 这里第一版正是这样，导致"不排序"的变异存活。
    ///
    /// **`name` must be unique per row.** `LinearInequalityConstraint` is a `record` with **value equality** — three identical `demand:I1` rows **collapse into one
    /// dictionary key**, so the fixture's "three demand rows" were really one and the dual-row-order criterion could not discriminate. That is exactly how the first
    /// version of let the "no sorting" mutant survive.
    /// </remarks>
    private static MetaDualSolution Dual(params (string Args, double Price)[] prices) {
        var constraints = new Dictionary<MathConstraint, Flt64>();
        int rowIndex = 0;
        foreach ((string args, double price) in prices) {
            var row = new LinearInequalityConstraint<Flt64>(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
                    new LinearPolynomial<Flt64>(new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE),
                args: args,
                name: $"row_{rowIndex}");
            constraints[row] = new Flt64(price);
            rowIndex++;
        }
        return new MetaDualSolution(constraints,
            new Dictionary<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol,
                IReadOnlyList<(Fuookami.Ospf.Core.Model.Mechanism.IConstraint<Flt64, Fuookami.Ospf.Math.Symbol.LinearCategory> Constraint, Flt64 Price)>>());
    }

    /// <summary>构造方案 / Build a pattern.</summary>
    /// <param name="binTypeId">箱型 / Bin type.</param>
    /// <param name="amounts">件数 / Amounts.</param>
    /// <returns>方案 / The pattern.</returns>
    private static PackingPattern<Flt64> Pattern(string binTypeId, params (string Item, ulong Amount)[] amounts) =>
        PatternWithAmounts(binTypeId, amounts);

    /// <summary>按给定键顺序构造方案 / Build a pattern with the given key order.</summary>
    /// <param name="binTypeId">箱型 / Bin type.</param>
    /// <param name="amounts">件数，顺序即字典插入顺序 / Amounts in insertion order.</param>
    /// <returns>方案 / The pattern.</returns>
    private static PackingPattern<Flt64> PatternWithAmounts(string binTypeId, params (string Item, ulong Amount)[] amounts) {
        var map = new Dictionary<string, ulong>();
        foreach ((string item, ulong amount) in amounts) {
            map[item] = amount;
        }
        return new PackingPattern<Flt64>(binTypeId, map);
    }

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    /// <remarks>
    /// **重量单位用 SI 基准的千克。** 这里的判据只关心**箱长**的量纲换算，
    /// 重量的具体单位不影响系数；用基准单位可以少一次换算，也就少一处可能的误差源。
    ///
    /// **The weight unit is the SI-base kilogram**: these criteria only care about the **bin length** conversion, the exact mass unit does not affect the coefficient, and
    /// using the base unit removes one conversion and with it one possible error source.
    /// </remarks>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);

    /// <summary>固定附加检验数的扩展 / An extension adding a fixed reduced-cost contribution.</summary>
    private sealed class ConstantExtension : IPatternModelingExtension<Flt64> {
        private readonly double _value;

        /// <summary>构造 / Constructor.</summary>
        /// <param name="value">附加检验数 / The additional reduced cost.</param>
        public ConstantExtension(double value) {
            _value = value;
        }

        /// <inheritdoc/>
        public Flt64 ReducedCost(PackingPattern<Flt64> pattern, PatternAssignmentContext<Flt64> context, MetaDualSolution dual) =>
            new(_value);
    }
}
