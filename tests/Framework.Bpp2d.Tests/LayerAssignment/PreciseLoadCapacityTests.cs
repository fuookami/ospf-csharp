// 精确装载容量的判据 / Criteria for the precise load capacity.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **精确装载容量** / The precise load capacity.
/// </summary>
/// <remarks>
/// 判据的重点：**板材面积为零时装载率系数为零而不做除法**（除零得到 `NaN`，求解器不会拒绝它）、
/// **两个符号组都注册**、**注册幂等**。
///
/// The criteria focus on **a zero sheet area making the loading-rate coefficient zero rather than dividing**, since a division by zero yields NaN that the solver will not reject, on **both symbol
/// groups being registered**, and on **registration being idempotent**.
/// </remarks>
public class PreciseLoadCapacityTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static SheetType<FltX> SheetType(double width, double height) =>
        new(Q(width), Q(height), Q(1000.0), new SheetTypeId("S1"));

    private static Layer<FltX> Layer(SheetType<FltX>? sheetType, double itemWidth, double itemHeight, string tag) =>
        new(0L, null, sheetType, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(itemWidth), Q(itemHeight)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: tag),
            });

    /// <summary>
    /// **两个符号组都注册，系数按面积算** / Both symbol groups are registered, with coefficients from the areas.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 注册前两个列表**都是空的**，`Register` 之后各有每列一项。
    /// 2. **两个组都在模型上**——判据查模型里的符号名（`load_area_0` / `loading_rate_0`）。
    /// 3. `load_area` 的系数是**该列实际面积**（20×10 = 200）。
    /// 4. `loading_rate` 的系数是**实际面积 / 板材面积**（200 / 10000 = 0.02）。
    ///
    /// Four paired points: both lists are **empty** before registration and hold one entry per column after; **both groups are on the model**, checked by symbol name; the `load_area` coefficient
    /// is **the column's actual area** (20×10 = 200); and the `loading_rate` coefficient is **actual over sheet area** (200 / 10000 = 0.02).
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildBothSymbolGroups() {
        Layer<FltX>[] layers = { Layer(SheetType(100.0, 100.0), 20.0, 10.0, "a") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL });
        using var model = new LinearMetaModel<Flt64>();
        var capacity = new PreciseLoadCapacity(layers, assignment);

        capacity.LoadArea.Should().BeEmpty("nothing is built before registration");
        capacity.LoadingRate.Should().BeEmpty();
        capacity.ColumnCount().Should().Be(1);
        capacity.SheetAreaAt(0).ToFlt64().ToDouble().Should().BeApproximately(10000.0, 1e-6);
        capacity.SheetAreaAt(5).ToFlt64().ToDouble().Should().Be(0.0, "out of range is zero, not a throw");

        assignment.Register(model).IsOk.Should().BeTrue();
        capacity.Register(model).IsOk.Should().BeTrue();
        capacity.LoadArea.Should().HaveCount(1);
        capacity.LoadingRate.Should().HaveCount(1);

        capacity.LoadArea[0].Name.Should().Be("load_area_0");
        capacity.LoadingRate[0].Name.Should().Be("loading_rate_0");
        capacity.LoadArea[0].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().BeApproximately(200.0, 1e-6, "the load area is the column's actual area");
        capacity.LoadingRate[0].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().BeApproximately(0.02, 1e-9, "the loading rate is actual over sheet area");
        capacity.LoadingRate[0].Polynomial.Monomials.Single().Symbol.Name.Should().StartWith("y");

        // **两个符号组真的在模型上**：再 `Add` 同一个符号会因重名被拒绝；若从未加进去，这次 `Add` 会成功。
        // **Both groups really are on the model**: re-adding the same symbol is rejected on the duplicate name, while a symbol that was never added would be accepted here.
        model.Add(capacity.LoadArea[0]).IsOk.Should().BeFalse("load_area_0 is already on the model");
        model.Add(capacity.LoadingRate[0]).IsOk.Should().BeFalse("loading_rate_0 is already on the model");
    }

    /// <summary>
    /// **板材面积为零时装载率系数为零，不做除法** / A zero sheet area makes the loading-rate coefficient zero rather than dividing.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 无板材类型的层 → 板材面积为 0 → **装载率系数为 0**。
    /// 2. **不是 `NaN`**：断言系数是零而不是"非数"。除零在 `FltX`（decimal）上会抛异常，在浮点上会得到 `NaN`，
    /// 两条路都错——前者炸掉求解，后者让求解器给出无意义的最优解。
    /// 3. `load_area` 仍然按实际面积给出系数（它不依赖板材面积）。
    /// 4. 三列里只有中间一层无板材 → 只有它的装载率系数为零。
    ///
    /// Four paired points: a layer without a sheet type has a sheet area of 0 and therefore **a loading-rate coefficient of 0**; it is **not NaN**, since a division by zero either throws on
    /// `FltX` (decimal) or yields NaN on floats and both are wrong — one kills the solve, the other lets the solver report a meaningless optimum; `load_area` still uses the actual area, which
    /// does not depend on the sheet; and with the middle of three layers sheet-less only its loading rate is zero.
    /// </remarks>
    [Fact]
    public void Register_ShouldNotDivideByAZeroSheetArea() {
        Layer<FltX>[] layers = {
            Layer(SheetType(100.0, 100.0), 20.0, 10.0, "a"),
            Layer(null, 20.0, 10.0, "b"),
            Layer(SheetType(50.0, 50.0), 20.0, 10.0, "c"),
        };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        assignment.Register(model).IsOk.Should().BeTrue();
        var capacity = new PreciseLoadCapacity(layers, assignment);
        capacity.Register(model).IsOk.Should().BeTrue();

        capacity.LoadingRate[0].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().BeApproximately(0.02, 1e-9);
        capacity.LoadingRate[1].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().Be(0.0, "a zero sheet area must not divide");
        capacity.LoadingRate[1].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().NotBe(double.NaN);
        capacity.LoadingRate[2].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().BeApproximately(200.0 / 2500.0, 1e-9);

        capacity.LoadArea[1].Polynomial.Monomials.Single().Coefficient.ToFlt64().ToDouble()
            .Should().BeApproximately(200.0, 1e-6, "the load area does not depend on the sheet");
    }

    /// <summary>
    /// **注册幂等，缺份数变量时失败** / Registration is idempotent, and a missing copy variable fails.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 未注册 `y` 就注册容量 → **失败**（不是建出指向空符号的容量）。
    /// 2. 注册后再次注册 → **成功且复用同一批符号对象**（`BeSameAs`）。
    /// 3. 空层列表也能注册，两个列表都是空的。
    ///
    /// Three paired points: registering capacity before `y` **fails** rather than building capacity that points at nothing; a second registration **succeeds and reuses the same symbol objects**;
    /// and an empty layer list registers with both lists empty.
    /// </remarks>
    [Fact]
    public void Register_ShouldBeIdempotentAndRequireTheCopyVariable() {
        Layer<FltX>[] layers = { Layer(SheetType(100.0, 100.0), 20.0, 10.0, "a") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL });
        using var model = new LinearMetaModel<Flt64>();
        var capacity = new PreciseLoadCapacity(layers, assignment);

        capacity.Register(model).IsOk.Should().BeFalse("y is not registered yet");

        assignment.Register(model).IsOk.Should().BeTrue();
        capacity.Register(model).IsOk.Should().BeTrue();
        IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> firstArea = capacity.LoadArea;
        IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> firstRate = capacity.LoadingRate;

        capacity.Register(model).IsOk.Should().BeTrue();
        capacity.LoadArea.Should().BeSameAs(firstArea, "re-registration reuses the symbol group");
        capacity.LoadingRate.Should().BeSameAs(firstRate);

        using var emptyModel = new LinearMetaModel<Flt64>();
        var emptyAssignment = new PreciseAssignment(Array.Empty<Layer<FltX>>(), Array.Empty<ulong>());
        emptyAssignment.Register(emptyModel).IsOk.Should().BeTrue();
        var empty = new PreciseLoadCapacity(Array.Empty<Layer<FltX>>(), emptyAssignment);
        empty.Register(emptyModel).IsOk.Should().BeTrue();
        empty.ColumnCount().Should().Be(0);
        empty.LoadArea.Should().BeEmpty();
    }
}
