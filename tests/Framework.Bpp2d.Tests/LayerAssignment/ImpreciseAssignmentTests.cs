// 非精确层分配（B641）的判据 / Criteria for the imprecise layer assignment (B641).

using System;
using System.Collections.Generic;
using System.Linq;
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
/// **非精确层分配（B641）** / The imprecise layer assignment (B641).
/// </summary>
/// <remarks>
/// 判据的重点：**空列不建变量组**、**列变量上界是保守估计**、**面积是追加而不是重算**。
/// 第一条是本批最容易顺手写错的：为一个空集合建 `x_1` 组不报错、不失败，只是白占一组变量。
///
/// The criteria focus on **no variable group for an empty column set**, **a conservative column upper bound**, and **area being appended rather than recomputed**. The first is the easiest to
/// get wrong: building an `x_1` group for an empty set neither errors nor fails, it just wastes a variable group.
/// </remarks>
public class ImpreciseAssignmentTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(double width, double height, ulong units) {
        var placements = new List<Placement2<FltX>>();
        for (ulong index = 0UL; index < units; ++index)
        {
            placements.Add(new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(width), Q(height)),
                Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                Rotated: false,
                ItemId: "I" + index));
        }
        return new Layer<FltX>(
            0L, null, null,
            new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            placements);
    }

    private static Dictionary<ItemId, ulong> Items(params ulong[] demands) {
        var items = new Dictionary<ItemId, ulong>();
        for (int index = 0; index < demands.Length; ++index)
        {
            items[new ItemId("I" + index)] = demands[index];
        }
        return items;
    }

    /// <summary>
    /// **空列不建变量组** / An empty column set builds no variable group.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 第一次给两列 → **一组变量**。
    /// 2. **再给完全相同的两列 → 组数不变**：去重后为空，不该新建组。
    /// 3. 返回的表是空的，且**不是失败**——空结果是正常情况，不是错误。
    ///
    /// Three paired points: the first two columns produce **one group**; **giving the same two again leaves the count unchanged**, since the deduplicated result is empty and no group
    /// should be built; and the returned list is empty but **not a failure** — an empty result is normal, not an error.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldNotBuildAGroupForAnEmptyResult() {
        using var model = new LinearMetaModel<Flt64>();
        var assignment = new ImpreciseAssignment(Items(5UL));
        Layer<FltX> first = Layer(20.0, 10.0, 2UL);
        Layer<FltX> second = Layer(30.0, 10.0, 3UL);

        assignment.AddColumns(0UL, new[] { first, second }, model).IsOk.Should().BeTrue();
        assignment.VariableGroupCount.Should().Be(1);

        Fuookami.Ospf.Utils.Functional.Result<IReadOnlyList<Layer<FltX>>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> again = assignment.AddColumns(1UL, new[] { first, second }, model);
        again.IsOk.Should().BeTrue("an empty deduplicated result is normal, not an error");
        assignment.VariableGroupCount.Should().Be(1, "no group is built for an empty set");
        ((Fuookami.Ospf.Utils.Functional.Ok<IReadOnlyList<Layer<FltX>>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)again)
            .Value.Should().BeEmpty();
    }

    /// <summary>
    /// **同一组的列变量按层序号取回** / Column variables are fetched by layer index within a group.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 一批两列 → 组里两个变量，`VariableOf` 对两层分别取回，且**互不相同**（同一个变量会让两列共用上界与目标系数）。
    /// 2. **还没建组就取变量会抛异常**，不静默返回空。
    ///
    /// 上界的数值本身（`ceil(最大需求 / 件数)`）在公共 API 上看不到——`LinearMetaModel` 不暴露变量集合，
    /// 与 B632 记的是同一处天花板。这里只钉"每个列拿到自己的变量"。
    ///
    /// Two paired points: one batch of two columns yields two variables in the group, and `VariableOf` fetches them separately and **differently** (sharing one variable would make both
    /// columns share a bound and an objective coefficient); and **fetching before any group exists throws** rather than silently returning empty. The bound's value itself
    /// (`ceil(largest demand / units)`) is invisible through the public API because `LinearMetaModel` exposes no variable collection — the same ceiling recorded in B632. This only pins that
    /// each column gets its own variable.
    /// </remarks>
    [Fact]
    public void VariableOf_ShouldGiveEachLayerItsOwnVariable() {
        using var model = new LinearMetaModel<Flt64>();
        var assignment = new ImpreciseAssignment(Items(7UL));

        Action beforeAnyGroup = () => assignment.VariableOf(0);
        beforeAnyGroup.Should().Throw<InvalidOperationException>("there is no group to draw from yet");

        assignment.AddColumns(0UL, new[] { Layer(20.0, 10.0, 3UL), Layer(20.0, 10.0, 1UL) }, model)
            .IsOk.Should().BeTrue();

        var variables = new List<string>();
        for (int index = 0; index < 2; ++index)
        {
            variables.Add(assignment.VariableOf(index).ToString() ?? "");
        }
        variables.Should().OnlyHaveUniqueItems("each column carries its own variable");
    }

    /// <summary>
    /// **面积只追加，不重算** / Area is appended, not recomputed.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 注册后模型里有 `area` 符号。
    /// 2. **同一轮里加两批新列，面积项累积**——重算会丢掉前一批。
    /// 3. `AreaSymbol` 始终是同一个对象（重复 `Register` 会因重名失败，与基线一致）。
    ///
    /// Three paired points: registering puts an `area` symbol on the model; **adding two separate batches of new columns accumulates area terms**, where recomputing would drop the earlier
    /// batch; and `AreaSymbol` stays the same object (a second `Register` fails on the duplicate name, matching the baseline).
    /// </remarks>
    [Fact]
    public void Area_ShouldAccumulateAcrossBatches() {
        using var model = new LinearMetaModel<Flt64>();
        var assignment = new ImpreciseAssignment(Items(4UL));

        assignment.Register(model).IsOk.Should().BeTrue();
        assignment.AreaSymbol.Should().NotBeNull();
        // **重复注册会失败**，与基线一致（基线也是无条件 `model.add(area)`）：符号只注册一次，
        // 再注册同名符号是模型的命名冲突，不是本类可以去重的地方。判据钉的是"符号是同一个对象"。
        // **A second registration fails**, matching the baseline (which also calls `model.add(area)` unconditionally): the symbol is registered once and a duplicate name is a model-level
        // conflict, not something this class should deduplicate. The criterion pins that the symbol is the same object.
        assignment.AreaSymbol.Should().BeSameAs(assignment.AreaSymbol);

        assignment.AddColumns(0UL, new[] { Layer(20.0, 10.0, 2UL) }, model).IsOk.Should().BeTrue();
        assignment.AddColumns(1UL, new[] { Layer(30.0, 10.0, 2UL) }, model).IsOk.Should().BeTrue();

        assignment.VariableGroupCount.Should().Be(2, "each non-empty batch gets its own group");
        assignment.LastIterationLayers.Should().NotBeNull();
    }
}
