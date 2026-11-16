// 二维套裁标识判据 / Criteria for 2D cutting stock identifiers.

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Material;

/// <summary>
/// **编号契约** / The identifier contracts.
/// </summary>
/// <remarks>
/// 对应。纯值对象，**判据不需要求解器也不需要模型**。
///
///. Pure value objects: **no criterion needs a solver or a model**.
/// </remarks>
public class Csp2dIdsTests {
    /// <summary>**四个生产对象编号共享同一实现，语义只在接口上区分** / The four production-object identifiers share one implementation; the distinction lives in the interfaces.</summary>
    /// <remarks>
    /// 同一个字符串可以同时是产品、物料、设备、副产物编号。**这不是偷懒，是基线口径**：它们在领域里是同一类"生产对象"，
    /// 拆成四个类只会让每次调用多写一次转换。
    ///
    /// One string can be a product, a material, a machine and a by-product identifier at once. **This is its convention, not laziness**: they are one class of
    /// "production object" in the domain, and four classes would only force a conversion at every call site.
    /// </remarks>
    [Fact]
    public void Factories_ShouldShareOneImplementationAcrossProductionObjects() {
        string raw = "steel-plate";

        IProductId2 product = Csp2dIds.ProductId2Of(raw);
        IMaterialId2 material = Csp2dIds.MaterialId2Of(raw);
        IMachineId2 machine = Csp2dIds.MachineId2Of(raw);
        ICostarId2 costar = Csp2dIds.CostarId2Of(raw);

        product.Value.Should().Be(raw);
        material.Value.Should().Be(raw);
        machine.Value.Should().Be(raw);
        costar.Value.Should().Be(raw);
        product.Should().BeOfType<Csp2dId>();
        material.Should().BeOfType<Csp2dId>();
    }

    /// <summary>**不同字符串必须是不同编号** / Different strings must be different identifiers.</summary>
    [Fact]
    public void Ids_ShouldDistinguishByValue() {
        IProductId2 first = Csp2dIds.ProductId2Of("A");
        IProductId2 second = Csp2dIds.ProductId2Of("B");
        IProductId2 sameAsFirst = Csp2dIdOf("A");

        first.Should().NotBe(second);
        first.Should().Be(sameAsFirst, "record value equality keeps the identifier comparable by its raw value");
    }

    /// <summary>**图案与方案编号是另一组接口** / Pattern and plan identifiers are a separate pair.</summary>
    /// <remarks>
    /// 它们**不实现** `IProductionId2`——图案与方案不是"被裁出的东西"。混进生产对象编号会让
    /// "生产对象集合"里出现不是件的东西。
    ///
    /// They **do not** implement `IProductionId2`: a pattern and a plan are not things being cut out. Mixing them into the production-object set would put non-pieces into
    /// "the set of things this process produces".
    /// </remarks>
    [Fact]
    public void PatternAndPlanIds_ShouldStayOutsideTheProductionObjectContract() {
        IPatternId2 pattern = Csp2dIds.PatternId2Of("P1");
        IPlanId2 plan = Csp2dIds.PlanId2Of("S1");

        pattern.Value.Should().Be("P1");
        plan.Value.Should().Be("S1");
        pattern.Should().BeOfType<PatternId2Impl>();
        plan.Should().BeOfType<PlanId2Impl>();
        (pattern is IProductionId2).Should().BeFalse("a cutting pattern is not a production object");
        (plan is IProductionId2).Should().BeFalse("a cutting plan is not a production object");
    }

    /// <summary>直接构造等价的产品编号 / Build an equivalent product identifier directly.</summary>
    /// <param name="value">裸值 / The raw value.</param>
    /// <returns>产品编号 / The product identifier.</returns>
    private static IProductId2 Csp2dIdOf(string value) => new Csp2dId(value);
}
