#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Ga;
using Fuookami.Ospf.Core.Plugin.Heuristic.Gwo;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests;

public class HeuristicSmokeTest {
    [Fact]
    public void GeneAlgorithm_ShouldExist() {
        // Verify GA algorithm class exists and can be instantiated
        var policy = new GAPolicy(mutationRate: 0.1, mutationStrength: 0.5, tournamentSize: 3);
        var ga = new GeneAlgorithm(policy);
        ga.Should().NotBeNull();
        ga.Policy.Should().BeSameAs(policy);
    }

    [Fact]
    public void GreyWolfOptimizer_ShouldExist() {
        // Verify GWO algorithm class exists and can be instantiated
        var policy = new GwoPolicy();
        var gwo = new GreyWolfOptimizer(policy, populationSize: 10, solutionAmount: 5);
        gwo.Should().NotBeNull();
        gwo.Policy.Should().BeSameAs(policy);
    }
}
