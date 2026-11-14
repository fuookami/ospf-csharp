// 货类集合与舱位类型的判据 / Criteria for cargo-class sets and position types.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **一件货可以同时属于多个货类（B574）** / One item can belong to several cargo classes (B574).
/// </summary>
/// <remarks>
/// 基线的 `enabled()` 反复问"这件货是不是 AOG **或** MAT"——因为它真的可能是两者。C# 的 `Item` 原来只有一个
/// `CargoType`，只能表达"是 AOG 或 MAT 中的一个"。这不是风格问题：`AOGMATAppointed`、`NormalBulkAppointed`
/// 两条规则就是为组合货类存在的，单值模型下它们要么永远成立、要么永远不成立，**两种都错**。
///
/// 舱位类型同理：`enabled()` 的十几条检查全是"这个码在不在集合里"，所以类型必须挂在**舱位**上，且是集合。
///
/// The baseline's `enabled()` repeatedly asks "is this item AOG **or** MAT" — because it can be both. C#'s `Item` used to hold a single `CargoType`, which can only say "it is one of
/// AOG or MAT". That is not a style point: the `AOGMATAppointed` and `NormalBulkAppointed` rules exist exactly for combined classes, and under a single-value model they either
/// always hold or never hold — **both wrong**. Same for position types: a dozen checks in `enabled()` are "is this code in the set", so the type belongs to the **position** as a set.
/// </remarks>
public class Demo2StowageTypeTests {
    private static readonly PositionLocation MainLocation = PositionLocation.NormalMainLocation;

    private static Item MakeItem(params CargoCode[] codes) {
        var cargos = codes.Select(CargoType.FromCode).ToList();
        return new Item(
            name: "I1",
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: cargos[0],
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: new ItemLocation(new HashSet<DeckLocation> { DeckLocation.Main }),
            cargos: cargos.ToHashSet());
    }

    private static StowagePosition MakePosition(PositionType? type = null) {
        var position = new Position(
            id: 1UL,
            spaceName: "P1",
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(0.0, 1.0, -0.5, 0.5),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: MainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(stowageNeeded: true, adjustmentNeeded: false, predicateWeightNeeded: true, recommendedWeightNeeded: false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 1UL,
            type: type);
    }

    /// <summary>
    /// **组合货类必须能被表达** / A combined cargo class must be expressible.
    /// </summary>
    /// <remarks>
    /// 成对：`{AOG, MAT}` 两码都成立、第三个码不成立；只给一个码时其余码不成立。
    /// 只测"包含"会让"全部返回 true"通过。
    ///
    /// Paired: both codes of `{AOG, MAT}` hold and a third does not; a single-code item does not contain the others. Testing only "contains" would pass if everything returned true.
    /// </remarks>
    [Fact]
    public void Item_ShouldCarryEveryCargoClass() {
        Item both = MakeItem(CargoCode.AOG, CargoCode.MAT);
        Item single = MakeItem(CargoCode.Crush);

        both.Contains(CargoCode.AOG).Should().BeTrue();
        both.Contains(CargoCode.MAT).Should().BeTrue();
        both.Contains(CargoCode.Crush).Should().BeFalse("an item that is AOG+MAT is not crush cargo");

        single.Contains(CargoCode.Crush).Should().BeTrue();
        single.Contains(CargoCode.AOG).Should().BeFalse();
        single.Cargos.Should().HaveCount(1);
        both.Cargos.Should().HaveCount(2);
    }

    /// <summary>
    /// **不传货类集合时回落成主货类** / Without a cargo set the primary class is used.
    /// </summary>
    /// <remarks>
    /// 老调用方一行都不用改，但也不能出现"空货类"这种第三种状态——那会让 `Contains` 全线返回 `false`，
    /// 于是所有"是不是 AOG"的规则静默失效。
    ///
    /// Existing callers need no change, but there must be no third state called "no cargo class": it would make `Contains` return `false` across the board and silently disable every
    /// "is it AOG" rule.
    /// </remarks>
    [Fact]
    public void Item_ShouldFallBackToThePrimaryCargoClass() {
        var item = new Item(
            name: "I2",
            weight: 10.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.AVI),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: new ItemLocation(new HashSet<DeckLocation> { DeckLocation.Main }));

        item.Cargos.Should().ContainSingle().Which.Code.Should().Be(CargoCode.AVI);
        item.Contains(CargoCode.AVI).Should().BeTrue();
        item.Contains(CargoCode.MAT).Should().BeFalse();
    }

    /// <summary>
    /// **舱位类型码与基线逐项对齐** / The position type codes match the baseline member for member.
    /// </summary>
    /// <remarks>
    /// 名字就是规则的开关：把 `CopyItemForbidden` 之类差一个字母的名字写进来不会报错，只会让那条规则永远不成立。
    /// 因此这里连**数量**一起钉（基线 7 项）。
    ///
    /// The names are the rules' switches: a near-miss spelling never fails to compile, it just makes that rule never fire. The **count** is pinned too (7 in the baseline).
    /// </remarks>
    [Fact]
    public void PositionType_ShouldMirrorTheBaselineCodes() {
        string[] names = Enum.GetNames<PositionTypeCode>();

        names.Should().BeEquivalentTo(new[] {
            "EmptyForbidden", "EmptyHated", "EmptyItemForbidden", "CrushItemForbidden",
            "StiffCargoForbidden", "AOGMATAppointed", "NormalBulkAppointed",
        });
        names.Should().HaveCount(7);
    }

    /// <summary>
    /// **舱位携带自己的类型集合** / A position carries its own type set.
    /// </summary>
    /// <remarks>
    /// 成对：给了集合就只含给的码，没给就是空集合（空集合里 `Contains` 必须返回 `false`，而不是"默认允许"）。
    ///
    /// Paired: an explicit set contains exactly those codes; without one the set is empty and `Contains` must return `false` rather than "allowed by default".
    /// </remarks>
    [Fact]
    public void Position_ShouldCarryItsTypeSet() {
        var typed = new PositionType(new[] { PositionTypeCode.AOGMATAppointed, PositionTypeCode.EmptyForbidden });
        StowagePosition position = MakePosition(typed);

        position.Type.Contains(PositionTypeCode.AOGMATAppointed).Should().BeTrue();
        position.Type.Contains(PositionTypeCode.EmptyForbidden).Should().BeTrue();
        position.Type.Contains(PositionTypeCode.CrushItemForbidden).Should().BeFalse();

        StowagePosition untyped = MakePosition();
        untyped.Type.Codes.Should().BeEmpty();
        untyped.Type.Contains(PositionTypeCode.AOGMATAppointed).Should().BeFalse("an empty type set forbids nothing but contains nothing either");
        PositionType.Empty.Contains(PositionTypeCode.EmptyHated).Should().BeFalse();
    }
}
