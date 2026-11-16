// 板数与板材面积目标的判据 / Criteria for the sheet-amount and sheet-area objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **板数与板材面积目标** / The sheet-amount and sheet-area objectives.
/// </summary>
/// <remarks>
/// 判据的重点：**系数是函数，真的逐层调用**、**零系数项被跳过**、以及**没有板材类型的层被跳过**（与零系数跳过是两回事）。
///
/// The criteria focus on **the coefficient being a function actually invoked per layer**, **zero-coefficient terms being skipped**, and **layers without a sheet type being skipped**, which
/// is a different rule from the zero-coefficient one.
/// </remarks>
public class SheetObjectivesTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(SheetType<FltX>? sheetType, ulong units, string tag) {
        var placements = new List<Placement2<FltX>>();
        for (ulong index = 0UL; index < units; ++index)
        {
            placements.Add(new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                Rotated: false,
                ItemId: tag + index));
        }
        return new Layer<FltX>(0L, null, sheetType, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)), placements);
    }

    private static SheetType<FltX> SheetType(double width, double height) =>
        new(Q(width), Q(height), Q(1000.0), new SheetTypeId("S1"));

    /// <summary>
    /// **系数函数逐层调用，零系数项被跳过** / The coefficient function is called per layer, and zero-coefficient terms are skipped.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三层、系数 `1 / 0 / 3` → 目标里**只有两项**，系数是 1 和 3。
    /// 2. **零系数项被跳过**（不是留着系数 0 的项）。
    /// 3. **系数函数被调用三次**——用常数顶替函数会只调一次或零次。
    /// 4. `y` 未注册时失败，且不留目标。
    ///
    /// Four paired points: three layers with coefficients `1 / 0 / 3` leave **exactly two terms**, of 1 and 3; **the zero-coefficient term is skipped** rather than kept at zero; **the function
    /// is called three times**, where a constant would be called once or never; and an unregistered `y` fails without leaving an objective.
    /// </remarks>
    [Fact]
    public void SheetAmountMinimization_ShouldCallTheCoefficientFunctionPerLayer() {
        Layer<FltX>[] layers = { Layer(null, 1UL, "a"), Layer(null, 2UL, "b"), Layer(null, 3UL, "c") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        var calls = new List<Layer<FltX>>();
        var objective = new SheetAmountMinimization(layers, assignment, layer => {
            calls.Add(layer);
            return new FltX(layer.Amount * 1.0);
        });

        objective.Invoke(model).IsOk.Should().BeFalse("y is not registered yet");
        model.MetaSubObjects.Should().BeEmpty("a failed invoke leaves nothing behind");

        assignment.Register(model).IsOk.Should().BeTrue();
        objective.Invoke(model).IsOk.Should().BeTrue();
        calls.Should().HaveCount(3, "the coefficient is a function, called once per layer");
        calls[0].Should().BeSameAs(layers[0]);

        model.MetaSubObjects.Should().HaveCount(1);
        model.MetaSubObjects[0].Name.Should().Be("sheet_amount_minimization");
        model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Minimum);
        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(3);
        model.MetaSubObjects[0].Polynomial.Monomials.Select(m => m.Coefficient.ToFlt64().ToDouble())
            .Should().BeEquivalentTo(new[] { 1.0, 2.0, 3.0 });
        model.MetaSubObjects[0].Polynomial.Monomials.Select(m => m.Symbol.Name)
            .Should().OnlyContain(name => name.StartsWith("y"));
    }

    /// <summary>
    /// **零系数跳过，全零仍成功** / Zero coefficients are skipped, and an all-zero set still succeeds.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 中间层系数 0 → **只留两项**。
    /// 2. 全部系数 0 → **目标为空但成功**（"所有层都不要"是合法输入，不是错误）。
    /// 3. 全零目标的多项式项数为 0，不是留一堆零项。
    ///
    /// Three paired points: a zero coefficient in the middle leaves **only two terms**; all-zero coefficients yield **an empty objective but still succeed**, since "use no layer" is a legitimate
    /// input rather than an error; and the all-zero objective has zero monomials rather than a pile of zero terms.
    /// </remarks>
    [Fact]
    public void SheetAmountMinimization_ShouldSkipZeroCoefficients() {
        Layer<FltX>[] layers = { Layer(null, 1UL, "a"), Layer(null, 2UL, "b"), Layer(null, 3UL, "c") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        assignment.Register(model).IsOk.Should().BeTrue();

        double[] coefficients = { 1.0, 0.0, 3.0 };
        int index = 0;
        new SheetAmountMinimization(layers, assignment, _ => new FltX(coefficients[index++]))
            .Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(2, "the zero coefficient is skipped");

        using var allZeroModel = new LinearMetaModel<Flt64>();
        var allZero = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        allZero.Register(allZeroModel).IsOk.Should().BeTrue();
        new SheetAmountMinimization(layers, allZero, _ => new FltX(0.0))
            .Invoke(allZeroModel).IsOk.Should().BeTrue("using no layer is a legitimate input");
        allZeroModel.MetaSubObjects[0].Polynomial.Monomials.Should().BeEmpty();
    }

    /// <summary>
    /// **没有板材类型的层被跳过，与零系数跳过是两回事** / Layers without a sheet type are skipped, a different rule from the zero-coefficient skip.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三层里中间一层**没有板材类型** → 目标只剩两项。
    /// 2. 系数 = `weight × 板材面积`：100×100 的板材在权重 1 下系数为 10000。
    /// 3. **权重 0 时全部跳过**（面积不为零也跳过，因为乘积为零）。
    /// 4. 目标名是 `sheet_area_minimization`（与板数目标的默认名不同）。
    ///
    /// Four paired points: a middle layer **without a sheet type** leaves two terms; the coefficient is `weight × sheet area`, so a 100×100 sheet at weight 1 gives 10000; **a zero weight skips
    /// everything** even though areas are non-zero, because the product is zero; and the objective name is `sheet_area_minimization`, distinct from the sheet-amount default.
    /// </remarks>
    [Fact]
    public void SheetAreaMinimization_ShouldSkipLayersWithoutASheetType() {
        Layer<FltX>[] layers = {
            Layer(SheetType(100.0, 100.0), 1UL, "a"),
            Layer(null, 2UL, "b"),
            Layer(SheetType(50.0, 50.0), 3UL, "c"),
        };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        assignment.Register(model).IsOk.Should().BeTrue();

        var objective = new SheetAreaMinimization(layers, assignment);
        objective.Invoke(model).IsOk.Should().BeTrue();
        objective.Name.Should().Be("sheet_area_minimization");
        objective.Weight.ToFlt64().ToDouble().Should().Be(1.0);

        model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(2, "the sheet-less layer is skipped");
        model.MetaSubObjects[0].Polynomial.Monomials.Select(m => m.Coefficient.ToFlt64().ToDouble())
            .Should().BeEquivalentTo(new[] { 10000.0, 2500.0 });

        using var zeroWeightModel = new LinearMetaModel<Flt64>();
        var zeroWeight = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        zeroWeight.Register(zeroWeightModel).IsOk.Should().BeTrue();
        new SheetAreaMinimization(layers, zeroWeight, new FltX(0.0))
            .Invoke(zeroWeightModel).IsOk.Should().BeTrue("a zero weight is a legitimate input");
        zeroWeightModel.MetaSubObjects[0].Polynomial.Monomials.Should().BeEmpty("the product is zero for every layer");
    }
}
