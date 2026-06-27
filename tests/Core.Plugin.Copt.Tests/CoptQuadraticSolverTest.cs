#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Copt;
using Fuookami.Ospf.Core.Solver;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>
/// COPT 二次求解器结构测试 / COPT quadratic solver structural tests.
/// </summary>
public class CoptQuadraticSolverTest {
    [Fact]
    [Trait("Solver", "COPT")]
    public void QuadraticSolver_Should_Exist() {
        var solver = new CoptQuadraticSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("copt");
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public void QuadraticSolver_Should_Implement_IQuadraticSolver() {
        var solver = new CoptQuadraticSolver();
        solver.Should().BeAssignableTo<IQuadraticSolver>();
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public void QuadraticSolver_Should_Have_Default_Config() {
        var solver = new CoptQuadraticSolver();
        solver.Config.Should().NotBeNull();
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public void QuadraticSolver_Should_Accept_Custom_Config() {
        var config = new CoptSolverConfig { Server = "localhost" };
        var solver = new CoptQuadraticSolver(config);
        solver.Config.Should().BeSameAs(config);
    }
}
