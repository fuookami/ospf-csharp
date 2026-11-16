// 影子价格回填的判据 / Criteria for shadow-price refresh.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Produce;

/// <summary>
/// **影子价格回填** / Shadow-price refresh.
/// </summary>
/// <remarks>
/// 判据的重点：**只有挂键的行才有价格**、**按键累加而不是覆盖**、**对偶表里没有时什么都不写**、**表未建立时跳过整段**。
///
/// The criteria focus on **only a keyed row getting a price**, on **accumulating per key rather than overwriting**, on **writing nothing when the dual table lacks the row**, and on **skipping the
/// whole block when the table was never built**.
/// </remarks>
public class ProduceShadowPriceRefreshTests {
    private sealed class TestShadowPriceMap
        : AbstractShadowPriceMap<IGanttSchedulingShadowPriceArguments<TestExecutor, TestAssignmentPolicy>, TestShadowPriceMap> { }

    private static MetaDualSolution Dual(IReadOnlyDictionary<MathConstraint, Flt64> constraints) =>
        new(constraints, new Dictionary<
            Core.Symbol.IIntermediateSymbol,
            IReadOnlyList<(IConstraint<Flt64, Fuookami.Ospf.Math.Symbol.LinearCategory> Constraint, Flt64 Price)>>());

    private static MathConstraint Row(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints.Single(constraint => constraint.Name == name);

    /// <summary>
    /// **只有挂键的行才有价格；没提到的行不写** / Only a keyed row gets a price, and a row the dual table omits is not written.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 约束行**都挂**了 `ShadowPriceKey`。
    /// 2. 对偶表里给上界行一个价格 → 映射里出现**那一个**键，值是给的那个。
    /// 3. 下界行**没给**价格 → 不出现——不能写成零价，零价会被后续过滤掉，看起来像"算过"。
    /// 4. 映射只有一条。
    ///
    /// Four paired points: the constraint rows **carry** a `ShadowPriceKey` (attached by ); giving the upper row a price in the dual table makes **that one** key appear with that value; the lower
    /// row **is not given** a price and therefore does not appear, since writing a zero price would be filtered out later and look like "already computed"; and the map holds exactly one entry.
    /// </remarks>
    [Fact]
    public void Refresh_ShouldWriteOnlyTheRowsTheDualTableMentions() {
        var product = new TestProduct("Widget", 0);
        // **第二个产品完全不出现在对偶表里**：它是"不能写零价"那条的唯一探针——写零价会给它凭空造出一个键。
        // **A second product entirely absent from the dual table**: it is the only probe for "no zero price", since writing a zero would invent a key for it.
        var untouched = new TestProduct("Gadget", 1);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), null, new Flt64(10.0));
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)demand), (untouched, (MaterialDemand?)demand) });
        using var model = new LinearMetaModel<Flt64>();
        produce.Register(model).IsOk.Should().BeTrue();
        new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)demand), (untouched, (MaterialDemand?)demand) }, produce)
            .Invoke(model).IsOk.Should().BeTrue();

        MathConstraint upper = Row(model, "produce_quantity_ub_0");
        MathConstraint lower = Row(model, "produce_quantity_lb_0");
        upper.Args.Should().BeOfType<ProduceQuantityShadowPriceKey>("B704 attaches the key to the row");
        lower.Args.Should().BeOfType<ProduceQuantityShadowPriceKey>();
        lower.Args.Should().Be(upper.Args, "both bounds of one product share the key");

        var map = new TestShadowPriceMap();
        MetaDualSolution dual = Dual(new Dictionary<MathConstraint, Flt64> { [upper] = new Flt64(3.5) });

        Result<Success, ErrorCode, Error<ErrorCode>> refreshed = produce.Refresh(map, model, dual);
        refreshed.IsOk.Should().BeTrue("refresh failed: " + refreshed);

        // **两条行共用同一个键**：`ProduceQuantityShadowPriceKey` 按 `Product.Index` 判等，
        // 上界与下界是同一个产品的两行，键必然相等。所以"没提到的那一行不写"只能靠**总数**来看——
        // 若把下界也写成零价，总数会变成两条（`PutOrAdd` 会新建一个键？不会，键相同，所以仍是一条）。
        // 真正能分辨的是**值**：只有上界的 3.5，说明下界那行没被算进去。
        // **Both rows share one key**: `ProduceQuantityShadowPriceKey` compares by `Product.Index`, so the upper and lower rows of one product necessarily have equal keys. "The unmentioned row is not
        // written" is therefore observed through the **value**: 3.5 rather than 5.0 proves the lower row did not contribute.
        var upperKey = (ShadowPriceKey)upper.Args!;
        map.Map.Should().ContainKey(upperKey);
        map.Map[upperKey].Price.ToDouble().Should().Be(3.5,
            "the lower row is absent from the dual table and must not add anything");
        map.Map.Should().HaveCount(1);
    }

    /// <summary>
    /// **按键累加，不是覆盖** / Prices accumulate per key rather than overwriting.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 同一个键被**两条行**命中（上界与下界都挂着同一个产品的键）→ 映射里是**它们的和** 5.0。
    /// 2. 同一张映射被回填两次 → 累加成 10.0（`PutOrAdd` 的语义），不是保持 5.0。
    /// 3. 只有**一个**键——两条行属于同一个产品，不该变成两个键。
    ///
    /// Three paired points: one key hit by **two rows** (both bounds carry the same product's key) makes the map hold **their sum** 5.0; refreshing the same map twice accumulates to 10.0 (the semantics
    /// of `PutOrAdd`) rather than staying at 5.0; and there is only **one** key, since both rows belong to the same product and must not become two.
    /// </remarks>
    [Fact]
    public void Refresh_ShouldAccumulateRatherThanOverwrite() {
        var product = new TestProduct("Widget", 0);
        // **超量必须启用**：`Tables` 只回填超量 / 不足两张表，不启用超量时表是 `null`，一条都取不到。
        // **Over must be enabled**: `Tables` refreshes only the over/less tables, so with over disabled the table is null and nothing is taken.
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0));
        var produce = new BunchSchedulingProduce<TestProduct>(new[] { (product, (MaterialDemand?)demand) });
        using var model = new LinearMetaModel<Flt64>();
        produce.Register(model).IsOk.Should().BeTrue();
        new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce).Invoke(model).IsOk.Should().BeTrue();

        MathConstraint upper = Row(model, "produce_quantity_ub_0");
        MathConstraint lower = Row(model, "produce_quantity_lb_0");
        var map = new TestShadowPriceMap();
        MetaDualSolution dual = Dual(new Dictionary<MathConstraint, Flt64> {
            [upper] = new Flt64(3.5),
            [lower] = new Flt64(1.5),
        });

        produce.Refresh(map, model, dual).IsOk.Should().BeTrue();
        produce.OverQuantity.Should().NotBeNull("the over table must exist for this criterion to mean anything");
        map.Map.Should().HaveCount(1);
        var key = (ShadowPriceKey)upper.Args!;
        map.Map[key].Price.ToDouble().Should().Be(5.0, "the two rows of one product add up");

        produce.Refresh(map, model, dual).IsOk.Should().BeTrue();
        map.Map[key].Price.ToDouble().Should().Be(10.0, "a second refresh accumulates");
    }

    /// <summary>
    /// **表未建立时跳过整段，不是失败** / An unbuilt table skips the whole block rather than failing.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 未注册就 `Refresh` → **返回 `Ok`**（`Refresh` 可能在注册之前被调用，那时没有表可回填）。
    /// 2. 映射保持空——不能凭空写出价格。
    /// 3. 注册并加行之后同一个调用就有内容了，所以"空"是因为没表，不是因为回填不工作。
    ///
    /// Three paired points: calling `Refresh` before registration returns **`Ok`**, since `Refresh` may run before registration when there is no table to fill; the map stays empty rather than
    /// inventing prices; and after registration plus row construction the same call does produce entries, so the emptiness was "no table", not "refresh is broken".
    /// </remarks>
    [Fact]
    public void Refresh_ShouldSkipBeforeRegistration() {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0));
        var produce = new BunchSchedulingProduce<TestProduct>(new[] { (product, (MaterialDemand?)demand) });
        using var model = new LinearMetaModel<Flt64>();
        var map = new TestShadowPriceMap();

        produce.Refresh(map, model, Dual(new Dictionary<MathConstraint, Flt64>()))
            .IsOk.Should().BeTrue("refresh before registration is not an error");
        map.Map.Should().BeEmpty();

        produce.Register(model).IsOk.Should().BeTrue();
        new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)demand) }, produce).Invoke(model).IsOk.Should().BeTrue();
        MathConstraint upper = Row(model, "produce_quantity_ub_0");
        produce.Refresh(map, model, Dual(new Dictionary<MathConstraint, Flt64> { [upper] = new Flt64(2.0) }))
            .IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(1, "after registration the same call does fill the map");
    }

    /// <summary>
    /// **消费侧同样回填，且用消费的键类型** / The consumption side refreshes too, with its own key type.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 消费的 `Refresh` 把价格写进映射。
    /// 2. 写进去的键是 `ConsumptionQuantityShadowPriceKey`，不是生产的——两个模块的影子价格不能串。
    /// 3. 参数类型不对时**返回 `Failed`**：`null` 抛，`new object` 作为映射 / 模型 / 对偶都失败。
    ///
    /// Three paired points: the consumption `Refresh` writes the price into the map; the key written is a `ConsumptionQuantityShadowPriceKey` rather than the produce one, since the two modules'
    /// shadow prices must not cross; and wrong argument types **return `Failed`** — `null` throws, while `new object` as the map, model or dual all fail.
    /// </remarks>
    [Fact]
    public void Refresh_ShouldWorkOnTheConsumptionSide() {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(new Flt64(10.0), new Flt64(100.0), null, new Flt64(10.0));
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(new[] { (material, (MaterialReserves?)reserves) });
        using var model = new LinearMetaModel<Flt64>();
        consumption.Register(model).IsOk.Should().BeTrue();
        new ConsumptionQuantityConstraint<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) }, consumption).Invoke(model).IsOk.Should().BeTrue();

        MathConstraint upper = Row(model, "consumption_quantity_ub_0");
        var map = new TestShadowPriceMap();
        consumption.Refresh(map, model, Dual(new Dictionary<MathConstraint, Flt64> { [upper] = new Flt64(7.0) }))
            .IsOk.Should().BeTrue();

        map.Map.Should().HaveCount(1);
        ShadowPriceKey key = map.Map.Keys.Single();
        key.Should().BeOfType<ConsumptionQuantityShadowPriceKey>("the two modules must not share a key type");
        map.Map[key].Price.ToDouble().Should().Be(7.0);

        Action nullMap = () => consumption.Refresh(null!, model, new object());
        nullMap.Should().Throw<ArgumentNullException>();
        consumption.Refresh(map, model, new object()).IsFailed.Should().BeTrue("a non-dual third argument is rejected");
        consumption.Refresh(new object(), model, Dual(new Dictionary<MathConstraint, Flt64>()))
            .IsFailed.Should().BeTrue("a non-map first argument is rejected");
        consumption.Refresh(map, new object(), Dual(new Dictionary<MathConstraint, Flt64>()))
            .IsFailed.Should().BeTrue("a non-model second argument is rejected");
    }
}
