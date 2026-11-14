#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver.Config;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>Gurobi Benders 时间预算契约 / Gurobi Benders time-budget contract.</summary>
public class GurobiBendersBudgetContractTest {
    [Fact]
    public void WithTimeLimit_ReturnsBudgetedCopyWithoutMutatingOriginal() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(30),
            ThreadCount = 3,
        };
        var original = new GurobiBendersDecompositionSolver(config);

        GurobiBendersDecompositionSolver budgeted = Assert.IsType<GurobiBendersDecompositionSolver>(
            original.WithTimeLimit(TimeSpan.FromSeconds(7)));

        Assert.NotSame(original, budgeted);
        Assert.Equal(TimeSpan.FromSeconds(7), budgeted.Config.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), original.Config.TimeLimit);
        Assert.Equal(config.ThreadCount, budgeted.Config.ThreadCount);
    }
}
