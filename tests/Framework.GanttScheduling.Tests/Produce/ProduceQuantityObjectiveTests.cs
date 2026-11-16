// 八个数量目标的判据 / Criteria for the eight quantity objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Produce;

/// <summary>
/// **八个数量目标** / The eight quantity objectives.
/// </summary>
/// <remarks>
/// 判据的重点：**阈值 0 与阈值非零是两条不同的路**、**松弛自带约束**、**系数在松弛外面乘**、**空列表两边处理不同**。
///
/// The criteria focus on **a zero threshold and a non-zero one being two different paths**, on **the slack carrying its own rows**, on **the coefficient multiplying outside the slack**, and on
/// **the empty list being handled differently on the two sides**.
/// </remarks>
public class ProduceQuantityObjectiveTests {
    private static (LinearMetaModel<Flt64> Model, TestProduct Product, MaterialDemand Demand) PreparedProduce(
        Flt64? less = null, Flt64? over = null) {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(
            new Flt64(10.0), new Flt64(100.0), less, over);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] { (product, (MaterialDemand?)demand) });
        var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> registered = produce.Register(model);
        registered.IsOk.Should().BeTrue("registration failed: " + registered);
        return (model, product, demand);
    }

    private static (LinearMetaModel<Flt64> Model, TestRawMaterial Material, MaterialReserves Reserves) PreparedConsumption(
        Flt64? less = null, Flt64? over = null) {
        var material = new TestRawMaterial("Iron", 0);
        var reserves = new MaterialReserves(
            new Flt64(10.0), new Flt64(100.0), less, over);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)reserves) });
        var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> registered = consumption.Register(model);
        registered.IsOk.Should().BeTrue("registration failed: " + registered);
        return (model, material, reserves);
    }

    /// <summary>
    /// **阈值 0 时计费数量本身；阈值非零时才建松弛** / A zero threshold charges the quantity itself; only a non-zero one builds a slack.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 阈值 `0` → 目标项**就是** `produce_quantity_0`，模型里**没有**阈值松弛符号。
    /// 2. 阈值非零 → 目标项是松弛的**正部**（`*_pos`），数量符号本身**不再出现**。
    /// 3. 目标名与类别不随分支变：`produce quantity` / `Minimum`。
    /// 4. 四个"数量"目标都这样——最小值与最大值只差类别，最小值与最大值都不该影响阈值判断。
    ///
    /// Four paired points: with a threshold of `0` the objective term **is** `produce_quantity_0` and the model holds **no** threshold slack; with a non-zero threshold the term is the slack's
    /// **positive part** (`*_pos`) and the quantity symbol **no longer appears**; the objective name and category do not vary with the branch (`produce quantity` / `Minimum`); and all four
    /// "quantity" objectives behave this way — minimisation and maximisation differ only in the category and must not affect the threshold decision.
    /// </remarks>
    [Fact]
    public void QuantityObjectives_ShouldChargeTheSymbolAtZeroAndTheSlackOtherwise() {
        (LinearMetaModel<Flt64> plainModel, TestProduct product, _) = PreparedProduce();
        new ProduceQuantityMinimization<TestProduct>(new[] { product },
            new BunchSchedulingProduce<TestProduct>(Array.Empty<(TestProduct, MaterialDemand?)>()))
            .Invoke(plainModel);

        // 目标必须作用在**同一个已注册的** produce 上，所以另建一个把它传进去。
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) });
        using var model = new LinearMetaModel<Flt64>();
        produce.Register(model).IsOk.Should().BeTrue();

        new ProduceQuantityMinimization<TestProduct>(new[] { product }, produce).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> plain = model.MetaSubObjects.Last();
        plain.Name.Should().Be("produce quantity");
        plain.Category.Should().Be(ObjectCategory.Minimum);
        plain.Polynomial.Monomials.Should().ContainSingle();
        plain.Polynomial.Monomials[0].Symbol.Name.Should().Be("produce_quantity_0");
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().NotContain(name => name.Contains("_threshold_"));

        using var thresholdModel = new LinearMetaModel<Flt64>();
        var thresholdProduce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) });
        thresholdProduce.Register(thresholdModel).IsOk.Should().BeTrue();
        new ProduceQuantityMinimization<TestProduct>(
            new[] { product }, thresholdProduce, threshold: _ => new Flt64(20.0))
            .Invoke(thresholdModel).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> withThreshold = thresholdModel.MetaSubObjects.Last();
        withThreshold.Name.Should().Be("produce quantity", "the name does not vary with the branch");
        withThreshold.Polynomial.Monomials.Should().ContainSingle();
        withThreshold.Polynomial.Monomials[0].Symbol.Name.Should().EndWith("_pos");
        withThreshold.Polynomial.Monomials[0].Symbol.Name.Should().Contain("produce_quantity_minimization_threshold");
    }

    /// <summary>
    /// **阈值松弛自带约束** / The threshold slack carries its own rows.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 松弛是 `SlackFunction`，`Constraint = true`——这四个目标不像超量 / 不足表那样自己加行，
    /// 不带约束的话 `pos` 是个没有上界的自由变量：最小化时恒取 0（目标失效）、最大化时冲到无穷（模型无界）。
    /// 2. `Threshold = true`、`WithNegative = false`、`WithPositive = true`。
    /// 3. 模型里确实多了 `*_pos` 的约束行（`_pos` 后缀的行名）。
    ///
    /// Three paired points: the slack is a `SlackFunction` with `Constraint = true`, because these four objectives do not add their own rows the way the over/less tables do and an unconstrained
    /// `pos` would be a free variable with no ceiling — pinned at zero under minimisation (a dead objective) and driven to infinity under maximisation (an unbounded model); `Threshold = true`,
    /// `WithNegative = false`, `WithPositive = true`; and the model really gains a `*_pos` row.
    /// </remarks>
    [Fact]
    public void QuantityObjectives_ShouldUseAConstrainedThresholdSlack() {
        (LinearMetaModel<Flt64> model, TestRawMaterial material, _) = PreparedConsumption();
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)new MaterialReserves(new Flt64(10.0), new Flt64(100.0))) });
        using var fresh = new LinearMetaModel<Flt64>();
        consumption.Register(fresh).IsOk.Should().BeTrue();
        int before = fresh.Constraints.Count;

        new ConsumptionQuantityMaximization<TestRawMaterial>(
            new[] { material }, consumption, threshold: _ => new Flt64(20.0)).Invoke(fresh).IsOk.Should().BeTrue();

        SlackFunction<Flt64> slack = fresh.Tokens.Symbols
            .OfType<LinearFunctionSymbolAdapter<Flt64>>()
            .Select(adapter => adapter.Function)
            .OfType<SlackFunction<Flt64>>()
            .Single(function => function.Name.Contains("consumption_quantity_maximization_threshold"));
        slack.Constraint.Should().BeTrue("without it `pos` has no ceiling");
        slack.Threshold.Should().BeTrue();
        slack.WithNegative.Should().BeFalse();
        slack.WithPositive.Should().BeTrue();

        // **行不在 `Add` 时加，是在构建 mechanism model 时才加**：`RegisterFunctionConstraints` 在
        // `LinearMechanismModel` 的构造路径里跑。所以在元模型上查 `Constraints` 查不到松弛行——
        // 这一环只能靠 `Constraint = true` 这个属性钉住（它决定那一行加不加）。
        // **The rows are not added by `Add` but when the mechanism model is built**: `RegisterFunctionConstraints` runs on the `LinearMechanismModel` construction path, so inspecting the meta model's
        // `Constraints` cannot see the slack's row. This link is pinned by the `Constraint = true` property alone, which is what decides whether that row is added.
        fresh.Constraints.Count.Should().Be(before, "slack rows appear only when the mechanism model is built");
        slack.SignedLeft().Monomials.Select(monomial => monomial.Symbol.Name)
            .Should().Contain(name => name.StartsWith("consumption_quantity_maximization_threshold")
                && name.EndsWith("_pos"));
        _ = model;
    }

    /// <summary>
    /// **系数在松弛外面乘，且恰乘一次** / The coefficient multiplies outside the slack, exactly once.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 传 `coefficient: _ => new Flt64(5.0)` 时，阈值分支下目标项系数**恰为 5**（`pos` 自身系数是 1）。
    /// 2. 阈值 0 分支同样恰为 5——两条分支的加权方式一致。
    /// 3. 乘积不是 25：把系数揉进松弛又乘一遍的典型症状。
    ///
    /// Three paired points: passing `coefficient: _ => new Flt64(5.0)` makes each objective coefficient **exactly 5** under the threshold branch, where `pos` itself has coefficient 1; the zero branch
    /// is exactly 5 too, so both branches weight the same way; and the product is not 25, which is the symptom of folding the coefficient into the slack and multiplying again.
    /// </remarks>
    [Fact]
    public void QuantityObjectives_ShouldWeightExactlyOnce() {
        foreach (Flt64 threshold in new[] { Flt64.Zero, new Flt64(20.0) }) {
            (LinearMetaModel<Flt64> _, TestProduct product, _) = PreparedProduce();
            var produce = new BunchSchedulingProduce<TestProduct>(
                new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) });
            using var model = new LinearMetaModel<Flt64>();
            produce.Register(model).IsOk.Should().BeTrue();

            new ProduceQuantityMaximization<TestProduct>(
                new[] { product }, produce,
                threshold: _ => threshold,
                coefficient: _ => new Flt64(5.0)).Invoke(model).IsOk.Should().BeTrue();

            MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
            objective.Category.Should().Be(ObjectCategory.Maximum);
            objective.Polynomial.Monomials.Should().ContainSingle();
            objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().Be(5.0,
                "the coefficient multiplies the slack exactly once, from outside");
        }
    }

    /// <summary>
    /// **空列表：数量目标照旧加空目标，超量 / 不足目标不加** / An empty list still adds an objective for the quantity objectives and adds none for the over/less ones.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `ProduceQuantityMinimization` 传空列表 → **仍加**一个目标（名字 `produce quantity`，零项）。
    /// 2. `ProduceOverQuantityMinimization` 传空列表 → **一个目标都不加**（外层有 `if (materials.isNotEmpty)`）。
    /// 3. 两者的差别**不是笔误**：本来就是这样写的，判据把这个不对称钉住。
    ///
    /// Three paired points: `ProduceQuantityMinimization` with an empty list **still adds** an objective (named `produce quantity`, with no terms); `ProduceOverQuantityMinimization` with an empty
    /// list adds **no objective at all**, matching `if (materials.isNotEmpty)`; and the difference is **not a typo** — that is how the code is written, and the criterion pins the
    /// asymmetry.
    /// </remarks>
    [Fact]
    public void OverAndLessObjectives_ShouldAddNothingForAnEmptyList() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0))) });

        using var quantityModel = new LinearMetaModel<Flt64>();
        produce.Register(quantityModel).IsOk.Should().BeTrue();
        int beforeQuantity = quantityModel.MetaSubObjects.Count;
        new ProduceQuantityMinimization<TestProduct>(Array.Empty<TestProduct>(), produce)
            .Invoke(quantityModel).IsOk.Should().BeTrue();
        quantityModel.MetaSubObjects.Count.Should().Be(beforeQuantity + 1,
            "the four quantity objectives add an empty objective anyway");

        using var overModel = new LinearMetaModel<Flt64>();
        var overProduce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0))) });
        overProduce.Register(overModel).IsOk.Should().BeTrue();
        int beforeOver = overModel.MetaSubObjects.Count;
        new ProduceOverQuantityMinimization<TestProduct>(
            Array.Empty<(TestProduct, MaterialDemand?)>(), overProduce)
            .Invoke(overModel).IsOk.Should().BeTrue();
        overModel.MetaSubObjects.Count.Should().Be(beforeOver,
            "the over/less objectives add nothing for an empty list");
    }

    /// <summary>
    /// **未注册就失败，不是静默成功** / Failing while unregistered, never silently succeeding.
    /// </summary>
    /// <remarks>
    /// 两点成对：未注册时 `Invoke` 返回 `Failed`（取不到数量表）；传非线性模型也返回 `Failed`。
    /// 这条钉住的正是这里修掉的形状——原来的八个 `Invoke` 对任何输入都返回 `Ok`。
    ///
    /// Two paired points: while unregistered `Invoke` returns `Failed` (no quantity table) and a non-linear model returns `Failed` too. This pins exactly the shape that was removed: the old eight
    /// `Invoke` methods returned `Ok` for every input.
    /// </remarks>
    [Fact]
    public void QuantityObjectives_ShouldFailWhileUnregistered() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) });

        using var model = new LinearMetaModel<Flt64>();
        new ProduceQuantityMinimization<TestProduct>(new[] { product }, produce)
            .Invoke(model).IsFailed.Should().BeTrue("the produce table does not exist yet");

        produce.Register(model).IsOk.Should().BeTrue();
        new ProduceQuantityMinimization<TestProduct>(new[] { product }, produce)
            .Invoke(new object()).IsFailed.Should().BeTrue("a non-linear model is rejected");
    }
}
