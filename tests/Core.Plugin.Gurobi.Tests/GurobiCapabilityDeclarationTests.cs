#nullable enable

using System;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// B856：能力声明**不得低于实际后端**，也不得高于 / The declaration must neither understate nor overstate the backend.
/// </summary>
/// <remarks>
/// B856 之前 Gurobi 的 `SolverCapabilities` 写着 `Dual: false, Farkas: false`，而 `GurobiBendersDecompositionSolver` 里
/// **明明在用** `Get(GRB.DoubleAttr.Pi)` 与 `Get(GRB.DoubleAttr.FarkasDual)`。这个错误不会让任何测试变红——
/// 它只是让报告里的一句能力声明**低于真实能力**，而下游据此做的选择会莫名其妙地保守。
///
/// Before B856 Gurobi's `SolverCapabilities` said `Dual: false, Farkas: false` while `GurobiBendersDecompositionSolver` **plainly uses** `Get(GRB.DoubleAttr.Pi)`
/// and `Get(GRB.DoubleAttr.FarkasDual)`. That mistake reds no test — it just makes one sentence in the report **understate the real backend**, and choices made
/// downstream from it end up oddly conservative.
/// </remarks>
public sealed class GurobiCapabilityDeclarationTests
{
    /// <summary>**对偶与 Farkas 是实测支持的，必须声明** / Duals and Farkas are measurably supported and must be declared.</summary>
    [Fact]
    public void DualAndFarkas_ShouldBeDeclared() {
        SolverCapabilities capabilities = Declared();

        capabilities.Dual.Should().BeTrue("the plugin reads dual values back from the SDK");
        capabilities.Farkas.Should().BeTrue("the plugin reads Farkas duals back from the SDK");
    }

    /// <summary>**原生 IIS 可以声明，但必须有分析器兑现它** / Native IIS may be declared, but an analyzer has to keep that promise.</summary>
    /// <remarks>
    /// **这条判据在 B980 改了方向，改的理由要写清楚。**
    ///
    /// 原来的版本断言 `NativeIis == false`，理由很具体：那时唯一的 IIS 入口 `InvokeAsync(…, IisConfig?)`
    /// **压根没读 `iisConfig`**，声明"支持原生 IIS"会让失败分析拿到一次普通求解的结果，看上去像"IIS 没找到反例"。
    ///
    /// B980 之后这个理由**不成立了**：`GurobiNativeIisInfeasibilityAnalyzer` 读 `iisConfig`、跑 `computeIIS`、
    /// 并在真实后端上验过。**所以现在要挡的不是"声明"，而是"声明了却没人兑现"**——这正好是同一条规矩的当前写法。
    ///
    /// **一个判据翻转时，最容易顺手把它 weaken 成 `BeTrue()`。** 那样它就从"防止多说"变成了"记录现状"。
    /// 这里保留原来的锋芒：**声明为真，且兑现者必须真实存在。**
    ///
    /// **This criterion reversed direction in B980, and the reason has to be written down.** The old version asserted `NativeIis == false` for a concrete reason: the only IIS
    /// entry at the time, `InvokeAsync(…, IisConfig?)`, **never read `iisConfig`**, so declaring native IIS handed the infeasibility analysis **a plain solve's result** that
    /// looked like "IIS found no counterexample". After B980 that reason **no longer holds**: `GurobiNativeIisInfeasibilityAnalyzer` reads `iisConfig`, runs `computeIIS` and was
    /// verified on the real backend. **So what has to be blocked now is not the declaration but a declaration nobody keeps** — the same rule, written for today. **When a
    /// criterion flips, the easy mistake is to weaken it into `BeTrue()`**, which turns it from "prevent overstating" into "record the present". This keeps the original edge:
    /// **declared true, and the keeper has to really exist.**
    /// </remarks>
    [Fact]
    public void NativeIis_ShouldBeDeclaredOnlyBecauseAnAnalyzerKeepsIt() {
        Declared().NativeIis.Should().BeTrue(
            "a native IIS analyzer now reads iisConfig, runs computeIIS and is verified on the real backend");

        // **兑现者必须存在**：没有这一句，上面那条只是把旧判据反过来抄了一遍。
        // **The keeper has to exist**: without this line, the assertion above is just the old criterion copied backwards.
        new GurobiLinearSolver()
            .DiagnosticAnalyzers(new Fuookami.Ospf.Core.Solver.Iis.IisConfig())
            .Select(analyzer => analyzer.Source)
            .Should().Contain(InfeasibilityEvidenceSource.NativeIis,
                "a declared capability with no analyzer behind it is exactly what the old criterion blocked");
    }

    /// <summary>**检查点与恢复没有实现，不能声明** / Checkpoint and resume are not implemented and must not be declared.</summary>
    [Fact]
    public void CheckpointAndResume_ShouldNotBeDeclared() {
        SolverCapabilities capabilities = Declared();

        capabilities.Checkpoint.Should().BeFalse("no checkpoint export exists in the Gurobi plugin");
        capabilities.Resume.Should().BeFalse("no resume path exists in the Gurobi plugin");
    }

    /// <summary>取 Gurobi 线性求解器声明的能力 / Read the capabilities the linear Gurobi solver declares.</summary>
    /// <returns>能力声明 / The capability declaration.</returns>
    private static SolverCapabilities Declared() {
        var config = new SolverConfig();
        // 直接取元组里的那一项，不留局部变量：IDE0007 与 IDE0008 在元组局部变量上**互相反对**，留一个变量就多两条警告。
        // Take the element straight out of the tuple with no local: IDE0007 and IDE0008 **contradict each other** on a tuple local, and keeping one costs two warnings.
        return GurobiReportSupport.Metadata(
            config,
            quadratic: false,
            GurobiBackendIdentity.Gurobi,
            nativeVersion: null).Provenance.Descriptor.Capabilities;
    }
}
