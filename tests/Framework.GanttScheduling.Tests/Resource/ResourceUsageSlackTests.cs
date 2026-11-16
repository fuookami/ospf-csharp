// 松弛的开关真的分两层管/ The slack switches really act at two levels.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **松弛的开关真的分两层管** / The slack switches really act at two levels.
/// </summary>
/// <remarks>
/// `ResourceUsageSlackHelper.BuildSlacks` 有两层开关：**整体开关**（`overEnabled` / `lessEnabled`）与**逐时隙开关**
/// （`slotOverEnabled` / `slotLessEnabled`）。两层都容易写反，而且写反之后模型照样能解——**只是松弛到不了它该到的地方**。
///
/// 这里钉四件事：整体关掉就不建表 / 整体开着但某时隙关掉就建**空符号** / 两条路径的符号都进模型 / 名字带方向前缀。
///
/// `BuildSlacks` has two levels of switches: the **global** (`overEnabled` / `lessEnabled`) and the **per-slot** (`slotOverEnabled` / `slotLessEnabled`). Both are easy to invert, and an
/// inverted switch still solves — **the slack simply never reaches where it belongs**. Four things are pinned: a globally disabled direction builds no table; a globally enabled
/// direction with a disabled slot builds an **empty symbol**; symbols from both paths reach the model; and the names carry the direction prefix.
/// </remarks>
public class ResourceUsageSlackTests {
    private sealed record Slot(int Index);

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (List<Slot> Slots, Dictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> Quantity, LinearMetaModel<Flt64> Model) Ready() {
        var slots = new List<Slot> { new(0), new(1) };
        var model = new LinearMetaModel<Flt64>();
        var quantity = new Dictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>();
        foreach (Slot slot in slots) {
            var symbol = new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(constant: new Flt64(2.0)),
                name: "quantity_" + slot.Index);
            model.Add(symbol).IsOk.Should().BeTrue();
            quantity[slot] = symbol;
        }
        return (slots, quantity, model);
    }

    /// <summary>
    /// **整体关掉时一张表都不建** / A globally disabled direction builds no table at all.
    /// </summary>
    /// <remarks>
    /// 两点成对：`overEnabled: false` → `over` 为 `null` / `lessEnabled: false` → `less` 为 `null`。
    /// **建一张空表就是错的**：调用方拿到非空字典会以为松弛已经进模型。
    ///
    /// Two paired points: `overEnabled: false` leaves `over` null; `lessEnabled: false` leaves `less` null. **Building an empty table is wrong**: a caller holding a non-null dictionary
    /// believes the slack reached the model.
    /// </remarks>
    [Fact]
    public void BuildSlacks_ShouldNotBuildWhenGloballyDisabled() {
        (List<Slot> slots, Dictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> quantity, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        _ = unused;

        ResourceUsageSlackHelper.BuildSlacks(
            model, "usage", slots, quantity,
            out IReadOnlyDictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>? over,
            out IReadOnlyDictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>? less,
            overEnabled: false, lessEnabled: false,
            slotOverEnabled: _ => true, slotLessEnabled: _ => true,
            upper: _ => new Flt64(5.0), lower: _ => Flt64.Zero,
            id: slot => slot.Index.ToString()).IsOk.Should().BeTrue();

        over.Should().BeNull("a globally disabled over-quantity must not build a table");
        less.Should().BeNull("a globally disabled less-quantity must not build a table");
    }

    /// <summary>
    /// **逐时隙关掉就建空符号，而不是不建** / A disabled slot gets an empty symbol rather than no entry.
    /// </summary>
    /// <remarks>
    /// 三点成对：整体开着时表非空 / **每个时隙都有条目**（关掉的那个也在）/ 关掉的时隙拿到的是空符号而非带约束的符号。
    /// 第二条是重点：少一个条目会让下游按"这个时隙没有松弛"处理，而语义是"这个时隙的松弛恒为零"。
    ///
    /// Three paired points: with the global switch on the table is non-empty; **every slot has an entry** (including the disabled one); and the disabled slot holds an empty symbol
    /// instead of a constrained one. The second matters most: a missing entry makes downstream treat it as "this slot has no slack", while the meaning is "this slot's slack is
    /// identically zero".
    /// </remarks>
    [Fact]
    public void BuildSlacks_ShouldGiveDisabledSlotsAnEmptySymbol() {
        (List<Slot> slots, Dictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> quantity, LinearMetaModel<Flt64> model) = Ready();
        using LinearMetaModel<Flt64> unused = model;
        _ = unused;

        ResourceUsageSlackHelper.BuildSlacks(
            model, "usage", slots, quantity,
            out IReadOnlyDictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>? over,
            out IReadOnlyDictionary<Slot, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>? less,
            overEnabled: true, lessEnabled: true,
            slotOverEnabled: slot => slot.Index == 0, slotLessEnabled: _ => true,
            upper: _ => new Flt64(5.0), lower: _ => Flt64.Zero,
            id: slot => slot.Index.ToString()).IsOk.Should().BeTrue();

        over.Should().NotBeNull();
        over!.Should().HaveCount(slots.Count, "every slot must have an entry");
        over![slots[0]].Name.Should().Be("usage_over_quantity_0");
        over![slots[1]].Name.Should().Be("usage_over_quantity_1");
        over![slots[1]].Should().NotBeSameAs(over![slots[0]], "a disabled slot gets its own empty symbol");
        // **空符号与带约束的符号只能从多项式上分开**：两者的类型与名字都一样，唯一区别是有没有引用数量。
        // **Empty and constrained symbols differ only in their polynomial**: same type, same name, and the only difference is whether the quantity is referenced.
        // 两者连类型都不同：开着的槽是**函数适配符号**（引用数量），关着的槽是**空表达式符号**。
        // They even differ in type: the enabled slot yields a function adapter symbol (referencing the quantity), the disabled one an empty expression symbol.
        over![slots[0]].Should().NotBeAssignableTo<Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol>(
            "the enabled slot's slack must reference the quantity, not be a bare expression");
        var disabledOver = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)over![slots[1]];
        disabledOver.Polynomial!.Monomials.Should().BeEmpty("the disabled slot's slack is identically zero");

        less.Should().NotBeNull();
        less!.Should().HaveCount(slots.Count);
        less![slots[0]].Name.Should().Be("usage_less_quantity_0");
    }
}
