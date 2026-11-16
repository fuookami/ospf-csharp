#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>
/// SCIP 的能力声明与代码路径逐项对齐 / SCIP's declaration matched against its code paths, one field at a time.
/// </summary>
/// <remarks>
/// 在 Gurobi 上修了一处"声明低于实际"的错误。这里按**同样的方法**核对 SCIP 的两份声明：
/// 线性后端 `ScipLinearNativeSolver.LinearCapabilities` 与约束规划 `ScipConstraintProgrammingSolver`。
///
/// 核对结果——**没有第二处同类错误**，但两处判断的理由不同，值得写下来：
///
/// | 字段 | 依据 | 结论 |
/// | --- | --- | --- |
/// | 线性 `Dual` / `Farkas` | `ScipDualTargets` 与对偶提取模式设置（`:161`、`:239`） | 声明正确 |
/// | 线性 `WarmStart` | `SubmitInitialSolution`（`:212`） | 声明正确 |
/// | CP `WarmStart` | `ApplyHints`（`:680`）——**解提示就是热启动**，与 `SolutionHint: ExactLowering` 一致 | 声明正确 |
/// | CP `Interrupt` | CP 路径全程传 `cancellationToken`（11 处） | 声明正确 |
/// | 两者 `NativeIis` | 线性声明 `false`，CP 未声明——SCIP 这边确实没有 IIS 分析器（Copt 才有） | 声明正确 |
///
/// fixed one "the declaration is lower than the backend" bug on Gurobi. checks SCIP's two declarations **the same way**: the linear backend's
/// `LinearCapabilities` and the constraint-programming solver's. The outcome is that **there is no second instance of that bug**, but the reasons differ between the two
/// and are worth writing down (table above).
/// </remarks>
public sealed class ScipCapabilityDeclarationTests
{
    /// <summary>线性后端必须声明对偶、Farkas 与热启动 / The linear backend must declare duals, Farkas and warm start.</summary>
    [Fact]
    public void LinearBackend_ShouldDeclareDualFarkasAndWarmStart() {
        SolverCapabilities capabilities = ScipLinearNativeSolver.LinearCapabilities;

        capabilities.Dual.Should().BeTrue("the linear path sets dual extraction targets and reads them back");
        capabilities.Farkas.Should().BeTrue("the same extraction mode carries Farkas duals");
        capabilities.WarmStart.Should().BeTrue("SubmitInitialSolution submits an initial solution");
    }

    /// <summary>线性后端不声明原生 IIS / The linear backend does not declare native IIS.</summary>
    /// <remarks>
    /// Copt 有 `CoptInfeasibilityAnalyzer`，SCIP 没有——**"别的后端能做"不是"这个后端能做"**。
    ///
    /// Copt has `CoptInfeasibilityAnalyzer`; SCIP does not. **"another backend can" is not "this one can".**
    /// </remarks>
    [Fact]
    public void LinearBackend_ShouldNotDeclareNativeIis() {
        ScipLinearNativeSolver.LinearCapabilities.NativeIis.Should().BeFalse(
            "SCIP has no native IIS analyzer here; claiming one would promise a path nothing implements");
    }
}
