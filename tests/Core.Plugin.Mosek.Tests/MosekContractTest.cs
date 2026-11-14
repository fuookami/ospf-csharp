#nullable enable

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mosek.Tests;

/// <summary>
/// MOSEK 无许可证契约测试 / License-independent MOSEK contract tests.
/// </summary>
public class MosekContractTest {
    [Fact]
    [Trait("Solver", "MOSEK")]
    public void CapabilitiesMatchImplementedPaths() {
        SolverCapabilities capabilities = MosekReportSupport.LinearCapabilities;

        capabilities.ModelTypes.Should().BeEquivalentTo([
            SolverModelType.Lp,
            SolverModelType.Mip,
        ]);
        capabilities.WarmStart.Should().BeTrue();
        capabilities.NativeIis.Should().BeFalse();
        capabilities.Dual.Should().BeFalse();
        capabilities.Farkas.Should().BeFalse();
        capabilities.SolutionPool.Should().BeFalse();
        capabilities.Callback.Should().BeFalse();
        capabilities.Interrupt.Should().BeFalse();
    }

#if MOSEK_AVAILABLE
    [Fact]
    [Trait("Solver", "MOSEK")]
    public void DescriptorUsesNativeVersion() {
        SolverDescriptor descriptor = Assert.IsType<SolverDescriptor>(
            new MosekLinearSolver().Descriptor);

        descriptor.SolverId.Should().Be("mosek");
        descriptor.BackendName.Should().Be("MOSEK");
        descriptor.BackendVersion.Should().MatchRegex(@"^10\.2(\.|$)");
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task CancellationReturnsReportWithFingerprints() {
        using LinearTriadModel model = MosekTestModels.SimpleLp();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver().InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);

        result.IsOk.Should().BeTrue();
        result.Value.TerminationReason.Should().Be(TerminationReason.Cancelled);
        result.Value.SolutionPresence.Should().Be(SolutionPresence.None);
        result.Value.Fingerprints.Configuration.Should().NotBeNull();
        result.Value.Fingerprints.Configuration!.Value.Should().NotBeNullOrWhiteSpace();
        result.Value.Fingerprints.Solver.Should().NotBeNull();
        result.Value.Fingerprints.Solver!.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task InvalidSnapshotFailsBeforeCreatingEnvironment() {
        using LinearTriadModel model = MosekTestModels.InvalidVariableModel();
        bool called = false;
        var callback = new MosekSolverCallBack(
            creatingEnvironmentFunction: _ => {
                called = true;
                return Results.OkInstance;
            });

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver(callBack: callback).InvokeReportAsync(model);

        result.Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        called.Should().BeFalse();
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task SolutionPoolIsRejectedBeforeCreatingEnvironment() {
        using LinearTriadModel model = MosekTestModels.SimpleLp();
        bool called = false;
        var callback = new MosekSolverCallBack(
            creatingEnvironmentFunction: _ => {
                called = true;
                return Results.OkInstance;
            });

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver(callBack: callback).InvokeReportAsync(
                model: model,
                solutionAmount: 2);

        result.Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
        called.Should().BeFalse();
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task NativeCallbackIsRejectedWithoutClaimingSupport() {
        using LinearTriadModel model = MosekTestModels.SimpleLp();
        var callback = new MosekSolverCallBack(
            nativeCallback: _ => { });

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver(callBack: callback).InvokeReportAsync(model);

        result.Should().BeOfType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
    }
#endif
}
