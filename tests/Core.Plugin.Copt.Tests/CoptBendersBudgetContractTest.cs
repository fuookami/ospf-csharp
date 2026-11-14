#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Copt;
using Fuookami.Ospf.Core.Solver.Config;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>COPT Benders 时间预算契约 / COPT Benders time-budget contract.</summary>
public class CoptBendersBudgetContractTest {
    [Fact]
    public void WithTimeLimit_ReturnsBudgetedCopyWithoutMutatingOriginal() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(30),
            ThreadCount = 3,
        };
        var original = new CoptBendersDecompositionSolver(config);

        CoptBendersDecompositionSolver budgeted = Assert.IsType<CoptBendersDecompositionSolver>(
            original.WithTimeLimit(TimeSpan.FromSeconds(7)));

        Assert.NotSame(original, budgeted);
        Assert.Equal(TimeSpan.FromSeconds(7), budgeted.Config.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), original.Config.TimeLimit);
        Assert.Equal(config.ThreadCount, budgeted.Config.ThreadCount);
    }
}
