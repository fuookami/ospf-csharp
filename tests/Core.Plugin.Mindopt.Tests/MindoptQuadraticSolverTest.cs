#nullable enable

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
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

    [Fact]
    public async Task QuadraticDump_ShouldConvertAndRespectCancellationWithoutSdk() {
        using var parent = new QuadraticMetaModel<Flt64>();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IAbstractQuadraticSolver solver = new MindoptQuadraticSolver();
        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result = await solver.DumpAsync(mechanism);
        result.IsOk.Should().BeTrue();
        using QuadraticTetradModel converted = result.Value;
        converted.Objective.Cells.Should().BeEmpty();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (await solver.DumpAsync(mechanism, cancellation.Token))
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }
}
