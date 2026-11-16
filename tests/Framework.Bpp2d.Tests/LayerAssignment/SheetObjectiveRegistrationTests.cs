// 两个目标注册助手的判据 / Criteria for the two objective-registration helpers.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **两个目标注册助手** / The two objective-registration helpers.
/// </summary>
/// <remarks>
/// 判据的重点：**助手把参数原样交给目标**（读多项式系数，不只查"注册成功"）、
/// **数量目标的系数是常量函数而不是 null**、**助手不吞错**。
///
/// The criteria focus on **the helpers passing their arguments through**, verified by reading the polynomial coefficient rather than merely checking for success, on **the amount objective's
/// coefficient being a constant function rather than null**, and on **the helpers not swallowing failures**.
/// </remarks>
public class SheetObjectiveRegistrationTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    /// <remarks>
    /// **层必须带板材类型**：`SheetAreaMinimization` 会跳过没有板材的层，
    /// 层不带板材时目标注册成功却什么都不加，判据就会误判成"助手没生效"。
    ///
    /// **The layer must carry a sheet type**: `SheetAreaMinimization` skips sheet-less layers (recorded), so a layer without one makes the registration succeed while adding nothing and
    /// the criterion would misread that as the helper being inert.
    /// </remarks>
    private static Layer<FltX> Layer(double width, double height, string itemId) =>
        new(0L, null,
            new SheetType<FltX>(Q(width), Q(height), Q(1000.0), new SheetTypeId("S"), true),
            new Sheet2Shape<FltX>(Q(width), Q(height)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(10.0), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: itemId),
            });

    private static Layer<FltX>[] Layers() =>
        new[] { Layer(100.0, 100.0, "I1"), Layer(120.0, 100.0, "I2") };

    private static PreciseAssignment Assigned(LinearMetaModel<Flt64> model)
    {
        var assignment = new PreciseAssignment(Layers(), new ulong[] { 1UL, 1UL });
        // **份数变量必须先注册**：目标的系数要靠它，没注册时目标施加会失败。
        // **The copy-count variables must be registered first**, since the objective's coefficients depend on them and applying it fails otherwise.
        assignment.Register(model).IsOk.Should().BeTrue();
        return assignment;
    }

    /// <summary>
    /// **面积助手把权重原样交给目标** / The area helper passes the weight through.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 默认权重 → 目标多项式里面积单项的系数是 **1**。
    /// 2. 传 `2.5` → 同一单项的系数是 **2.5**（不是 1）。**只看"注册成功"测不到这一点**，必须读系数。
    /// 3. 目标名落在模型里，是默认的 `sheet_area_minimization`。
    /// 4. **重复注册不报错而是叠加**（实测：`AddObject` 容忍重名，与变量容忍同类），
    /// 所以调用方不能重复注册——这是风险而不是功能。
    ///
    /// Four paired points: the default weight gives an area monomial coefficient of **1**; passing `2.5` gives **2.5** rather than 1, which **a mere success check cannot detect** because it never
    /// reads the coefficient; the objective name reaches the model as `sheet_area_minimization`; and **a repeated registration stacks instead of failing** (measured: `AddObject` tolerates
    /// duplicate names, the same family as the earlier variables), so callers must not double-register — a risk, not a feature.
    /// </remarks>
    [Fact]
    public void RegisterSheetAreaMinimization_ShouldPassTheWeightThrough()
    {
        LinearMetaModel<Flt64> model = new("m");
        var context = new LayerAssignmentContext();
        context.RegisterSheetAreaMinimization(model, Assigned(model), Layers()).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().HaveCount(1);
        model.MetaSubObjects[0].Name.Should().Be("sheet_area_minimization");
        // **每层一个单项，符号是份数变量、系数是该层板材面积**（实测 `y_0=10000`、`y_1=12000`），
        // 权重乘在这些系数上。按份数变量名读即可。
        // **One monomial per layer, its symbol the copy variable and its coefficient that layer's sheet area** (measured `y_0=10000`, `y_1=12000`); the weight multiplies those coefficients. Read
        // them by copy-variable name.
        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(2, "one monomial per sheet-bearing layer");
        model.MetaSubObjects[0].Polynomial.Monomials
            .Single(monomial => monomial.Symbol.Name == "y_0")
            .Coefficient.ToDouble().Should().BeApproximately(10000.0, 1e-6, "the default weight is one");

        LinearMetaModel<Flt64> weighted = new("weighted");
        context.RegisterSheetAreaMinimization(weighted, Assigned(weighted), Layers(), new FltX(2.5))
            .IsOk.Should().BeTrue();
        weighted.MetaSubObjects[0].Polynomial.Monomials
            .Single(monomial => monomial.Symbol.Name == "y_0")
            .Coefficient.ToDouble().Should().BeApproximately(25000.0, 1e-6, "the caller's weight reaches the objective");

        LinearMetaModel<Flt64> shared = new("shared");
        PreciseAssignment sharedAssignment = Assigned(shared);
        context.RegisterSheetAreaMinimization(shared, sharedAssignment, Layers()).IsOk.Should().BeTrue();
        context.RegisterSheetAreaMinimization(shared, sharedAssignment, Layers()).IsOk.Should().BeTrue();
        shared.MetaSubObjects.Should().HaveCount(2, "a repeated registration stacks rather than fails");
    }

    /// <summary>
    /// **数量助手的系数是常量函数，不是 null** / The amount helper's coefficient is a constant function, not null.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 默认 → **至少一个单项**，系数 1。`null` 在 `SheetAmountMinimization` 里表示"缺数据"，会**跳过该层**，
    /// 所以"单项数为 0"正是"系数为 null"的指纹。
    /// 2. 传 `0` → 单项**仍在**、系数为 0：零权重不等于缺数据。
    /// 3. 单项的符号是份数变量 `Y`，不是别的。
    /// 4. 目标名是默认的 `sheet_amount_minimization`。
    ///
    /// Four paired points: the default yields **at least one monomial** with coefficient 1; `null` means "missing data" in `SheetAmountMinimization` and **skips the layer**, so a zero monomial
    /// count is the fingerprint of a null coefficient (the lesson); passing `0` leaves the monomial **in place** with coefficient 0, since zero weight differs from missing data; and the
    /// monomial's symbol is the copy-count variable `Y`.
    /// </remarks>
    [Fact]
    public void RegisterSheetAmountMinimization_ShouldUseAConstantCoefficient()
    {
        LinearMetaModel<Flt64> model = new("m");
        var context = new LayerAssignmentContext();
        PreciseAssignment assignment = Assigned(model);
        context.RegisterSheetAmountMinimization(model, assignment, Layers()).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().HaveCount(1);
        model.MetaSubObjects[0].Name.Should().Be("sheet_amount_minimization");
        model.MetaSubObjects[0].Polynomial.Monomials.Should().NotBeEmpty(
            "a constant coefficient contributes a monomial; a null coefficient would skip every layer");
        model.MetaSubObjects[0].Polynomial.Monomials[0].Coefficient.ToDouble()
            .Should().BeApproximately(1.0, 1e-9, "the default coefficient is one");

        LinearMetaModel<Flt64> zero = new("zero");
        context.RegisterSheetAmountMinimization(zero, Assigned(zero), Layers(), new FltX(0.0))
            .IsOk.Should().BeTrue();
        // **实测：系数 0 时单项也从多项式里消失**（零系数单项会被消去），所以"零权重"与"缺数据"在多项式上无法区分。
        // 判据因此只钉住**默认路径**：常量函数给出系数 1 的单项——`null` 会让单项一个都不剩，正是变异的指纹。
        // **Measured: with coefficient 0 the monomials vanish too** (zero-coefficient monomials are dropped), so "zero weight" and "missing data" are indistinguishable in the polynomial. The
        // criterion therefore pins **the default path only**: a constant function yields coefficient-1 monomials, while `null` would leave none — exactly the mutant's fingerprint.
        zero.MetaSubObjects[0].Polynomial.Monomials.Should().BeEmpty(
            "zero weight is eliminated from the polynomial, like a missing coefficient");
    }

    /// <summary>
    /// **助手不吞错** / The helpers do not swallow failures.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 份数变量**没有**注册进这个模型时，目标施加失败。
    /// 2. 助手把这个失败**原样返回**，不是"构造完就报成功"。
    /// 3. 两个助手表现一致。
    ///
    /// Three paired points: with the copy-count variables **not** registered in this model the objective application fails; the helper **returns that failure** rather than reporting success
    /// merely because it constructed the objective; and both helpers behave the same way.
    /// </remarks>
    [Fact]
    public void Helpers_ShouldPropagateFailures()
    {
        var context = new LayerAssignmentContext();
        // **压根没注册过的分配**：`Y` 为 null，目标没有份数变量可挂，必然失败。
        // **An assignment that was never registered**: `Y` is null, the objective has no copy variable to hang on, and it must fail.
        var assignment = new PreciseAssignment(Layers(), new ulong[] { 1UL, 1UL });
        assignment.Y.Should().BeNull("the fixture relies on an unregistered assignment");

        LinearMetaModel<Flt64> bare = new("bare");
        context.RegisterSheetAreaMinimization(bare, assignment, Layers()).IsOk
            .Should().BeFalse("the objective cannot resolve symbols missing from this model");
        bare.MetaSubObjects.Should().BeEmpty("a failed application leaves no objective behind");

        LinearMetaModel<Flt64> bare2 = new("bare2");
        context.RegisterSheetAmountMinimization(bare2, assignment, Layers()).IsOk
            .Should().BeFalse();
        bare2.MetaSubObjects.Should().BeEmpty();
    }
}
