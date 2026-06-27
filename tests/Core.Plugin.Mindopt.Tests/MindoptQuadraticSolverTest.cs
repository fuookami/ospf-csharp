#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Mindopt;
using Fuookami.Ospf.Core.Solver;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mindopt.Tests;

/// <summary>
/// MindOPT 二次求解器结构测试 / MindOPT quadratic solver structural tests.
/// </summary>
public class MindoptQuadraticSolverTest {
    [Fact]
    [Trait("Solver", "MindOPT")]
    public void QuadraticSolver_Should_Exist() {
        var solver = new MindoptQuadraticSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("mindopt");
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public void QuadraticSolver_Should_Implement_IQuadraticSolver() {
        var solver = new MindoptQuadraticSolver();
        solver.Should().BeAssignableTo<IQuadraticSolver>();
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public void QuadraticSolver_Should_Have_Default_Config() {
        var solver = new MindoptQuadraticSolver();
        solver.Config.Should().NotBeNull();
    }
}
