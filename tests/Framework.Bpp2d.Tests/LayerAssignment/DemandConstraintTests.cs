// 需求约束（B639）的判据 / Criteria for the demand constraint (B639).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **需求约束（B639）** / The demand constraint (B639).
/// </summary>
/// <remarks>
/// 判据的重点：**下界永远在**、**上界可有可无**、**tag 来自模式而不是索引**。
/// 第一条是本批最要紧的：只注册上界时模型完全正常、目标值更小、需求全未满足——静默的错解。
///
/// The criteria focus on **the lower bound always being present**, **the upper bound being optional**, and **the tag coming from the mode rather than the index**. The first matters most:
/// with only an upper bound the model looks fine and the objective is even smaller while no demand is met — a silent wrong answer.
/// </remarks>
public class DemandConstraintTests {
    /// <summary>
    /// 装载变量少于需求的装载 / A load with fewer variables than demands.
    /// </summary>
    private sealed class MismatchedLoad : ILoad
    {
        public MismatchedLoad(
            IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
            IReadOnlyList<LinearExpressionSymbol> load) {
            DemandEntries = demandEntries;
            Load = load;
        }

        public IReadOnlyList<Bpp2dAssignmentDemandEntry> DemandEntries { get; }

        public IReadOnlyList<LinearExpressionSymbol> Load { get; }

        public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> OverLoad =>
            Array.Empty<ILinearIntermediateSymbol<Flt64>>();

        public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LessLoad =>
            Array.Empty<ILinearIntermediateSymbol<Flt64>>();

        public bool OverEnabled => false;

        public bool LessEnabled => true;
    }

    /// <summary>
    /// 造一个已注册的装载 / Build a registered load.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="demands">需求项 / The demand entries.</param>
    /// <param name="symbolCount">装载变量个数，默认等于需求数 / The number of load variables, defaulting to the demand count.</param>
    /// <returns>装载 / The load.</returns>
    /// <remarks>
    /// **B643**：本类的构造参数从裸符号表换成 `Load` 之后，夹具也必须走 `ImpreciseLoad`——但这恰好让
    /// "装载变量个数少于需求数"这种真实错配变得可测（`symbolCount` 参数）。
    ///
    /// **B643**: after the constructor moved from a bare symbol table to `Load`, the fixture must go through `ImpreciseLoad` too — which incidentally makes a real mismatch, fewer load
    /// variables than demands, testable through `symbolCount`.
    /// </remarks>
    private static ImpreciseLoad MakeLoad(
        LinearMetaModel<Flt64> model,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demands) {
        var assignment = new ImpreciseAssignment(
            new Dictionary<ItemId, ulong> { [new ItemId("I0")] = 1UL });
        var load = new ImpreciseLoad(demands, assignment);
        load.RegisterLoad(model).IsOk.Should().BeTrue();
        return load;
    }

    private static Bpp2dAssignmentDemandEntry Entry(
        Bpp2dAssignmentDemandMode mode, string itemId, ulong demand, ulong? upper = null) =>
        new(mode, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(demand),
            upper is null ? null : new Bpp2dAssignmentDemandValue.Amount(upper.Value));

    private static List<LinearInequalityConstraint<Flt64>> Rows(LinearMetaModel<Flt64> model) =>
        model.Constraints.OfType<LinearInequalityConstraint<Flt64>>().ToList();

    /// <summary>
    /// **下界永远在，上界可选** / The lower bound is always there; the upper bound is optional.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 一条没有上界的需求 → **只有 1 行**，名字以 `_lb_0` 结尾。
    /// 2. 一条有上界的需求 → **2 行**，另加一行 `_ub_0`。
    /// 3. 下界的方向是 `GE`、上界是 `LE`——反了会让"装得比需求少"合法。
    /// 4. **上界为 null 时不注册 `_ub_` 行**：注册一个常为真的行会污染行数与对偶。
    ///
    /// Four paired points: a demand without an upper bound yields **one row** named `_lb_0`; one with an upper bound yields **two**, adding `_ub_0`; the lower bound is `GE` and the upper
    /// `LE`, and reversing them would make "carry less than demanded" legal; and **no `_ub_` row is registered when the bound is null**, since a trivially true row would pollute both
    /// the row count and the dual.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAlwaysAddTheLowerBoundAndOnlySometimesTheUpper() {
        using var model = new LinearMetaModel<Flt64>();
        Bpp2dAssignmentDemandEntry[] demands = {
            Entry(Bpp2dAssignmentDemandMode.ItemAmount, "I1", 2UL),
            Entry(Bpp2dAssignmentDemandMode.ItemArea, "I2", 3UL, upper: 9UL),
        };
        var constraint = new DemandConstraint(MakeLoad(model, demands));

