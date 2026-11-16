// 精确层分配的判据 / Criteria for the precise layer assignment.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.LayerAssignment;

/// <summary>
/// **精确层分配** / The precise layer assignment.
/// </summary>
/// <remarks>
/// 判据的重点：**不相容的格子被钉成 0 且不注册**、**上界向下取整**、以及四个符号都真的落到模型上。
///
/// The criteria focus on **incompatible cells being pinned to zero and not registered**, **the upper bound rounding down**, and all four symbols really landing on the model.
/// </remarks>
public class PreciseAssignmentTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static BinType<FltX> Type(string code, double width, double height, double depth) =>
        new(code, Q(width), Q(height), Q(depth), Q(width * height * depth));

    private static BinLayer Layer(BinType<FltX> type, double depth, double width = 1.0) {
        // **变化量放在深度上**：`enabled` 与 `floor(箱高/层高)` 比的都是深度，放进高度那一格测不到任何东西。
        // **The varying quantity goes on depth**: both `enabled` and `floor(bin depth / layer depth)` compare depth, so putting it in the height slot would test nothing.
        var shape = new Container3Shape<FltX>(Q(width), Q(1.0), Q(depth));
        return new BinLayer { Bin = type, Shape = shape };
    }

    private static Bin<BinLayer, FltX> Bin(BinType<FltX> type) =>
        new(type, Array.Empty<QuantityPlacement3<BinLayer, FltX>>());

    /// <summary>
    /// **不相容的格子钉成 0 且不注册；相容格子按向下取整给上界** / Incompatible cells are pinned to zero and skipped; compatible ones get a floor bound.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. **箱型不同**的格子：上界为 0，且**没有被注册**（`model.Variables` 里找不到它）。
    /// 2. **箱型相同但装不下**的格子：同样为 0、同样不注册——几何与类型是两个条件，都要满足。
    /// 3. **相容格子**有正上界。
    /// 4. **上界向下取整**：箱高 3.0、层高 1.5 给 2，箱高 3.0、层高 2.0 给 **1**（不是 2）。
    /// 5. `X` 的维度是 `箱数 × 层数`。
    ///
    /// Five paired points: a **type-mismatched** cell is bounded by zero and **not registered** (absent from `model.Variables`); a **same-type-but-does-not-fit** cell is likewise zero and
    /// unregistered, since geometry and type are separate conditions that must both hold; a **compatible** cell carries a positive bound; and **the bound rounds down**, 3.0 over 1.5 giving
    /// 2 while 3.0 over 2.0 gives **1**, not 2.
    /// </remarks>
    [Fact]
    public void Register_ShouldPinIncompatibleCellsAndFloorTheBound() {
        BinType<FltX> tall = Type("A", 10.0, 10.0, 3.0);
        BinType<FltX> other = Type("B", 10.0, 10.0, 3.0);
        var bins = new List<Bin<BinLayer, FltX>> { Bin(tall) };
        var layers = new List<BinLayer> {
            Layer(tall, 1.5),   // 相容，上界 2
            Layer(tall, 2.0),   // 相容，上界 1（向下取整）
            Layer(tall, 4.0),   // 装不下：层比箱深
            Layer(tall, 1.0, width: 20.0),  // 比箱宽：几何装不下，但 floor(3.0/1.0) = 3 > 0
            Layer(other, 1.5),  // 箱型不同
        };
        var assignment = new PreciseAssignment(bins, layers);
        using var model = new LinearMetaModel<Flt64>();

        assignment.Register(model).IsOk.Should().BeTrue();

        assignment.X.Should().NotBeNull();
        assignment.X!.Items.Length.Should().Be(5, "the matrix is 1 bin by 5 layers");

        // **`Matches` 与 `Enabled` 是两个独立条件**，在注册时是"且"的关系。
        // **`Matches` and `Enabled` are two independent conditions**, and registration requires both.
        PreciseAssignment.Matches(bins[0], layers[0]).Should().BeTrue();
        PreciseAssignment.Enabled(bins[0], layers[0]).Should().BeTrue();
        PreciseAssignment.Enabled(bins[0], layers[2]).Should().BeFalse("the bin is 3.0 deep and the layer 4.0");
        // **这一格是"几何装不下但下界仍为正"**：去掉几何判断时它会被赋成 3 而不是 0，所以只有这种层才测得出几何条件。
        // **This cell is "does not fit geometrically yet still floors positive"**: dropping the geometry check would set it to 3 rather than 0, so only this kind of layer tests it.
        PreciseAssignment.Enabled(bins[0], layers[3]).Should().BeFalse("the layer is 20 wide and the bin 10");
        PreciseAssignment.Matches(bins[0], layers[4]).Should().BeFalse("the layer belongs to a different bin type");

        // 上界用字符串比：不相容的两格与相容格必须不同，且 3.0/1.5 与 3.0/2.0 也必须不同——
        // 后者正是"向下取整"与"向上取整"的分界（ceil 会让两格相等）。
        // Bounds are compared as strings: the two incompatible cells must differ from the compatible ones, and 3.0/1.5 must differ from 3.0/2.0 — the latter is exactly where floor and
        // ceil diverge (ceil would make the two cells equal).
        string compatible = assignment.X[0, 0].Range.UpperBound!.ToString();
        string floorBound = assignment.X[0, 1].Range.UpperBound!.ToString();
        string tooTall = assignment.X[0, 2].Range.UpperBound!.ToString();
        string tooWide = assignment.X[0, 3].Range.UpperBound!.ToString();
        string wrongType = assignment.X[0, 4].Range.UpperBound!.ToString();
        compatible.Should().NotBe(floorBound, "floor division gives 2 and 1, not 2 and 2");
        tooTall.Should().Be(wrongType, "an unplaceable layer and a mismatched type are both pinned to zero");
        tooWide.Should().Be(wrongType, "a layer too wide is pinned to zero even though floor(3.0/1.0) is 3");
        tooTall.Should().NotBe(compatible, "zero must differ from the compatible bound");

        assignment.Tail.Should().NotBeNull("the tail flags are registered too");
        assignment.Tail!.Items.Length.Should().Be(1);
    }

    /// <summary>
    /// **`u` 与 `v` 逐格/逐行都建了出来** / Both `u` and `v` are built cell by cell and row by row.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **两个箱三层** → `v` 有两个（每箱一个），不是三个也不是六个。
    /// 2. `V(i)` 对每个箱下标都非空。
    /// 3. `tail` 的维度等于箱数。
    ///
    /// Three paired points: **two bins and three layers** give **two** `v` symbols (one per bin), not three or six; `V(i)` is non-null for every bin index; and `tail`'s length equals
    /// the bin count.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildEveryIndicator() {
        BinType<FltX> type = Type("A", 10.0, 10.0, 3.0);
        var bins = new List<Bin<BinLayer, FltX>> { Bin(type), Bin(type) };
        var layers = new List<BinLayer> { Layer(type, 1.0), Layer(type, 1.0), Layer(type, 1.0) };
        var assignment = new PreciseAssignment(bins, layers);
        using var model = new LinearMetaModel<Flt64>();

        assignment.Register(model).IsOk.Should().BeTrue();

        assignment.V(0).Should().NotBeNull();
        assignment.V(1).Should().NotBeNull();
        assignment.V(2).Should().BeNull("there are only two bins");
        assignment.X!.Items.Length.Should().Be(6, "two bins by three layers");
        assignment.Tail!.Items.Length.Should().Be(2);
    }
}
