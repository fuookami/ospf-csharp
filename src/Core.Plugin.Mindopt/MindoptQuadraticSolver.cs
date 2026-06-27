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

namespace Fuookami.Ospf.Core.Plugin.Mindopt;

/// <summary>
/// MindOPT 二次求解器（桩实现） / MindOPT quadratic solver (structural stub).
/// 运行时需加载 MindOPT 托管 DLL，否则返回 OREngineEnvironmentLost。
/// Requires MindOPT managed DLL at runtime; returns OREngineEnvironmentLost otherwise.
/// </summary>
public sealed class MindoptQuadraticSolver : IQuadraticSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "mindopt";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    public MindoptQuadraticSolver(SolverConfig? config = null) {
        Config = config ?? new VendorSolverConfig();
    }

    /// <summary>
    /// 求解二次模型 / Solve quadratic model.
    /// </summary>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return Task.FromResult<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
            new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "MindOPT managed DLL not loaded at runtime")));
    }

    /// <summary>
    /// 求解二次模型并启用 IIS 诊断 / Solve quadratic model with IIS diagnostics.
    /// </summary>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) {
        return Task.FromResult<Result<SolverOutput, ErrorCode, Error<ErrorCode>>>(
            new Failed<SolverOutput, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "MindOPT managed DLL not loaded at runtime")));
    }

    /// <summary>
    /// 求解二次模型获取多个解 / Solve quadratic model for multiple solutions.
    /// </summary>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        return Task.FromResult<Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(
            new Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "MindOPT managed DLL not loaded at runtime")));
    }

    /// <summary>
    /// 转储二次机制模型为四元组模型 / Dump quadratic mechanism model to tetrad model.
    /// </summary>
    public Task<QuadraticTetradModel> DumpAsync(QuadraticMechanismModel<Flt64> model, CancellationToken cancellationToken = default)
        => throw new System.NotSupportedException("Dump not supported by MindoptQuadraticSolver");

    /// <summary>
    /// 转储二次元模型为机制模型 / Dump quadratic meta model to mechanism model.
    /// </summary>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        return Task.FromResult<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(
            new Failed<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, "MindOPT managed DLL not loaded at runtime")));
    }
}
