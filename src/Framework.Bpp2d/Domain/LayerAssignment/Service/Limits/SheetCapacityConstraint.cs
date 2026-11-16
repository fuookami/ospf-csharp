#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 板材容量约束 / The sheet-capacity constraint.
/// </summary>
/// <remarks>
/// `SheetCapacityConstraint` 对齐：**每列一条** `load_area_i &lt;= 该列板材面积`。
///
/// 三处必须沿用：
///
/// 1. **板材面积为零或负的列直接跳过**：那条约束会变成 `load_area_i &lt;= 0`，等于强制该列不用。
///    而"没有板材类型的列"恰恰是列生成正在评估的候选，强制不用会让它永远进不了解。
/// 2. **一行一列，名字 `{name}_{i}`**：合并成一条求和约束会让求解器无法判断"是哪一列超了"。
/// 3. **上界是常数项，不是符号**：右边是空单项式 + 常数 `sheetArea`。写成符号会引入一个恒等于常数的变量，
///    白白增加求解规模。
///
/// Aligned with its `SheetCapacityConstraint`: **one row per column**, `load_area_i &lt;= that column's sheet area`. Three things must be copied: **a column whose sheet area is zero or
/// negative is skipped**, since that row would read `load_area_i &lt;= 0` and force the column unused while such a column — one without a sheet type — is exactly what column generation is
/// evaluating; **one row per column named `{name}_{i}`**, because a single summed constraint leaves the solver unable to say which column overflowed; and **the right-hand side is a constant
/// term, not a symbol**, since using a symbol would introduce a variable that always equals a constant and inflate the solve.
/// </remarks>
public sealed class SheetCapacityConstraint
{
    private readonly ICapacity _capacity;

    /// <summary>
    /// 构造板材容量约束 / Construct the sheet-capacity constraint.
    /// </summary>
    /// <param name="capacity">装载容量 / The load capacity.</param>
    /// <param name="name">约束名 / The constraint name.</param>
    public SheetCapacityConstraint(ICapacity capacity, string name = "sheet_capacity")
    {
        ArgumentNullException.ThrowIfNull(capacity);
        _capacity = capacity;
        Name = name;
    }

    /// <summary>约束名 / The constraint name</summary>
    public string Name { get; }

    /// <summary>
    /// 把约束加到模型 / Add the constraint to the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> areaSymbols = _capacity.LoadArea;
        int columnCount = _capacity.ColumnCount();
        if (areaSymbols.Count < columnCount)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "装载面积符号少于列数，容量约束会漏列 / the load-area symbols are fewer than the columns, so the capacity rows would skip columns"));
        }

        for (int index = 0; index < columnCount; ++index)
        {
            // **板材面积为零或负的列跳过**：那条约束等于强制该列不用。
            // **A zero or negative sheet area is skipped**: that row would force the column unused.
            FltX sheetArea = _capacity.SheetAreaAt(index);
            if (sheetArea.ToFlt64().ToDouble() <= 0.0)
            {
                continue;
            }

            var left = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, areaSymbols[index]) },
                Flt64.Zero);
            // **右边是常数项，不是符号。**
            // **The right-hand side is a constant term**, not a symbol.
            var right = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                sheetArea.ToFlt64());
            Try added = model.AddConstraint(
                new LinearInequality<Flt64>(left, right, Comparison.LE),
                group: null,
                name: Name + "_" + index);
            if (!added.IsOk)
            {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
