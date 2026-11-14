// demo5 求解器工厂测试 / Tests for the demo5 solver factory.

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// demo5 求解器工厂 / The demo5 solver factory.
/// </summary>
public sealed class Demo5SolverFactoryTests {
    /// <summary>**未知求解器名失败并指名它** / An unknown solver name fails and names itself.</summary>
    /// <remarks>
    /// 基线是 `else -> Gurobi`——写错一个名字会**静默拿到 Gurobi**，而错误要到后端不可用时才以一个无关的失败形式出现。
    ///
    /// The baseline is `else -> Gurobi` — a typo **silently gets Gurobi**, and the mistake only surfaces later as an unrelated backend-unavailable
    /// failure.
    /// </remarks>
    [Fact]
    public void Create_ShouldRejectUnknownSolverNames() {
        Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> created =
            Demo5SolverFactory.Create("gruobi");

        created.Should().BeOfType<Failed<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>>)created).Error.Message
            .Should().Contain("gruobi",
                "错误必须指名那个拼错的名字 / the error must name the typo");
    }

    /// <summary>**两个已知后端各自映射到自己的实现** / Each known backend maps to its own implementation.</summary>
    /// <remarks>
    /// 断言的是**后端的 `Name`**，不是"创建成功"：两个后端都不需要 SDK 就能构造，但它们的 `Name` 不同，
    /// 而**"创建成功"在两者互换时依然成立**。
    ///
    /// The assertion is on **the backend's `Name`**, not on "creation succeeded": both backends construct without an SDK, so their names are what
    /// distinguishes them, and **"creation succeeded" stays true even when the two are swapped**.
    /// </remarks>
    [Fact]
    public void Create_ShouldMapKnownNamesToDistinctBackends() {
        Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> gurobi =
            Demo5SolverFactory.Create(Demo5SolverFactory.GurobiName);
        Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> scip =
            Demo5SolverFactory.Create(Demo5SolverFactory.ScipName);

        gurobi.IsOk.Should().BeTrue(gurobi.ToString());
        scip.IsOk.Should().BeTrue(scip.ToString());
        gurobi.Value.Name.Should().Be(Demo5SolverFactory.GurobiName);
        scip.Value.Name.Should().Be(Demo5SolverFactory.ScipName);
        gurobi.Value.Name.Should().NotBe(scip.Value.Name);
    }

    /// <summary>**名字大小写与空白不敏感** / Names ignore case and surrounding whitespace.</summary>
    [Fact]
    public void Create_ShouldNormalizeNames() {
        Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> upper =
            Demo5SolverFactory.Create("  SCIP  ");

        upper.IsOk.Should().BeTrue(upper.ToString());
        upper.Value.Name.Should().Be(Demo5SolverFactory.ScipName);
    }

    /// <summary>**配置被带进后端** / The configuration reaches the backend.</summary>
    /// <remarks>
    /// 丢掉配置不会报错，只会让求解**以默认时间上限运行**，而**调用方以为设了上限**。
    ///
    /// Dropping the configuration throws nothing; it only makes the solve **run with the default time limit** while **the caller believes a limit is
    /// set**.
    /// </remarks>
    [Fact]
    public void Create_ShouldPassTheConfigurationToTheBackend() {
        var config = new SolverConfig { TimeLimit = System.TimeSpan.FromSeconds(7) };

        Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> created =
            Demo5SolverFactory.Create(Demo5SolverFactory.ScipName, config);

        created.IsOk.Should().BeTrue(created.ToString());
        created.Value.Should().BeOfType<ScipColumnGenerationSolver>();
        ((ScipColumnGenerationSolver)created.Value).Config.TimeLimit
            .Should().Be(System.TimeSpan.FromSeconds(7));
    }
}
