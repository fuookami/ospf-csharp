// 每列最大份数与最终阶段的判据 / Criteria for the copy bound and the final phase.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **每列最大份数与最终阶段** / The copy bound and the final phase.
/// </summary>
/// <remarks>
/// 判据的重点：**取最小份数**（最紧的物品决定份数）、**向上取整**（向下取整会少一份）、**面积需求不参与**、
/// 以及**最终阶段按顺序注册四件**。
///
/// The criteria focus on **taking the minimum copy count** (the tightest item decides), **rounding up** (rounding down comes up one short), **area demands taking no part**, and **the final phase
/// registering four pieces in order**.
/// </remarks>
public class MaxCopiesAndFinalTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static SheetType<FltX> SheetType(double width, double height) =>
        new(Q(width), Q(height), Q(1000.0), new SheetTypeId("S1"));

    private static Layer<FltX> Layer(SheetType<FltX>? sheetType, params (string ItemId, int Count)[] items) {
        var placements = new List<Placement2<FltX>>();
        foreach ((string itemId, int count) in items)
        {
            for (int index = 0; index < count; ++index)
            {
                placements.Add(new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: itemId));
            }
        }
        return new Layer<FltX>(0L, null, sheetType, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)), placements);
    }

    private static Bpp2dAssignmentDemandEntry AmountDemand(string itemId, ulong amount) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(amount), null);

    /// <summary>
    /// **取最小份数、向上取整** / The minimum copy count, rounded up.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 一列覆盖两种物品（各 2 件），需求分别 5 与 3 → **取最小的 2 份**（`ceil(3/2)`），不是 `ceil(5/2)=3`。
    /// 2. **向上取整**：`ceil(5/2) = 3` 而不是 2——向下取整会少一件。
    /// 3. **面积需求不参与**：只有面积需求的物品不产生份数上界。
    /// 4. **没有覆盖价值的列限制为一份**，且上界至少为 1。
    ///
    /// Four paired points: a column covering two items with two units each, demanded 5 and 3, gives **the smaller count of 2** (`ceil(3/2)`) rather than `ceil(5/2)=3`; **the division rounds up**,
    /// so `ceil(5/2) = 3` rather than 2, which would come up one short; **area demands take no part**, an item demanded only by area yielding no copy bound; and **a column with no coverage value
    /// is limited to one copy**, with a floor of 1.
    /// </remarks>
    [Fact]
    public void PerColumn_ShouldTakeTheTightestItemAndRoundUp() {
        Layer<FltX> twoItems = Layer(SheetType(100.0, 100.0), ("A", 2), ("B", 2));
        Layer<FltX> oneItem = Layer(SheetType(100.0, 100.0), ("A", 2));
        Layer<FltX> noDemand = Layer(SheetType(100.0, 100.0), ("Z", 3));

        IReadOnlyList<ulong> bound = MaxCopies.PerColumn(
            new[] { twoItems, oneItem, noDemand },
            new[] { AmountDemand("A", 5UL), AmountDemand("B", 3UL) });

        bound.Should().HaveCount(3);
        bound[0].Should().Be(2UL, "the tightest item (B, demand 3 over coverage 2) decides");
        bound[1].Should().Be(3UL, "ceil(5 / 2) is 3, not 2");
        bound[2].Should().Be(1UL, "a column in no demand is limited to one copy");

        Bpp2dAssignmentDemandEntry areaOnly = new(
            Bpp2dAssignmentDemandMode.ItemArea, new ItemId("A"),
            new Bpp2dAssignmentDemandValue.Area(Q(500.0)), null);
        MaxCopies.PerColumn(new[] { oneItem }, new[] { areaOnly })[0]
            .Should().Be(1UL, "an area demand contributes no copy bound");

        // **面积需求真的被排除，而不是"恰好算出一份"**：同一列同时有条数与面积需求时，
        // 面积需求若被当成件数，`Area` 分支没有 `Amount` 可读，会落到 `?? 1UL` 或跳过——
        // 用一条**件数需求很大**的对照来区分：件数需求 9 时份数是 5，面积需求不得把上界拉回 1。
        // **Area demands are really excluded** rather than incidentally yielding one: with an item-amount demand of 9 the bound is 5, and an accompanying area demand must not pull it back to 1.
        Bpp2dAssignmentDemandEntry[] both = {
            AmountDemand("A", 9UL),
            new Bpp2dAssignmentDemandEntry(
                Bpp2dAssignmentDemandMode.ItemArea, new ItemId("A"),
                new Bpp2dAssignmentDemandValue.Area(Q(500.0)), null),
        };
        MaxCopies.PerColumn(new[] { oneItem }, both)[0]
            .Should().Be(5UL, "the amount demand decides; the area demand neither overrides nor is counted");

        // **模式字段本身是判据的一部分**：`Mode` 与 `Demand` 是两个字段，可能不一致。
        // 造一条"模式说面积、值却是件数"的错配——只看 `Demand` 的实现会把它当件数用，而看 `Mode`，因此应当忽略。
        // **The mode field is part of the criterion**: its `Mode` and `Demand` are separate fields and can disagree. A row whose mode says area while its value is an amount is ignored
        // by an implementation that reads the mode, but counted by one that only inspects the value.
        Bpp2dAssignmentDemandEntry mismatched = new(
            Bpp2dAssignmentDemandMode.ItemArea, new ItemId("A"),
            new Bpp2dAssignmentDemandValue.Amount(9UL), null);
        MaxCopies.PerColumn(new[] { oneItem }, new[] { mismatched })[0]
            .Should().Be(1UL, "the mode says area, so the entry contributes no copy bound");
    }

    /// <summary>
    /// **最终阶段按顺序注册四件** / The final phase registers four pieces in order.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 四个构件**都非空**。
    /// 2. **`y` 在模型上**（份数变量），且 `y` 的上界等于 `MaxCopies` 的结果。
    /// 3. **容量符号与容量约束行都在**：两列两行，名字 `sheet_capacity_0` / `sheet_capacity_1`。
    /// 4. **关掉容量约束时符号仍在、行不在**——构件要用符号，所以符号不能省。
    ///
    /// Four paired points: all four artifacts are non-null; **`y` is on the model** and its bound equals the `MaxCopies` result; **both the capacity symbols and the capacity rows are present**,
    /// two of each named `sheet_capacity_0` and `sheet_capacity_1`; and **with the capacity constraint off the symbols remain while the rows do not**, since the artifacts need the symbols.
    /// </remarks>
    [Fact]
    public void RegisterFinal_ShouldRegisterTheFourPieces() {
        Layer<FltX>[] layers = { Layer(SheetType(100.0, 100.0), ("A", 2)), Layer(SheetType(50.0, 50.0), ("A", 1)) };
        Bpp2dAssignmentDemandEntry[] demands = { AmountDemand("A", 5UL) };
        using var model = new LinearMetaModel<Flt64>();
        var context = new LayerAssignmentContext();

        Fuookami.Ospf.Utils.Functional.Result<FinalAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> registered = context.RegisterFinal(model, layers, demands);
        registered.IsOk.Should().BeTrue();
        FinalAssignmentArtifacts artifacts = ((Fuookami.Ospf.Utils.Functional.Ok<FinalAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)registered).Value;

        artifacts.Assignment.Y.Should().NotBeNull();
        artifacts.Load.Load.Should().HaveCount(1);
        artifacts.DemandConstraint.Should().NotBeNull();
        artifacts.Capacity.ColumnCount().Should().Be(2);
        artifacts.Capacity.LoadArea.Should().HaveCount(2);

        artifacts.Assignment.MaxCopies.Should().BeEquivalentTo(new[] { 3UL, 5UL });
        artifacts.Assignment.Y![0].Range.UpperBound!.Value.UnwrapOrNull()!
            .Should().Be(new Fuookami.Ospf.Math.Algebra.Number.UInt64(3UL));
        artifacts.Assignment.Y[1].Range.UpperBound!.Value.UnwrapOrNull()!
            .Should().Be(new Fuookami.Ospf.Math.Algebra.Number.UInt64(5UL));

        LinearInequalityConstraint<Flt64>[] rows = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>().ToArray();
        rows.Select(row => row.Name).Should().Contain("sheet_capacity_0").And.Contain("sheet_capacity_1");
        rows.Select(row => row.Name).Should().Contain("demand_item_amount_lb_0");

        using var noCapacity = new LinearMetaModel<Flt64>();
        FinalAssignmentArtifacts withoutRows = ((Fuookami.Ospf.Utils.Functional.Ok<FinalAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)
            new LayerAssignmentContext().RegisterFinal(noCapacity, layers, demands, enableCapacityConstraint: false)).Value;
        withoutRows.Capacity.LoadArea.Should().HaveCount(2, "the symbols are still needed by the artifacts");
        noCapacity.Constraints.OfType<LinearInequalityConstraint<Flt64>>()
            .Select(row => row.Name)
            .Should().NotContain("sheet_capacity_0", "the capacity rows are switched off");
    }

    /// <summary>
    /// **空层列表也能注册** / An empty layer list registers too.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 空层列表 → **成功**（不是错误）。
    /// 2. `y` 有零个元素，`MaxCopies` 为空。
    /// 3. 没有容量行。
    ///
    /// Three paired points: an empty layer list **succeeds** rather than erroring; `y` has zero elements and `MaxCopies` is empty; and there are no capacity rows.
    /// </remarks>
    [Fact]
    public void RegisterFinal_ShouldAcceptAnEmptyLayerList() {
        using var model = new LinearMetaModel<Flt64>();
        Fuookami.Ospf.Utils.Functional.Result<FinalAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> registered = new LayerAssignmentContext().RegisterFinal(
            model, Array.Empty<Layer<FltX>>(), new[] { AmountDemand("A", 1UL) });

        registered.IsOk.Should().BeTrue("an empty candidate set is a normal input");
        FinalAssignmentArtifacts artifacts = ((Fuookami.Ospf.Utils.Functional.Ok<FinalAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)registered).Value;
        artifacts.Assignment.MaxCopies.Should().BeEmpty();
        artifacts.Capacity.ColumnCount().Should().Be(0);
        model.Constraints.OfType<LinearInequalityConstraint<Flt64>>()
            .Select(row => row.Name)
            .Should().NotContain(name => name.StartsWith("sheet_capacity"));
    }
}
