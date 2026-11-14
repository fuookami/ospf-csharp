// 板材类型成本目标（B652）的判据 / Criteria for the sheet-type cost objective (B652).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **板材类型成本目标（B652）** / The sheet-type cost objective (B652).
/// </summary>
/// <remarks>
/// 判据的重点：**成本表缺该类型时跳过而不是按零计**（按零计等于"这个类型免费"）、**成本表为空或权重为零时不建目标**。
///
/// The criteria focus on **a type missing from the cost table being skipped rather than priced at zero**, since zero means "this type is free", and on **no objective being built when the cost
/// table is empty or the weight is zero**.
/// </remarks>
public class SheetTypeCostMinimizationTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static SheetType<FltX> SheetType(string code) =>
        new(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId(code));

    private static Layer<FltX> Layer(SheetType<FltX>? sheetType, string tag) =>
        new(0L, null, sheetType, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: tag),
            });

    /// <summary>
    /// **按类型码查成本，缺项跳过** / Costs are looked up by type code, and missing entries are skipped.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三层、成本表只覆盖 `S1` 与 `S2` → 目标里**只有两项**。
    /// 2. 系数是 `weight × cost`（权重 2、成本 3 → 6）。
    /// 3. **表里没有的类型被跳过**，不是按零计——按零计会让该类型看起来免费。
    /// 4. 目标名是 `sheet_type_cost_minimization`。
    ///
    /// Four paired points: three layers with a table covering only `S1` and `S2` leave **two terms**; the coefficient is `weight × cost` (weight 2, cost 3 gives 6); **a type missing from the table
    /// is skipped rather than priced at zero**, since zero would make it look free; and the name is `sheet_type_cost_minimization`.
    /// </remarks>
    [Fact]
    public void Build_ShouldLookUpCostsByTypeCodeAndSkipMissingOnes() {
        Layer<FltX>[] layers = { Layer(SheetType("S1"), "a"), Layer(SheetType("S2"), "b"), Layer(SheetType("S3"), "c") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        assignment.Register(model).IsOk.Should().BeTrue();

        Dictionary<SheetTypeId, FltX> costs = new()
        {
            [new SheetTypeId("S1")] = new FltX(3.0),
            [new SheetTypeId("S2")] = new FltX(5.0),
        };
        SheetAmountMinimization? objective = SheetTypeCostMinimization.Build(layers, assignment, costs, new FltX(2.0));
        objective.Should().NotBeNull();
        objective!.Name.Should().Be("sheet_type_cost_minimization");
        objective.Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().HaveCount(1);
        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(2, "S3 has no cost and is skipped");
        model.MetaSubObjects[0].Polynomial.Monomials.Select(m => m.Coefficient.ToFlt64().ToDouble())
            .Should().BeEquivalentTo(new[] { 6.0, 10.0 });

        // **缺项是 `null`，不是零**：目标层两者都被跳过、看不出差别，所以在这里直接查系数函数。
        // **A missing entry is null, not zero**: both are skipped at the objective level, so the coefficient function is inspected directly.
        objective.Coefficient(layers[2]).Should().BeNull("S3 has no cost, which is not the same as costing zero");
        objective.Coefficient(layers[0]).Should().NotBeNull();

        // **成本为零是 `0`，不是 `null`**：有数据但权重为零。
        // **A zero cost is 0, not null**: the data exists and the value is zero.
        Dictionary<SheetTypeId, FltX> freeCosts = new() { [new SheetTypeId("S1")] = new FltX(0.0) };
        SheetAmountMinimization? free = SheetTypeCostMinimization.Build(layers, assignment, freeCosts);
        free.Should().NotBeNull("the table is non-empty and the weight is non-zero");
        free!.Coefficient(layers[0]).Should().NotBeNull("a priced-at-zero type still has data");
        free.Coefficient(layers[0])!.Value.ToFlt64().ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **空成本表或零权重时不建目标** / No objective is built for an empty table or a zero weight.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 空成本表 → **返回 null**，调用方由此知道这一步什么都不用做。
    /// 2. 权重为零 → 同样返回 null（乘积恒为零）。
    /// 3. 权重为零时即使表非空也不建——不是"建一个全零目标"。
    /// 4. **没有板材类型的层被跳过**：三层里中间一层无板材，只剩两项。
    ///
    /// Four paired points: an empty table **returns null**, telling the caller this step has nothing to do; a zero weight also returns null since the product is always zero; a zero weight builds
    /// nothing even with a non-empty table rather than "an all-zero objective"; and **a layer without a sheet type is skipped**, leaving two of three terms.
    /// </remarks>
    [Fact]
    public void Build_ShouldReturnNullWhenThereIsNothingToPrice() {
        Layer<FltX>[] layers = { Layer(SheetType("S1"), "a") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL });

        SheetTypeCostMinimization.Build(
            layers, assignment, new Dictionary<SheetTypeId, FltX>()).Should().BeNull("an empty table means nothing to price");

        Dictionary<SheetTypeId, FltX> costs = new() { [new SheetTypeId("S1")] = new FltX(3.0) };
        SheetTypeCostMinimization.Build(layers, assignment, costs, new FltX(0.0))
            .Should().BeNull("a zero weight makes every coefficient zero");

        Layer<FltX>[] mixed = { Layer(SheetType("S1"), "a"), Layer(null, "b"), Layer(SheetType("S1"), "c") };
        var mixedAssignment = new PreciseAssignment(mixed, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        mixedAssignment.Register(model).IsOk.Should().BeTrue();
        SheetAmountMinimization? objective = SheetTypeCostMinimization.Build(mixed, mixedAssignment, costs);
        objective.Should().NotBeNull();
        objective!.Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(2, "the sheet-less layer is skipped");
    }
}
