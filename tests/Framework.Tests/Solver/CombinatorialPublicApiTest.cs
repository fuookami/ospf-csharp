#nullable enable

using System.Reflection;
using Xunit;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Framework.Solver;
namespace Fuookami.Ospf.Framework.Tests.Solver;

public class CombinatorialPublicApiTest {
    [Fact]
    public void LinearAndQuadraticCombinatorsHaveOneAbstractInterfaceHome() {
        Assembly core = typeof(IAbstractLinearSolver).Assembly;
        Assert.Null(core.GetType("Fuookami.Ospf.Core.Solver.ParallelCombinatorialLinearSolver"));
        Assert.Null(core.GetType("Fuookami.Ospf.Core.Solver.ParallelCombinatorialQuadraticSolver"));
        Assert.NotNull(core.GetType("Fuookami.Ospf.Core.Solver.ParallelCombinatorialColumnGenerationSolver"));
        Assert.NotNull(core.GetType("Fuookami.Ospf.Core.Solver.ParallelCombinatorialMode"));

        Assert.True(typeof(IAbstractLinearSolver).IsAssignableFrom(typeof(ParallelCombinatorialLinearSolver)));
        Assert.True(typeof(IAbstractLinearSolver).IsAssignableFrom(typeof(SerialCombinatorialLinearSolver)));
        Assert.True(typeof(IAbstractQuadraticSolver).IsAssignableFrom(typeof(ParallelCombinatorialQuadraticSolver)));
        Assert.True(typeof(IAbstractQuadraticSolver).IsAssignableFrom(typeof(SerialCombinatorialQuadraticSolver)));
        Assert.False(typeof(ILinearSolver).IsAssignableFrom(typeof(ParallelCombinatorialLinearSolver)));
        Assert.False(typeof(ILinearSolver).IsAssignableFrom(typeof(SerialCombinatorialLinearSolver)));
        Assert.False(typeof(IQuadraticSolver).IsAssignableFrom(typeof(ParallelCombinatorialQuadraticSolver)));
        Assert.False(typeof(IQuadraticSolver).IsAssignableFrom(typeof(SerialCombinatorialQuadraticSolver)));
        Assert.Null(typeof(ParallelCombinatorialLinearSolver).GetProperty("Config"));
        Assert.Null(typeof(SerialCombinatorialLinearSolver).GetProperty("Config"));
        Assert.Null(typeof(ParallelCombinatorialQuadraticSolver).GetProperty("Config"));
        Assert.Null(typeof(SerialCombinatorialQuadraticSolver).GetProperty("Config"));
    }
}
