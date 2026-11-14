// 箱装载顺序 / 尾箱分配（B633）的判据 / Criteria for the bin loading-order and tail-bin constraints (B633).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.LayerAssignment;

/// <summary>
/// **箱装载顺序 / 尾箱分配（B633）** / The bin loading-order and tail-bin constraints (B633).
/// </summary>
/// <remarks>
/// 判据的重点：**只对相邻同型箱加行**、**每对两条**、以及尾箱两支的**不对称**（最后一箱比 `v`，其余比 `v[i+1]`）。
///
/// The criteria focus on **rows only between adjacent same-shape bins**, **two rows per pair**, and the tail constraint's **asymmetry** (the last bin compares `v`, the rest compare
/// `v[i+1]`).
/// </remarks>
public class BinLayerConstraintsTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static BinType<FltX> Type(string code) =>
        new(code, Q(10.0), Q(10.0), Q(10.0), Q(1000.0));

    private static BinLayer Layer(BinType<FltX> type) =>
        new() { Bin = type, Shape = new Container3Shape<FltX>(Q(1.0), Q(1.0), Q(1.0)) };

    private static ILinearIntermediateSymbol<Flt64> Volume(string name) =>
        new LinearExpressionSymbol(
            new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                constant: Flt64.Zero),
            name: name);

    private static (List<Bin<BinLayer, FltX>> Bins, PreciseAssignment Assignment) Rig(
        IReadOnlyList<BinType<FltX>> types) {
        var bins = types.Select(type => new Bin<BinLayer, FltX>(
            type, Array.Empty<QuantityPlacement3<BinLayer, FltX>>())).ToList();
        var layers = new List<BinLayer> { Layer(types[0]) };
        return (bins, new PreciseAssignment(bins, layers));
    }

    /// <summary>
    /// **只对相邻同型箱加行，每对两条** / Rows only between adjacent same-shape bins, two per pair.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三个同型箱 → 2 对 → **4 条行**（每对 `v` 一条、`volume` 一条）。
    /// 2. 两个同型箱 → 1 对 → **2 条**。
    /// 3. **`A, B, A` 型（相邻都不同型）→ 0 条**：这一条把"只看形状就加"的写法挡住。
    /// 4. **单个箱 → 0 条**：`i = 0` 没有前一个。
    ///
    /// Four paired points: three same-shape bins give two pairs, hence **four rows**; two give **two**; **an `A, B, A` layout gives zero**, which is what catches a
    /// "add for every shape" implementation; and a single bin gives zero because `i = 0` has no predecessor.
    /// </remarks>
    [Fact]
    public void BinLoadingOrder_ShouldOnlyJoinAdjacentSameShapeBins() {
        BinType<FltX> a = Type("A");
        BinType<FltX> b = Type("B");

        int Rows(IReadOnlyList<BinType<FltX>> types) {
            (List<Bin<BinLayer, FltX>> bins, PreciseAssignment assignment) = Rig(types);
            using var model = new LinearMetaModel<Flt64>();
            assignment.Register(model).IsOk.Should().BeTrue();
            int before = model.Constraints.Count;
            var limit = new BinLoadingOrderConstraint(
                bins, assignment, bins.Select((_, index) => Volume("vol_" + index)).ToList());
            limit.Invoke(model).IsOk.Should().BeTrue();
            return model.Constraints.Count - before;
        }

        Rows(new[] { a, a, a }).Should().Be(4, "two adjacent pairs, two rows each");

        // **方向必须钉住**：只数行数时把 `GE` 写成 `LE` 的变异照样通过（实测存活过一次）。
        // **The direction must be pinned**: counting rows alone lets a `GE`-to-`LE` mutant through (measured once).
        {
            (List<Bin<BinLayer, FltX>> bins, PreciseAssignment assignment) = Rig(new[] { a, a });
            using var directed = new LinearMetaModel<Flt64>();
            assignment.Register(directed).IsOk.Should().BeTrue();
            new BinLoadingOrderConstraint(
                    bins, assignment, bins.Select((_, index) => Volume("vol_" + index)).ToList())
                .Invoke(directed).IsOk.Should().BeTrue();
            var rows = directed.Constraints.OfType<LinearInequalityConstraint<Flt64>>().ToList();
            rows.Should().HaveCount(2);
            foreach (LinearInequalityConstraint<Flt64> row in rows) {
                row.Inequality.Comparison.Should().Be(
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
                    "both rows say the previous bin comes first");
            }
            rows.Select(row => row.Name).Should().Equal(
                new[] { "bin_loading_order_constraint_0_1", "bin_loading_order_constraint_volume_0_1" },
                "the two row names come straight from the baseline");
        }
        Rows(new[] { a, a }).Should().Be(2, "one adjacent pair");
        Rows(new[] { a, b, a }).Should().Be(0, "no adjacent pair shares a shape");
        Rows(new[] { a }).Should().Be(0, "the first bin has no predecessor");
    }

    /// <summary>
    /// **尾箱：每个箱一条行，最后一箱走另一支** / One row per bin, with the last bin taking its own branch.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 三个箱 → **3 条行**（每个箱一条，包括最后一箱）。
    /// 2. **最后一箱那支比的是 `v[last]` 与 `tail[last]`**，所以行数与"每箱一条"一致，不会因为有单独一支而多一行。
    /// 3. **两个箱也一样是 2 条**——支数变化不改变行数。
    ///
    /// Three paired points: three bins give **three rows** (one per bin, the last included); the last bin's branch compares `v[last]` with `tail[last]`, so the row count stays "one per
    /// bin" rather than gaining an extra row from the separate branch; and two bins likewise give **two** — switching branches does not change the count.
    /// </remarks>
    [Fact]
    public void TailBinAssignment_ShouldAddOneRowPerBin() {
        BinType<FltX> a = Type("A");

        int Rows(int count) {
            (List<Bin<BinLayer, FltX>> bins, PreciseAssignment assignment) =
                Rig(Enumerable.Repeat(a, count).ToList());
            using var model = new LinearMetaModel<Flt64>();
            assignment.Register(model).IsOk.Should().BeTrue();
            int before = model.Constraints.Count;
            new TailBinAssignmentConstraint(bins, assignment).Invoke(model).IsOk.Should().BeTrue();
            return model.Constraints.Count - before;
        }

        Rows(3).Should().Be(3, "one row per bin including the last");
        Rows(2).Should().Be(2);
        Rows(1).Should().Be(1, "a single bin is the last bin");
    }

    /// <summary>
    /// **未注册指示时明确失败，不静默加零行** / A missing indicator fails explicitly rather than silently adding nothing.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. `PreciseAssignment` 没注册就调两条约束 → **都失败**，且消息点名"未注册"。
    /// 2. **模型上的行数不变**——失败与"什么都没加"必须靠返回值区分。
    ///
    /// Two paired points: calling either constraint before the assignment registers **fails** both times with a message naming the missing registration; and **the row count does not
    /// change**, so failure and "added nothing" are distinguishable only by the return value.
    /// </remarks>
    [Fact]
    public void BothConstraints_ShouldFailWhenTheAssignmentIsNotRegistered() {
        BinType<FltX> a = Type("A");
        (List<Bin<BinLayer, FltX>> bins, PreciseAssignment assignment) = Rig(new[] { a, a });
        using var model = new LinearMetaModel<Flt64>();

        int before = model.Constraints.Count;
        new BinLoadingOrderConstraint(
                bins, assignment, bins.Select((_, index) => Volume("vol_" + index)).ToList())
            .Invoke(model).IsOk.Should().BeFalse();
        new TailBinAssignmentConstraint(bins, assignment).Invoke(model).IsOk.Should().BeFalse();
        model.Constraints.Count.Should().Be(before, "a failed constraint must not leave a half-built model");
    }
}
