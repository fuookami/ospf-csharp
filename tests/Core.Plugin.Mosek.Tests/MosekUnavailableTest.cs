#nullable enable
#if !MOSEK_AVAILABLE

using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Mosek;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mosek.Tests;

/// <summary>
/// MOSEK 无 SDK 契约测试 / MOSEK SDK-free contract tests.
/// </summary>
public class MosekUnavailableTest {
    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task LinearEntriesReturnEnvironmentLost() {
        using LinearTriadModel model = MosekTestModels.SimpleLp();
        ILinearSolver solver = new MosekLinearSolver();

        AssertEnvironmentLost(await solver.InvokeReportAsync(model: model, solutionAmount: 2));
        AssertEnvironmentLost(await solver.InvokeAsync((ILinearTriadModelView)model));
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task DumpRemainsAvailableWithoutSdk() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> mechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IAbstractLinearSolver solver = new MosekLinearSolver();

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result =
            await solver.DumpAsync(mechanism);
        Assert.True(result.IsOk, result.ToString());
        using LinearTriadModel converted = result.Value;
        Assert.Empty(converted.Variables);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(
            ErrorCode.ApplicationStopped,
            Assert.IsType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>(
                await solver.DumpAsync(
                    model: mechanism,
                    cancellationToken: cancellation.Token)).Code);
    }

    private static void AssertEnvironmentLost<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(
            ErrorCode.OREngineEnvironmentLost,
            Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code);
}
#endif
