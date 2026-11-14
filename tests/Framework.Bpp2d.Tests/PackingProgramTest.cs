// 排样程序测试 / Tests for packing programs.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 排样程序 / Packing programs.
/// </summary>
public sealed class PackingProgramTest {
    /// <summary>总件数等于每行件数乘行数 / The total amount is per-row times rows.</summary>
    [Fact]
    public void Program_ShouldMultiplyAmount() =>
        new PackingProgram<FltX>("p", Item("a", 2, 2, allowRotate: false), 4UL, 3UL)
            .Amount.Should().Be(12UL);

    /// <summary>零行时总件数为零 / Zero rows yields a zero amount.</summary>
    [Fact]
    public void Program_ShouldHandleZeroRows() =>
        new PackingProgram<FltX>("p", Item("a", 2, 2, allowRotate: false), 4UL, 0UL)
            .Amount.Should().Be(0UL);

    /// <summary>字符串表示包含件数与行数 / The string form carries per-row amount and rows.</summary>
    [Fact]
    public void Program_ShouldRenderToString() =>
        new PackingProgram<FltX>("p", Item("a", 2, 2, allowRotate: false), 4UL, 3UL)
            .ToString().Should().Be("p: 4×3");

    /// <summary>不可旋转物品只给一个程序 / A non-rotatable item yields one program.</summary>
    [Fact]
    public void Factory_ShouldYieldOneForNonRotatable() {
        IReadOnlyList<PackingProgram<FltX>> programs =
            PackingProgramFactory.PackingProgramsOf(Item("a", 2, 2, allowRotate: false), 4UL, 3UL);

        programs.Should().ContainSingle();
        programs[0].Rotated.Should().BeFalse();
        programs[0].Id.Should().Be("a-upright");
    }

    /// <summary>
    /// **允许旋转时同时给出不旋转与旋转两种**：候选生成需要两者对比，只给一种就丢掉了旋转带来的改进空间。
    /// **A rotatable item yields both upright and rotated**: candidate generation compares them, and yielding only one
    /// discards the improvement rotation offers.
    /// </summary>
    [Fact]
    public void Factory_ShouldYieldBothForRotatable() {
        IReadOnlyList<PackingProgram<FltX>> programs =
            PackingProgramFactory.PackingProgramsOf(Item("a", 2, 4, allowRotate: true), 4UL, 3UL);

        programs.Should().HaveCount(2);
        programs.Select(program => program.Rotated).Should().BeEquivalentTo(new[] { false, true });
        programs.Select(program => program.Id)
            .Should().BeEquivalentTo(new[] { "a-upright", "a-rotated" });
    }

    /// <summary>两种程序的件数相同，只有旋转状态不同 / Both programs share the amount, differing only in rotation.</summary>
    [Fact]
    public void Factory_ShouldShareAmountAcrossRotations() {
        IReadOnlyList<PackingProgram<FltX>> programs =
            PackingProgramFactory.PackingProgramsOf(Item("a", 2, 4, allowRotate: true), 5UL, 2UL);

        programs.Should().OnlyContain(program => program.Amount == 10UL);
    }

    /// <summary>程序携带原物品引用 / Programs carry the originating item.</summary>
    [Fact]
    public void Factory_ShouldCarryItem() {
        ActualItem<FltX> item = Item("a", 2, 2, allowRotate: true);

        // 用编号比较而不是引用比较：`Item` 是接口类型，`==` 在这里只比较引用（CS0252）。
        // Compares ids rather than references: `Item` is an interface type and `==` compares references here (CS0252).
        PackingProgramFactory.PackingProgramsOf(item, 3UL, 1UL)
            .Should().OnlyContain(program => program.Item.Id.Equals(item.Id));
    }

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> Item(string id, double w, double h, bool allowRotate) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), allowRotate, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
