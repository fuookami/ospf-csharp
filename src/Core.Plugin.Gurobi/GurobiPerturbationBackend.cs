#nullable enable

using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 侧的扰动后端工厂 / The Gurobi-side perturbation backend factory.
/// </summary>
/// <remarks>
/// **这是基线 `GurobiCriticalConstraintAnalysis.kt` 里 `gurobiPerturbationBackend` 的对应物。**
///
/// ### 它&lt;b&gt;不**另写一个"扰动后端"类，这是与基线的一处有意分歧&lt;/b&gt;
///
/// **基线里有一个 `ConstraintProgrammingPerturbationBackend` 类，
/// 职责是"拿扰动后的快照建模型、开 session、求解、回结果"；而两个插件各写一个 `…PerturbationBackend()`
/// 工厂去包它。**
///
/// **本仓的 `ConstraintPerturbationPipeline.AnalyzeAsync` &lt;b&gt;已经直接接收 `IConstraintProgrammingSolver`**，
/// 并把上面那件事做完了**——**所以再搬一个同样的类进来，只会得到两条做同一件事的路径。**
///
/// **这个工厂因此只做一件事：** **给出一个&lt;b&gt;配置好的** CP 求解器，
/// 而"怎么扰动"这件事仍然只有流水线一份实现。**
///
/// **The baseline has a `ConstraintProgrammingPerturbationBackend` class that builds the perturbed model, opens a session and
/// solves, with each plugin adding a `…PerturbationBackend()` factory around it.** **This repo's
/// `ConstraintPerturbationPipeline.AnalyzeAsync` already accepts an `IConstraintProgrammingSolver` and does exactly that — so
/// porting the class too would leave two paths doing one job.** **This factory therefore does one thing: hand back a
/// &lt;b&gt;configured&lt;/b&gt; CP solver, leaving how to perturb to the pipeline's single implementation.**
/// </remarks>
public static class GurobiPerturbationBackend
{
    /// <summary>构造一个可交给扰动流水线的 Gurobi CP 求解器 / A Gurobi CP solver ready for the perturbation pipeline.</summary>
    /// <param name="policy">降阶策略 / The lowering policy.</param>
    /// <returns>CP 求解器 / A CP solver.</returns>
    /// <remarks>
    /// **Gurobi 没有自己的 CP 求解器实现：** **它走的是与后端无关的 `MipBackedConstraintProgrammingSolver`
    /// 套 `GurobiLinearSolver`**——**这与 B1261 建立的那条路径是同一条。**
    ///
    /// **Gurobi has no CP solver of its own: it uses the backend-neutral `MipBackedConstraintProgrammingSolver` over
    /// `GurobiLinearSolver`, the same path B1261 established.**
    /// </remarks>
    public static IConstraintProgrammingSolver Create(ConstraintProgrammingLoweringPolicy? policy = null) =>
        new MipBackedConstraintProgrammingSolver(new GurobiLinearSolver(), policy);

    /// <summary>
    /// **本工厂是否提供扰动后端** / Whether this factory supplies a perturbation backend.
    /// </summary>
    /// <remarks>
    /// **恒为 `true`，而它是一个&lt;b&gt;判据的锚点**：** **B1282 之前它是 `false`，
    /// 那时 SCIP 与 Gurobi 都还没有这条路径。** **写成常量，是为了让"有没有"能被判断，而不是只能被读。**
    ///
    /// **Always `true`, and it is an anchor for a &lt;b&gt;criterion&lt;/b&gt;: it was `false` before B1282/B1283, when neither plugin had this
    /// path.** **It is a constant so that "does it exist" can be judged rather than merely read.**
    /// </remarks>
    public const bool SuppliesPerturbationBackend = true;
}