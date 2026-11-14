// 超额 / 欠额符号层（B697）的判据 / Criteria for the over- and under-load symbol layers (B697).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **超额 / 欠额符号层（B697）** / The over- and under-load symbol layers (B697).
/// </summary>
/// <remarks>
/// 判据的重点：**开关关着就一个都不建**、**初值是 `load` 的拷贝**、**只建一次**、**同名重复注册被拒**。
///
/// The criteria focus on **a disabled switch building nothing**, on **the initial value being a copy of `load`**, on **building only once**, and on **a duplicate registration being rejected**.
/// </remarks>
public class ImpreciseLoadRegisterTests {
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
    /// **开关关着就一个都不建，开着各建一个** / A disabled switch builds nothing, an enabled one builds exactly one per demand.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `overEnabled: false` → `OverLoad` **为空**（建了不加等于留一个悬空符号，查不到也删不掉）。
    /// 2. `lessEnabled: false` → `LessLoad` **为空**。
    /// 3. 两个都开 → 各 **两条**，名字 `over_load_0` / `less_load_1`。
    /// 4. 再注册一次 → **失败**（模型拒绝同名符号），且**个数没有翻倍**。
    ///
    /// Four paired points: `overEnabled: false` leaves `OverLoad` **empty**, since a built-but-unregistered symbol is unreachable and undeletable; `lessEnabled: false` leaves `LessLoad`
    /// **empty**; enabling both yields **two each**, named `over_load_0` / `less_load_1`; and registering again **fails** because the model rejects a duplicate name, **without doubling the count**.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildOnlyWhatTheSwitchesEnable() {
        using var offModel = new LinearMetaModel<Flt64>();
        var off = new ImpreciseLoad(new[] { Entry("I0"), Entry("I1") }, Assignment(),
            overEnabled: false, lessEnabled: false);
        off.RegisterLoad(offModel).IsOk.Should().BeTrue();
        off.Register(offModel).IsOk.Should().BeTrue();
        off.OverLoad.Should().BeEmpty("the over-load switch is off");
        off.LessLoad.Should().BeEmpty("the under-load switch is off");

        using var model = new LinearMetaModel<Flt64>();
        var both = new ImpreciseLoad(new[] { Entry("I0"), Entry("I1") }, Assignment(),
            overEnabled: true, lessEnabled: true);
        both.RegisterLoad(model).IsOk.Should().BeTrue();
        both.Register(model).IsOk.Should().BeTrue();

        both.OverLoad.Should().HaveCount(2);
        both.OverLoad[0].Name.Should().Be("over_load_0");
        both.OverLoad[1].Name.Should().Be("over_load_1");
        both.LessLoad.Should().HaveCount(2);
        both.LessLoad[0].Name.Should().Be("less_load_0");
        both.LessLoad[1].Name.Should().Be("less_load_1");

        // **符号真的进了模型**：再加一次必须因重名被拒。只查 `OverLoad` 有两条，分不出"建了"和"建了并注册了"。
        // **The symbols are really in the model**: adding one again must be rejected for the duplicate name. Merely asserting `OverLoad` holds two cannot separate "built" from "built and registered".
        model.Add(both.OverLoad[0]).IsOk.Should().BeFalse("the over-load symbol is already registered");
        model.Add(both.LessLoad[0]).IsOk.Should().BeFalse("the under-load symbol is already registered");

