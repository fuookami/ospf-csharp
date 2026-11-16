// 产品与需求判据 / Criteria for products and demands.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Material;

/// <summary>
/// **产品方向、需求口径与重复产品** / Product orientations, demand units and duplicate products.
/// </summary>
/// <remarks>
/// 对应基线 `Product2.kt`。单件面积（`area`）与逐条需求的几何校验按既有原则推迟——它们都需要
/// `Quantity&lt;V&gt;` 的乘法或 `quantityIsPositive`。
///
/// Mirrors the baseline `Product2.kt`. The single-piece area and the per-demand geometry validation are scoped out under the established rule — both need
/// `Quantity&lt;V&gt;` multiplication or `quantityIsPositive`.
/// </remarks>
public class Product2Tests {
    /// <summary>**不允许旋转的件只有一个方向** / A non-rotatable piece has exactly one orientation.</summary>
    [Fact]
    public void Orientations_ShouldCollapseWhenRotationIsForbidden() {
        Product2<Flt64> rotatable = Product2Of(allowRotate: true);
        Product2<Flt64> fixedPiece = Product2Of(allowRotate: false);

        rotatable.Orientations().Should().HaveCount(2);
        rotatable.Orientations()[0].Should().BeFalse("the first orientation is unrotated");
        fixedPiece.Orientations().Should().ContainSingle();
    }

    /// <summary>**件数口径给出件数，其它口径给出空** / The amount unit yields a count; other units yield null.</summary>
    /// <remarks>
    /// 面积口径下的 0 会被下游当成"不需要任何件"，而真实语义是**"这个数在件数口径下没有意义"**。
    ///
    /// A zero under the area unit would be read downstream as "no pieces needed", while the real meaning is **"this number is meaningless in pieces"**.
    /// </remarks>
    [Fact]
    public void PiecesOrNull_ShouldOnlyAnswerForTheAmountUnit() {
        ProductDemand2<Flt64> amount = new(Product2Of(allowRotate: true), new DemandValue2<Flt64>.Amount(7UL));
        ProductDemand2<Flt64> area = new(
            Product2Of(allowRotate: true), new DemandValue2<Flt64>.Area(SquareMetres(2.0)));
        ProductDemand2<Flt64> weight = new(
            Product2Of(allowRotate: true), new DemandValue2<Flt64>.Weight(Kilograms(3.0)));

        amount.PiecesOrNull().Should().Be(7UL);
        amount.Mode.Should().Be(DemandMode2.Amount);
        area.PiecesOrNull().Should().BeNull("an area demand has no piece count");
        area.Mode.Should().Be(DemandMode2.Area);
        weight.PiecesOrNull().Should().BeNull("a weight demand has no piece count");
        weight.Mode.Should().Be(DemandMode2.Weight);
    }

    /// <summary>**重复产品必须被拒** / Duplicate products must be rejected.</summary>
    /// <remarks>
    /// `ByProduct` **后写覆盖先写**，所以同一个产品只留下一条需求，**需求总量少了，看起来像"需求更小、更容易做"**。
    ///
    /// `ByProduct` **lets a later entry overwrite an earlier one**, so one demand survives per product, **the total shrinks, and it looks like a smaller, easier
    /// problem**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectDuplicateProducts() {
        ProductDemands2<Flt64> duplicated = new(new List<ProductDemand2<Flt64>> {
            new(Product2Of(allowRotate: true), new DemandValue2<Flt64>.Amount(1UL)),
            new(Product2OfWithId("P1", allowRotate: true), new DemandValue2<Flt64>.Amount(2UL)),
        });
        ProductDemands2<Flt64> distinct = new(new List<ProductDemand2<Flt64>> {
            new(Product2Of(allowRotate: true), new DemandValue2<Flt64>.Amount(1UL)),
            new(Product2OfWithId("P2", allowRotate: true), new DemandValue2<Flt64>.Amount(2UL)),
        });

        Result<Success, ErrorCode, Error<ErrorCode>> rejected = duplicated.Validate();
        rejected.IsFailed.Should().BeTrue("the same product appears twice");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)rejected).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)rejected).Error
            .Should().BeOfType<Csp2dValidationError>("the semantic subtype identifies the failure class");
        duplicated.DuplicateProducts().Should().ContainSingle().Which.Should().Be("P1");

        distinct.Validate().IsOk.Should().BeTrue("distinct products are legal");
        distinct.ByProduct().Should().HaveCount(2);
    }

    /// <summary>**错误子类型带默认文案，也接受自定义文案** / The error subtype defaults its message and accepts a custom one.</summary>
    [Fact]
    public void ValidationError_ShouldCarryItsOwnCodeAndMessage() {
        new Csp2dValidationError().Code.Should().Be(ErrorCode.IllegalArgument);
        new Csp2dValidationError().Message.Should().Be("CSP2D validation error.");
        new Csp2dValidationError("bad geometry").Message.Should().Be("bad geometry");
        new Csp2dInternalError().Code.Should().Be(ErrorCode.ApplicationError);
        new Csp2dSolvingError().Code.Should().Be(ErrorCode.ApplicationFailed);
        new Csp2dCapabilityError("irregular part").Message.Should().Contain("irregular part");
    }

    /// <summary>构造产品 / Build a product.</summary>
    /// <param name="allowRotate">是否允许旋转 / Whether rotation is allowed.</param>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> Product2Of(bool allowRotate) => Product2OfWithId("P1", allowRotate);

    /// <summary>按编号构造产品 / Build a product with the given identifier.</summary>
    /// <param name="id">产品编号 / The product identifier.</param>
    /// <param name="allowRotate">是否允许旋转 / Whether rotation is allowed.</param>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> Product2OfWithId(string id, bool allowRotate) =>
        new(
            Id: Csp2dIds.ProductId2Of(id),
            Name: "part-" + id,
            Width: Metres(1.0),
            Height: Metres(0.5),
            AllowRotate: allowRotate);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造平方米量 / Build an area in square metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> SquareMetres(double value) =>
        new(new Flt64(value), AreaUnits.SquareMeter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
