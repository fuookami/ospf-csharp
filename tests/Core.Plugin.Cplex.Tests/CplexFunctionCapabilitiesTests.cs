#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Function;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

/// <summary>
/// CPLEX 能力探测的契约 / The contract of CPLEX's capability probe.
/// </summary>
/// <remarks>
/// B1234 与同批的 `CoptFunctionCapabilitiesTests` 同源，但重点不同：
/// **COPT 与 CPLEX"都支持 SOS2"是对的，而"它们用同一个方法支持 SOS2"是错的。**
///
/// **CPLEX 用专用的 `AddSOS2`（返回接口 `ISOS2`），COPT 用 `AddSos` 加 `type` 参数（返回类 `Sos`）。**
/// **判据要能分开这两句话——否则"合写一个探测函数"会让其中一侧永远返回 `false`。**
///
/// B1234 shares its motive with this batch's `CoptFunctionCapabilitiesTests`, with a different focus:
/// **"COPT and CPLEX both support SOS2" is true; "they support it through the same method" is false.**
///
/// **CPLEX uses a dedicated `AddSOS2` (returning the interface `ISOS2`); COPT uses `AddSos` with a `type` argument
/// (returning the class `Sos`).** **A criterion has to separate those two sentences, or a shared probe would leave one side
/// permanently false.**
/// </remarks>
public class CplexFunctionCapabilitiesTests
{
    /// <summary>
    /// **没有 SDK 时一项都不声明，且版本保持 `null`** /
    /// With no SDK nothing is claimed, and the version stays `null`.
    /// </summary>
    [Fact]
    public void WithNoSdk_ShouldClaimNothingAndLeaveTheVersionNull() {
        FunctionSolverCapabilities capabilities = CplexFunctionCapabilities.Detect(modelType: null);

        capabilities.Solver.Should().Be("CPLEX");
        capabilities.Version.Should().BeNull("an absent SDK is not probed, not probed-and-empty");
        capabilities.Supported.Should().BeEmpty();
    }

    /// <summary>
    /// **两个后端的探测方法名不同，而这正是它们不能合并的理由** /
    /// The two backends probe different method names, which is exactly why they cannot be merged.
    /// </summary>
    /// <remarks>
    /// **这条判据钉的是<b>两个文件之间的差异</b>，而不是其中任何一个的内容。**
    ///
    /// **它防的是一种很自然的合并：** **"两个后端都有 SOS2，所以写一个 `HasSos2Creator` 共用吧。"
    /// 那样做的话，其中一个后端的探测会无条件返回 `false`——
    /// 而 `false` 在能力声明里的意思是"这个 SDK 不支持"，是一个**看起来合理**的错答案。**
    ///
    /// **"探测失败"与"不支持"在报告上是同一个词，而它们的原因完全不同。**
    ///
    /// **This pins the <b>difference between two files</b>, not the contents of either.**
    ///
    /// **It guards a natural merge: "both backends have SOS2, so share one `HasSos2Creator`".** **Doing that makes one
    /// backend's probe return `false` unconditionally — and `false` in a capability declaration means "this SDK does not
    /// support it", a **plausible-looking wrong answer**.** **"Detection failed" and "unsupported" read as the same word in
    /// a report, and their causes are entirely different.**
    /// </remarks>
    [Fact]
    public void TheTwoBackends_ShouldProbeDifferentMethodNames() {
        string root = RepositoryRoot();
        string copt = File.ReadAllText(Path.Combine(root, "src/Core.Plugin.Copt/CoptFunctionCapabilities.cs"));
        string cplex = File.ReadAllText(Path.Combine(root, "src/Core.Plugin.Cplex/CplexFunctionCapabilities.cs"));

        copt.Should().Contain("\"AddSos\"", "COPT's SOS2 creator is AddSos with a type argument");
        cplex.Should().Contain("\"AddSOS2\"", "CPLEX's SOS2 creator is the dedicated AddSOS2");
        cplex.Should().NotContain("\"AddSos\"", "CPLEX has no AddSos, and probing for it would always be false");
    }

    /// <summary>
    /// **CPLEX 只声明 SOS2，不声明 SOS1** / CPLEX declares SOS2 only, never SOS1.
    /// </summary>
    /// <remarks>
    /// **COPT 的 `addSos` 用一个 `type` 参数同时支撑 1 和 2，所以那边两条都声明。**
    /// **CPLEX 的 `AddSOS2` <b>就是</b> SOS2——它没有一条能创建 SOS1 的专用方法被探到。**
    ///
    /// **所以这里的 `Sos1` 不是"还没测"，而是"没有证据"——而这两件事在能力声明里必须都读作"不声明"。**
    ///
    /// **COPT's `addSos` backs 1 and 2 through one `type` argument, so that side declares both.** **CPLEX's `AddSOS2`
    /// <b>is</b> SOS2 — no dedicated SOS1 creator is probed.**
    ///
    /// **So `Sos1` here is not "not yet measured" but "no evidence" — and both must read as "not declared" in a capability
    /// declaration.**
    /// </remarks>
    [Fact]
    public void TheCplexProbe_ShouldNotDeclareSos1() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Cplex/CplexFunctionCapabilities.cs");
        string text = File.ReadAllText(path);

        text.Should().Contain("Sos2");
        text.Should().NotContain("FunctionNativeCapability.Sos1",
            "AddSOS2 creates SOS2 only; declaring SOS1 would be evidence we do not have");
    }

    /// <summary>
    /// **仓库根目录从测试运行目录往上找** / Walk up to the repository root.
    /// </summary>
    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
