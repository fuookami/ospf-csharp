#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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
/// 箱装载顺序约束 / The bin loading-order constraint.
/// </summary>
/// <remarks>
/// 与基线 `layer_assignment/service/limits/BinLoadingOrderConstraint.kt` 对齐：**相邻两个同型箱之间**加两条单调约束。
///
/// 五点必须照抄：
///
/// 1. **从 `i = 1` 开始**——`i = 0` 没有前一个箱可比。
/// 2. **只在相邻两箱形状相同时才加**（基线 `bins[i].shape == bins[i - 1].shape`）。不同型的箱之间没有先后可言，
///    硬加会让解凭空受限。
/// 3. **两条约束**：`v[i-1] &gt;= v[i]`（用到第 i 个箱就必须用到第 i-1 个）与 `volume[i-1] &gt;= volume[i]`。
/// 4. **都是 `Comparison.GE`**，方向写反会让"后面的箱先用"成为可能。
/// 5. 命名：`{name}_{i-1}_{i}` 与 `{name}_volume_{i-1}_{i}`。
///
/// Aligned with the baseline: two monotonicity rows between **adjacent same-shape bins**. Five things must be copied: **the loop starts at `i = 1`**, since bin 0 has no predecessor;
/// **rows are added only when adjacent shapes match** (the baseline's `bins[i].shape == bins[i - 1].shape`), because different-shaped bins have no ordering and forcing one would
/// restrict the solution for no reason; **there are two rows**, `v[i-1] &gt;= v[i]` (using bin i requires using bin i-1) and `volume[i-1] &gt;= volume[i]`; **both are `Comparison.GE`**, and
/// reversing them would let later bins be used first; and the names are `{name}_{i-1}_{i}` and `{name}_volume_{i-1}_{i}`.
/// </remarks>
public sealed class BinLoadingOrderConstraint
{
    private readonly IReadOnlyList<Bin<BinLayer, FltX>> _bins;
    private readonly Model.PreciseAssignment _assignment;
    private readonly IReadOnlyList<ILinearIntermediateSymbol<Flt64>> _loadVolume;
    private readonly string _name;

    /// <summary>
    /// 构造箱装载顺序约束 / Construct the bin loading-order constraint.
    /// </summary>
    /// <param name="bins">箱 / The bins.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="loadVolume">逐箱装载体积符号 / The per-bin load-volume symbols.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public BinLoadingOrderConstraint(
        IReadOnlyList<Bin<BinLayer, FltX>> bins,
        Model.PreciseAssignment assignment,
        IReadOnlyList<ILinearIntermediateSymbol<Flt64>> loadVolume,
        string name = "bin_loading_order_constraint")
    {
        ArgumentNullException.ThrowIfNull(bins);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(loadVolume);
        _bins = bins;
        _assignment = assignment;
        _loadVolume = loadVolume;
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

        for (int i = 0; i < _bins.Count; ++i)
        {
            // **`i = 0` 没有前一个箱。**
            // **`i = 0` has no predecessor.**
            if (i == 0)
            {
                continue;
            }
            // **只在相邻同型时才加。**
            // **Only between same-shape neighbours.**
            if (_bins[i].Type != _bins[i - 1].Type)
            {
                continue;
            }
            if (_assignment.V(i - 1) is not { } previous || _assignment.V(i) is not { } current)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "层分配指示未注册 / the assignment indicators are not registered at " + (i - 1) + "," + i));
            }

            // **第一条：用到第 i 个箱就必须用到第 i-1 个。**
            // **Row one: using bin i requires using bin i-1.**
            string assignName = _name + "_" + (i - 1) + "_" + i;
            Try assign = model.AddConstraint(
                new LinearInequality<Flt64>(
                    SymbolOf(previous), SymbolOf(current), Comparison.GE, assignName),
                group: null,
                name: assignName);
            if (!assign.IsOk)
            {
                return assign;
            }

            // **第二条：体积同向单调。**
            // **Row two: volume is monotone in the same direction.**
            string volumeName = _name + "_volume_" + (i - 1) + "_" + i;
            Try volume = model.AddConstraint(
                new LinearInequality<Flt64>(
                    SymbolOf(_loadVolume[i - 1]), SymbolOf(_loadVolume[i]), Comparison.GE, volumeName),
                group: null,
                name: volumeName);
            if (!volume.IsOk)
            {
                return volume;
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
}
