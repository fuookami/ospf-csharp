// 模型属性挂在哪个类型上 / Which type each model property lives on.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **模型属性在 C# 里挂在哪个类型上** / Which type each model property lives on here.
/// </summary>
/// <remarks>
/// **这里的结论是一个测量方法失败，不是一次实现。** 早先试过的办法是
///// 把源侧的 `val` 与 C# 同**文件**的属性做差集。
/// 连修五轮（`Is` 前缀、record 位置参数、末位参数、枚举成员、嵌套类）**仍然大面积误报**——
/// 因为这个代码库把模型的属性拆进了嵌套类型：`PositionShape.Length`、`ItemOrder.CarBoard`、`ItemLocation.IsNormalMain`。
///
/// **按文件比对配不上这个代码库。** 所以脚本删掉了，改成这条判据：把"哪个属性该在哪个类型上"直接钉住。
/// 它挡的不是"字段缺失"，而是**"属性被搬到别处或删掉"**。
///
/// **这三条"属性在哪个类型上"的判据，价值有限，必须说清楚。** 我试过变异验证：删掉 `PositionShape.DeclaredArea`，
/// **编译就失败了**——所以这三条**抓不到删除，编译器先抓到了**。它们能抓的只有"属性被挪到另一个公开类型、
/// 而调用方也跟着改了"那种情形，**而那恰恰是编译器抓不到、也不该抓的重构**。
///
/// **保留它们的理由只有一个**：把"属性挂在哪"写成文档，让下一个人不必再跑一遍源码比对。
/// **不保留一个"看起来在守护、其实编译器先响"的假象。**
///
/// **These three "which type does it live on" criteria are of limited value, and that has to be said.** Mutation testing: deleting
/// `PositionShape.DeclaredArea` **fails to compile**, so these criteria **cannot catch a deletion — the compiler does, first**. What they can catch is "the property moved
/// to another public type and the callers moved with it", **which is a refactor the compiler neither catches nor should**. They are kept for one reason only: to write
/// "where does it live" down as documentation, so nobody runs the source diff again. **No pretending to guard what the compiler already guards.**
///
/// **The deliverable here is a failed measuring method, not a port.** tried to mirror / by diffing its
///// `val`s against the C# properties in the **same file**. After five rounds of fixes (`Is` prefix, record positional parameters,
/// last parameter, enum members, nested classes) it **still over-reported**, because this codebase splits model properties across nested types. **Per-file diffing does not
/// fit this codebase.** The script is gone; this criterion pins **which type each property belongs to**, guarding against a property being moved or deleted.
/// </remarks>
public class Demo2ModelPropertyLayoutTests
{
    /// <summary>**舱位几何属性挂在 `PositionShape` 上** / The position geometry lives on `PositionShape`.</summary>
    /// <remarks>
    /// 它们直接放在 `Position` 上。搬到 `PositionShape` 是一次**有意的重构**（形状与坐标分开），
    /// **不是缺失**——但"搬走了"和"删掉了"从外面看一样，所以这里钉住。
    ///
    /// The code puts them straight on `Position`. Moving them to `PositionShape` is a **deliberate refactor** (shape apart from coordinates), **not a gap** — but
    /// "moved" and "deleted" look the same from outside, which is what this pins.
    /// </remarks>
    [Fact]
    public void ThePositionGeometry_ShouldLiveOnPositionShape()
    {
        typeof(PositionShape).GetProperty(nameof(PositionShape.Length)).Should().NotBeNull(
            "`length` is a real baseline property; its absence here would silently change every area computation");
        typeof(PositionShape).GetProperty(nameof(PositionShape.Width)).Should().NotBeNull();
        typeof(PositionShape).GetProperty(nameof(PositionShape.Volume)).Should().NotBeNull();
        typeof(PositionShape).GetProperty(nameof(PositionShape.DeclaredArea)).Should().NotBeNull(
            "a declared area overrides length x width, so dropping it changes the answer");
    }

