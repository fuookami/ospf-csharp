// 集装器规则与决策身份的判据 / Criteria for the ULD rules and decision identity.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Xunit;
using NumUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **两条集装器规则与决策身份** / The two ULD rules and decision identity.
/// </summary>
/// <remarks>
/// 把 `Enabled` 从恒真壳改成真判断时，留了两条**查不了**的规则：`item.uld.code !in enabledULDs` 与 `taboo[item]` 的编号区间。
/// 原因是 C# 的 `Item` 没有 `Uld`、`StowagePosition` 没有 `Taboo`。这里把这两个前置补上，两条规则随即落地。
///
/// 同时 `Item` 补了 `Id`，装载矩阵的决策键从"名称"改成"身份"——按身份才是对的，C# 之前用名称，**同名两件货会共用一列**。
///
/// When turned `Enabled` from an always-true shell into a real decision, two rules stayed **uncheckable**: `item.uld.code !in enabledULDs` and the `taboo[item]` number range,
/// because C#'s `Item` had no `Uld` and `StowagePosition` had no `Taboo`. adds those prerequisites and the rules follow. `Item` also gains an `Id`, so the stowage
/// matrix keys by identity instead of name; keying by name made **two items with the same name share a column**.
/// </remarks>
public class Demo2UldRulesTests {
    private static Item MakeItem(string name, ULD? uld, string id = "", string destination = "SHA") =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new NumUInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.BAL),
            source: "PEK",
            destination: destination,
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck,
            id: id,
            uld: uld);

    private static StowagePosition MakePosition(
        IReadOnlySet<ULDCode> enabledUlds,
        PositionStowageTaboo? taboo = null) {
        var coordinate = new PositionCoordinate(
            frontArm: 500.0,
            backArm: 500.0,
            leftArm: 60.0,
            rightArm: 60.0,
            // `EnabledULDs` 由 `coordinate.Offsets.Keys` 推导（`Position` 的构造函数如此），所以"启用哪些集装器"
            // 就是"坐标表里有哪几个偏移"。
            // `EnabledULDs` is derived from `coordinate.Offsets.Keys` (that is what `Position`'s constructor does), so "which ULDs are enabled" is "which offsets exist".
            offsets: enabledUlds.ToDictionary(code => code, _ => 0.0));
        var position = new Position(
            id: 1UL,
            spaceName: "P1",
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: coordinate,
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, true, true),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 1UL,
            taboo: taboo);
    }

    private static PositionStowageTaboo Range(ULDCode code, ulong lower, ulong upper) =>
        new(new[] {
            new PositionStowageTaboo.TabooRange(
                code,
                ValueRange<NumUInt64>.Of(new NumUInt64(lower), new NumUInt64(upper)).Value,
                "禁运品"),
        });

    /// <summary>
    /// **不在 `enabledULDs` 里的集装器被拒** / A ULD outside `enabledULDs` is refused.
    /// </summary>
    /// <remarks>
    /// 成对：同一个舱位、同一种货物，换集装器代码就从拒变通。只测"拒"会让"什么都不支持"通过。
    ///
    /// Paired: same position, same cargo, changing only the ULD code flips refusal into a pass. Testing only the refusal would pass if nothing were supported.
    /// </remarks>
    [Fact]
    public void Enabled_ShouldRefuseAULDOutsideTheEnabledSet() {
        StowagePosition position = MakePosition(new HashSet<ULDCode> { ULDCode.AKE });

        position.Enabled(MakeItem("I1", new ULD("PMC12345MU", ULDCode.PMC))).Ok.Should().BeFalse(
            "PMC is not in the enabled set");
        position.Enabled(MakeItem("I1", new ULD("PMC12345MU", ULDCode.PMC))).Reason.Should().Contain("不支持该集装器类型");

        position.Enabled(MakeItem("I1", new ULD("AKE12345MU", ULDCode.AKE))).Ok.Should().BeTrue(
            "AKE is in the enabled set");
        position.Enabled(MakeItem("I1", null)).Ok.Should().BeTrue(
            "cargo with no ULD at all is not constrained by this rule");
    }

    /// <summary>
    /// **命中禁忌区间被拒，区间外通过** / A taboo hit is refused; outside the range it passes.
    /// </summary>
    /// <remarks>
    /// 编号取的是 ULD 名称的第 4—8 个字符（`AKE12345MU` 的 `12345`）。成对断言：`12345` 落在 `10000..20000` 内被拒，
    /// `99999` 落在区间外通过——只测前者会让"该 ULD 代码一律禁"通过。
    ///
    /// The number is characters 4..8 of the ULD name (`12345` for `AKE12345MU`). Paired: `12345` inside `10000..20000` is refused and `99999` outside passes — testing only the
    /// first would pass a blanket ban on that ULD code.
    /// </remarks>
    [Fact]
    public void Enabled_ShouldRefuseATabooHitOnly() {
        StowagePosition position = MakePosition(
            new HashSet<ULDCode> { ULDCode.AKE },
            Range(ULDCode.AKE, 10000UL, 20000UL));

        position.Enabled(MakeItem("I1", new ULD("AKE12345MU", ULDCode.AKE))).Ok.Should().BeFalse();
        position.Enabled(MakeItem("I1", new ULD("AKE12345MU", ULDCode.AKE))).Reason.Should().Contain("禁止装载 AKE");

        position.Enabled(MakeItem("I1", new ULD("AKE99999MU", ULDCode.AKE))).Ok.Should().BeTrue(
            "99999 is outside the forbidden range");

        StowagePosition otherCode = MakePosition(
            new HashSet<ULDCode> { ULDCode.AKE, ULDCode.PMC },
            Range(ULDCode.AKE, 10000UL, 20000UL));
        otherCode.Enabled(MakeItem("I1", new ULD("PMC12345MU", ULDCode.PMC))).Ok.Should().BeTrue(
            "the range is bound to its own ULD code");
    }

    /// <summary>
    /// **名称不合规范时不命中，也不抛异常** / A malformed name is a non-hit, not an exception.
    /// </summary>
    /// <remarks>
    /// 直接对短名称调 `substring(3, 8)` 会抛异常。一条读不懂的集装器号不该让整份配载崩掉，而"不命中"与"名称不合规范"
    /// 在禁忌检查上是同一个结果。这条是**刻意的差异**，写在这里而不是埋着。
    ///
    /// Calling `substring(3, 8)` on a short name would throw. An unreadable ULD number should not bring the whole stowage run down, and "no hit" is the same outcome as
    /// "malformed name" for taboo purposes. This is a **deliberate divergence**, recorded rather than hidden.
    /// </remarks>
    [Fact]
    public void Enabled_ShouldTreatAMalformedUldNameAsANonHit() {
        StowagePosition position = MakePosition(
            new HashSet<ULDCode> { ULDCode.AKE },
            Range(ULDCode.AKE, 0UL, 99999UL));

        position.Enabled(MakeItem("I1", new ULD("AKE", ULDCode.AKE))).Ok.Should().BeTrue(
            "a three-character name has no number to check");
        position.Enabled(MakeItem("I1", new ULD("AKEABCDEMU", ULDCode.AKE))).Ok.Should().BeTrue(
            "a non-numeric slice has no number to check");
    }

    /// <summary>
    /// **决策键是 `Id`，不是名称** / The decision key is the `Id`, not the name.
    /// </summary>
    /// <remarks>
    /// 成对：`Name` 相同而 `Id` 不同的两件货各自有决策；`Id` 相同才共用。键用名称时前一对会共用一列——两次装载写进同一个变量，
    /// 报告里两件货的数量互相顶替。
    ///
    /// Paired: two items with the same `Name` and different `Id`s get separate decisions; only the same `Id` shares. Keying by name makes the first pair share a column — two
    /// loadings write the same variable and the report swaps their quantities.
    /// </remarks>
    [Fact]
    public void ItemId_ShouldSeparateSameNamedItems() {
        Item first = MakeItem("Same", null, id: "A");
        Item second = MakeItem("Same", null, id: "B");
        Item sameId = MakeItem("Same", null, id: "A");

        first.Id.Should().Be("A");
        second.Id.Should().Be("B");
        first.Name.Should().Be(second.Name);

        Item fallback = new(
            name: "OnlyName",
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new NumUInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.BAL),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);
        fallback.Id.Should().Be("OnlyName", "omitting the id falls back to the name so old callers keep working");
        sameId.Id.Should().Be(first.Id, "two items that share an Id are the same identity by design");
        sameId.Id.Should().NotBe(second.Id, "and a different Id is a different identity even with the same name");
    }

    /// <summary>
    /// **`PositionStowageTaboo.Find` 直接判命中** / `Find` reports the hit directly.
    /// </summary>
    /// <remarks>
    /// 索引器与 `Find` 是同一个判断；单独钉一次是因为 `Enabled` 只用索引器，而 `Find` 是给"报告里说清是哪条禁忌"用的。
    ///
    /// The indexer and `Find` are one decision; pinned separately because `Enabled` only uses the indexer while `Find` exists for "say which taboo fired" in a report.
    /// </remarks>
    [Fact]
    public void Taboo_ShouldReportTheHitWithItsCommodity() {
        PositionStowageTaboo taboo = Range(ULDCode.AKE, 12345UL, 12345UL);
        Item item = MakeItem("I1", new ULD("AKE12345MU", ULDCode.AKE));

        PositionStowageTaboo.TabooRange? hit = taboo.Find(item);
        hit.Should().NotBeNull();
        hit!.Uld.Should().Be(ULDCode.AKE);
        hit.Commodity.Should().Be("禁运品");
        taboo[item].Should().Be(hit);

        PositionStowageTaboo.Empty.Find(item).Should().BeNull("an empty taboo list hits nothing");
    }
}
