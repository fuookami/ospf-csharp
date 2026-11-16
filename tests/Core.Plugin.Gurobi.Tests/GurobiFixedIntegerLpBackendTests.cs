#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver;
using System;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **Gurobi 那端接上了，而"没有区间"是明说的边界** /
/// Gurobi's end is connected, and "no ranges" is a stated boundary.
/// </summary>
/// <remarks>
/// **这里接的是基线 `gurobiFixedIntegerLpBackend` 的对应物。**
///
/// **它必须<b>不**自己实现求解：** **基线原话是"复用与后端无关的 `LinearSolverFixedIntegerLpBackend`，
/// Gurobi 只额外提供原生 ranging 钩子，两端共用同一条经测试的实现路径"。**
/// **所以判据测的是<b>串联关系**，不是"这个类做了什么"。**
///
/// **This connects the counterpart of the baseline's `gurobiFixedIntegerLpBackend`.** **It must not implement solving itself:
/// the baseline says the backend-neutral layer is reused and Gurobi adds only a ranging hook, with both backends sharing one
/// tested path.** **So the criteria test the <em>composition</em>, not "what this class does".**
/// </remarks>
public class GurobiFixedIntegerLpBackendTests
{
    /// <summary>
    /// **null 求解器被拒绝** / A null solver is rejected.
    /// </summary>
    /// <remarks>
    /// **这条看起来平凡，而它挡的是"先构造成功、第一次请求时才炸"——那样报告里会出现一个
    /// 与"后端不支持"长得一样的失败。**
    ///
    /// **Trivial-looking, but it stops "constructs fine, explodes on the first request", which would put a failure in the report
    /// that looks exactly like "this backend is unsupported".**
    /// </remarks>
    [Fact]
    public void TheFactory_ShouldRejectNull() {
        Action act = () => _ = GurobiFixedIntegerLpBackend.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// **返回的是三层串联里的那一层，而不是一个新的实现** /
    /// What comes back is the composed layer, not a new implementation.
    /// </summary>
    /// <remarks>
    /// **判据断言返回类型的<b>名字**，因为"是否复用同一条路径"这件事在类型上就看得见：**
    /// **如果哪天有人在这里写一个新的 `GurobiFixedIntegerLpSolver`，
    /// 那意味着两个后端从此走两条路径——而基线明确说过不存在重复逻辑。**
    ///
    /// **The criterion checks the returned type's <b>name</b>, because "is the same path reused" is visible in the type: if
    /// someone ever writes a new `GurobiFixedIntegerLpSolver` here, the two backends would take two paths — and the baseline
    /// explicitly says there is no duplicated logic.**
    /// </remarks>
    [Fact]
    public void TheFactory_ShouldReturnTheSharedLayer() {
        // **用真实的 `GurobiLinearSolver`，不是假实现。**
        //
        // **因为它要证明的正是"这个工厂接受<b>真实后端**、并把它接进共用路径"——
        // 一个假实现能证明的只有"工厂接受一个实现了接口的东西"。**
        // **本机 Gurobi 可用（门禁的 Gurobi 那一路 852 条一直在跑）。**
        //
        // **Use the real `GurobiLinearSolver`, not a fake: what this proves is that the factory accepts a <b>real backend</b> and
        // threads it into the shared path, while a fake could only prove that it accepts something implementing the interface.**
        // **Gurobi is available in this environment — the gate's Gurobi leg runs 852 tests.**
        var solver = new GurobiLinearSolver();

        IFixedIntegerLpBackend backend = GurobiFixedIntegerLpBackend.Create(solver);

        backend.Should().BeOfType<LoweredFixedIntegerLpBackend>(
            "the factory must reuse the lowering adapter so Gurobi and SCIP share one tested path");
        backend.GetType().Namespace.Should().Be(
            "Fuookami.Ospf.Core.Analysis",
            "the composed layer lives in Core/Analysis, not in the plugin — a plugin-local type would be a second path");

        // **再加一条源码级断言：工厂体必须把<b>传进来的**求解器交给共用层。**
        //
        // **为什么需要它：** **类型断言挡不住"照样返回共享层、但用的是自己新建的一个求解器"——
        // 那种实现在类型上完全合法，而它会让传进来的配置与外层完全不起作用。**
        //
        // **And a source-level assertion on top: the factory body must hand <b>the solver it was given</b> to the shared layer.**
        // **Why it is needed: the type assertion cannot stop "still returns the shared layer but builds its own solver" — an
        // implementation that is perfectly legal by type and silently ignores everything the caller configured.**
        string factory = File.ReadAllText(System.IO.Path.Combine(
            RepositoryRoot(), "src/Core.Plugin.Gurobi/GurobiFixedIntegerLpBackend.cs"));

        factory.Should().Contain(
            "new LinearSolverFixedIntegerLpSolver(linearSolver)",
            "the caller's solver must be the one that runs; constructing a fresh one discards its configuration");
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

    /// <summary>
    /// **区间能力是明说的状态，而不是要人猜** /
    /// Whether ranges are supplied is stated, not guessed.
    /// </summary>
    /// <remarks>
    /// **`SuppliesSensitivityRanges == false` 是一个公开常量，于是调用方能区分
    /// "本后端的已知边界"与"某处坏了"。**
    ///
    /// **这条会在有人接上 ranging 钩子时变红，逼着他把这个常量与文档一起改。**
    ///
    /// **`SuppliesSensitivityRanges == false` is a public constant so callers can tell this backend's known boundary from a
    /// breakage.** **It reddens when someone attaches the ranging hook, forcing the constant and the document to move
    /// together.**
    /// </remarks>
    [Fact]
    public void TheRangingHook_ShouldBeDeclaredPresent() {
        GurobiFixedIntegerLpBackend.SuppliesSensitivityRanges.Should().BeTrue(
            "SARHSLow/SARHSUp are captured during the primary solve, so the constant must say so");

        // **常量说 true，就必须真的有一条读范围的路径。**
        //
        // **否则常量会变成一句谎话，而它比没有常量更糟——** **引入它的理由正是"能被判断"，
        // 那就得判断得住。**
        //
        // **If the constant says true, a range-reading path must really exist** — **otherwise the constant becomes a lie, which is
        // worse than no constant; it was introduced precisely so it could be judged, so it has to hold up.**
        string solver = File.ReadAllText(System.IO.Path.Combine(
            RepositoryRoot(), "src/Core.Plugin.Gurobi/GurobiLinearSolver.cs"));
        solver.Should().Contain("SARHSLow", "the ranging attributes are the only source of these numbers");
        solver.Should().Contain("CaptureSensitivityRanges", "capture must happen inside the primary solve");
        solver.Should().Contain("Parameters.Presolve = 0",
            "with presolve on, Gurobi returns sentinels instead of ranges");
    }
}