    /// <summary>**时刻类属性挂在 `ItemOrder` 上** / The instant-like properties live on `ItemOrder`.</summary>
    [Fact]
    public void TheItemOrderInstants_ShouldLiveOnItemOrder()
    {
        typeof(ItemOrder).GetProperty(nameof(ItemOrder.Hardstand)).Should().NotBeNull();
        typeof(ItemOrder).GetProperty(nameof(ItemOrder.CarBoard)).Should().NotBeNull(
            "the baseline calls it `car`; renaming is fine, deleting is not");
        typeof(ItemOrder).GetProperty(nameof(ItemOrder.Reweighed)).Should().NotBeNull();
    }

    /// <summary>**舱位标签的派生判定挂在 `ItemLocation` 上** / The derived location predicates live on `ItemLocation`.</summary>
    /// <remarks>
    /// `normalMain = main && !head && !tail` 这种**派生属性**在这里用 `Is` 前缀。
    /// **派生属性和它的原始标签必须同时存在**：只留标签，判定就得在调用方重复一遍。
    ///
    /// Derived properties like `normalMain = main && !head && !tail` use an `Is` prefix here. **The derived property and the raw tags
    /// have to coexist** — keeping only the tags would push the predicate into every caller.
    /// </remarks>
    [Fact]
    public void TheDerivedLocationPredicates_ShouldLiveOnItemLocation()
    {
        typeof(ItemLocation).GetProperty(nameof(ItemLocation.IsNormalMain)).Should().NotBeNull();
        typeof(ItemLocation).GetProperty(nameof(ItemLocation.IsLow)).Should().NotBeNull();
        typeof(ItemLocation).GetProperty(nameof(ItemLocation.IsBulk)).Should().NotBeNull();
        typeof(ItemLocation).GetProperty(nameof(ItemLocation.IsHead)).Should().NotBeNull();
        typeof(ItemLocation).GetProperty(nameof(ItemLocation.IsTail)).Should().NotBeNull();
    }

    /// <summary>**一处真实差异：单位标签没有搬过来** / One real difference: the unit labels did not come across.</summary>
    /// <remarks>
    /// 参考侧 `AircraftModel` 上有 `volumeUnit`/`fuelVolumeUnit`/`fuelDensityUnit`/`linearDensityUnit`，而本仓**一个都没有**：
    /// `LinearDensityLimit` 用的是裸 `double MaxLinearDensity = 500.0`。
    ///
    /// **今天这不改变任何数值**——两边都从头到尾不做单位换算，同一个隐式单位。**但"隐式"就是隐患**：
    /// 哪天有人把一侧换成带单位的量，另一侧不会报错，只会算出一个错的值。
    ///
    /// 这条判据**钉住当前的裸 double**，而不是假装标签在。**写明"这里没有单位"比给一个假单位好。**
    ///
    /// The code's `AircraftModel` carries `volumeUnit`/`fuelVolumeUnit`/`fuelDensityUnit`/`linearDensityUnit`; this implementation has **none of them** — `LinearDensityLimit`
    /// uses a bare `double MaxLinearDensity = 500.0`. **This changes no number today**, since neither side converts units at all and both share one implicit unit. **But
    /// "implicit" is the hazard**: the day someone makes one side unit-carrying, the other raises nothing and simply computes a wrong value. This criterion **pins
    /// the bare double** rather than pretending the labels exist. **Saying "there is no unit here" beats inventing one.**
    /// </remarks>
    [Fact]
    public void TheLinearDensity_ShouldStillBeABareDouble()
    {
        typeof(AircraftModel).GetProperty("LinearDensityUnit").Should().BeNull(
            "if a unit label appears, the 500.0 in LinearDensityLimit must be expressed in it — otherwise the two silently disagree");
        LinearDensityLimitZone zone = new("Z1", new HashSet<DeckLocation>(), 0.0, 1.0, 500.0);
        zone.MaxLinearDensity.Should().Be(500.0, "and until then the number stands as declared");
    }
}