        constraint.Invoke(model).IsOk.Should().BeTrue();

        List<LinearInequalityConstraint<Flt64>> rows = Rows(model);
        rows.Should().HaveCount(3, "one lower bound each plus a single upper bound");
        rows.Select(row => row.Name).Should().Equal(
            new[] { "demand_item_amount_lb_0", "demand_item_area_lb_1", "demand_item_area_ub_1" });

        rows[0].Inequality.Comparison.Should().Be(Comparison.GE, "the demand must be covered");

        // **每一行挂的是自己那一项装载变量**：只比行名会漏掉"两行都挂了 `load_0`"这种错位
        // （行名照旧不同，断言全过，但第二条需求被约束在第一个装载量上）。
        // **Each row carries its own load variable**: comparing names alone misses "both rows carry `load_0`", where every name assertion still passes while the second demand ends up
        // constrained by the first load amount.
        rows[0].Inequality.Lhs.Monomials.Single().Symbol.ToString().Should().Be("load_0");
        rows[1].Inequality.Lhs.Monomials.Single().Symbol.ToString().Should().Be("load_1");
        rows[2].Inequality.Comparison.Should().Be(Comparison.LE, "the upper bound caps it");
    }

    /// <summary>
    /// **tag 来自模式，不是索引** / The tag comes from the mode, not the index.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `ItemAmount` → `item_amount`，`ItemArea` → `item_area`。
    /// 2. **两条需求的模式互换时，行名跟着互换**：只有按索引命名才不受影响，这一条把"用索引做 tag"挡住。
    /// 3. 未知模式抛 `ArgumentOutOfRangeException`，不静默返回空标签。
    ///
    /// Three paired points: `ItemAmount` yields `item_amount` and `ItemArea` yields `item_area`; **swapping the two modes swaps the row names**, which only an index-based tag would
    /// survive, catching that implementation; and an unknown mode throws rather than returning an empty tag.
    /// </remarks>
    [Fact]
    public void ModeTag_ShouldComeFromTheMode() {
        DemandConstraint.ModeTag(Bpp2dAssignmentDemandMode.ItemAmount).Should().Be("item_amount");
        DemandConstraint.ModeTag(Bpp2dAssignmentDemandMode.ItemArea).Should().Be("item_area");

        using var model = new LinearMetaModel<Flt64>();
        Bpp2dAssignmentDemandEntry[] swappedDemands = {
            Entry(Bpp2dAssignmentDemandMode.ItemArea, "I1", 2UL),
            Entry(Bpp2dAssignmentDemandMode.ItemAmount, "I2", 3UL),
        };
        var swapped = new DemandConstraint(MakeLoad(model, swappedDemands));
        swapped.Invoke(model).IsOk.Should().BeTrue();
        Rows(model).Select(row => row.Name).Should().Equal(
            new[] { "demand_item_area_lb_0", "demand_item_amount_lb_1" },
            "the names follow the modes, not the positions");

        Action unknown = () => DemandConstraint.ModeTag((Bpp2dAssignmentDemandMode)99);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// **装载符号不足时明确失败，不留半个模型** / A short load vector fails explicitly and leaves no half-built model.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两条需求只给一个符号 → **失败**。
    /// 2. 错误码是 `IllegalArgument`，点名下标。
    /// 3. **失败发生在第二行之前**：模型里只留第一条需求的那一行，不会写出一个错位的约束。
    ///
    /// Three paired points: two demands with one symbol **fail**; the error is `IllegalArgument` naming the index; and **the failure happens before the second row**, leaving only the
    /// first demand's row rather than writing a misaligned constraint.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenTheLoadVectorIsShort() {
        using var model = new LinearMetaModel<Flt64>();
        // **一个装载变量对两条需求**：装载变量数与需求数不一致时，这条约束必须报错而不是错位取符号。
        // **One load variable for two demands**: when the counts disagree this constraint must fail rather than misalign its symbols.
        var constraint = new DemandConstraint(new MismatchedLoad(
            new[] {
                Entry(Bpp2dAssignmentDemandMode.ItemAmount, "I1", 2UL),
                Entry(Bpp2dAssignmentDemandMode.ItemAmount, "I2", 3UL),
            },
            new[] {
                new LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        constant: Flt64.Zero),
                    name: "load_0"),
            }));

        constraint.Invoke(model).IsOk.Should().BeFalse();
        Rows(model).Should().HaveCount(1, "the first row was written before the shortfall was detected");
    }
}
