// 两个数量约束的判据 / Criteria for the two quantity constraints.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Produce;

/// <summary>
/// **两个数量约束** / The two quantity constraints.
/// </summary>
/// <remarks>
/// 判据的重点：**有松弛时行加在 `polyX` 上而不是数量本身**、**上下界方向不反**、**影子价格键挂在行上**。
///
/// The criteria focus on **the rows applying to `polyX` rather than the quantity itself when a slack exists**, on **the bound directions not being swapped**, and on **the shadow-price key being
/// attached to the row**.
/// </remarks>
public class ProduceQuantityConstraintRowsTests {
    private static (LinearMetaModel<Flt64> Model, TestProduct Product) Prepared(
        Flt64? less = null, Flt64? over = null) {
        var product = new TestProduct("Widget", 0);
        var demand = new MaterialDemand(new Flt64(10.0), new Flt64(100.0), less, over);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] { (product, (MaterialDemand?)demand) });
        var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> registered = produce.Register(model);
        registered.IsOk.Should().BeTrue("registration failed: " + registered);
        return (model, product);
    }

    private static LinearInequality<Flt64> Row(LinearMetaModel<Flt64> model, string rowName) =>
        ((LinearInequalityConstraint<Flt64>)model.Constraints.Single(constraint => constraint.Name == rowName)).Inequality;

    // **界值在 `Rhs`，不在 `Lhs`**：`AddConstraint` 把常数规范化到了右端。
    // **The bound lives in `Rhs`, not `Lhs`**: `AddConstraint` normalises the constant onto the right-hand side.
    private static LinearPolynomial<Flt64> Left(LinearMetaModel<Flt64> model, string rowName) => Row(model, rowName).Lhs;

    /// <summary>
    /// **没有超量 / 不足时，行直接加在数量符号上** / Without an over/less slack the rows apply to the quantity symbol itself.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 需求不带超量 / 不足 → 两条行 `produce_quantity_ub_0` / `produce_quantity_lb_0`。
    /// 2. 上界行的左端**就是** `produce_quantity_0`，不是别的符号。
    /// 3. 方向：上界 `LE`、下界 `GE`——反了照样能解，只是约束变成"上界至少"。
    ///
    /// Three paired points: a demand without over/less yields the two rows `produce_quantity_ub_0` and `produce_quantity_lb_0`; the upper row's left-hand side **is** `produce_quantity_0` and
    /// nothing else; and the directions are `LE` for the upper bound and `GE` for the lower, since a swap still solves and merely asserts "at least the upper bound".
    /// </remarks>
    [Fact]
    public void Constraint_ShouldApplyTheRowsToTheQuantityWithoutASlack() {
        (LinearMetaModel<Flt64> model, TestProduct product) = Prepared();
        var constraint = new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) },
            new BunchSchedulingProduce<TestProduct>(Array.Empty<(TestProduct, MaterialDemand?)>()));

        // 约束必须作用在**同一个已注册的** produce 上。
        var produce = new BunchSchedulingProduce<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) });
        using var fresh = new LinearMetaModel<Flt64>();
        produce.Register(fresh).IsOk.Should().BeTrue();
        var wired = new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) },
            produce);

        wired.Invoke(fresh).IsOk.Should().BeTrue();

        LinearPolynomial<Flt64> upper = Left(fresh, "produce_quantity_ub_0");
        upper.Monomials.Should().ContainSingle();
        upper.Monomials[0].Symbol.Name.Should().Be("produce_quantity_0");
        Row(fresh, "produce_quantity_ub_0").Rhs.Constant.ToDouble().Should().Be(100.0);

        LinearPolynomial<Flt64> lower = Left(fresh, "produce_quantity_lb_0");
        lower.Monomials.Should().ContainSingle();
        lower.Monomials[0].Symbol.Name.Should().Be("produce_quantity_0");
        Row(fresh, "produce_quantity_lb_0").Rhs.Constant.ToDouble().Should().Be(10.0);

        var row = (LinearInequalityConstraint<Flt64>)fresh.Constraints.Single(c => c.Name == "produce_quantity_ub_0");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        var lowerRow = (LinearInequalityConstraint<Flt64>)fresh.Constraints.Single(c => c.Name == "produce_quantity_lb_0");
        lowerRow.Inequality.Comparison.Should().Be(Comparison.GE);
        _ = model;
        _ = constraint;
    }

    /// <summary>
    /// **有超量时上界加在 `polyX` 上（数量 − 正松弛），不是数量本身** / With an over slack the upper bound applies to `polyX` (quantity minus the positive slack), not to the quantity.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 上界行的左端**同时含** `produce_quantity_0` 与 `produce_over_quantity_0_pos`，且后者的系数为**负**——
    /// 这就是 `x - pos`。加在数量本身的话超量变量失去意义：超出的部分照样违反上界。
    /// 2. 下界行的左端**只有** `produce_quantity_0`（不足没启用）。
    /// 3. 两个符号的系数都非零，且正负相反——只查"含两个符号"分不出 `x + pos` 与 `x - pos`。
    ///
    /// Three paired points: the upper row's left-hand side holds **both** `produce_quantity_0` and `produce_over_quantity_0_pos` with the latter's coefficient **negative** — that is `x - pos`;
    /// applying it to the quantity itself would make the over variable pointless since the excess would still violate the bound. The lower row holds **only** `produce_quantity_0`, since less is not
    /// enabled. And both coefficients are non-zero with opposite signs, because checking "holds both symbols" alone cannot tell `x + pos` from `x - pos`.
    /// </remarks>
    [Fact]
    public void Constraint_ShouldApplyTheUpperBoundToPolyXWhenOverIsEnabled() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] {
            (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), null, new Flt64(10.0))),
        });
        using var model = new LinearMetaModel<Flt64>();
        produce.Register(model).IsOk.Should().BeTrue();

        new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), null, new Flt64(10.0))) },
            produce).Invoke(model).IsOk.Should().BeTrue();

        LinearPolynomial<Flt64> upper = Left(model, "produce_quantity_ub_0");
        upper.Monomials.Should().HaveCount(2, "polyX is the quantity minus the positive slack");
        LinearMonomial<Flt64> quantity = upper.Monomials.Single(m => m.Symbol.Name == "produce_quantity_0");
        LinearMonomial<Flt64> positive = upper.Monomials.Single(m => m.Symbol.Name.EndsWith("_pos"));
        quantity.Coefficient.ToDouble().Should().BeGreaterThan(0.0);
        positive.Coefficient.ToDouble().Should().BeLessThan(0.0,
            "the positive slack is subtracted, which is what discounts the excess");

        LinearPolynomial<Flt64> lower = Left(model, "produce_quantity_lb_0");
        lower.Monomials.Should().ContainSingle("less is not enabled, so the lower bound stays on the quantity");
        lower.Monomials[0].Symbol.Name.Should().Be("produce_quantity_0");
    }

    /// <summary>
    /// **有不足时下界加在 `x + neg` 上（数量加负松弛）** / With a less slack the lower bound applies to `x + neg` (quantity plus the negative slack).
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 下界行的左端**同时含** `produce_quantity_0` 与 `produce_less_quantity_0_neg`，后者系数为**正**——
    /// 这就是 `x + neg`。加在数量本身的话不足变量失去意义：低于下界的部分照样违反下界。
    /// 2. 上界行的左端**只有** `produce_quantity_0`（超量没启用）。
    /// 3. 两个系数都非零且**同号**——与上界那一侧（异号）相反，这正是两个方向不能互换的实证。
    ///
    /// Three paired points: the lower row's left-hand side holds **both** `produce_quantity_0` and `produce_less_quantity_0_neg` with the latter's coefficient **positive** — that is `x + neg`;
    /// applying it to the quantity itself would make the less variable pointless since the shortfall would still violate the bound. The upper row holds **only** `produce_quantity_0`, since over is
    /// not enabled. And both coefficients are non-zero with the **same** sign, opposite to the upper side's mixed signs, which is the concrete reason the two directions cannot be swapped.
    /// </remarks>
    [Fact]
    public void Constraint_ShouldApplyTheLowerBoundToPolyXWhenLessIsEnabled() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] {
            (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), null)),
        });
        using var model = new LinearMetaModel<Flt64>();
        produce.Register(model).IsOk.Should().BeTrue();

        new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), null)) },
            produce).Invoke(model).IsOk.Should().BeTrue();

        LinearPolynomial<Flt64> lower = Left(model, "produce_quantity_lb_0");
        lower.Monomials.Should().HaveCount(2, "polyX is the quantity plus the negative slack");
        LinearMonomial<Flt64> quantity = lower.Monomials.Single(m => m.Symbol.Name == "produce_quantity_0");
        LinearMonomial<Flt64> negative = lower.Monomials.Single(m => m.Symbol.Name.EndsWith("_neg"));
        quantity.Coefficient.ToDouble().Should().BeGreaterThan(0.0);
        negative.Coefficient.ToDouble().Should().BeGreaterThan(0.0,
            "the negative slack is added, which is what fills the shortfall up to the bound");

        LinearPolynomial<Flt64> upper = Left(model, "produce_quantity_ub_0");
        upper.Monomials.Should().ContainSingle("over is not enabled, so the upper bound stays on the quantity");
        upper.Monomials[0].Symbol.Name.Should().Be("produce_quantity_0");
    }

    /// <summary>
    /// **影子价格键挂在行上** / The shadow-price key is attached to the row.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 上界行的 `Args` 是 `ProduceQuantityShadowPriceKey`，其 `Product` 就是那个产品。
    /// 2. 下界行同样带键——只给一条带键会让一半影子价格取不到。
    /// 3. 消费侧用的是**另一个**键类型：两个模块的影子价格不能串。
    ///
    /// Three paired points: the upper row's `Args` is a `ProduceQuantityShadowPriceKey` carrying that product; the lower row carries one too, since attaching it to only one row would leave half
    /// the shadow prices unreachable; and the consumption side uses a **different** key type, because the two modules' shadow prices must not be crossed.
    /// </remarks>
    [Fact]
    public void Constraint_ShouldAttachTheShadowPriceKey() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] {
            (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))),
        });
        using var model = new LinearMetaModel<Flt64>();
        produce.Register(model).IsOk.Should().BeTrue();
        new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) },
            produce).Invoke(model).IsOk.Should().BeTrue();

        foreach (string rowName in new[] { "produce_quantity_ub_0", "produce_quantity_lb_0" }) {
            var row = (LinearInequalityConstraint<Flt64>)model.Constraints.Single(c => c.Name == rowName);
            row.Args.Should().BeOfType<ProduceQuantityShadowPriceKey>();
            ((ProduceQuantityShadowPriceKey)row.Args!).Product.Should().Be(product);
        }

        var material = new TestRawMaterial("Iron", 0);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(new[] {
            (material, (MaterialReserves?)new MaterialReserves(new Flt64(10.0), new Flt64(100.0))),
        });
        using var consumptionModel = new LinearMetaModel<Flt64>();
        consumption.Register(consumptionModel).IsOk.Should().BeTrue();
        new ConsumptionQuantityConstraint<TestRawMaterial>(
            new[] { (material, (MaterialReserves?)new MaterialReserves(new Flt64(10.0), new Flt64(100.0))) },
            consumption).Invoke(consumptionModel).IsOk.Should().BeTrue();

        var consumptionRow = (LinearInequalityConstraint<Flt64>)consumptionModel.Constraints
            .Single(c => c.Name == "consumption_quantity_ub_0");
        consumptionRow.Args.Should().BeOfType<ConsumptionQuantityShadowPriceKey>(
            "the two modules must not share a shadow-price key type");
    }

    /// <summary>
    /// **未注册就失败，不是静默成功** / Failing while unregistered, never silently succeeding.
    /// </summary>
    /// <remarks>
    /// 两点成对：未注册时 `Invoke` 返回 `Failed`；传非线性模型也返回 `Failed`。
    /// 这条钉住的正是这里修掉的形状——原来的两个 `Invoke` 对任何输入都返回 `Ok`。
    ///
    /// Two paired points: while unregistered `Invoke` returns `Failed`, and a non-linear model returns `Failed` too. This pins exactly the shape that was removed: the old two `Invoke` methods
    /// returned `Ok` for every input.
    /// </remarks>
    [Fact]
    public void Constraint_ShouldFailWhileUnregistered() {
        var product = new TestProduct("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] {
            (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))),
        });
        var constraint = new ProduceQuantityConstraint<TestProduct>(
            new[] { (product, (MaterialDemand?)new MaterialDemand(new Flt64(10.0), new Flt64(100.0))) },
            produce);

        using var model = new LinearMetaModel<Flt64>();
        constraint.Invoke(model).IsFailed.Should().BeTrue("the produce table does not exist yet");

        produce.Register(model).IsOk.Should().BeTrue();
        constraint.Invoke(new object()).IsFailed.Should().BeTrue("a non-linear model is rejected");

        // **`null` 抛，不是返回 `Failed`**：丢掉 `ArgumentNullException.ThrowIfNull` 之后，`null` 会走
        // "非线性模型"那条分支返回 `Failed`——两者都是"没成功"，只查 `IsFailed` 分不出来。
        // **`null` throws rather than returning `Failed`**: without `ArgumentNullException.ThrowIfNull` a `null` falls into the "non-linear model" branch and returns `Failed`, and both are
        // "did not succeed", so checking `IsFailed` alone cannot tell them apart.
        Action nullModel = () => constraint.Invoke(null!);
        nullModel.Should().Throw<ArgumentNullException>();
    }
}
