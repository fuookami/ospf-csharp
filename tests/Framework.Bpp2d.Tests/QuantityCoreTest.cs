// 物理量核心算子测试 / Tests for quantity core operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 物理量核心算子 / Quantity core operators.
/// </summary>
public sealed class QuantityCoreTest {
    /// <summary>同量纲可以比较 / Same-dimension quantities compare.</summary>
    [Fact]
    public void OrdOrNull_ShouldCompareSameDimension() {
        QuantityCore.OrdOrNull(Meters(1), Meters(2)).Should().BeOfType<Order.Less>();
        QuantityCore.OrdOrNull(Meters(2), Meters(2)).Should().BeOfType<Order.Equal>();
        QuantityCore.OrdOrNull(Meters(3), Meters(2)).Should().BeOfType<Order.Greater>();
    }

    /// <summary>不同量纲不可比较，返回 null 而不是猜 / Different dimensions yield null rather than a guess.</summary>
    [Fact]
    public void OrdOrNull_ShouldReturnNullOnDimensionMismatch() =>
        QuantityCore.OrdOrNull(Meters(1), Kilograms(1)).Should().BeNull();

    /// <summary>
    /// **`IsPositive` 与 `IsNonPositive` 必须恰好互补**：若对某个输入两者都为假，调用方按二分支写就会漏掉一个分支。
    /// **`IsPositive` and `IsNonPositive` must complement exactly**: if both were false for some input, a caller
    /// branching on them would silently drop a case.
    /// </summary>
    [Fact]
    public void Positivity_ShouldBeComplementary() {
        foreach (Quantity<FltX> value in new[] { Meters(-1), Meters(0), Meters(1) }) {
            bool positive = QuantityCore.IsPositive(value);
            bool nonPositive = QuantityCore.IsNonPositive(value);
            (positive ^ nonPositive).Should().BeTrue("二选一必须恰好成立 / exactly one must hold");
        }
        QuantityCore.IsPositive(Meters(1)).Should().BeTrue();
        QuantityCore.IsPositive(Meters(0)).Should().BeFalse();
        QuantityCore.IsPositive(Meters(-1)).Should().BeFalse();
    }

    /// <summary>
    /// 与自身零值比较恒为同量纲，因此 1 千克是正的——"不可比较"不会在这里出现。
    /// Comparing against the value's own zero is always same-dimension, so 1 kg is positive; incomparability
    /// cannot arise here.
    /// </summary>
    [Fact]
    public void IsPositive_ShouldWorkForAnyUnit() {
        QuantityCore.IsPositive(Kilograms(1)).Should().BeTrue();
        QuantityCore.IsPositive(Kilograms(-1)).Should().BeFalse();
    }

    /// <summary>同量纲不同单位可以比较并换算 / Same dimension, different units compare through conversion.</summary>
    [Fact]
    public void OrdOrNull_ShouldCompareAcrossUnits() =>
        QuantityCore.OrdOrNull(Meters(1), Centimeters(150)).Should().BeOfType<Order.Less>();

    /// <summary>量纲判定只比较量纲，不比较单位 / Dimension checking ignores the specific unit.</summary>
    [Fact]
    public void IsSameDimension_ShouldIgnoreUnitChoice() {
        QuantityCore.IsSameDimension(Meters(1), Centimeters(1)).Should().BeTrue();
        QuantityCore.IsSameDimension(Meters(1), Kilograms(1)).Should().BeFalse();
    }

    /// <summary>求和为空序列时返回 null / Summing an empty sequence yields null.</summary>
    [Fact]
    public void Sum_ShouldReturnNullForEmpty() =>
        QuantityCore.Sum(Array.Empty<Quantity<FltX>>()).Should().BeNull();

    /// <summary>求和逐项累加 / Sum accumulates every element.</summary>
    [Fact]
    public void Sum_ShouldAccumulate() =>
        D(QuantityCore.Sum(new[] { Meters(1), Meters(2), Meters(3) })!)
            .Should().BeApproximately(6.0, 1e-9);

    /// <summary>带选择器的求和与直接求和一致 / Summing with a selector matches summing directly.</summary>
    [Fact]
    public void SumOfQuantity_ShouldUseSelector() =>
        D(new[] { 1.0, 2.0, 3.0 }.SumOfQuantity(Meters)!)
            .Should().BeApproximately(6.0, 1e-9);

    /// <summary>取最大按量值而非元素自身排序 / Max picks by quantity, not by the element itself.</summary>
    [Fact]
    public void MaxByQuantity_ShouldPickLargest() =>
        new[] { "c", "a", "b" }
            .MaxByQuantityOrNull(text => Meters(text.Length))
            .Should().Be("c");

