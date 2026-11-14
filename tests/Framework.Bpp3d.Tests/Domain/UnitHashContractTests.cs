// 单位哈希契约的判据 / Criteria for the unit hash contract.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **值相等的量必须哈希相等** / Equal quantities must hash equally.
/// </summary>
/// <remarks>
/// 这一批查出一个跨层的真实缺陷：`Scale` 是 `record` 且持有集合，自动生成的 `GetHashCode` 对集合**按引用**算，
/// 于是同一个比例因子两次取哈希不同，连带 `UnitConversionRule`、`PhysicalUnit`、`Quantity`、以及任何含单位的 record
/// 都违反了 .NET 哈希契约。症状是字典查找抛 `KeyNotFoundException`——**看起来像"键没插进去"，其实是哈希不匹配**。
///
/// 判据必须打在**契约本身**上（相等则同哈希、字典可查），而不是打在某个调用点上：调用点只能暴露症状。
///
/// This batch found a real cross-layer defect: `Scale` is a `record` holding a collection, so the generated `GetHashCode` treats it **by reference**; the same factor then
/// hashes differently on two reads, and that propagates through `UnitConversionRule`, `PhysicalUnit`, `Quantity` and every unit-carrying record, breaking the .NET hash
/// contract. The symptom is a `KeyNotFoundException` — **it looks like "the key was never inserted" when the hash simply did not match**. The criteria must hit the
/// **contract itself** (equal implies same hash, and dictionaries work), not some call site: a call site only shows the symptom.
/// </remarks>
public class UnitHashContractTests {
    /// **同一个单位两次取哈希必须相同** / The same unit must hash the same twice.
    [Fact]
    public void PhysicalUnit_ShouldHashStably() {
        PhysicalUnit unit = SIBaseUnits.Kilogram;

        unit.GetHashCode().Should().Be(unit.GetHashCode(),
            "an unstable hash breaks every dictionary keyed by a unit-carrying value");
        unit.ConversionRule.GetHashCode().Should().Be(unit.ConversionRule.GetHashCode(),
            "the conversion rule carries the scale, which is where the instability came from");
    }

    /// **值相等的量哈希相等，且可作为字典键** / Equal quantities hash equally and work as dictionary keys.
    [Fact]
    public void Quantity_ShouldBeUsableAsADictionaryKey() {
        var left = new Quantity<Flt64>(new Flt64(1.0), SIBaseUnits.Kilogram);
        var right = new Quantity<Flt64>(new Flt64(1.0), SIBaseUnits.Kilogram);

        left.Should().Be(right, "same value, same unit");
        left.GetHashCode().Should().Be(right.GetHashCode(), "equal values must share a hash");

        var map = new Dictionary<Quantity<Flt64>, string> { [left] = "steel" };
        map[right].Should().Be("steel", "a lookup with an equal key must succeed");
    }

    /// **含单位的 record 也能作为字典键** / A unit-carrying record also works as a dictionary key.
    [Fact]
    public void QuantityMaterial_ShouldBeUsableAsADictionaryKey() {
        var weight = new Quantity<Flt64>(new Flt64(1.0), SIBaseUnits.Kilogram);
        var first = new QuantityMaterial<Flt64>("M1", MaterialType.RawMaterial, "steel", weight);
        var second = new QuantityMaterial<Flt64>("M1", MaterialType.RawMaterial, "steel", weight);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());

        var map = new Dictionary<QuantityMaterial<Flt64>, ulong> { [first] = 3UL };
        map[second].Should().Be(3UL, "the package roll-up relies on exactly this lookup");
    }
}
