#nullable enable

using System;
using System.IO;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Report;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>
/// B858：COPT 的两份声明，以及它们之间**不该对称**的那部分 / COPT's two declarations, and the part that must not be symmetric.
/// </summary>
/// <remarks>
/// 线性那份声明 `Dual` / `Farkas` / `NativeIis` 全为真，都有代码出处：
///
/// | 声明 | 出处 |
/// | --- | --- |
/// | `Dual` / `Farkas` | `CoptNativeSolver.CaptureDuals` 读 `"Dual"` / `"DualFarkas"` 属性 |
/// | `NativeIis` | `CoptNativeIis` + `CoptInfeasibilityAnalyzer`（本仓唯一一个原生 IIS 实现） |
/// | `WarmStart` / `SolutionPool` / `Interrupt` | 常规路径 |
///
/// **二次那份没有这三项，而且必须没有**：`SolveQuadratic` 调 `Run` 时**不传 `dualCapture`**，
/// 所以二次路径根本没有对偶抽取。两份声明"看起来应该一样"是这里最容易犯的错——
/// 抄过去不会让任何测试变红，只会让报告对二次问题**多声称两项能力**。
///
/// The linear one declares `Dual` / `Farkas` / `NativeIis`, each with a code origin (table above). **The quadratic one omits those three, and must omit them**:
/// `SolveQuadratic` calls `Run` **without a `dualCapture`**, so that path has no dual extraction at all. "The two declarations ought to look alike" is the easiest
/// mistake here — copying them across reds no test and merely makes the report **claim two extra capabilities** for quadratic problems.
/// </remarks>
public sealed class CoptCapabilityDeclarationTests
{
    /// <summary>线性那份必须声明对偶、Farkas 与原生 IIS / The linear one must declare duals, Farkas and native IIS.</summary>
    [Fact]
    public void LinearCapabilities_ShouldDeclareDualFarkasAndNativeIis() {
        SolverCapabilities capabilities = CoptReportSupport.LinearCapabilities;

        capabilities.Dual.Should().BeTrue("CaptureDuals reads the \"Dual\" attribute");
        capabilities.Farkas.Should().BeTrue("CaptureDuals reads the \"DualFarkas\" attribute");
        capabilities.NativeIis.Should().BeTrue("CoptInfeasibilityAnalyzer is a real native IIS implementation");
    }

    /// <summary>
    /// **二次那份不许声明对偶、Farkas 与原生 IIS** / The quadratic one must not declare duals, Farkas or native IIS.
    /// </summary>
    [Fact]
    public void QuadraticCapabilities_ShouldNotDeclareDualExtraction() {
        SolverCapabilities capabilities = CoptReportSupport.QuadraticCapabilities;

        capabilities.Dual.Should().BeFalse("SolveQuadratic passes no dualCapture, so the quadratic path extracts nothing");
        capabilities.Farkas.Should().BeFalse("the same call carries no Farkas capture either");
        capabilities.NativeIis.Should().BeFalse("the IIS analyzer works on linear snapshots only");
    }

    /// <summary>
    /// **声明的证据还在这两个文件里** / The evidence for these claims still lives in these two files.
    /// </summary>
    /// <remarks>
    /// 与 B853 的"行为判据必须仍在调用入口"同一个思路：**声明还在，而验证它的判据被删了**，是最难发现的一类空壳。
    ///
    /// The same idea as B853's "the behavioural criteria must still call the entries": **the claim survives while the criterion that checks it is deleted** is the hardest
    /// kind of empty shell to notice.
    /// </remarks>
    [Fact]
    public void TheEvidenceCriteria_ShouldStillExist() {
        string directory = TestDirectory();

        File.Exists(Path.Combine(directory, "CoptDualIntegrationTest.cs")).Should()
            .BeTrue("the dual declaration needs its integration criterion");
        File.Exists(Path.Combine(directory, "CoptIisIntegrationTest.cs")).Should()
            .BeTrue("the native IIS declaration needs its integration criterion");
    }

    /// <summary>找到本测试目录 / Locate this test directory.</summary>
    /// <returns>目录路径 / The directory path.</returns>
    private static string TestDirectory() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = Path.Combine(directory.FullName, "tests", "Core.Plugin.Copt.Tests");
            if (Directory.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the Copt test directory");
    }
}
