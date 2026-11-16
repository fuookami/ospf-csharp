// 舱位合法性判断的判据 / Criteria for position-legality decisions.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **`Enabled` 从"恒真壳"变成真判断** / `Enabled` goes from an always-true shell to a real decision.
/// </summary>
/// <remarks>
/// 之前它是 `// TODO: Port from Kotlin Position.enabled` + `return new PositionCapability(true)`——**所有组合都合法**。
/// 于是"主舱不能装下舱货"、"散货舱只装散货"这些规则一条都没生效，而 `Stowage`/`Load` 建在上面的每一列都是错的。
///
/// 判据按**规则族**成对写：每条规则一个"该拒绝"、一个"该通过"，拒绝时还要断言**原因**。
/// 只测拒绝会让"全拒绝"通过，只测通过会让"全通过"（这里修的就是这个）通过。
///
/// It used to be `// TODO: Port from Kotlin Position.enabled` plus `return new PositionCapability(true)` — **every combination allowed**. So "main deck takes no lower-deck cargo",
/// "a bulk hold takes only bulk" and the rest were all inert, and every column `Stowage`/`Load` built on top was wrong. The criteria are **paired per rule family**: one "must refuse"
/// and one "must allow" per rule, and a refusal also has to name **which rule** fired. Testing refusals only would pass if everything refused; testing passes only would pass the
/// all-allow shell.
/// </remarks>
public class Demo2PositionEnabledTests {
    private static Item MakeItem(
        ItemLocation location,
        params CargoCode[] codes) {
        var cargos = codes.Select(CargoType.FromCode).ToHashSet();
        return new Item(
            name: "I",
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: cargos.First(),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: location,
            cargos: cargos);
    }

    private static Item MakeItemAt(
        string destination,
        ItemLocation location,
        params CargoCode[] codes) {
        var cargos = codes.Select(CargoType.FromCode).ToHashSet();
        return new Item(
            name: "I" + destination,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: cargos.First(),
            source: "PEK",
            destination: destination,
            status: ItemStatus.Optional,
            location: location,
            cargos: cargos);
    }

    private static StowagePosition MakePosition(
        PositionLocation location,
        PositionType? type = null,
        IReadOnlyList<Item>? loaded = null) {
        var position = new Position(
            id: 1UL,
            spaceName: "P1",
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(0.0, 1.0, -0.5, 0.5),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: location);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(stowageNeeded: true, adjustmentNeeded: false, predicateWeightNeeded: true, recommendedWeightNeeded: false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 1UL,
            loadedItems: loaded,
            type: type);
    }

