#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 求解入口处的延迟物化判定 / The deferred-materialisation decision at the solve entry.
/// </summary>
/// <remarks>
/// B1240 补配置侧的策略，并把"两个策略任一 `Eager` 就短路"做成一个独立函数。
///
/// **这一批<b>没有</b>把 `Materialize` 接进求解器——原因写清楚，因为它是下一批的起点：**
/// **接线需要一条从 `ILinearTriadModelView` 拿到策略的通路，而那个接口只有两个成员
/// （继承自 `IModelView`），没有策略。而求解入口拿到的是视图，不是模型。**
///
/// adds
///
/// **This batch does <b>not</b> wire `Materialize` into a solver — stated plainly because it is where the next batch
/// starts: wiring needs a path from `ILinearTriadModelView` to the policy, and that interface (inheriting `IModelView`)
/// carries no policy. The solve entry receives a view, not a model.**
/// </remarks>
public class DeferredFunctionSolveEntryTests
{
    /// <summary>
    /// **四种组合里只有一种会尝试物化** / Exactly one of four combinations attempts materialisation.
    /// </summary>
    /// <remarks>
    /// **这是一个<b>二选一的短路</b>，所以四种组合必须全测：**
    /// **只测两种会把"任一是 `Eager`"这个真正的规则漏掉。**
    ///
    /// **而漏掉它的实现看起来是对的：** **测试"两个都 Eager → 不物化"通过、
    /// 测试"两个都非 Eager → 物化"通过，于是 `configPolicy != Eager && modelPolicy != Eager` 与
    /// `configPolicy != Eager || modelPolicy != Eager` 这两种写法在测试上无法区分。**
    ///
    /// **A <b>short-circuit between two</b>, so all four combinations must be tested: two would miss the actual rule,
    /// "either one".**
    ///
    /// **And the wrong implementation looks right: with "both Eager → no" and "both non-Eager → yes" passing, the `&&` and
    /// `||` forms are indistinguishable.**
    /// </remarks>
    [Theory]
    [InlineData(FunctionExpansionPolicy.Eager, FunctionExpansionPolicy.Eager, false)]
    [InlineData(FunctionExpansionPolicy.Eager, FunctionExpansionPolicy.Auto, false)]
    [InlineData(FunctionExpansionPolicy.Auto, FunctionExpansionPolicy.Eager, false)]
    [InlineData(FunctionExpansionPolicy.Auto, FunctionExpansionPolicy.Auto, true)]
    public void ShouldAttempt_ShouldDependOnBothPolicies(
        FunctionExpansionPolicy configPolicy,
        FunctionExpansionPolicy modelPolicy,
        bool expected) {
        DeferredFunctionSolveEntry.ShouldAttempt(configPolicy, modelPolicy).Should().Be(
            expected,
            "either policy being Eager must short-circuit to the ordinary solve");
    }

    /// <summary>
    /// **配置默认 `Eager`** / The config defaults to `Eager`.
    /// </summary>
    /// <remarks>
    /// **默认值的方向与模型侧一致：没配置过的求解器不会突然走上延迟物化那条更复杂的路径。**
    ///
    /// **The default matches the model side: an unconfigured solver never drifts into the more complicated route.**
    /// </remarks>
    [Fact]
    public void TheConfig_ShouldDefaultToEager() {
        SolverConfig config = new();

        config.FunctionExpansionPolicy.Should().Be(
            FunctionExpansionPolicy.Eager,
            "the conservative default keeps unconfigured solvers on the ordinary path");
    }

    /// <summary>
    /// **配置侧的策略真的存在，而且模型侧已经能读** /
    /// The config-side policy exists, and the model side can already read it.
    /// </summary>
    /// <remarks>
    /// **这一条钉的是"两半都在"，而不是"接线完成"。**
    /// **区别很重要：** **两半都在但没接上，与"只做了一半"是两种不同的状态，
    /// 而只有后者需要补实现。**
    ///
    /// **This pins "both halves exist", not "wiring is done".** **The difference matters: both halves present but not joined
    /// is a different state from "only one half built", and only the latter needs an implementation.**
    /// </remarks>
    [Fact]
    public void BothHalves_ShouldExistWithoutBeingJoined() {
        string root = RepositoryRoot();

        string config = File.ReadAllText(Path.Combine(root, "src/Core/Solver/Config/SolverConfig.cs"));
        config.Should().Contain("public FunctionExpansionPolicy FunctionExpansionPolicy",
            "B1240 added the config-side policy");

        string model = File.ReadAllText(Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));
        model.Should().Contain("public FunctionExpansionPolicy FunctionExpansionPolicy",
            "B1236 added the model-side policy");

        // **而求解入口仍然没有读任何一个。**
        // **And no solve entry reads either one yet.**
        string[] solvers = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*Solver.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains("obj", StringComparison.Ordinal))
            .Where(static path => !path.Contains("bin", StringComparison.Ordinal))
            .ToArray();
        solvers.Should().NotBeEmpty("there are solvers to inspect");

        string[] reading = solvers
            .Where(path => File.ReadAllText(path).Contains("DeferredFunctionMaterialization.Materialize",
                StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray()!;

        reading.Should().BeEmpty(
            "the entry point is not wired into any solver yet, and pretending otherwise would be the defect this line has "
            + "already hit twice");
    }

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
