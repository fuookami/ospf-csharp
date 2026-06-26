#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Optverse;

public sealed class OptverseLinearSolver : ILinearSolver {
    public string Name => "optverse";
    public SolverConfig Config { get; }

    public OptverseLinearSolver(SolverConfig? config = null) {
        Config = config ?? new VendorSolverConfig();
    }

    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
            new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "Optverse managed DLL not loaded at runtime")));

    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<SolverOutput, ErrorCode, Error<ErrorCode>>>(
            new Failed<SolverOutput, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "Optverse managed DLL not loaded at runtime")));

    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(
            new Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "Optverse managed DLL not loaded at runtime")));

    public Task<LinearTriadModel> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default)
        => throw new System.NotSupportedException();

    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? rcb,
        MechanismModelDumpingStatusCallBack? dcb,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(
            new Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "Optverse managed DLL not loaded at runtime")));
}
