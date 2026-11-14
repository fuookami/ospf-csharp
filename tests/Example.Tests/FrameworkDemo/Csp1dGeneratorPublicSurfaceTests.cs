#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 四个生成器必须能从**外部程序集**直接使用，而不只是测试程序集里可见。
/// The four generators must be usable from an **external assembly**, not only from the test assembly.
/// </summary>
/// <remarks>
/// B313 之前四个生成器全部是 `internal sealed class`，而 `Framework.Csp1d.csproj` 只给
/// **`Fuookami.Ospf.Framework.Csp1d.Tests`** 开了 `InternalsVisibleTo`。
///
/// 结果是：接口 `ICsp1dInitialCuttingPlanGenerator` 是 `public`，实现有四个，
/// **但任何外部消费者都无法实例化其中任何一个**。生产侧 `_initialGenerator` 因此始终是
/// `SimpleInitialGenerator`，四个生成器**从未在任何真实运行中被调用过**。
///
/// **这是本项目反复出现的形态的最后一环**：接口、实现、测试都在，唯独缺"让外面能拿到它"的那一步。
///
/// 本文件所在的 `Example.Tests` **没有** `InternalsVisibleTo`，所以这里的编译通过本身就是证据：
/// 若生成器仍为 `internal`，这些测试**连编译都过不去**。
///
/// Before B313 all four generators were `internal sealed class`, and `Framework.Csp1d.csproj` granted `InternalsVisibleTo` only to
/// **`Fuookami.Ospf.Framework.Csp1d.Tests`**.
///
/// The result: the `ICsp1dInitialCuttingPlanGenerator` interface was `public` and there were four implementations, **yet no external consumer could
/// instantiate any of them**. Production `_initialGenerator` therefore stayed `SimpleInitialGenerator`, and the four generators **were never invoked in any real
/// run**.
///
/// **This is the final link of the recurring shape in this project**: interface, implementations and tests all present, and the one step missing is "letting the
/// outside reach it".
///
/// The `Example.Tests` assembly **has no** `InternalsVisibleTo`, so compiling at all is the evidence: if the generators were still `internal`, these tests **would
/// not even compile**.
/// </remarks>
public class Csp1dGeneratorPublicSurfaceTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );

    private static GenerationInput<Flt64> MakeInput() {
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) }
        );
        return new GenerationInput<Flt64> {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
        };
    }

    /// <summary>**`NSameGenerator` 必须可从外部实例化并产出方案** / `NSameGenerator` must be constructible outside and produce plans.</summary>
    [Fact]
    public void NSameGenerator_ShouldBeUsableFromAnExternalAssembly() {
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().NotBeEmpty();
    }

    /// <summary>**`NSumGenerator` 必须可从外部实例化并产出方案** / `NSumGenerator` must be constructible outside and produce plans.</summary>
    [Fact]
    public void NSumGenerator_ShouldBeUsableFromAnExternalAssembly() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().NotBeEmpty();
    }

    /// <summary>**`FullSumGenerator` 必须可从外部实例化并产出方案** / `FullSumGenerator` must be constructible outside and produce plans.</summary>
    [Fact]
    public void FullSumGenerator_ShouldBeUsableFromAnExternalAssembly() {
        var generator = new FullSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().NotBeEmpty();
    }

    /// <summary>**`DFSGenerator` 必须可从外部实例化并产出方案** / `DFSGenerator` must be constructible outside and produce plans.</summary>
    [Fact]
    public void DFSGenerator_ShouldBeUsableFromAnExternalAssembly() {
        var generator = new DFSGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().NotBeEmpty();
    }

    /// <summary>**四个生成器都必须满足公开接口** / All four must satisfy the public interface.</summary>
    /// <remarks>
    /// 这条把"可从外部使用"写成**类型层面的契约**：四个实例都必须能赋给公开接口变量。
    /// 若将来有人把某个生成器改回 `internal`，这条会**编译失败**，而不是等到运行时才发现。
    ///
    /// This turns "usable from outside" into a **type-level contract**: all four instances must assign to the public interface. If anyone reverts a generator to
    /// `internal`, this **fails to compile** instead of surfacing at runtime.
    /// </remarks>
    [Fact]
    public void AllFourGenerators_ShouldSatisfyThePublicInterface() {
        var constraints = GenerationConstraints<Flt64>.Unconstrained();

        var generators = new List<ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>>> {
            new NSameGenerator<Flt64>(constraints),
            new NSumGenerator<Flt64>(constraints),
            new FullSumGenerator<Flt64>(constraints),
            new DFSGenerator<Flt64>(constraints),
        };

        generators.Should().HaveCount(4);
        generators.Should().OnlyContain(generator => generator.Generate(MakeInput()).Count > 0);
    }
}
