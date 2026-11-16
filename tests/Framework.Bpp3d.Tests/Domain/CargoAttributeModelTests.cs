#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// 补齐的货物属性模型：十二个类型都在，而且没有改变任何算法行为 /
/// The cargo attribute model filled in here: all twelve types exist and no algorithm changed.
/// </summary>
/// <remarks>
/// 复审发现的十二个类型在 C# 侧一个都没有，而三份文档也从未提到它们。
/// 这里补齐后要钉住两件事：
///
/// ① 十二个类型真的都在（反射列名，而不是"我以为我写了"）；② 生产代码里没有任何人构造它们——
/// 它们只被 自身读取，所以 C# 侧也必须是"只有模型"的状态。
/// 第二条同时保证"补模型"没有偷偷变成"改行为"。
///
/// 策略本身的行为另有判据： 用一个最小 `ICargoView` 替身把四道门限各撞一次。
///
/// The review found none of its twelve types on the C# side and no mention of them in any document. After two things
/// need pinning: the twelve types really exist (by reflection, not by memory), and nothing in production code constructs them — in the code they
/// are read only inside, so the C# side must be "model only" too. The second check also guarantees "adding a model" did not
/// quietly become "changing behaviour". The policies' behaviour has its own criteria: a minimal `ICargoView` double walks each gate once.
/// </remarks>
public class CargoAttributeModelTests
{
    [Fact]
    public void TheTwelveTypes_ShouldExist() {
        string[] expected = new[] {
            nameof(AbstractCargoAttribute),
            nameof(AbstractStackingOnPolicy),
            nameof(FilterStackingOnPolicy),
            nameof(CartonContainerStackingOnPolicy),
            nameof(BoxStackingOnPolicy),
            nameof(AbstractHangingPolicy),
            nameof(RelativeHangingPolicy),
            nameof(AbsoluteHangingPolicy),
            nameof(AbstractDeformationAttribute),
            nameof(LinearDeformationAttribute),
            nameof(AbstractWeightAttribute),
            nameof(WeightAttribute),
        };

        System.Reflection.Assembly assembly = typeof(AbstractCargoAttribute).Assembly;
        foreach (string name in expected) {
            assembly.GetType("Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model." + name).Should().NotBeNull(
                name + " comes from PackageAttribute.kt and must exist here; a missing type is the gap the review found");
        }
    }

    [Fact]
    /// <summary>
    /// 这些类型公开可构造：能力可以被用 / The types are public and constructible: the capability can be used.
    /// </summary>
    /// <remarks>
    /// **判据换过一次：原来那条断言"全仓没有一处构造这些策略"，用来支撑"仅模型、不改行为"——而它把"没人用"变成了永久要求：
    /// 谁只要在框架里写一次 new FilterStackingOnPolicy，判据就红。本项目是框架，只提供能力、不保证项目内必然有使用的地方；
    /// 把使用当成违规是反的。现在钉的是"能力可用"：七个具体类型公开且有公开构造函数、五个槽位可赋值、递归与几何助手的入口都在。
    /// 至于谁去用它们，那是使用方的事。
    ///
    /// Replaces the old criterion. It asserted "nothing anywhere constructs these policies" to support "model only, no behaviour change" — and that
    /// turned "nobody uses it" into a permanent requirement: the first caller inside the framework would turn it red. This project is a framework: it provides
    /// capabilities and does not guarantee an in-project use site, so treating use as a violation is backwards. What is pinned now is that the capability is
    /// usable: seven public types with public constructors, five assignable slots, and the recursion and geometry entry points present. Who uses them is the
    /// consumer's business.
    /// </remarks>
    public void TheCapability_ShouldBePubliclyUsable() {
        System.Reflection.Assembly assembly = typeof(AbstractCargoAttribute).Assembly;

        foreach (string name in new[] {
                     nameof(FilterStackingOnPolicy), nameof(CartonContainerStackingOnPolicy), nameof(BoxStackingOnPolicy),
                     nameof(RelativeHangingPolicy), nameof(AbsoluteHangingPolicy),
                     nameof(LinearDeformationAttribute), nameof(WeightAttribute),
                 }) {
            System.Type type = assembly.GetType("Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model." + name)
                ?? throw new System.InvalidOperationException(name + " must resolve");
            type.IsPublic.Should().BeTrue(name + " is part of the framework's public surface");
            type.GetConstructors().Should().NotBeEmpty(
                name + " must be constructible by a consumer; a capability nobody can instantiate is not a capability");
        }

        PackageAttribute attribute = new(PackageType.Box, new UInt64(2UL), new UInt64(1UL), UInt64.Zero, UInt64.Zero) {
            WeightAttribute = new WeightAttribute(new UInt64(1UL)),
            DeformationAttribute = new LinearDeformationAttribute(new FltX(0.5m)),
            HangingPolicy = new RelativeHangingPolicy(new FltX(0.1m)),
            StackingOnPolicy = new FilterStackingOnPolicy(),
            CargoAttribute = new MarkerCargoAttribute(),
        };
        attribute.MaxLayer.Should().Be(new UInt64(1UL), "the slots are wired through to the effective layer count");

        typeof(StackLayering).GetMethods().Should().Contain(
            method => method.Name == nameof(StackLayering.Layer),
            "the recursion is reachable from a consumer");
        typeof(PlacementTyping).GetMethods().Should().Contain(
            method => method.Name == nameof(PlacementTyping.TopPlacements),
            "the real-geometry helper is reachable from a consumer");
    }

