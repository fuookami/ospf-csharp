#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>
/// **SCIP 与 Gurobi 走同一条路径，而不是各写一遍** /
/// SCIP and Gurobi take one path rather than each writing their own.
/// </summary>
/// <remarks>
/// **基线里这两个后端各有一个自己的 `…FixedIntegerLpBackend` 类，
/// 把"降阶 → dump → 求解 → 取对偶"写了两遍。**
///
/// **本仓不照抄那份重复：** **这条链已经抽成了一层与后端无关的实现，
/// 于是两个插件工厂现在<b>是同一句话，只换了求解器**。
///
/// **而"是同一句话"这件事本身要能被判据看见——否则下一个人照基线把 SCIP 那份重复抄回来，
/// 一切测试照样绿，而两个后端从此可能悄悄分叉。**
///
/// **The baseline has a separate `…FixedIntegerLpBackend` class in each plugin, writing the "lower → dump → solve → duals" chain
/// twice.** **This repo does not copy that: the chain is extracted into a backend-neutral layer, so the two plugin factories
/// are now the same sentence with a different solver.** **And "the same sentence" must be visible to a criterion — otherwise the
/// next person can copy the baseline's SCIP duplication back, every test stays green, and the two backends may quietly diverge.**
/// </remarks>
public class ScipFixedIntegerLpBackendTests
{
    /// <summary>**null 求解器被拒绝** / A null solver is rejected.</summary>
    [Fact]
    public void TheFactory_ShouldRejectNull() {
        Action act = () => _ = ScipFixedIntegerLpBackend.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>**返回的是共享层，且用的是传进来的求解器** / The shared layer comes back, using the solver it was given.</summary>
    /// <remarks>
    /// **这条与 Gurobi 那批的判据同形，理由也同形：**
    /// **只断言类型挡不住"照样返回共享层、但自己新建一个求解器"——那种实现在类型上合法，
    /// 而传进来的配置会完全不起作用。**
    ///
    /// **Same shape and same reason as the Gurobi batch: the type alone cannot stop "returns the shared layer but builds its own
    /// solver", which is legal by type and silently discards the caller's configuration.**
    /// </remarks>
    [Fact]
    public void TheFactory_ShouldReturnTheSharedLayer() {
        var solver = new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver();

        IFixedIntegerLpBackend backend = ScipFixedIntegerLpBackend.Create(solver);

        backend.Should().BeOfType<LoweredFixedIntegerLpBackend>(
            "the factory must reuse the lowering adapter so both plugins share one tested path");
        backend.GetType().Namespace.Should().Be(
            "Fuookami.Ospf.Core.Analysis",
            "a plugin-local backend type would be the baseline's duplication coming back");

        string factory = File.ReadAllText(System.IO.Path.Combine(
            RepositoryRoot(), "src/Core.Plugin.Scip/ScipFixedIntegerLpBackend.cs"));
        factory.Should().Contain(
            "new LinearSolverFixedIntegerLpSolver(linearSolver)",
            "the caller's solver must be the one that runs");
    }

    /// <summary>
    /// **两个插件都必须把传进来的求解器交给共享层，谁都不许自己实现求解** /
    /// Both plugins must hand their solver to the shared layer; neither may implement solving itself.
    /// </summary>
    /// <remarks>
    /// **这条判据原本比的是"两份 `Create` 方法体逐字相同"。** 把它推翻了，而推翻它是对的：**
    /// **Gurobi 那一端现在要在主求解里顺手读 `SARHSLow`／`SARHSUp`，而 SCIP 这端没有 ranging 能力——
    /// 两者不可能再是同一句话。**
    ///
    /// **所以判据改成断言那条<b>真正重要**的不变式：两个工厂都必须"把<b>传进来的**求解器包进共享层"，
    /// 谁都不许另写一份求解。** **这条在"同一句话"成立时成立，在它不成立时仍然成立——
    /// 而它挡的正是基线的重复：`ScipCriticalConstraintAnalysis.kt` 里那个自己重写一遍
    /// "降阶 → dump → 求解 → 取对偶"的类。**
    ///
    /// **An earlier criterion compared the two `Create` bodies verbatim; that was refuted,hat, and correctly: the Gurobi end now reads
    /// `SARHSLow`/`SARHSUp` during the primary solve while SCIP has no ranging capability, so they cannot be the same
    /// sentence any more.** **The criterion now asserts the invariant that still matters: both factories wrap the solver they were
    /// given in the shared layer, and neither writes its own solving.** **That holds whether or not the bodies match, and it is what
    /// stops the baseline's duplication.**
    /// </remarks>
    [Fact]
    public void TheTwoFactories_ShouldBothReuseTheSharedLayer() {
        string root = RepositoryRoot();

        foreach (string relative in new[] {
            "src/Core.Plugin.Scip/ScipFixedIntegerLpBackend.cs",
            "src/Core.Plugin.Gurobi/GurobiFixedIntegerLpBackend.cs",
        }) {
            string body = Body(File.ReadAllText(System.IO.Path.Combine(root, relative)));

            body.Should().NotBeNullOrWhiteSpace(relative + " must have a Create body to compare");
            body.Should().Contain(
                "new LinearSolverFixedIntegerLpSolver(",
                relative + " must reuse the shared layer rather than solve on its own");
            body.Should().Contain(
                "new LoweredFixedIntegerLpBackend(",
                relative + " must lower first, so both plugins share one tested path");
        }
    }

    /// <summary>取出 `Create` 的方法体，并去掉只随插件变化的名字。</summary>
    private static string Body(string source) {
        Match match = Regex.Match(
            source,
            // **`[^}]*` 不够用：Gurobi 那个方法体里有 `if` 块，花括号成对出现。**
            // **改成"抓到下一个 4 空格缩进的 `}` 为止"。**
            //
            // **`[^}]*` is not enough: the Gurobi body contains an `if` block, so braces nest.** **Capture up to the next `}` at
            // four-space indentation instead.**
            @"public static IFixedIntegerLpBackend Create\(IAbstractLinearSolver linearSolver\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);
        if (!match.Success) {
            return string.Empty;
        }
        return Regex.Replace(match.Groups["body"].Value, @"\s+", " ").Trim();
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}