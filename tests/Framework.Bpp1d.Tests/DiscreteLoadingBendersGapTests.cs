using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Application;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests;

/// <summary>
/// 装载服务里**真实**的 Benders 路径 / The real Benders path inside the loading service.
/// </summary>
/// <remarks>
/// 这里本来要记"框架层 Benders 没移植"，**查下来结论相反**，所以判据改成钉"它其实在"：
///
/// ```csharp
/// private readonly IBendersDecompositionSolver? _bendersSolver;
///...
/// return await new LinearBendersAlgorithm(_bendersSolver).SolveAsync(models, config.Benders);
/// ```
///
/// 同一批还查清一件更要紧的事：demo2 里那份 **只有测试在调**
/// （`git grep "BendersSolver.solve" 12fdb587 -- '*.kt'` 的三个调用点全在 `src/test/` 下），
/// 生产路径是这里的 `DiscreteLoadingApplicationService` + `bendersSolver`。
/// **也就是说，本仓 demo2 在 `FullLoadApplication` 里手搓的那套主子模型，不是生产路径。**
///
/// set out to record "the framework Benders path is not implemented" and **found the opposite**, so the criteria pin that it exists (snippet above). The same
/// batch settled something more important: its demo2 **is called only from tests** (all three call sites live under
/// `src/test/`), and production goes through `DiscreteLoadingApplicationService` plus `bendersSolver`. **So the master/sub models demo2 hand-rolls in
/// `FullLoadApplication` are not its production path.**
/// </remarks>
public sealed class DiscreteLoadingBendersGapTests
{
    /// <summary>
    /// **服务确实收 Benders 后端，而且存下来** / The service really takes a Benders backend, and stores it.
    /// </summary>
    [Fact]
    public void TheLoadingService_ShouldAcceptABendersBackend() {
        ConstructorInfo? constructor = typeof(DiscreteLoadingApplicationService<Flt64>)
            .GetConstructors()
            .FirstOrDefault(candidate => candidate.GetParameters()
                .Any(parameter => parameter.ParameterType.Name.Contains("IBendersDecompositionSolver", StringComparison.Ordinal)));

        constructor.Should().NotBeNull("the baseline's `bendersSolver` parameter exists here too");
        typeof(DiscreteLoadingApplicationService<Flt64>)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().Contain(field => field.Name.Contains("bendersSolver", StringComparison.Ordinal),
                "the backend must be stored, not merely accepted");
    }

    /// <summary>
    /// **它把后端交给了共享算法** / It hands the backend to the shared algorithm.
    /// </summary>
    /// <remarks>
    /// 反射看不到方法体，所以读源码：这一段必须是 `new LinearBendersAlgorithm(_bendersSolver)`。
    /// **如果哪天有人把它改成"接了参数但不用"**（本仓记录过的那种空壳缺陷），这条判据会红。
    ///
    /// Reflection cannot see a method body, so the source is read: this line must be `new LinearBendersAlgorithm(_bendersSolver)`. **If someone ever turns it back
    /// into "accept the parameter and ignore it"** — the empty-shell defect this implementation has recorded — this criterion reds.
    /// </remarks>
    [Fact]
    public void TheService_ShouldDriveTheSharedBendersAlgorithm() {
        string source = ReadServiceSource();

        source.Should().Contain("new LinearBendersAlgorithm(_bendersSolver)",
            "accepting a backend without driving the algorithm is exactly the empty-shell defect");
        source.Should().Contain("LinearBendersPartition.BuildAsync",
            "the master/sub models must come from the partition builder, not from hand-assembly");
    }

    /// <summary>
    /// 从仓根读装载服务源码 / Read the loading service source from the repository root.
    /// </summary>
    /// <remarks>
    /// 测试进程的工作目录是仓库根（与文档判据同一套做法），先用它，再退到 `AppContext.BaseDirectory` 往上找。
    ///
    /// The working directory is the repository root for these runs (same practice as the doc criteria), with a fall back to walking up from `AppContext.BaseDirectory`.
    /// </remarks>
    private static string ReadServiceSource() {
        string relative = Path.Combine(
            "src", "Framework.Bpp1d", "Application", "DiscreteLoadingApplicationService.cs");

        if (File.Exists(relative)) {
            return File.ReadAllText(relative);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate the loading service source: " + relative);
    }
}