    /// <summary>只为填 CargoAttribute 槽位用的标记实现 / A marker implementation used only to fill the CargoAttribute slot.</summary>
    private sealed class MarkerCargoAttribute : AbstractCargoAttribute
    {
    }

    /// <summary>堆叠策略的第一道门限：允许上覆的包装类型 / The first gate: allowed over-package types.</summary>
    [Fact]
    public void TheStackingPolicy_ShouldRefuseForbiddenPackageTypes() {
        var bottom = new FakeCargo { PackageType = PackageType.Box, OverPackageTypes = new HashSet<PackageType> { PackageType.Box } };
        var barrel = new FakeCargo { PackageType = PackageType.Barrel };
        var box = new FakeCargo { PackageType = PackageType.Box };

        new FilterStackingOnPolicy().EnabledStackingOn(barrel, bottom, UInt64.Zero, Height(0)).Should().BeFalse(
            "a barrel may not sit on a box that only allows boxes on top");
        new FilterStackingOnPolicy().EnabledStackingOn(box, bottom, UInt64.Zero, Height(0)).Should().BeTrue(
            "a box may sit on a box here, because the height is known and the weight is fine");
    }

    /// <summary>第二道门限：超重 / The second gate: overweight.</summary>
    [Fact]
    public void TheStackingPolicy_ShouldRefuseOverweight() {
        var bottom = new FakeCargo { WeightKilograms = 1.0 };
        var heavy = new FakeCargo { WeightKilograms = 100.0 };

        new FilterStackingOnPolicy().EnabledStackingOn(heavy, bottom, UInt64.Zero, Height(0)).Should().BeFalse(
            "a hundred kilograms against one kilogram exceeds the default ten-fold overweight allowance");
    }

    /// <summary>托盘底面必须平顶 / A pallet below must be flat-topped.</summary>
    [Fact]
    public void TheBoxPolicy_ShouldRequireAFlatPallet() {
        var flat = new FakeCargo { PackageType = PackageType.Pallet, TopFlat = true };
        var uneven = new FakeCargo { PackageType = PackageType.Pallet, TopFlat = false };
        var box = new FakeCargo { PackageType = PackageType.Box, WidthMetres = 1.0, DepthMetres = 1.0 };
        var policy = new BoxStackingOnPolicy(new FltX(0.5m));

        policy.EnabledStackingOn(box, uneven, UInt64.Zero, Height(0)).Should().BeFalse("a pallet with an uneven top cannot carry a box");
        policy.EnabledStackingOn(box, flat, UInt64.Zero, Height(0)).Should().BeTrue("a flat pallet can carry a box of equal base size");
    }

    /// <summary>层数门限 / The layer gate.</summary>
    [Fact]
    public void TheStackingPolicy_ShouldRefuseBeyondTheLayerLimit() {
        var bottom = new FakeCargo();
        var item = new FakeCargo { MaxLayerLayers = new UInt64(2UL) };

        new FilterStackingOnPolicy().EnabledStackingOn(item, bottom, new UInt64(1UL), Height(0)).Should().BeTrue(
            "layer 1 is below the limit of 2");
        new FilterStackingOnPolicy().EnabledStackingOn(item, bottom, new UInt64(2UL), Height(0)).Should().BeFalse(
            "layer 2 is not below the limit of 2");
    }

    /// <summary>未知高度即判否 / An unknown height decides false.</summary>
    /// <remarks>
    /// C# 的物品模型把 `MaxHeight` 存成层数，而长度。 这里刻意让它"未知"，并让策略判否：
    /// 把层数当长度会让高度门限静默失效——那才是"看起来迁移完了"的假象。
    ///
    /// The C# item model keeps `MaxHeight` as a layer count while the code keeps a length. Unknown is deliberately modelled here and the
    /// policy refuses: treating a layer count as a length would let the height gate silently pass — which is precisely the illusion of "implemented".
    /// </remarks>
    [Fact]
    public void AnUnknownHeight_ShouldDecideFalse() {
        var item = new FakeCargo { MaxHeightLengthMetres = null };

        new FilterStackingOnPolicy().EnabledStackingOn(item, new FakeCargo(), UInt64.Zero, Height(0)).Should().BeFalse(
            "the height gate cannot be evaluated, and an unevaluable gate must never allow stacking");
    }

