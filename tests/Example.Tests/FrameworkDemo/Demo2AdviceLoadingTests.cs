// 建议装载松弛的判据 / Criteria for the advised-load slack.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **建议装载松弛** / The advised-load slack.
/// </summary>
/// <remarks>
/// 判据的重点：`SlackFunction` 的单侧配置（`withNegative = false`）、阈值为 `null` 时给恒零、以及两条目标各自
/// **按 `Ala` / `Alw` 过滤**且空集合早退。
///
/// The criteria focus on `SlackFunction`'s one-sided configuration (`withNegative = false`), the constant-zero symbol for a null threshold, and each objective **filtering on
/// `Ala` / `Alw`** with an empty-set early return.
/// </remarks>
public class Demo2AdviceLoadingTests {
    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(
        string name, ulong id, byte order, double? ala = null, double? alw = null) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "N",
            linearLoadingOrder: order,
            coordinate: new PositionCoordinate(100.0 * id, 100.0 * id + 100.0, -60.0, -10.0),
            shape: new PositionShape(100.0, 50.0, 5000.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, false, false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL) {
            Ala = ala,
            Alw = alw,
        };
    }

    private static StowageModel MakeStowage(
        IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
        var allowed = new HashSet<AssignmentKey>();
        foreach (Item item in items) {
            foreach (StowagePosition position in positions) {
                if (StowageModel.StowageNeeded(item, position)) {
                    allowed.Add(new AssignmentKey(item.Id, position.Name));
                }
            }
        }
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: items.Select(item => new LoadingItem<Flt64>(
                item.Id, new Quantity<Flt64>(new Flt64(item.Weight), MassUnits.Gram))).ToList(),
            Positions: positions.Select(position => new LoadingPosition<Flt64>(
                position.Name, position.Mla,
                new Quantity<Flt64>(new Flt64(position.Mlw.Mlw), MassUnits.Gram))).ToList(),
            AllowedAssignments: allowed,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        return new StowageModel(items, positions) {
            LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
        };
    }

    /// <summary>
    /// **有建议值给真松弛，没有给恒零；矩阵维度与舱位数一致** / Real slack with an advice, constant zero without; the matrix matches the position count.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **两个舱位、一个有 `Ala` 一个没有** → 数量松弛矩阵 **2 格**，不是 1 格。跳过会让维度对不上。
    /// 2. 有建议值那格是 `LinearFunctionSymbolAdapter`（真松弛）。
    /// 3. 没有那格是 `LinearExpressionSymbol` 且多项式为空。
    /// 4. 重量松弛同理，**按 `Alw` 独立判断**——有 `Ala` 不等于有 `Alw`。
    ///
    /// Four paired points: **two positions, one with `Ala` and one without**, give a **two-slot** amount matrix rather than one, since skipping would misalign the dimension; the advised slot
    /// is a `LinearFunctionSymbolAdapter` (a real slack); the other is a `LinearExpressionSymbol` with an empty polynomial; and the weight slack is **decided independently by `Alw`**,
    /// since having `Ala` does not imply having `Alw`.
    /// </remarks>
    [Fact]
    public void AdviceLoading_ShouldKeepTheMatrixComplete() {
        var items = new List<Item> { MakeItem("A") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1, ala: 2.0, alw: 500.0), MakePosition("P2", 2UL, 2),
        };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        // **`Load.Register` 必须显式调用**，否则装载量符号表是空的，松弛一律退化成恒零。
        // **`Load.Register` must be called explicitly**, or the load-amount table stays empty and every slack degenerates to zero.
        load.Register(model).IsOk.Should().BeTrue();
        var advice = new AdviceLoading(positions, load);
        advice.Register(model).IsOk.Should().BeTrue();

        advice.AmountSlack(0).Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>(
            "P1 carries an advised amount");
        advice.AmountSlack(1).Should().BeOfType<Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol>(
            "P2 carries none, but still occupies a slot");
        ((Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)advice.AmountSlack(1)!).Polynomial.Monomials
            .Should().BeEmpty("the zero branch is constant");
        advice.WeightSlack(0).Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>();

        // **松弛的配置本身必须钉住**：只看符号类型无法区分单侧/双侧，也无法区分阈值形式与普通形式。
        // **The slack's configuration itself must be pinned**: the symbol type alone cannot distinguish one-sided from two-sided, nor the threshold form from the plain form.
        var slack = (Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>)
            ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)advice.AmountSlack(0)!).Function;
        slack.WithNegative.Should().BeFalse("a lower side would grow the slack when the load is below the advice, inverting the penalty");
        slack.WithPositive.Should().BeTrue("the upper side is the whole point");
        slack.Threshold.Should().BeTrue("the baseline goes through `exampleThresholdSlack`, which passes threshold = true");
        slack.Constraint.Should().BeTrue();

        // **重量松弛放松的是估计装载重量，不是装载量**：两者的单项式集合不同。
        // **The weight slack relaxes the estimated load weight, not the load amount**: their monomial sets differ here.
        var weightSlack = (Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>)
            ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)advice.WeightSlack(0)!).Function;
        var amountSlack = (Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>)
            ((Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)advice.AmountSlack(0)!).Function;
        // 两者用的是**同一批**配载符号（重量 = Σ 单件重 × 配载），区别在**系数**：件数是 1，重量是单件重。
        // Both use the **same** stowage symbols (weight = Σ per-item weight × stowage); the difference is the **coefficient** — one per piece versus the piece's weight.
        amountSlack.X.Monomials.Select(monomial => monomial.Symbol.Name).Should()
            .BeEquivalentTo(weightSlack.X.Monomials.Select(monomial => monomial.Symbol.Name),
                "both relax the same stowage symbols");
        amountSlack.X.Monomials.Select(monomial => monomial.Coefficient.ToDouble()).Should().OnlyContain(value => value == 1.0,
            "the load amount counts pieces");
        weightSlack.X.Monomials.Select(monomial => monomial.Coefficient.ToDouble()).Should().OnlyContain(value => value > 1.0,
            "the estimated weight scales each piece by its weight");

        // 只有 `Alw`、没有 `Ala` 的舱位：两条之间互不影响。
        // A position with only `Alw` and no `Ala`: the two are independent.
        var mixed = new List<StowagePosition> { MakePosition("Q1", 1UL, 1, alw: 500.0) };
        StowageModel mixedStowage = MakeStowage(items, mixed);
        using var mixedModel = new LinearMetaModel<Flt64>();
        mixedStowage.Register(mixedModel).IsOk.Should().BeTrue();
        var mixedLoad = new Load(items, mixed, mixedStowage, withMultiLoadingSchema: true);
        mixedLoad.Register(mixedModel).IsOk.Should().BeTrue();
        var mixedAdvice = new AdviceLoading(mixed, mixedLoad);
        mixedAdvice.Register(mixedModel).IsOk.Should().BeTrue();
        mixedAdvice.AmountSlack(0).Should().BeOfType<Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol>();
        mixedAdvice.WeightSlack(0).Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>();
    }

    /// <summary>
    /// **两条目标都最小化、名字带空格、按各自的建议值过滤** / Both objectives minimise with spaced names, filtering on their own advice.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 数量目标类别 `Minimum`、名字 `"advice load amount"`。
    /// 2. 重量目标类别 `Minimum`、名字 `"advice load weight"`。
    /// 3. **两个名字都用空格**。
    /// 4. **只收了有建议值的舱位**：给三个舱位、只有一个带 `Ala`，数量目标的项数明显少于三。
    ///
    /// Four paired points: the amount objective is `Minimum` named `"advice load amount"`; the weight one is `Minimum` named `"advice load weight"`; **both names use spaces**; and **only
    /// positions carrying an advice are collected**, so three positions with one advised give far fewer terms than three.
    /// </remarks>
    [Fact]
    public void AdviceObjectives_ShouldMinimiseAndFilter() {
        var items = new List<Item> { MakeItem("A") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1, ala: 2.0, alw: 500.0),
            MakePosition("P2", 2UL, 2),
            MakePosition("P3", 3UL, 3),
        };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        // **`Load.Register` 必须显式调用**，否则装载量符号表是空的，松弛一律退化成恒零。
        // **`Load.Register` must be called explicitly**, or the load-amount table stays empty and every slack degenerates to zero.
        load.Register(model).IsOk.Should().BeTrue();
        var advice = new AdviceLoading(positions, load);
        advice.Register(model).IsOk.Should().BeTrue();

        new AdviceLoadAmountLimit(positions, advice).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> amount = model.MetaSubObjects[^1];
        amount.Name.Should().Be("advice load amount");
        amount.Category.Should().Be(ObjectCategory.Minimum);
        amount.Polynomial.Monomials.Should().NotBeEmpty();

        new AdviceLoadWeightLimit(positions, advice).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> weight = model.MetaSubObjects[^1];
        weight.Name.Should().Be("advice load weight");
        weight.Category.Should().Be(ObjectCategory.Minimum);
        weight.Polynomial.Monomials.Should().NotBeEmpty();

        // **只有 `Ala`、没有 `Alw`** 的舱位必须被重量目标跳过——判 `Ala` 会让它混进来。
        // A position with **only `Ala` and no `Alw`** must be skipped by the weight objective; testing `Ala` would let it in.
        var amountOnly = new List<StowagePosition> { MakePosition("R1", 1UL, 1, ala: 2.0) };
        StowageModel amountOnlyStowage = MakeStowage(items, amountOnly);
        using var amountOnlyModel = new LinearMetaModel<Flt64>();
        amountOnlyStowage.Register(amountOnlyModel).IsOk.Should().BeTrue();
        var amountOnlyLoad = new Load(items, amountOnly, amountOnlyStowage, withMultiLoadingSchema: true);
        amountOnlyLoad.Register(amountOnlyModel).IsOk.Should().BeTrue();
        var amountOnlyAdvice = new AdviceLoading(amountOnly, amountOnlyLoad);
        amountOnlyAdvice.Register(amountOnlyModel).IsOk.Should().BeTrue();
        int amountOnlyBefore = amountOnlyModel.MetaSubObjects.Count;
        new AdviceLoadWeightLimit(amountOnly, amountOnlyAdvice).Invoke(amountOnlyModel).IsOk.Should().BeTrue();
        amountOnlyModel.MetaSubObjects.Count.Should().Be(amountOnlyBefore,
            "the position carries an advised amount but no advised weight");
        new AdviceLoadAmountLimit(amountOnly, amountOnlyAdvice).Invoke(amountOnlyModel).IsOk.Should().BeTrue();
        amountOnlyModel.MetaSubObjects.Count.Should().Be(amountOnlyBefore + 1, "the amount objective does apply");
    }

    /// <summary>
    /// **没有任何建议值时不加目标** / No advice at all means no objective.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 三个舱位**都没有** `Ala`/`Alw` → 两条目标都**不加**、返回成功。
    /// 2. 目标位数量不变——`if (monomials.isEmpty) return ok`。
    ///
    /// Two paired points: three positions with **no** `Ala` or `Alw` give **no** objective from either limit, both returning success; and the objective count is unchanged, per the
    /// baseline's early return.
    /// </remarks>
    [Fact]
    public void AdviceObjectives_ShouldSkipWhenNoAdviceExists() {
        var items = new List<Item> { MakeItem("A") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2), MakePosition("P3", 3UL, 3),
        };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        // **`Load.Register` 必须显式调用**，否则装载量符号表是空的，松弛一律退化成恒零。
        // **`Load.Register` must be called explicitly**, or the load-amount table stays empty and every slack degenerates to zero.
        load.Register(model).IsOk.Should().BeTrue();
        var advice = new AdviceLoading(positions, load);
        advice.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;

        // **没有 `Ala` 却仍会被"不过滤"的变异收进来**：这里三个舱位都没有建议值，只有 `Load.Register` 提供的
        // 装载量符号在，所以不过滤时 `monomials` 非空、目标会被加。断言条数不变就是钉住过滤本身。
        // **Without the filter the mutation would still collect something**: all three positions lack an advice, yet the load-amount symbols from `Load.Register` exist, so an unfiltered
        // version would find a non-empty monomial set and add an objective. Asserting the count is unchanged pins the filter itself.
        new AdviceLoadAmountLimit(positions, advice).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "no position carries an advised amount");
        new AdviceLoadWeightLimit(positions, advice).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "no position carries an advised weight");
    }
}
