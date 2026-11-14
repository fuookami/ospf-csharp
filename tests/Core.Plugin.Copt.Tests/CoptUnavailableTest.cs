#nullable enable
#if !COPT_AVAILABLE

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Copt;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>
/// COPT SDK-free contract tests.
/// / COPT 无 SDK 契约测试。
/// </summary>
public class CoptUnavailableTest {
    [Fact]
    [Trait("Solver", "COPT")]
    public async Task LinearEntriesReturnEnvironmentLost() {
        using LinearTriadModel model = new(
            variables: [],
            constraints: EmptyLinearConstraints(),
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, []),
            name: "no-sdk-linear");
        var solver = new CoptLinearSolver();

        AssertEnvironmentLost(await solver.InvokeReportAsync(model, 3));
        AssertEnvironmentLost(await solver.InvokeAsync((ILinearTriadModelView)model));
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task QuadraticEntriesReturnEnvironmentLost() {
        using QuadraticTetradModel model = new(
            variables: [],
            constraints: EmptyQuadraticConstraints(),
            objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, []),
            name: "no-sdk-quadratic");
        var solver = new CoptQuadraticSolver();

        AssertEnvironmentLost(await solver.InvokeReportAsync(model, 3));
        AssertEnvironmentLost(await solver.InvokeAsync((IQuadraticTetradModelView)model));
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task LinearDumpRemainsAvailableWithoutSdk() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> mechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IAbstractLinearSolver solver = new CoptLinearSolver();

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
                await solver.DumpAsync(mechanism, cancellation.Token)).Code);
    }

    private static LinearConstraintBatch EmptyLinearConstraints() => new(
        lhs: new SparseMatrix(),
        signs: [],
        rhs: [],
        names: [],
        sources: []);

    private static QuadraticConstraintBatch EmptyQuadraticConstraints() => new(
        lhs: new SparseQuadraticMatrix(),
        signs: [],
        rhs: [],
        names: [],
        sources: []);

    private static void AssertEnvironmentLost<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(
            ErrorCode.OREngineEnvironmentLost,
            Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code);
}
#endif