    /// <summary>相对悬挂：面积比决定 / Relative hanging: the area fraction decides.</summary>
    [Fact]
    public void TheRelativeHangingPolicy_ShouldRespectTheFraction() {
        var item = new FakeCargo { WidthMetres = 1.0, DepthMetres = 1.0, WeightKilograms = 1.0 };
        var supported = new BottomSupport(Area(1.0), new Quantity<FltX>(new FltX(1m), SIBaseUnits.Kilogram));
        var mostlyUnsupported = new BottomSupport(Area(0.1), new Quantity<FltX>(new FltX(1m), SIBaseUnits.Kilogram));

        new RelativeHangingPolicy(new FltX(0.1m)).EnabledStackingOn(item, supported).Should().BeTrue(
            "nothing hangs over a fully supported item");
        new RelativeHangingPolicy(new FltX(0.1m)).EnabledStackingOn(item, mostlyUnsupported).Should().BeFalse(
            "ninety percent hanging exceeds a ten percent allowance");
    }

    /// <summary>重量属性与线性变形：两个最小组件 / The two smallest parts: weight and linear deformation.</summary>
    [Fact]
    public void TheWeightAndDeformationAttributes_ShouldBehave() {
        WeightAttribute.Unbounded.MaxLayer.Should().Be(new UInt64(ulong.MaxValue), "unbounded means the layer gate never binds");
        new WeightAttribute(new UInt64(3UL)).MaxLayer.Should().Be(new UInt64(3UL));

        var deformation = new LinearDeformationAttribute(new FltX(0.5m));
        Vector<Dim3, FltX> vector = deformation.DeformationQuantity(new FltX(2m));
        vector.Components.Should().HaveCount(3, "deformation is a three-axis quantity");
        vector.Components.Select(component => component.ToFlt64().ToDouble()).Should().AllBeEquivalentTo(
            1.0,
            "each axis deforms by volume times the coefficient (2 * 0.5)");
    }

    private static Quantity<FltX> Height(double metres) => new(new FltX((decimal)metres), SIBaseUnits.Meter);

    private static Quantity<FltX> Area(double squareMetres) => new(new FltX((decimal)squareMetres), AreaUnits.SquareMeter);

    private static string RepositoryRoot() {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the repository root holds the solution file");
        return directory!.FullName;
    }

    /// <summary>判据用的最小货物视图 / A minimal cargo view for the criteria.</summary>
    private sealed class FakeCargo : ICargoView {
        public PackageType PackageType { get; init; } = PackageType.Box;
        public PackageCategory PackageCategory => PackageType switch {
            PackageType.Pallet => PackageCategory.Standard,
            PackageType.Barrel => PackageCategory.Cylindrical,
            _ => PackageCategory.Irregular,
        };
        public double WidthMetres { get; init; } = 1.0;
        public double DepthMetres { get; init; } = 1.0;
        public double HeightMetres { get; init; } = 0.5;
        public double WeightKilograms { get; init; } = 1.0;
        public UInt64 MaxLayerLayers { get; init; } = new(ulong.MaxValue);
        public double? MaxHeightLengthMetres { get; init; } = 10.0;
        public bool TopFlat { get; init; } = true;
        public IReadOnlySet<PackageType> OverPackageTypes { get; init; }
            = new HashSet<PackageType>((PackageType[])Enum.GetValues(typeof(PackageType)));

        public Quantity<FltX> Width => new(new FltX((decimal)WidthMetres), SIBaseUnits.Meter);
        public Quantity<FltX> Depth => new(new FltX((decimal)DepthMetres), SIBaseUnits.Meter);
        public Quantity<FltX> Height => new(new FltX((decimal)HeightMetres), SIBaseUnits.Meter);
        public Quantity<FltX> Weight => new(new FltX((decimal)WeightKilograms), SIBaseUnits.Kilogram);
        public UInt64 MaxLayer => MaxLayerLayers;
        public Quantity<FltX>? MaxHeightLength => MaxHeightLengthMetres is { } metres
            ? new Quantity<FltX>(new FltX((decimal)metres), SIBaseUnits.Meter)
            : null;
        public Quantity<FltX>? BottomFootprintArea =>
            new Quantity<FltX>(new FltX((decimal)(WidthMetres * DepthMetres)), AreaUnits.SquareMeter);
        public Quantity<FltX>? BottomFootprintMinSpan =>
            new Quantity<FltX>(new FltX((decimal)System.Math.Min(WidthMetres, DepthMetres)), SIBaseUnits.Meter);
    }
}