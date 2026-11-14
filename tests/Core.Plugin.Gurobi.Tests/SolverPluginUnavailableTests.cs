// 求解器插件不可用时的行为判据 / Criteria for solver-plugin behaviour when the SDK is absent.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Test.Core.Plugin;

/// <summary>
/// **无 SDK 时插件必须显式失败，不能假装成功** / Without the SDK a plugin must fail explicitly, never pretend success.
/// </summary>
/// <remarks>
/// **B395 复核 P9 验收项**：「代表性真实求解闭环」仍未达成。本批查清了**为什么**：
///
/// | 项 | 实测结论 |
/// | --- | --- |
/// | `IColumnGenerationSolver` 的实现 | 全在 `Core.Plugin.{Copt,Cplex,Gurobi,Hexaly,…}` |
/// | 当前环境有 Gurobi SDK 吗 | **没有**（`bin` 里无 `Gurobi*.dll`） |
/// | `GurobiAvailable` 判定 | `Exists('$(GurobiManagedAssembly)')` ⇒ 假 |
/// | 于是编译进来的 | **`GurobiUnavailable.cs`**（其余 Gurobi 源文件被 `Compile Remove`） |
///
/// **所以"真实求解闭环"在当前环境下不可达**——不是代码缺口，是**没有许可**。
/// 这条判据钉住的是**次优但正确**的行为：无 SDK 时**显式失败**，
/// 而不是返回一个空解让调用方以为求解成功了。
///
/// **B395 re-checked the P9 acceptance item** "representative real solve loop", still unmet. This batch established **why** (table above). **The real solve loop is
/// unreachable in this environment** — not a code gap but **the absence of a licence**. This criterion pins the **second-best but correct** behaviour: **fail explicitly**
/// when the SDK is missing, rather than returning an empty solution that looks like a successful solve.
/// </remarks>
public class SolverPluginUnavailableTests {
    /// <summary>**无 SDK 时线性求解必须返回失败** / A linear solve must fail without the SDK.</summary>
    [Fact]
    public async Task GurobiLinearSolver_ShouldFailExplicitlyWithoutTheSdk() {
        var solver = new GurobiLinearSolver();

        // 不可用实现**不读 `model`**，所以传 null 是安全的——这本身也说明它没有真的去求解。
        // The unavailable implementation **never reads `model`**, so null is safe — which itself shows it never really solves.
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)null!);

        result.IsFailed.Should().BeTrue(
            "without the SDK the solver must report failure; a fabricated empty solution would look like a successful solve");
    }

    /// <summary>**求解器名仍是 `gurobi`**——反向对照 / The name is still `gurobi` — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，一个**改名成别的**的占位实现也能满足上一条。
    ///
    /// Without this, a placeholder **renamed to something else** would also satisfy the previous criterion.
    /// </remarks>
    [Fact]
    public void GurobiLinearSolver_ShouldKeepItsName() {
        var solver = new GurobiLinearSolver();

        solver.Name.Should().Be("gurobi");
    }
}