        both.Register(model).IsOk.Should().BeFalse("a duplicate symbol name is rejected by the model");
        both.OverLoad.Should().HaveCount(2, "the second attempt must not double the symbols");
        both.LessLoad.Should().HaveCount(2);
    }

    /// <summary>
    /// **初值是 `load` 的拷贝，且之后不再跟着涨** / The initial value copies `load` and does not grow with it afterwards.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 注册那一刻 `over_load_i` 的多项式 **与 `load_i` 相同**——表达的是"这个需求现在被超/欠了多少"。
    /// 2. 追加新列之后 `load_0` 的项数**增加**，而 `over_load_0` **不变**——基线就是这样，本批照抄并记档。
    /// 3. 拷贝是**深拷贝**：`load_0` 后续 `Flush` 不会清空 `over_load_0` 的多项式。
    ///
    /// Three paired points: at registration `over_load_i`'s polynomial **equals `load_i`'s**, expressing by how much the demand is currently over-supplied; after appending new columns `load_0`'s term
    /// count **grows** while `over_load_0`'s **does not**, which is the baseline's behaviour, copied and recorded here; and the copy is **deep**, so a later `Flush` on `load_0` does not clear
    /// `over_load_0`'s polynomial.
    /// </remarks>
    [Fact]
    public void Register_ShouldCopyTheLoadPolynomialWithoutSharingIt() {
        using var model = new LinearMetaModel<Flt64>();
        ImpreciseAssignment assignment = Assignment();
        var load = new ImpreciseLoad(new[] { Entry("I0") }, assignment,
            overEnabled: true, lessEnabled: false);

        // **先加列再注册**：注册那一刻装载量已经有项，拷贝才有内容可比。空对空比什么都证明不了。
        // **Add the column before registering**: the load already has a term at registration, so the copy has content to compare. Comparing two empty polynomials proves nothing.
        assignment.AddColumns(0UL, new[] { Layer(2UL) }, model).IsOk.Should().BeTrue();
        load.RegisterLoad(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        load.Load[0].Polynomial.Monomials.Should().NotBeEmpty("the column contributes a term to the load");
        load.OverLoad[0].Polynomial.Monomials.Should().NotBeEmpty(
            "the over-load layer starts as a copy of the load, not as an empty polynomial");
        load.OverLoad[0].Polynomial.Monomials.Should().Equal(load.Load[0].Polynomial.Monomials);

        int before = load.OverLoad[0].Polynomial.Monomials.Count;
        ILinearIntermediateSymbol<Flt64> snapshot = load.OverLoad[0];

        assignment.AddColumns(1UL, new[] { Layer(1UL) }, model).IsOk.Should().BeTrue();
        load.AppendColumnTerms();

        load.Load[0].Polynomial.Monomials.Count.Should().BeGreaterThan(before,
            "the newly appended column contributes to the load");
        load.OverLoad[0].Polynomial.Monomials.Should().HaveCount(before,
            "the over-load layer is a snapshot taken at registration, as in the baseline");
        // **再注册一次，符号必须是原来那个对象**：只比较多项式内容分不出"复用"和"清空重建"——
        // 重建出来的符号多项式一模一样，但对象不同，于是"上加一次被拒"变成"加了个新符号"。
        // 第二次注册本身会因重名失败（模型已有一份），这里只关心**列表里的对象没被换掉**。
        load.Register(model);
        load.OverLoad[0].Should().BeSameAs(snapshot,
            "calling Register again must reuse the symbols, not rebuild them into new objects");
    }

    /// <summary>
    /// **`RegisterRmp` 真的把超额 / 欠额符号也装上模型** / `RegisterRmp` really puts the over- and under-load symbols on the model too.
    /// </summary>
    /// <remarks>
    /// 两点成对：`overEnabled: true` 时，返回的 `artifact.Load.OverLoad` **有一条**；
    /// 且**再加一次会被模型以重名拒绝**——只查"有对象"分不出"建了"和"建了并注册了"。
    /// 这条正是"`RegisterRmp` 漏掉 `load.Register`"这个变异的检出点。
    ///
    /// Two paired points: with `overEnabled: true` the returned `artifact.Load.OverLoad` **holds one symbol**, and **adding it again is rejected for the duplicate name** — asserting only that an object
    /// exists cannot separate "built" from "built and registered". This is exactly what catches the "`RegisterRmp` omits `load.Register`" mutation.
    /// </remarks>
    [Fact]
    public void RegisterRmp_ShouldRegisterTheOverLoadSymbols() {
        using var model = new LinearMetaModel<Flt64>();
        var context = new LayerAssignmentContext(new Dictionary<ItemId, ulong> { [new ItemId("I0")] = 6UL });

        Fuookami.Ospf.Utils.Functional.Result<RmpAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> registered =
            context.RegisterRmp(model, new[] { Entry("I0") }, overEnabled: true);
        registered.IsOk.Should().BeTrue();

        RmpAssignmentArtifacts artifacts =
            ((Fuookami.Ospf.Utils.Functional.Ok<RmpAssignmentArtifacts, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)registered).Value;

        artifacts.Load.OverLoad.Should().HaveCount(1);
        artifacts.Load.LessLoad.Should().HaveCount(1);
        model.Add(artifacts.Load.OverLoad[0]).IsOk.Should().BeFalse("the over-load symbol is already on the model");
        model.Add(artifacts.Load.LessLoad[0]).IsOk.Should().BeFalse("the under-load symbol is already on the model");
    }
}
