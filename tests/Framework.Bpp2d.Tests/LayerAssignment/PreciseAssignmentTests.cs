// 精确层分配的判据 / Criteria for the precise layer assignment.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **精确层分配** / The precise layer assignment.
/// </summary>
/// <remarks>
/// 判据的重点：**份数上界至少为 1**（`maxCopies` 为 0 时不能让该层被强制不用）、**`Register` 幂等**（第二次失败而不是翻倍）。
///
/// The criteria focus on **the copy bound being at least 1** (a zero `maxCopies` must not force the layer unused) and **`Register` being idempotent** in the baseline's sense: a second call
/// fails rather than doubling.
/// </remarks>
public class PreciseAssignmentTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(ulong units, string tag) {
        var placements = new List<Placement2<FltX>>();
        for (ulong index = 0UL; index < units; ++index)
        {
            placements.Add(new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                Rotated: false,
                ItemId: tag + index));
        }
        return new Layer<FltX>(0L, null, null, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)), placements);
    }

    /// <summary>
    /// **每层一个份数变量，上界至少为 1** / One copy variable per layer, with a bound of at least 1.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 注册后**每层都有变量**，越界下标返回 null。
    /// 2. **`maxCopies` 为 0 时上界仍为 1**——这是基线的 `if (upper < 1) 1 else upper`；上界 0 会让该层被强制不用，
    /// 而列生成正要用它判断"用不用"。
    /// 3. 上界按 `maxCopies` 取值（给 3 就是 3）。
    /// 4. **`maxCopies` 短于层数时缺失项按 1**，不抛越界。
    ///
    /// Four paired points: after registration **every layer has a variable** and an out-of-range index returns null; **a `maxCopies` of 0 still yields a bound of 1**, per the baseline's
    /// `if (upper < 1) 1 else upper`, since a bound of 0 would force the layer unused while column generation needs exactly that decision; a supplied bound is honoured (3 stays 3); and **a
    /// `maxCopies` shorter than the layer list treats missing entries as 1** rather than throwing.
    /// </remarks>
    [Fact]
    public void Register_ShouldBoundEveryLayerAtLeastOnce() {
        using var model = new LinearMetaModel<Flt64>();
        Layer<FltX>[] layers = { Layer(1UL, "a"), Layer(2UL, "b"), Layer(3UL, "c") };
        var assignment = new PreciseAssignment(layers, new[] { 0UL, 3UL });

        assignment.Y.Should().BeNull("nothing is built before registration");
        assignment.YOf(0).Should().BeNull();

        assignment.Register(model).IsOk.Should().BeTrue();
        assignment.Y.Should().NotBeNull();
        assignment.YOf(0).Should().NotBeNull();
        assignment.YOf(2).Should().NotBeNull("a missing maxCopies entry still gets a variable");
        assignment.YOf(3).Should().BeNull("out of range is null, not a throw");
        assignment.YOf(-1).Should().BeNull();

        assignment.Y![0].Range.UpperBound.Should().NotBeNull();
        assignment.Y[0].Range.UpperBound!.Value.UnwrapOrNull()!
            .Should().Be(new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL),
                "a bound of zero would force the layer unused");
        assignment.Y[1].Range.UpperBound!.Value.UnwrapOrNull()!
            .Should().Be(new Fuookami.Ospf.Math.Algebra.Number.UInt64(3UL));
        assignment.Y[2].Range.UpperBound!.Value.UnwrapOrNull()!
            .Should().Be(new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL),
                "the missing entry defaults to one");

        // **实测**：`model.Add(variable)` 容忍重复注册（与符号注册不同），所以第二次成功且层集合不变。
        // **Measured**: `model.Add(variable)` tolerates a duplicate registration, unlike symbol registration, so the second call succeeds and the layer set is unchanged.
        assignment.Register(model).IsOk.Should().BeTrue("a duplicate variable registration is tolerated");
        assignment.Layers.Should().HaveCount(3, "the second attempt must not change the layer set");
    }

    /// <summary>
    /// **层与份数原样保留，变量只建一次** / Layers and copy counts survive as given, and the variable is built once.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `Layers` / `MaxCopies` **原样读回**（不复制、不排序、不裁剪）。
    /// 2. 注册前后 `Y` 是**同一个对象**——重复注册不会换一个新变量让先前的约束失效。
    /// 3. 空层列表也能注册，不抛。
    ///
    /// Three paired points: `Layers` and `MaxCopies` **read back as given**, uncopied, unsorted and untruncated; `Y` is **the same object** before and after a second registration, so earlier
    /// constraints are not invalidated; and an empty layer list registers without throwing.
    /// </remarks>
    [Fact]
    public void Register_ShouldKeepTheInputsAndReuseTheVariable() {
        using var model = new LinearMetaModel<Flt64>();
        Layer<FltX>[] layers = { Layer(1UL, "a"), Layer(2UL, "b") };
        ulong[] copies = { 2UL, 5UL };
        var assignment = new PreciseAssignment(layers, copies);

        assignment.Layers.Should().BeSameAs(layers);
        assignment.MaxCopies.Should().BeSameAs(copies);

        assignment.Register(model).IsOk.Should().BeTrue();
        UIntVariable1 first = assignment.Y!;
        assignment.Register(model).IsOk.Should().BeTrue("a duplicate variable registration is tolerated");
        assignment.Y.Should().BeSameAs(first, "re-registration must not replace the variable");

        using var emptyModel = new LinearMetaModel<Flt64>();
        var empty = new PreciseAssignment(new Layer<FltX>[0], new ulong[0]);
        empty.Register(emptyModel).IsOk.Should().BeTrue("an empty layer set is a normal input");
    }
}
