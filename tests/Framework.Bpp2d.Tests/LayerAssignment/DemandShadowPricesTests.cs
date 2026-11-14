// 需求影子价格提取（B662）的判据 / Criteria for the demand shadow-price extraction (B662).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **需求影子价格提取（B662）** / Demand shadow-price extraction (B662).
/// </summary>
/// <remarks>
/// 判据的重点：**取下界约束的对偶值而不是上界**、**缺项跳过而不补零**、**键与约束名同源**、
/// **索引对齐需求项位置**。
///
/// The criteria focus on **taking the lower-bound constraint's dual rather than the upper's**, on **skipping missing entries rather than filling zero**, on **the key sharing its source with
/// the constraint name**, and on **the index aligning with the demand entry's position**.
/// </remarks>
public class DemandShadowPricesTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Bpp2dAssignmentDemandEntry AmountDemand(string itemId, ulong amount, ulong? upper = null) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(amount),
            upper is null
                ? null
                : new Bpp2dAssignmentDemandValue.Amount(upper.Value));

    /// <summary>
    /// 建一个带需求约束的模型，返回模型与它的约束 / Build a model carrying demand constraints and return it with its constraints.
    /// </summary>
    /// <remarks>
    /// **走真实的 `RegisterRmp`**，不手工拼约束名——判据要验证"提取逻辑与登记逻辑对得上"，
    /// 手工拼名字会让两边同时写错而看不出。
    ///
    /// **Goes through the real `RegisterRmp`** rather than hand-assembling constraint names: the criterion must verify that extraction and registration agree, and hand-built names would let both
    /// sides drift identically without showing.
    /// </remarks>
    private static (LinearMetaModel<Flt64> Model, IReadOnlyList<Bpp2dAssignmentDemandEntry> Demands) Fixture(
        params Bpp2dAssignmentDemandEntry[] demands) {
        LinearMetaModel<Flt64> model = new("rmp");
        var context = new LayerAssignmentContext();
        context.RegisterRmp(model, demands).IsOk.Should().BeTrue();
        return (model, demands);
    }

    private static MetaDualSolution Duals(LinearMetaModel<Flt64> model, Func<string, double?> price) {
        var constraints = new Dictionary<MathConstraint, Flt64>();
        foreach (MathConstraint constraint in model.Constraints) {
            double? value = price(constraint.Name);
            if (value is not null) {
                constraints[constraint] = new Flt64(value.Value);
            }
        }
        return new MetaDualSolution(
            constraints,
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
    }

    /// <summary>
    /// **取下界那条的对偶值，键是模式与物品** / Take the lower bound's dual, keyed by mode and item.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 下界给 `3.5`、上界给 `9.0` → 结果是 **3.5**，不是 9。取错上下界会让检验数整体偏符号。
    /// 2. 键是 `(item_amount, I1)`，与约束名里的 `item_amount` 同源。
    /// 3. 值从 `Flt64` 转成 `FltX`，数值不变。
    /// 4. 两条需求 → 表里两项，各归各的键。
    ///
    /// Four paired points: with the lower bound priced `3.5` and the upper `9.0` the result is **3.5**, not 9, since swapping the bounds flips the sign of every reduced cost; the key is
    /// `(item_amount, I1)`, sharing its source with the name's `item_amount`; the `Flt64` value converts to `FltX` unchanged; and two demands yield two entries under distinct keys.
    /// </remarks>
    [Fact]
    public void Extract_ShouldTakeTheLowerBoundDual() {
        Bpp2dAssignmentDemandEntry[] demands =
        {
            AmountDemand("I1", 4UL, upper: 9UL),
            AmountDemand("I2", 5UL),
        };
        (LinearMetaModel<Flt64> model, IReadOnlyList<Bpp2dAssignmentDemandEntry> entries) = Fixture(demands);
        MetaDualSolution duals = Duals(model, name =>
            name.EndsWith("_lb_0", StringComparison.Ordinal) ? 3.5
            : name.EndsWith("_lb_1", StringComparison.Ordinal) ? 7.25
            : name.EndsWith("_ub_0", StringComparison.Ordinal) ? 9.0
            : null);

        IReadOnlyDictionary<DemandModeKey, FltX> prices = DemandShadowPrices.Extract(entries, duals);

        prices.Should().HaveCount(2);
        prices[new DemandModeKey("item_amount", "I1")].ToFlt64().ToDouble()
            .Should().BeApproximately(3.5, 1e-9, "the lower bound carries the demand price, not the upper");
        prices[new DemandModeKey("item_amount", "I2")].ToFlt64().ToDouble()
            .Should().BeApproximately(7.25, 1e-9);
    }

    /// <summary>
    /// **前缀真的被用上** / The prefix is genuinely used.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 用**非默认前缀** `custom_demand` 登记约束，再按同一前缀提取 → 拿到价格。
    /// 2. 按**默认前缀** `demand` 提取 → **空表**：两种前缀是不同的名字，默认值不能悄悄顶替。
    /// 3. 这一条是为杀掉"忽略前缀"的变异——**默认前缀就是 `demand`，用默认值时忽略前缀也照样通过**（B649/B661 同类）。
    ///
    /// Three paired points: constraints registered under the **non-default** prefix `custom_demand` and extracted with the same prefix **do** yield prices; extracting with the **default** prefix
    /// `demand` yields **an empty table**, since the two are different names and the default must not quietly stand in; and this case exists to kill the "prefix ignored" mutant, because **the
    /// default prefix is already `demand` and with it an implementation that ignores the prefix still passes** (the B649/B661 family).
    /// </remarks>
    [Fact]
    public void Extract_ShouldHonourTheConstraintNamePrefix() {
        Bpp2dAssignmentDemandEntry[] demands = { AmountDemand("I1", 4UL) };
        LinearMetaModel<Flt64> model = new("prefixed");
        var context = new LayerAssignmentContext();
        Result<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>> rmp = context.RegisterRmp(model, demands);
        rmp.IsOk.Should().BeTrue();
        RmpAssignmentArtifacts artifacts = ((Ok<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>>)rmp).Value;

        // **用自定义前缀再登记一份约束**：默认名那份仍在，两者是不同的名字。
        // **Register another copy under a custom prefix**: the default-named one remains and the two are distinct names.
        new DemandConstraint(artifacts.Load, demands, "custom_demand").Invoke(model).IsOk.Should().BeTrue();

        MetaDualSolution duals = Duals(model, name =>
            name == "custom_demand_item_amount_lb_0" ? 4.5
            : name == "demand_item_amount_lb_0" ? 1.25
            : null);

        IReadOnlyDictionary<DemandModeKey, FltX> custom =
            DemandShadowPrices.Extract(demands, duals, "custom_demand");
        custom.Should().HaveCount(1);
        custom[new DemandModeKey("item_amount", "I1")].ToFlt64().ToDouble()
            .Should().BeApproximately(4.5, 1e-9, "the custom prefix addresses its own constraint");

        IReadOnlyDictionary<DemandModeKey, FltX> defaults = DemandShadowPrices.Extract(demands, duals);
        defaults[new DemandModeKey("item_amount", "I1")].ToFlt64().ToDouble()
            .Should().BeApproximately(1.25, 1e-9, "the default prefix addresses the default-named constraint");
    }

    /// <summary>
    /// **缺项跳过，不补零；索引对齐需求项位置** / Missing entries are skipped, and the index aligns with the entry's position.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 只有第二条需求有对偶值 → **表里只有一项**，缺的那条不出现（补零会宣称它的价格是零）。
    /// 2. 那一项的键是**第二条需求的键**——索引要对齐需求项位置，用错索引会把价格安到别的物品上。
    /// 3. 一条对偶值都没有 → **空表**，不抛。
    /// 4. 面积需求与件数需求同时存在 → 两个键不同（模式不同就是不同的键）。
    ///
    /// Four paired points: with only the second demand carrying a dual the table holds **one entry** and the missing one is absent, since a zero would claim its price is zero; that entry's key is
    /// **the second demand's**, because a wrong index would attach the price to another item; no duals at all yield **an empty table** rather than a throw; and an area demand beside an amount
    /// demand yields two distinct keys, since different modes are different keys.
    /// </remarks>
    [Fact]
    public void Extract_ShouldSkipMissingEntriesAndAlignIndexes() {
        Bpp2dAssignmentDemandEntry[] demands =
        {
            AmountDemand("I1", 4UL),
            AmountDemand("I2", 5UL),
        };
        (LinearMetaModel<Flt64> model, IReadOnlyList<Bpp2dAssignmentDemandEntry> entries) = Fixture(demands);
        MetaDualSolution onlySecond = Duals(model, name =>
            name.EndsWith("_lb_1", StringComparison.Ordinal) ? 2.0 : null);

        IReadOnlyDictionary<DemandModeKey, FltX> prices = DemandShadowPrices.Extract(entries, onlySecond);
        prices.Should().HaveCount(1, "a missing dual is skipped rather than filled with zero");
        prices.Should().ContainKey(new DemandModeKey("item_amount", "I2"),
            "the index must follow the demand entry's position");
        prices.Should().NotContainKey(new DemandModeKey("item_amount", "I1"));

        IReadOnlyDictionary<DemandModeKey, FltX> none = DemandShadowPrices.Extract(
            entries, Duals(model, _ => null));
        none.Should().BeEmpty("no duals at all is an empty table, not a throw");

        Bpp2dAssignmentDemandEntry[] mixed =
        {
            AmountDemand("I1", 4UL),
            new(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("I1"),
                new Bpp2dAssignmentDemandValue.Area(Q(500.0)), null),
        };
        (LinearMetaModel<Flt64> mixedModel, IReadOnlyList<Bpp2dAssignmentDemandEntry> mixedEntries) = Fixture(mixed);
        IReadOnlyDictionary<DemandModeKey, FltX> mixedPrices = DemandShadowPrices.Extract(
            mixedEntries,
            Duals(mixedModel, name =>
                name.EndsWith("_lb_0", StringComparison.Ordinal) ? 1.0
                : name.EndsWith("_lb_1", StringComparison.Ordinal) ? 2.0
                : null));
        mixedPrices.Should().HaveCount(2);
        mixedPrices[new DemandModeKey("item_amount", "I1")].ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
        mixedPrices[new DemandModeKey("item_area", "I1")].ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
    }
}
