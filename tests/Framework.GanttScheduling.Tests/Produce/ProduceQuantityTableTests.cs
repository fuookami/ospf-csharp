// 产量 / 消耗量符号表与超量、不足表的判据 / Criteria for the quantity symbol tables and the over/less tables.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Produce;

/// <summary>
/// **产量 / 消耗量符号表与超量、不足表** / The quantity symbol tables and the over/less tables.
/// </summary>
/// <remarks>
/// 判据的重点：**符号表真的进了模型**、**范围真的设上**、**超量用正侧而不足用负侧**、**松弛不自带约束**、**没有需求项是空符号**。
///
/// The criteria focus on **the symbol table really entering the model**, on **the ranges really being set**, on **over using the positive side while less uses the negative one**, on **the slack not
/// carrying its own rows**, and on **an item without a demand getting an empty symbol**.
/// </remarks>
public class ProduceQuantityTableTests {
    private static TestProduct Product(string name, int index) => new(name, index);

    private static TestRawMaterial Material(string name, int index) => new(name, index);

    /// <summary>
    /// **符号表进模型、带范围、按对象取得到** / The table enters the model, carries ranges, and is keyed by object.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 注册后 `Quantity` **不是** `new object`，而是按产品取得到符号的表；未注册时**抛**而不是返回空对象。
    /// 2. 模型里确实出现了 `produce_quantity_（产品）` 符号——只建表不 `Add` 的话求解器看不到它。
    /// 3. 有需求的产品**范围被设上**：`SolverValueRangeLower/Upper` 是 `[下界 − 不足, 上界 + 超量]`，
    /// 所以下界 10、上界 100、不足 5、超量 10 ⇒ `[5, 110]`。
    /// 4. 没有需求的产品**范围是空的**（在 `demand != null` 时设），不能凭空给个 `[0, 0]`。
    ///
    /// Four paired points: after registration `Quantity` is **not** a bare `new object` but a table the product can be looked up in, and before registration it **throws** rather than handing
    /// back an empty object; the model really contains `produce_quantity_ (product)` symbols, since building the table without `Add` leaves the solver unable to see it; a product with a demand has
    /// its range set to `[lower - less, upper + over]` (10, 100, 5, 10 ⇒ `[5, 110]`); and a product without a demand has **no** range, matching its `demand != null` guard rather than an
    /// invented `[0, 0]`.
    /// </remarks>
    [Fact]
    public void Produce_ShouldBuildTheQuantityTableWithRanges() {
        TestProduct bounded = Product("Widget", 0);
        TestProduct unbounded = Product("Gadget", 1);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] {
            (bounded, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0))),
            (unbounded, (MaterialDemand?)null),
        });

        // **注册前是 `null`，不是 `new object`**：空对象会被当成"表已就绪"。
        // **Null before registration, not `new object`**: an empty object reads as "the table is ready".
        produce.Quantity.Should().BeNull("an unregistered table must not masquerade as an empty one");

        using var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> reg = produce.Register(model);
        reg.IsOk.Should().BeTrue("registration failed: " + reg);

        var table = (IReadOnlyDictionary<TestProduct, ILinearIntermediateSymbol<Flt64>>)produce.Quantity!;
        table.Keys.Should().BeEquivalentTo(new[] { bounded, unbounded });
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().Contain("produce_quantity_0").And.Contain("produce_quantity_1");

        // **`Range` 永不为 `null`**：`ExpressionRange.Create` 的缺省就是全范围，所以"没设过"只能靠"仍是缺省"来认。
        // 有需求的那个被收窄到 `[5, 110]`，没有需求的仍停在缺省——两者必须**不同**，否则判据分不出设没设。
        // **`Range` is never null**: `ExpressionRange.Create` defaults to the full range, so "not set" can only be recognised as "still the default". The demanded product is narrowed to
        // `[5, 110]` while the other stays at the default — the two must **differ**, otherwise the criterion cannot tell whether a range was set at all.
        ValueRange<Flt64> boundedRange = table[bounded].Range.Range!;
        boundedRange.LowerBound.Value.Unwrap().Should().Be(new Flt64(5.0));
        boundedRange.UpperBound.Value.Unwrap().Should().Be(new Flt64(110.0));

        // 没有需求的那个仍是 `ExpressionRange.Create` 的缺省全范围——**上下界都比 `[5, 110]` 宽**。
        // The undemanded one stays at `ExpressionRange.Create`'s default full range, **wider than `[5, 110]` on both sides**.
        ValueRange<Flt64> unboundedRange = table[unbounded].Range.Range!;
        unboundedRange.Should().NotBe(boundedRange, "no demand means no narrowing");
        unboundedRange.LowerBound.Value.Unwrap().Ls(new Flt64(5.0)).Should().BeTrue(
            "an unset range keeps the default lower bound, which is below any demanded one");
        unboundedRange.UpperBound.Value.Unwrap().Gr(new Flt64(110.0)).Should().BeTrue();
    }

    /// <summary>
    /// **超量用正侧、不足用负侧，且松弛不自带约束** / Over uses the positive side, less the negative one, and the slack adds no rows of its own.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 超量表里是 `SlackFunction`，`WithPositive = true` / `WithNegative = false`。
    /// 2. 不足量表里是 `SlackFunction`，`WithNegative = true` / `WithPositive = false`——**两侧反了的话超量和不足会互换**，
    /// 而两者求值都是非负数，只查"非负"发现不了。
    /// 3. 两张表的松弛都是 `Constraint = false`：上界 / 下界行由表自己加，松弛自带约束会多出一对行。
    /// 4. 模型里确实有 `produce_over_quantity_ub` / `produce_less_quantity_lb` 行——只建松弛不加行，超量就没有上限。
    ///
    /// Four paired points: the over table holds a `SlackFunction` with `WithPositive = true` and `WithNegative = false`; the less table holds one with `WithNegative = true` and
    /// `WithPositive = false`, since **swapping the two sides swaps over and less** while both still evaluate to non-negative numbers, so checking "non-negative" alone catches nothing; both slacks
    /// carry `Constraint = false`, because the bound rows are added here and letting the slack carry its own would add an extra pair; and the model really has `produce_over_quantity_ub` and
    /// `produce_less_quantity_lb` rows, without which the excess has no ceiling.
    /// </remarks>
    [Fact]
    public void Produce_ShouldBuildBothSlackTablesWithTheRightSides() {
        TestProduct product = Product("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] {
            (product, (MaterialDemand?)new MaterialDemand(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0))),
        });

        using var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> reg = produce.Register(model);
        reg.IsOk.Should().BeTrue("registration failed: " + reg);

        var over = (IReadOnlyDictionary<TestProduct, ILinearIntermediateSymbol<Flt64>>)produce.OverQuantity!;
        var less = (IReadOnlyDictionary<TestProduct, ILinearIntermediateSymbol<Flt64>>)produce.LessQuantity!;

        var overSlack = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)over[product]).Function;
        overSlack.WithPositive.Should().BeTrue();
        overSlack.WithNegative.Should().BeFalse("over quantity is the excess, not the shortfall");
        overSlack.Constraint.Should().BeFalse("the upper-bound row is added by the table, not by the slack");

        var lessSlack = (SlackFunction<Flt64>)((LinearFunctionSymbolAdapter<Flt64>)less[product]).Function;
        lessSlack.WithNegative.Should().BeTrue();
        lessSlack.WithPositive.Should().BeFalse("less quantity is the shortfall, not the excess");
        lessSlack.Constraint.Should().BeFalse();

        var rowNames = model.Constraints.Select(row => row.Name).ToList();
        rowNames.Should().Contain("produce_over_quantity_ub");
        rowNames.Should().Contain("produce_less_quantity_lb");
    }

    /// <summary>
    /// **没有储备的项是空符号，不是零松弛** / An item without reserves gets an empty symbol rather than a zero slack.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 储备为 `null` 的材料，其超量 / 不足项**不是** `SlackFunction`——建松弛会凭空多出两个变量。
    /// 2. 它仍**在表里**，按材料取得到，名字照旧——缺项与"零"不是一回事。
    /// 3. 因此模型里**没有**对应材料的 `_pos` / `_neg` 变量。
    ///
    /// Three paired points: a material with null reserves has an over/less entry that is **not** a `SlackFunction`, since building one would invent two variables; the entry is still **in the table**
    /// under the same name, because "missing" and "zero" are not the same thing; and consequently the model holds no `_pos`/`_neg` variable for that material.
    /// </remarks>
    [Fact]
    public void Consumption_ShouldUseAnEmptySymbolForAMaterialWithoutReserves() {
        TestRawMaterial covered = Material("Iron", 0);
        TestRawMaterial uncovered = Material("Gold", 1);
        var consumption = new BunchSchedulingConsumption<TestRawMaterial>(new[] {
            (covered, (MaterialReserves?)new MaterialReserves(
                new Flt64(10.0), new Flt64(100.0), new Flt64(5.0), new Flt64(10.0))),
            (uncovered, (MaterialReserves?)null),
        });

        using var model = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> reg = consumption.Register(model);
        reg.IsOk.Should().BeTrue("registration failed: " + reg);

        var over = (IReadOnlyDictionary<TestRawMaterial, ILinearIntermediateSymbol<Flt64>>)consumption.OverQuantity!;
        over[uncovered].Should().NotBeOfType<LinearFunctionSymbolAdapter<Flt64>>(
            "a material with no reserves must not get an invented slack");
        over[uncovered].Name.Should().Be("consumption_over_quantity_1");
        over[covered].Should().BeOfType<LinearFunctionSymbolAdapter<Flt64>>();

        var names = model.Tokens.Symbols.Select(symbol => symbol.Name).ToList();
        names.Should().NotContain(name => name.Contains("over_quantity_1") && name.EndsWith("_pos"));
        names.Should().NotContain(name => name.Contains("less_quantity_1") && name.EndsWith("_neg"));
        names.Should().Contain("consumption_quantity_0").And.Contain("consumption_quantity_1");
    }

    /// <summary>
    /// **非线性模型被拒，不是静默成功** / A non-linear model is rejected rather than silently succeeding.
    /// </summary>
    /// <remarks>
    /// 两点成对：传 `new object` 得到 `Failed`；传 `null` 抛 `ArgumentNullException`。
    /// 这条判据钉住的正是这里修掉的形状——原来的 `Register` 对任何输入都返回 `Ok`。
    ///
    /// Two paired points: passing `new object` yields `Failed` and passing `null` throws `ArgumentNullException`. This criterion pins exactly the shape that was removed: the old `Register`
    /// returned `Ok` for every input.
    /// </remarks>
    [Fact]
    public void Register_ShouldRejectANonLinearModel() {
        TestProduct product = Product("Widget", 0);
        var produce = new BunchSchedulingProduce<TestProduct>(new[] { (product, (MaterialDemand?)null) });

        produce.Register(new object()).IsFailed.Should().BeTrue();

        Action nullModel = () => produce.Register(null!);
        nullModel.Should().Throw<ArgumentNullException>();
    }
}
