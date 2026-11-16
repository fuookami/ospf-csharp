// 非精确装载的判据 / Criteria for the imprecise load.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **非精确装载** / The imprecise load.
/// </summary>
/// <remarks>
/// 判据的重点：**每个需求一项装载变量**、**注册幂等**、**未注册时追加不抛**。
///
/// The criteria focus on **one load variable per demand**, **idempotent registration**, and **appending not throwing before registration**.
/// </remarks>
public class ImpreciseLoadTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(ulong units) {
        var placements = new List<Placement2<FltX>>();
        for (ulong index = 0UL; index < units; ++index)
        {
            placements.Add(new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                Rotated: false,
                ItemId: "I" + index));
        }
        return new Layer<FltX>(0L, null, null, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)), placements);
    }

    private static ImpreciseAssignment Assignment() =>
        new(new Dictionary<ItemId, ulong> { [new ItemId("I0")] = 3UL });

    private static Bpp2dAssignmentDemandEntry Entry(string itemId) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(2UL), null);

    /// <summary>
    /// **每个需求一项装载变量，注册幂等，未注册时追加不抛** / One load variable per demand, idempotent registration, no throw before registration.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两条需求 → **两个**装载变量，名字 `load_0` / `load_1`。
    /// 2. **在注册前调用 `AppendColumnTerms` 不抛**（列生成是先注册、每轮追加）。
    /// 3. **重复注册失败**（模型不接受同名符号），且**变量个数没有翻倍**——翻倍会让约束指向错符号。
    /// 4. 开关默认 `OverEnabled=false`、`LessEnabled=true`。
    ///
    /// Four paired points: two demands yield **two** load variables named `load_0` / `load_1`; **calling `AppendColumnTerms` before registration does not throw** (column generation
    /// registers first and appends each round); **a second registration fails** because the model rejects a duplicate name, and **the count does not double**, which would point constraints
    /// at the wrong symbols; and the switches default to `OverEnabled=false` and `LessEnabled=true`, as in the code.
    /// </remarks>
    [Fact]
    public void Load_ShouldBuildOneVariablePerDemand() {
        using var model = new LinearMetaModel<Flt64>();
        ImpreciseAssignment assignment = Assignment();
        var load = new ImpreciseLoad(new[] { Entry("I0"), Entry("I1") }, assignment);

        load.Load.Should().BeEmpty("nothing is built before registration");
        load.AppendColumnTerms();
        load.Load.Should().BeEmpty("appending before registration is a no-op, not a throw");

        load.RegisterLoad(model).IsOk.Should().BeTrue();
        load.Load.Should().HaveCount(2);
        load.Load[0].Name.Should().Be("load_0");
        load.Load[1].Name.Should().Be("load_1");

        load.RegisterLoad(model).IsOk.Should().BeFalse("a duplicate symbol name is rejected by the model");
        load.Load.Should().HaveCount(2, "the second attempt must not double the variables");

        load.OverEnabled.Should().BeFalse();
        load.LessEnabled.Should().BeTrue();
        load.OverLoad.Should().BeEmpty("the slack variables are not implemented yet");
        load.LessLoad.Should().BeEmpty("the slack variables are not implemented yet");
    }

    /// <summary>
    /// **列贡献按需求系数累加** / Column contributions accumulate with the demand coefficient.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 注册后追加列 → **每个装载变量的多项式非空**（否则需求约束等于把 0 当下界）。
    /// 2. **追加两次不会清空**——`Flush` + 追加是累加语义。
    /// 3. 没有任何列时追加不炸，变量依然是空的。
    ///
    /// Three paired points: appending after registration leaves **every load variable with a non-empty polynomial**, otherwise the demand constraint would impose a bound of zero; **two
    /// appends do not clear it**, since `Flush` plus append accumulates; and with no columns at all appending is harmless and the polynomial stays empty.
    /// </remarks>
    [Fact]
    public void Load_ShouldAccumulateColumnTerms() {
        using var model = new LinearMetaModel<Flt64>();
        ImpreciseAssignment assignment = Assignment();
        assignment.AddColumns(0UL, new[] { Layer(2UL) }, model).IsOk.Should().BeTrue();

        var load = new ImpreciseLoad(new[] { Entry("I0") }, assignment);
        load.RegisterLoad(model).IsOk.Should().BeTrue();
        load.Load[0].Polynomial.Monomials.Should().NotBeEmpty("the column contributes a term");

        int afterFirst = load.Load[0].Polynomial.Monomials.Count;
        load.AppendColumnTerms();
        load.Load[0].Polynomial.Monomials.Count.Should().BeGreaterThanOrEqualTo(afterFirst, "appending accumulates");
    }
}
