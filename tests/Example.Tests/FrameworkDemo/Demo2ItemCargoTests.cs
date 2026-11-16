// 货类的"多标签"语义判据/ Criteria for the multi-tag cargo semantics.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **`ItemCargo` 被拆进 `Item`，但多标签语义不能丢** / `ItemCargo` was flattened into `Item`, but the multi-tag semantics must survive.
/// </summary>
/// <remarks>
/// 基线把"货类集合 + 优先级"打成一个 `data class ItemCargo`，带三个 `contains` 重载。本仓把它拆成 `Item.Cargos`（集合）与 `Item.Priority`，
/// 只保留 `Item.Contains(CargoCode)`——**内容没丢，只是不再打成一个包**，所以它不进缺口表（见缺口清单第四之二节）。
///
/// 但"没丢"必须有用例兜住：基线的 `enabled` 反复问"这件货是不是 AOG **或** MAT"，而**一件货可以同时是两者**（`AOGMATAppointed` 那条规则就为此存在）。
/// 拆包时最容易犯的错是把集合退化成单个货类，那时 `AOGMAT` 货只会命中一个判定，**模型不会报错，只会少一条规则**。
///
/// 两点成对：一个带 `AOG`+`MAT` 双标签的货**两个都命中** / 一个只带 `CARGOMAT` 的货**两个都不命中**。
///
/// The baseline bundled "cargo-class set + priority" into a `data class ItemCargo` with three `contains` overloads. This port splits it into `Item.Cargos` (a set) and
/// `Item.Priority`, keeping only `Item.Contains(CargoCode)` — **nothing lost, only no longer bundled**, which is why it stays out of the inventory.
///
/// But "nothing lost" needs a test behind it: the baseline's `enabled` repeatedly asks "is this item AOG **or** MAT", and **one item can be both** (the
/// `AOGMATAppointed` rule exists for exactly that). The easy mistake while unpacking is collapsing the set into a single type — then an `AOGMAT` item satisfies only
/// one check, **and the model does not fail, it just silently loses a rule**. Two paired points: an item tagged both `AOG` and `MAT` **matches both**; an item tagged
/// only `CARGOMAT` **matches neither**.
/// </remarks>
public sealed class Demo2ItemCargoTests {
    [Fact]
    public void MultiTaggedItem_ShouldMatchEveryTagItCarries() {
        Item aogmat = Make("AOGMAT", new HashSet<CargoType> {
            CargoType.FromCode(CargoCode.AOG),
            CargoType.FromCode(CargoCode.MAT),
        });

        aogmat.Contains(CargoCode.AOG).Should().BeTrue();
        aogmat.Contains(CargoCode.MAT).Should().BeTrue();
        aogmat.Cargos.Should().HaveCount(2, "the flattened form must keep the whole set, not only the primary class");
    }

    [Fact]
    public void SingleTaggedItem_ShouldMatchOnlyItsOwnTag() {
        Item plain = Make("CARGOMAT", new HashSet<CargoType> { CargoType.FromName("CARGOMAT") });

        plain.Contains(CargoCode.AOG).Should().BeFalse();
        plain.Contains(CargoCode.MAT).Should().BeFalse();
        plain.Cargos.Should().ContainSingle("a single-tag item carries exactly one class");
    }

    [Fact]
    public void ItemWithoutAnExplicitSet_ShouldFallBackToItsPrimaryClass() {
        // 不给集合时退回主货类：**既有调用方因此一行都不用改**，也不会出现"空货类"这种第三种状态。
        // Without a set it falls back to the primary class: **existing callers need no change**, and there is no third state called "no cargo class".
        Item single = new(
            name: "C1",
            weight: 8.0,
            priority: new CargoPriority("normal", new Fuookami.Ospf.Math.Algebra.Number.UInt64(5), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "S1",
            destination: "D1",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

        single.Cargos.Should().ContainSingle();
        single.Cargos.Should().Contain(single.CargoType);
    }

    private static Item Make(string name, IReadOnlySet<CargoType> cargoSet) => new(
        name: name,
        weight: 8.0,
        priority: new CargoPriority("normal", new Fuookami.Ospf.Math.Algebra.Number.UInt64(5), CargoPriorityCategory.Normal),
        cargoType: cargoSet is HashSet<CargoType> set ? new List<CargoType>(set)[0] : CargoType.FromName(name),
        source: "S1",
        destination: "D1",
        status: ItemStatus.Optional,
        location: ItemLocation.MainDeck,
        cargos: cargoSet);
}
