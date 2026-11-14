#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Cplex;
using Fuookami.Ospf.Core.Solver.Config;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

/// <summary>CPLEX Benders 时间预算契约 / CPLEX Benders time-budget contract.</summary>
public class CplexBendersBudgetContractTest {
    [Fact]
    public void WithTimeLimit_ReturnsBudgetedCopyWithoutMutatingOriginal() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(30),
            ThreadCount = 3,
        };
        var original = new CplexBendersDecompositionSolver(config);

        CplexBendersDecompositionSolver budgeted = Assert.IsType<CplexBendersDecompositionSolver>(
            original.WithTimeLimit(TimeSpan.FromSeconds(7)));

        Assert.NotSame(original, budgeted);
        Assert.Equal(TimeSpan.FromSeconds(7), budgeted.Config.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), original.Config.TimeLimit);
        Assert.Equal(config.ThreadCount, budgeted.Config.ThreadCount);
    }
}