    /// <summary>空序列取最大返回默认值 / Max over an empty sequence yields default.</summary>
    [Fact]
    public void MaxByQuantity_ShouldReturnDefaultWhenEmpty() =>
        Array.Empty<string>().MaxByQuantityOrNull(text => Meters(text.Length))
            .Should().BeNull();

    /// <summary>取最小按量值 / Min picks by quantity.</summary>
    [Fact]
    public void MinByQuantity_ShouldPickSmallest() =>
        new[] { "ccc", "a", "bb" }
            .MinByQuantityOrNull(text => Meters(text.Length))
            .Should().Be("a");

    /// <summary>升序与降序排序互为反序 / Ascending and descending sorts are reverses of each other.</summary>
    [Fact]
    public void SortedByQuantity_ShouldOrderByValue() {
        string[] source = { "ccc", "a", "bb" };

        source.SortedByQuantity(text => Meters(text.Length))
            .Should().Equal("a", "bb", "ccc");
        source.SortedByDescendingQuantity(text => Meters(text.Length))
            .Should().Equal("ccc", "bb", "a");
    }

    /// <summary>
    /// 混入不可比元素时**不抛异常，且可比较的元素之间顺序正确**。
    /// With an incomparable element present the sort **does not throw, and comparable pairs stay ordered**.
    /// </summary>
    /// <remarks>
    /// 这里刻意不断言"不可比元素位置不变"：比较排序在存在不可比元素时，只要不与已知大小关系冲突，
    /// 挪动该元素是合法的。断言更强的性质会得到一个脆弱的测试，改天换个排序实现就会无故失败。
    ///
    /// Deliberately does not assert that the incomparable element keeps its slot: with incomparable elements a
    /// comparison sort may legitimately move it as long as no known ordering is contradicted. Asserting the
    /// stronger property would produce a brittle test that fails whenever the sort implementation changes.
    /// </remarks>
    [Fact]
    public void SortedByQuantity_ShouldTolerateIncomparable() {
        var items = new List<(string Name, Quantity<FltX> Size)> {
            ("length", Meters(5)),
            ("mass", Kilograms(1)),
            ("small", Meters(1)),
        };

        var sorted = items.SortedByQuantity(item => item.Size)
            .Select(item => item.Name).ToList();

        sorted.Should().HaveCount(3);
        sorted.Should().Contain(new[] { "length", "mass", "small" });
        sorted.IndexOf("small").Should().BeLessThan(
            sorted.IndexOf("length"), "1 米小于 5 米 / 1 m is less than 5 m");
    }

    /// <summary>可容纳数量按整除计 / The fitting count follows integer division.</summary>
    [Fact]
    public void CountFitting_ShouldCountCells() {
        QuantityCore.CountFitting(Meters(10), Meters(3)).Should().Be(3);
        QuantityCore.CountFitting(Meters(9), Meters(3)).Should().Be(3, "恰好整除 / exact division");
    }

    /// <summary>
    /// **非正可用量／单元、或量纲不同时返回 0**：这些情况下"能放几个"没有意义，返回 0 比编一个数字安全。
    /// **A non-positive available/cell size or a dimension mismatch yields 0**: "how many fit" is meaningless there,
    /// and 0 is safer than an invented number.
    /// </summary>
    [Fact]
    public void CountFitting_ShouldReturnZeroWhenMeaningless() {
        QuantityCore.CountFitting(Meters(0), Meters(1)).Should().Be(0);
        QuantityCore.CountFitting(Meters(10), Meters(0)).Should().Be(0);
        QuantityCore.CountFitting(Meters(10), Kilograms(1)).Should().Be(0);
    }

    /// <summary>单元尺寸的整数倍换算 / Integer multiples of a cell size.</summary>
    [Fact]
    public void ScaledQuantity_ShouldMultiply() =>
        D(QuantityCore.ScaledQuantity(Meters(3), 4)).Should().BeApproximately(12.0, 1e-9);

    /// <summary>不可比较时取左值 / Prefer the left value when incomparable.</summary>
    [Fact]
    public void MaxOf_ShouldPreferLeftWhenIncomparable() =>
        QuantityCore.MaxOf(Meters(1), Kilograms(5)).Should().Be(Meters(1));

    /// <summary>可取较大者 / Takes the larger value.</summary>
    [Fact]
    public void MaxOf_ShouldPickLarger() =>
        D(QuantityCore.MaxOf(Meters(1), Meters(2))).Should().BeApproximately(2.0, 1e-9);

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造以厘米为单位的量 / Build a quantity in centimetres.</summary>
    private static Quantity<FltX> Centimeters(double value) =>
        new(new FltX(value), LengthUnits.Centimeter);

    /// <summary>构造以千克为单位的量 / Build a quantity in kilograms.</summary>
    private static Quantity<FltX> Kilograms(double value) =>
        new(new FltX(value), SIBaseUnits.Kilogram);
}
