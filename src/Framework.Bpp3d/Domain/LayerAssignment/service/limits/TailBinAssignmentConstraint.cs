#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 尾箱分配约束 / The tail-bin assignment constraint.
/// </summary>
/// <remarks>
/// 与基线 `layer_assignment/service/limits/TailBinAssignmentConstraint.kt` 对齐：**尾箱必须排在最后**。
///
/// 四点必须照抄：
///
/// 1. **最后一个箱单独走一支**：`v[last] &lt;= tail[last]`（最后一个箱被用到时它必须是尾箱）。
/// 2. **其余每个箱走另一支**：`tail[i] &lt;= v[i + 1]`（标了尾箱就必须还有后一个箱被用到）。
/// 3. **两支都是 `Comparison.LE`**，方向反了会让"尾箱在前"成为可能。
/// 4. 命名：每个箱都是一条 `{name}_{i}`，包括最后一箱——**没有单独的尾条名字**。
///
/// 两支的方向**不对称**是基线自带的：最后一箱比的是 `v` 与 `tail`（同一箱），其余比的是 `tail[i]` 与 `v[i+1]`
/// （跨到下一个箱）。把它们统一成一种写法会让"最后一个箱既是尾箱又没被用到"通过。
///
/// Aligned with the baseline: **the tail bin must come last**. Four things must be copied: **the last bin takes its own branch**, `v[last] &lt;= tail[last]` (a used last bin must be the tail
/// bin); **every other bin takes the other**, `tail[i] &lt;= v[i + 1]` (marking a tail requires a used successor); **both are `Comparison.LE`**, and reversing them would allow a tail bin
/// up front; and the name is `{name}_{i}` for every bin including the last — **there is no separate tail row name**. The asymmetry between the branches is the baseline's own: the last
/// bin relates `v` and `tail` of the same bin, the rest relate `tail[i]` to `v[i+1]` across neighbours, and unifying them would let "the last bin is a tail and unused" through.
/// </remarks>
public sealed class TailBinAssignmentConstraint
{
    private readonly IReadOnlyList<Bin<BinLayer, FltX>> _bins;
    private readonly Model.PreciseAssignment _assignment;
    private readonly string _name;

    /// <summary>
    /// 构造尾箱分配约束 / Construct the tail-bin assignment constraint.
    /// </summary>
    /// <param name="bins">箱 / The bins.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public TailBinAssignmentConstraint(
        IReadOnlyList<Bin<BinLayer, FltX>> bins,
        Model.PreciseAssignment assignment,
        string name = "tail_bin_assignment_constraint")
    {
        ArgumentNullException.ThrowIfNull(bins);
        ArgumentNullException.ThrowIfNull(assignment);
        _bins = bins;
        _assignment = assignment;
        _name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name => _name;

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_assignment.Tail is not { } tail)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "尾箱变量未注册 / the tail-bin variable is not registered"));
        }

        for (int i = 0; i < _bins.Count; ++i)
        {
            if (_assignment.V(i) is not { } current)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "层分配指示未注册 / the assignment indicator is not registered at " + i));
            }

            string name = _name + "_" + i;
            Try row;
            if (i == _bins.Count - 1)
            {
                // **最后一箱：`v[last] <= tail[last]`。**
                // **The last bin: `v[last] <= tail[last]`.**
                row = model.AddConstraint(
                    new LinearInequality<Flt64>(
                        SymbolOf(current), SymbolOf(tail[i]), Comparison.LE, name),
                    group: null,
                    name: name);
            }
            else
            {
                // **其余：`tail[i] <= v[i + 1]`。**
                // **The rest: `tail[i] <= v[i + 1]`.**
                if (_assignment.V(i + 1) is not { } next)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "层分配指示未注册 / the assignment indicator is not registered at " + (i + 1)));
                }
                row = model.AddConstraint(
                    new LinearInequality<Flt64>(
                        SymbolOf(tail[i]), SymbolOf(next), Comparison.LE, name),
                    group: null,
                    name: name);
            }
            if (!row.IsOk)
            {
                return row;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 单位系数多项式 / A unit-coefficient polynomial over one symbol.
    /// </summary>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <returns>多项式 / The polynomial.</returns>
    private static LinearPolynomial<Flt64> SymbolOf(ILinearIntermediateSymbol<Flt64> symbol) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);

    /// <summary>
    /// 单位系数多项式（组合变量项）/ A unit-coefficient polynomial over one combination item.
    /// </summary>
    /// <param name="item">变量项 / The variable item.</param>
    /// <returns>多项式 / The polynomial.</returns>
    private static LinearPolynomial<Flt64> SymbolOf(IVariableItem item) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, item) }, Flt64.Zero);
}
