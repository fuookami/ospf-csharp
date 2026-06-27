#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Gurobi11;
using Fuookami.Ospf.Core.Solver;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11.Tests;

/// <summary>
/// Gurobi 11 二次求解器结构测试 / Gurobi 11 quadratic solver structural tests.
/// </summary>
public class Gurobi11QuadraticSolverTest {
    [Fact]
    [Trait("Solver", "Gurobi11")]
    public void QuadraticSolver_Should_Exist() {
        var solver = new Gurobi11QuadraticSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("gurobi11");
    }

    [Fact]
    [Trait("Solver", "Gurobi11")]
    public void QuadraticSolver_Should_Implement_IQuadraticSolver() {
        var solver = new Gurobi11QuadraticSolver();
        solver.Should().BeAssignableTo<IQuadraticSolver>();
    }

    [Fact]
    [Trait("Solver", "Gurobi11")]
    public void QuadraticSolver_Should_Have_Default_Config() {
        var solver = new Gurobi11QuadraticSolver();
        solver.Config.Should().NotBeNull();
    }
}
