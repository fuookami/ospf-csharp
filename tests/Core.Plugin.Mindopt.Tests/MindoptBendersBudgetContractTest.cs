#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Mindopt;
using Fuookami.Ospf.Core.Solver.Config;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mindopt.Tests;

/// <summary>MindOPT Benders 时间预算契约 / MindOPT Benders time-budget contract.</summary>
public class MindoptBendersBudgetContractTest {
    [Fact]
    public void WithTimeLimit_ReturnsBudgetedCopyWithoutMutatingOriginal() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(30),
            ThreadCount = 3,
        };
        var original = new MindoptBendersDecompositionSolver(config);

        MindoptBendersDecompositionSolver budgeted = Assert.IsType<MindoptBendersDecompositionSolver>(
            original.WithTimeLimit(TimeSpan.FromSeconds(7)));

        Assert.NotSame(original, budgeted);
        Assert.Equal(TimeSpan.FromSeconds(7), budgeted.Config.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), original.Config.TimeLimit);
        Assert.Equal(config.ThreadCount, budgeted.Config.ThreadCount);
    }
}
