// 板材容量约束（B654）的判据 / Criteria for the sheet-capacity constraint (B654).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **板材容量约束（B654）** / The sheet-capacity constraint (B654).
/// </summary>
/// <remarks>
/// 判据的重点：**一列一行**（合并成一条求和约束会让求解器无法定位超限的列）、**板材面积为零的列被跳过**
/// （那条约束等于强制该列不用）、**上界是常数不是符号**。
///
/// The criteria focus on **one row per column**, since a single summed row leaves the solver unable to locate the overflowing column, on **columns with a zero sheet area being skipped**, since
/// that row would force the column unused, and on **the bound being a constant rather than a symbol**.
/// </remarks>
public class SheetCapacityConstraintTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static SheetType<FltX> SheetType(double width, double height) =>
        new(Q(width), Q(height), Q(1000.0), new SheetTypeId("S1"));

    private static Layer<FltX> Layer(SheetType<FltX>? sheetType, string tag) =>
        new(0L, null, sheetType, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: tag),
            });

    /// <summary>
    /// **一列一行，名字带下标，上界是常数** / One row per column, named with the index, bounded by a constant.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两列 → **两行**，名字是 `sheet_capacity_0` / `sheet_capacity_1`。
    /// 2. **比较方向是 `LE`**（容量是上限，不是下限）。
    /// 3. **右边没有单项式**：上界是常数项，写成符号会引入一个恒等于常数的变量。
    /// 4. 左边只有一项，且挂的是该列自己的面积符号。
    ///
    /// Four paired points: two columns give **two rows** named `sheet_capacity_0` and `sheet_capacity_1`; **the comparison is `LE`**, since capacity is an upper bound rather than a lower one;
    /// **the right-hand side carries no monomials**, being a constant term, where a symbol would introduce a variable that always equals a constant; and the left side has one term carrying that
    /// column's own area symbol.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneRowPerColumn() {
        Layer<FltX>[] layers = { Layer(SheetType(100.0, 100.0), "a"), Layer(SheetType(50.0, 50.0), "b") };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        assignment.Register(model).IsOk.Should().BeTrue();
        var capacity = new PreciseLoadCapacity(layers, assignment);
        capacity.Register(model).IsOk.Should().BeTrue();

        var constraint = new SheetCapacityConstraint(capacity);
        constraint.Name.Should().Be("sheet_capacity");
        constraint.Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64>[] rows = model.Constraints.OfType<LinearInequalityConstraint<Flt64>>().ToArray();
        rows.Should().HaveCount(2, "one row per column, not one summed row");
        rows.Select(row => row.Name).Should().BeEquivalentTo(new[] { "sheet_capacity_0", "sheet_capacity_1" });
        rows.Should().OnlyContain(row => row.Inequality.Comparison == Comparison.LE);

        foreach (LinearInequalityConstraint<Flt64> row in rows)
        {
            row.Inequality.Lhs.Monomials.Should().HaveCount(1);
            row.Inequality.Rhs.Monomials.Should().BeEmpty("the bound is a constant, not a symbol");
        }
        rows.Select(row => row.Inequality.Rhs.Constant.ToDouble())
            .Should().BeEquivalentTo(new[] { 10000.0, 2500.0 });
        rows[0].Inequality.Lhs.Monomials[0].Symbol.Name.Should().Be("load_area_0");
        rows[1].Inequality.Lhs.Monomials[0].Symbol.Name.Should().Be("load_area_1");
    }

    /// <summary>
    /// **板材面积为零的列被跳过** / Columns with a zero sheet area are skipped.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三列里中间一层**没有板材类型** → **只有两行**。
    /// 2. **跳过的不是"面积为零的行"而是那一列整行**：留下会变成 `load_area_1 <= 0`，等于强制该列不用，
    ///    而那正是列生成正在评估的候选。
    /// 3. 剩下的两行下标**保持原样**（0 与 2，不是 0 与 1）——行名对应列号，重编号会让日志与列错位。
    /// 4. 全都没有板材类型时**零行且成功**。
    ///
    /// Four paired points: with the middle of three layers **without a sheet type** there are **only two rows**; **the whole row is skipped rather than kept at a zero area**, since keeping it would
    /// read `load_area_1 &lt;= 0` and force the column unused, which is exactly the candidate column generation is evaluating; the remaining indices **stay as they were** (0 and 2, not 0 and 1),
    /// because a row name must match its column or the log and the columns drift apart; and with no sheet types at all there are **zero rows and success**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSkipColumnsWithoutASheetArea() {
        Layer<FltX>[] layers = {
            Layer(SheetType(100.0, 100.0), "a"),
            Layer(null, "b"),
            Layer(SheetType(50.0, 50.0), "c"),
        };
        var assignment = new PreciseAssignment(layers, new ulong[] { 1UL, 1UL, 1UL });
        using var model = new LinearMetaModel<Flt64>();
        assignment.Register(model).IsOk.Should().BeTrue();
        var capacity = new PreciseLoadCapacity(layers, assignment);
        capacity.Register(model).IsOk.Should().BeTrue();

        new SheetCapacityConstraint(capacity).Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64>[] rows = model.Constraints.OfType<LinearInequalityConstraint<Flt64>>().ToArray();
        rows.Should().HaveCount(2, "the sheet-less column contributes no row");
        rows.Select(row => row.Name).Should().BeEquivalentTo(new[] { "sheet_capacity_0", "sheet_capacity_2" });

        Layer<FltX>[] sheetless = { Layer(null, "a"), Layer(null, "b") };
        var sheetlessAssignment = new PreciseAssignment(sheetless, new ulong[] { 1UL, 1UL });
        using var sheetlessModel = new LinearMetaModel<Flt64>();
        sheetlessAssignment.Register(sheetlessModel).IsOk.Should().BeTrue();
        var sheetlessCapacity = new PreciseLoadCapacity(sheetless, sheetlessAssignment);
        sheetlessCapacity.Register(sheetlessModel).IsOk.Should().BeTrue();
        new SheetCapacityConstraint(sheetlessCapacity).Invoke(sheetlessModel).IsOk.Should().BeTrue();
        sheetlessModel.Constraints.OfType<LinearInequalityConstraint<Flt64>>()
            .Should().BeEmpty("no sheet area means no capacity row");
    }

    /// <summary>
    /// **符号不足时报错而不是漏列** / Too few symbols fail rather than skipping columns.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 容量声明 2 列但只有 1 个面积符号 → **失败**。
    /// 2. **失败发生在加任何行之前**，模型上不留半套约束。
    /// 3. 自定义名被用作行名前缀。
    ///
    /// Three paired points: a capacity claiming two columns with one area symbol **fails**; **the failure happens before any row is added**, leaving no half-built constraint set; and a custom
    /// name becomes the row-name prefix.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenSymbolsAreShort() {
        using var model = new LinearMetaModel<Flt64>();
        var constraint = new SheetCapacityConstraint(new ShortCapacity(), name: "cap");
        constraint.Name.Should().Be("cap");

        constraint.Invoke(model).IsOk.Should().BeFalse("the symbols do not cover the columns");
        model.Constraints.OfType<LinearInequalityConstraint<Flt64>>()
            .Should().BeEmpty("a failed invoke leaves no rows behind");

        using var named = new LinearMetaModel<Flt64>();
        var good = new GoodCapacity();
        new SheetCapacityConstraint(good, "cap").Invoke(named).IsOk.Should().BeTrue();
        named.Constraints.OfType<LinearInequalityConstraint<Flt64>>()
            .Select(row => row.Name).Should().BeEquivalentTo(new[] { "cap_0" });
    }

    /// <summary>声明两列但只有一个面积符号的容量 / A capacity claiming two columns with one area symbol.</summary>
    private sealed class ShortCapacity : ICapacity
    {
        public IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> LoadArea { get; } =
            new[] { Symbol("only_one") };

        public IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> LoadingRate { get; } =
            Array.Empty<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>();

        public FltX SheetAreaAt(int index) => new(100.0);

        public int ColumnCount() => 2;

        private static Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> Symbol(string name) =>
            new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                    constant: Flt64.Zero),
                name: name);
    }

    /// <summary>一列一符号的完整容量 / A complete single-column capacity.</summary>
    private sealed class GoodCapacity : ICapacity
    {
        public IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> LoadArea { get; } =
            new[] {
                new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        constant: Flt64.Zero),
                    name: "load_area_0"),
            };

        public IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> LoadingRate { get; } =
            Array.Empty<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>();

        public FltX SheetAreaAt(int index) => new(100.0);

        public int ColumnCount() => 1;
    }
}
