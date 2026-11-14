#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.Config;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>SCIP Benders 时间预算契约 / SCIP Benders time-budget contract.</summary>
public class ScipBendersBudgetContractTest {
    [Fact]
    public void WithTimeLimit_ReturnsBudgetedCopyWithoutMutatingOriginal() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(30),
            ThreadCount = 3,
        };
        var original = new ScipBendersDecompositionSolver(config);

        ScipBendersDecompositionSolver budgeted = Assert.IsType<ScipBendersDecompositionSolver>(
            original.WithTimeLimit(TimeSpan.FromSeconds(7)));

        Assert.NotSame(original, budgeted);
        Assert.Equal(TimeSpan.FromSeconds(7), budgeted.Config.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), original.Config.TimeLimit);
        Assert.Equal(config.ThreadCount, budgeted.Config.ThreadCount);
    }
}
