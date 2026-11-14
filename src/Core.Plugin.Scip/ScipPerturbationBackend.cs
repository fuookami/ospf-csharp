#nullable enable

using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 侧的扰动后端工厂 / The SCIP-side perturbation backend factory.
/// </summary>
/// <remarks>
/// **这是基线 `ScipCriticalConstraintAnalysis.kt` 里 `scipPerturbationBackend` 的对应物。**
///
/// **基线的工厂体是 `ConstraintProgrammingPerturbationBackend(ScipConstraintProgrammingSolver(...))`；
/// 本仓的流水线直接接收 CP 求解器，所以这里只剩"给一个配置好的求解器"。**
///
/// **与 Gurobi 那一侧的区别值得说明：** **Gurobi 没有自己的 CP 求解器（走 `MipBacked` 套线性后端），
/// 而 SCIP 有一个原生的 `ScipConstraintProgrammingSolver`。** **两个工厂因此不是同一句话——
/// 这一点与 B1281 那个"同一句话"判据被推翻的理由相同：&lt;b&gt;能力不同，实现就不该假装相同**。
///
/// **The baseline's factory body is `ConstraintProgrammingPerturbationBackend(ScipConstraintProgrammingSolver(...))`; this
/// repo's pipeline accepts the CP solver directly, so all that is left is handing back a configured one.** **The difference from
/// Gurobi is worth stating: Gurobi has no CP solver of its own (it goes through `MipBacked` over the linear backend) while SCIP
/// has a native `ScipConstraintProgrammingSolver`.** **So the two factories are not the same sentence — the same reason B1281's
/// "same sentence" criterion was refuted: &lt;b&gt;different capabilities must not be made to look alike&lt;/b&gt;.**
/// </remarks>
public static class ScipPerturbationBackend
{
    /// <summary>构造一个可交给扰动流水线的 SCIP CP 求解器 / A SCIP CP solver ready for the perturbation pipeline.</summary>
    /// <param name="policy">降阶策略 / The lowering policy.</param>
    /// <returns>CP 求解器 / A CP solver.</returns>
    public static IConstraintProgrammingSolver Create(ConstraintProgrammingLoweringPolicy? policy = null) =>
        new ScipConstraintProgrammingSolver(policy);

    /// <summary>**本工厂是否提供扰动后端** / Whether this factory supplies a perturbation backend.</summary>
    public const bool SuppliesPerturbationBackend = true;
}