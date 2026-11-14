#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// SDK 缺失时的 Gurobi 线性求解器入口 / Gurobi linear solver entry when the SDK is absent.
/// </summary>
public sealed class GurobiLinearSolver : ILinearSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiBackendIdentity _identity;

    /// <summary>
    /// 创建不可用求解器入口 / Create an unavailable solver entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    public GurobiLinearSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiLinearSolver(
        SolverConfig? config,
        GurobiLinearSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
        _identity = identity;
    }

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(cancellationToken.IsCancellationRequested
            ? Results.Ok(GurobiReportSupport.Cancelled(Config, quadratic: false, _identity, nativeVersion: null))
            : GurobiUnavailableSupport.Failure<SolveReport<Flt64>>("solve report"));

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<FeasibleSolverOutput<Flt64>>("solve"));

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<SolverOutput>("IIS diagnostics"));

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>("solution pool"));

    /// <inheritdoc/>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToLinearTriadModel(cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<LinearMechanismModel<Flt64>>("dump"));
}

/// <summary>
/// SDK 缺失时的 Gurobi 二次求解器入口 / Gurobi quadratic solver entry when the SDK is absent.
/// </summary>
public sealed class GurobiQuadraticSolver : IQuadraticSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiBackendIdentity _identity;

    /// <summary>
    /// 创建不可用求解器入口 / Create an unavailable solver entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    public GurobiQuadraticSolver(
        SolverConfig? config = null,
        GurobiQuadraticSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiQuadraticSolver(
        SolverConfig? config,
        GurobiQuadraticSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
        _identity = identity;
    }

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(cancellationToken.IsCancellationRequested
            ? Results.Ok(GurobiReportSupport.Cancelled(Config, quadratic: true, _identity, nativeVersion: null))
            : GurobiUnavailableSupport.Failure<SolveReport<Flt64>>("quadratic solve report"));

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<FeasibleSolverOutput<Flt64>>("quadratic solve"));

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<SolverOutput>("quadratic IIS diagnostics"));

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>("quadratic solution pool"));

    /// <inheritdoc/>
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToQuadraticTetradModel(cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<QuadraticMechanismModel<Flt64>>("quadratic dump"));
}

/// <summary>
/// SDK 缺失时的 Gurobi 列生成入口 / Gurobi column-generation entry when the SDK is absent.
/// </summary>
public sealed class GurobiColumnGenerationSolver : Framework.Solver.IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建不可用列生成入口 / Create an unavailable column-generation entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    public GurobiColumnGenerationSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiColumnGenerationSolver(
        SolverConfig? config,
        GurobiLinearSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<FeasibleSolverOutput<Flt64>>("column-generation MILP"));

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>("column-generation solution pool"));

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Task.FromResult(GurobiUnavailableSupport.Failure<FeasibleSolverOutput<V>>("column-generation converted MILP"));

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<Framework.Solver.IColumnGenerationSolver.LpResult>("column-generation LP"));

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Task.FromResult(GurobiUnavailableSupport.Failure<Framework.Solver.IColumnGenerationSolver.LpResultOf<V>>("column-generation converted LP"));
}

/// <summary>
/// SDK 缺失时的 Gurobi Benders 入口 / Gurobi Benders entry when the SDK is absent.
/// </summary>
public sealed class GurobiBendersDecompositionSolver : Framework.Solver.IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiLinearSolverCallBack? _callBack;
    private readonly GurobiBackendIdentity _identity;

    /// <summary>
    /// 创建不可用 Benders 入口 / Create an unavailable Benders entry.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    public GurobiBendersDecompositionSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiBendersDecompositionSolver(
        SolverConfig? config,
        GurobiLinearSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
        _identity = identity;
    }

    /// <inheritdoc/>
    public Framework.Solver.IBendersDecompositionSolver WithTimeLimit(TimeSpan timeLimit) =>
        WithTimeLimitCore(timeLimit);

    internal GurobiBendersDecompositionSolver WithTimeLimitCore(TimeSpan timeLimit) =>
        new(Config with { TimeLimit = timeLimit }, _callBack, _identity);

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<SolverOutput>("Benders master"));

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMasterReportAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<SolveReport<Flt64>>("Benders master report"));

    /// <inheritdoc/>
    public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Task.FromResult(GurobiUnavailableSupport.Failure<SolverOutput>("converted Benders master"));

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.FromResult(GurobiUnavailableSupport.Failure<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>("Benders subproblem"));

    /// <inheritdoc/>
    public Task<Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        Framework.Solver.FrameworkSolveOptions options)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Task.FromResult(GurobiUnavailableSupport.Failure<Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V>>("converted Benders subproblem"));
}

/// <summary>SDK 缺失时的回调时机 / Callback points when the SDK is absent.</summary>
public enum CallBackPoint {
    /// <summary>建模完成后 / After modeling.</summary>
    AfterModeling,
    /// <summary>配置阶段 / Configuration phase.</summary>
    Configuration,
    /// <summary>分析解阶段 / Analyzing solution phase.</summary>
    AnalyzingSolution,
    /// <summary>求解失败后 / After failure.</summary>
    AfterFailure,
}

/// <summary>SDK 缺失时的线性回调占位 / Linear callback placeholder when the SDK is absent.</summary>
public sealed class GurobiLinearSolverCallBack : ICloneable {
    /// <summary>复制回调占位 / Copy the callback placeholder.</summary>
    /// <returns>新的回调占位 / New callback placeholder.</returns>
    public GurobiLinearSolverCallBack Copy() => new();

    /// <inheritdoc/>
    object ICloneable.Clone() => Copy();
}

/// <summary>SDK 缺失时的二次回调占位 / Quadratic callback placeholder when the SDK is absent.</summary>
public sealed class GurobiQuadraticSolverCallBack : ICloneable {
    /// <summary>复制回调占位 / Copy the callback placeholder.</summary>
    /// <returns>新的回调占位 / New callback placeholder.</returns>
    public GurobiQuadraticSolverCallBack Copy() => new();

    /// <inheritdoc/>
    object ICloneable.Clone() => Copy();
}

internal static class GurobiUnavailableSupport {
    public const string Message = "Gurobi SDK is not available. Set GUROBI_HOME to the Gurobi installation directory. / Gurobi SDK 不可用，请将 GUROBI_HOME 指向 Gurobi 安装目录。";

    public static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string operation) =>
        Results.Failed<T>(new Err<ErrorCode>(
            code: ErrorCode.OREngineEnvironmentLost,
            message: $"{operation}: {Message}"
        ));
}
