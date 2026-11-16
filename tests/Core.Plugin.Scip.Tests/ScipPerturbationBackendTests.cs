#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>
/// **SCIP 的扰动入口给的是原生 CP 求解器** / SCIP's perturbation entry hands back its native CP solver.
/// </summary>
/// <remarks>
/// **这里补的是基线 `scipPerturbationBackend` 的对应物。**
///
/// **它与 Gurobi 那一侧<b>有意不同**：**Gurobi 没有自己的 CP 求解器（走 `MipBacked` 套线性后端），
/// 而 SCIP 有一个原生的 `ScipConstraintProgrammingSolver`。** **两个工厂因此不是同一句话——
/// 这与那条"同一句话"判据被推翻的理由相同：<b>能力不同，实现就不该假装相同**。
///
/// **The counterpart of `scipPerturbationBackend`.** **It deliberately differs from the Gurobi side: Gurobi has no CP solver of
/// its own (it goes through `MipBacked` over a linear backend) while SCIP has a native `ScipConstraintProgrammingSolver`.** **So the
/// two factories are not the same sentence — the same reason the earlier "same sentence" criterion was refuted: different capabilities
/// must not be made to look alike.**
/// </remarks>
public class ScipPerturbationBackendTests
{
    /// <summary>**给的是 SCIP 原生的 CP 求解器** / It hands back SCIP's native CP solver.</summary>
    /// <remarks>
    /// **断言具体类型而不是"实现了接口"：** **如果哪天有人把它换成 `MipBacked`，
    /// 那意味着 SCIP 走回了那条通用路径——** **这不是不行，而是<b>必须有人明确决定并改判据**，
    /// 而不是悄悄发生。**
    ///
    /// **Asserting the concrete type rather than "implements the interface": if someone swaps in `MipBacked`, SCIP would fall back
    /// to the generic path — **not forbidden, but it must be a deliberate decision that changes this criterion, not something that
    /// happens quietly.**
    /// </remarks>
    [Fact]
    public void TheFactory_ShouldReturnTheNativeCpSolver() {
        IConstraintProgrammingSolver solver = ScipPerturbationBackend.Create();

        solver.Should().BeOfType<ScipConstraintProgrammingSolver>(
            "SCIP has a native CP solver; routing it through the generic path would lose that");
        solver.Descriptor.SolverId.Should().NotBeNullOrWhiteSpace(
            "the pipeline derives the capability matrix from this descriptor, so it has to identify the backend");
    }

    /// <summary>**能力标志必须与实现一致** / The capability flag must agree with the implementation.</summary>
    [Fact]
    public void TheCapabilityFlag_ShouldBeTrue() {
        ScipPerturbationBackend.SuppliesPerturbationBackend.Should().BeTrue(
            "the factory hands back a solver that the pipeline can actually perturb with");
    }

    /// <summary>**null 策略不是错误** / A null policy is not an error.</summary>
    [Fact]
    public void TheFactory_ShouldAcceptTheDefaultPolicy() {
        Action act = () => _ = ScipPerturbationBackend.Create(policy: null);
        act.Should().NotThrow("the default lowering policy is the normal case, not a special one");
    }
}