    /// <summary>**主舱与下舱互斥**（两个方向） / Main and lower deck exclude each other, both ways.</summary>
    [Fact]
    public void Enabled_ShouldExcludeMainAgainstLowerDeck() {
        StowagePosition main = MakePosition(PositionLocation.NormalMainLocation);
        StowagePosition low = MakePosition(PositionLocation.LowForwardNotBulkLocation);

        main.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.BAL)).Ok.Should().BeFalse(
            "main deck does not take lower-deck cargo");
        main.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.BAL)).Reason.Should().Contain("主舱禁止装载下货舱装载物");

        low.Enabled(MakeItem(ItemLocation.Head, CargoCode.BAL)).Ok.Should().BeFalse(
            "the lower hold takes neither head nor tail cargo");
        // 理由钉住**是哪条规则**拒绝的：同一对被两条规则覆盖（低舱拒头尾、非头舱位拒头舱货），
        // 只断言 false 时两条规则可以互相顶替——把其中一条关掉判据照样绿。
        // The reason pins **which rule** refused: the pair is covered by two rules (the lower hold refuses head/tail cargo, a non-head position refuses head cargo), and a bare
        // `false` lets them stand in for each other — disabling one leaves the criterion green.
        low.Enabled(MakeItem(ItemLocation.Head, CargoCode.BAL)).Reason.Should().Contain("下货舱禁止装载头尾舱装载物");

        main.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.BAL)).Ok.Should().BeTrue();
        low.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.BAL)).Ok.Should().BeTrue();
    }

    /// <summary>**散货与非散货互斥**（两个方向） / Bulk and non-bulk exclude each other, both ways.</summary>
    [Fact]
    public void Enabled_ShouldExcludeBulkAgainstNonBulk() {
        StowagePosition bulk = MakePosition(PositionLocation.LowForwardBulkLocation);
        StowagePosition notBulk = MakePosition(PositionLocation.LowForwardNotBulkLocation);

        bulk.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.BAL)).Ok.Should().BeFalse("a bulk hold takes no non-bulk cargo");
        bulk.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.BAL)).Reason.Should().Contain("散货舱禁止装载非散货装载物");
        notBulk.Enabled(MakeItem(ItemLocation.LowBulk, CargoCode.BAL)).Ok.Should().BeFalse("a non-bulk hold takes no bulk cargo");

        bulk.Enabled(MakeItem(ItemLocation.LowBulk, CargoCode.BAL)).Ok.Should().BeTrue();
        notBulk.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.BAL)).Ok.Should().BeTrue();
    }

    /// <summary>**头尾舱位互斥**（两个方向） / Head and tail sections exclude each other, both ways.</summary>
    [Fact]
    public void Enabled_ShouldExcludeHeadAgainstTail() {
        StowagePosition head = MakePosition(PositionLocation.HeadLocation);
        StowagePosition normal = MakePosition(PositionLocation.NormalMainLocation);

        head.Enabled(MakeItem(ItemLocation.Tail, CargoCode.BAL)).Ok.Should().BeFalse("the head section takes no tail cargo");
        head.Enabled(MakeItem(ItemLocation.Tail, CargoCode.BAL)).Reason.Should().Contain("头舱禁止装载非头舱装载物");
        normal.Enabled(MakeItem(ItemLocation.Head, CargoCode.BAL)).Ok.Should().BeFalse("a plain main-deck position takes no head cargo");

        head.Enabled(MakeItem(ItemLocation.Head, CargoCode.BAL)).Ok.Should().BeTrue();
        normal.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.BAL)).Ok.Should().BeTrue();
    }

    /// <summary>**下舱不收虚拟装载物** / The lower hold takes no virtual cargo.</summary>
    [Fact]
    public void Enabled_ShouldKeepVirtualCargoOffTheLowerDeck() {
        StowagePosition low = MakePosition(PositionLocation.LowForwardNotBulkLocation);
        StowagePosition main = MakePosition(PositionLocation.NormalMainLocation);

        low.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.Virtual)).Ok.Should().BeFalse();
        low.Enabled(MakeItem(ItemLocation.LowNotBulk, CargoCode.Virtual)).Reason.Should().Contain("下货舱禁止装载虚拟装载物");
        main.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Virtual)).Ok.Should().BeTrue("the rule is specific to the lower hold");
    }

    /// <summary>**舱位类型码的四条禁令各自生效** / Each of the four type-code bans takes effect.</summary>
    [Fact]
    public void Enabled_ShouldHonourEachTypeCodeBan() {
        StowagePosition stiff = MakePosition(PositionLocation.NormalMainLocation,
            new PositionType(new[] { PositionTypeCode.StiffCargoForbidden }));
        StowagePosition crush = MakePosition(PositionLocation.NormalMainLocation,
            new PositionType(new[] { PositionTypeCode.CrushItemForbidden }));
        StowagePosition emptyItem = MakePosition(PositionLocation.NormalMainLocation,
            new PositionType(new[] { PositionTypeCode.EmptyItemForbidden }));
        StowagePosition noEmpty = MakePosition(PositionLocation.NormalMainLocation,
            new PositionType(new[] { PositionTypeCode.EmptyForbidden }));

        stiff.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Stiff)).Ok.Should().BeFalse();
        crush.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Crush)).Ok.Should().BeFalse();
        emptyItem.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Empty)).Ok.Should().BeFalse();
        noEmpty.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Virtual)).Ok.Should().BeFalse();

        // 成对：同类型码下换成别的货类必须通过——否则"四条禁令"其实是"四条全禁"。
        // Paired: under the same code an unrelated cargo class must pass, otherwise the four bans are really one blanket ban.
        stiff.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.BAL)).Ok.Should().BeTrue();
        crush.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Stiff)).Ok.Should().BeTrue();
        emptyItem.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Empty)).Ok.Should().BeFalse();
        noEmpty.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.Empty)).Ok.Should().BeTrue();
    }

    /// <summary>**航材指定舱位只装航材** / An AOG/MAT appointed position takes only AOG/MAT.</summary>
    [Fact]
    public void Enabled_ShouldKeepNonAogCargoOutOfAnAogAppointedPosition() {
        StowagePosition appointed = MakePosition(PositionLocation.NormalMainLocation,
            new PositionType(new[] { PositionTypeCode.AOGMATAppointed }));

        appointed.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.BAL)).Ok.Should().BeFalse();
        appointed.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.BAL)).Reason.Should().Contain("航材指定舱位只能装载航材");
        appointed.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.AOG)).Ok.Should().BeTrue();
        appointed.Enabled(MakeItem(ItemLocation.MainDeck, CargoCode.MAT)).Ok.Should().BeTrue("AOG and MAT are two codes of the one rule");
    }

    /// <summary>**已装航材的散舱不收普通散货** / A bulk hold already holding AOG/MAT takes no ordinary bulk.</summary>
    [Fact]
    public void Enabled_ShouldKeepOrdinaryBulkOutOfAnAogLoadedHold() {
        Item loadedAog = MakeItemAt("SHA", ItemLocation.LowBulk, CargoCode.AOG);
        StowagePosition hold = MakePosition(PositionLocation.LowForwardBulkLocation, loaded: new[] { loadedAog });

        hold.Enabled(MakeItemAt("SHA", ItemLocation.LowBulk, CargoCode.BAL)).Ok.Should().BeFalse();
        hold.Enabled(MakeItemAt("SHA", ItemLocation.LowBulk, CargoCode.BAL)).Reason.Should().Contain("已经装载航材的散舱不能装载普通散货");
        hold.Enabled(MakeItemAt("PVG", ItemLocation.LowBulk, CargoCode.MAT)).Ok.Should().BeTrue("AOG and MAT may share the hold");
    }

    /// <summary>**普散货指定舱位不收航材** / A normal-bulk appointed position takes no AOG/MAT.</summary>
    [Fact]
    public void Enabled_ShouldKeepAogOutOfANormalBulkAppointedPosition() {
        StowagePosition appointed = MakePosition(PositionLocation.LowForwardBulkLocation,
            new PositionType(new[] { PositionTypeCode.NormalBulkAppointed }));

        appointed.Enabled(MakeItem(ItemLocation.LowBulk, CargoCode.AOG)).Ok.Should().BeFalse();
        appointed.Enabled(MakeItem(ItemLocation.LowBulk, CargoCode.AOG)).Reason.Should().Contain("普散货指定舱位只能装载普通散货");
        appointed.Enabled(MakeItem(ItemLocation.LowBulk, CargoCode.BAL)).Ok.Should().BeTrue();
    }

    /// <summary>**散货舱只装同一目的地的散货** / A bulk hold takes bulk to one destination only.</summary>
    [Fact]
    public void Enabled_ShouldKeepOneDestinationPerBulkHold() {
        Item loaded = MakeItemAt("SHA", ItemLocation.LowBulk, CargoCode.BAL);
        StowagePosition hold = MakePosition(PositionLocation.LowForwardBulkLocation, loaded: new[] { loaded });

        hold.Enabled(MakeItemAt("PVG", ItemLocation.LowBulk, CargoCode.BAL)).Ok.Should().BeFalse();
        hold.Enabled(MakeItemAt("PVG", ItemLocation.LowBulk, CargoCode.BAL)).Reason.Should().Contain("散货舱只能装载同一目的地的散货");
        hold.Enabled(MakeItemAt("SHA", ItemLocation.LowBulk, CargoCode.BAL)).Ok.Should().BeTrue("the same destination shares the hold");
    }
}
