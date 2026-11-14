#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using CancellationToken = System.Threading.CancellationToken;

namespace Fuookami.Ospf.Core.Solver.ConstraintProgramming;

/// <summary>CP 求解进度 / CP solve progress.</summary>
/// <param name="Stage">阶段标识 / Stage identifier.</param>
/// <param name="ProgressInStage">阶段内进度 / Progress within the stage.</param>
/// <param name="OverallProgress">总进度 / Overall progress.</param>
/// <param name="Diagnostics">结构化诊断字段 / Structured diagnostic fields.</param>
public sealed record ConstraintProgrammingProgress(
    string Stage,
    int ProgressInStage,
    int OverallProgress,
    IReadOnlyDictionary<string, string>? Diagnostics = null);

/// <summary>CP 求解选项 / CP solve options.</summary>
public sealed record ConstraintProgrammingSolveOptions(
    TimeSpan? TimeLimit = null,
    ulong? NodeLimit = null,
    ulong? SolutionLimit = null,
    int? ThreadCount = null,
    long? RandomSeed = null,
    SolveCancellationToken? CancellationToken = null,
    IProgress<ConstraintProgrammingProgress>? Progress = null,
    bool CollectConflict = false,
    bool ShrinkConflict = false,
    ulong? ConflictShrinkLimit = null,
    IReadOnlySet<string>? ConflictActivationIds = null,
    bool Deterministic = false,
    Flt64? RelativeObjectiveGap = null,
    Flt64? AbsoluteObjectiveGap = null,
    bool LogEnabled = false,
    BackendConfigurationSnapshot? BackendConfiguration = null,
    ConfigurationFingerprint? ConfigurationFingerprint = null) {
    /// <summary>可审计的有效配置指纹 / Auditable effective-configuration fingerprint.</summary>
    public ConfigurationFingerprint? EffectiveConfigurationFingerprint =>
        ConfigurationFingerprint
        ?? (BackendConfiguration is null
            ? null
            : new ConfigurationFingerprint(BackendConfiguration.Fingerprint()));
}

/// <summary>可在同一模型上重复求解的 CP session / CP session reusable on one model.</summary>
public interface IConstraintProgrammingSession : IDisposable {
    /// <summary>关联模型 / Associated model.</summary>
    ConstraintProgrammingModel Model { get; }

    /// <summary>会话选项 / Session options.</summary>
    ConstraintProgrammingSolveOptions Options { get; }

    /// <summary>会话是否已关闭 / Whether the session is closed.</summary>
    bool IsClosed { get; }

    /// <summary>求解一次；assumptions 与 fixed values 只影响本轮 / Solve once; assumptions and fixed values affect only this solve.</summary>
    /// <param name="assumptions">激活文字 / Activation literals.</param>
    /// <param name="fixedValues">本轮固定的整数值 / Integer values fixed for this solve.</param>
    /// <param name="hints">可选整数解提示 / Optional integer solution hint.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>CP 输出或结构化错误 / CP output or a structured error.</returns>
    Task<Result<
        ConstraintProgrammingSolverOutput,
        ErrorCode,
        Error<ErrorCode>>> SolveAsync(
        IReadOnlyList<BooleanLiteral>? assumptions = null,
        IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
        ConstraintProgrammingSolution? hints = null,
        CancellationToken cancellationToken = default);
}

/// <summary>CP 求解器通用接口 / Common CP solver interface.</summary>
public interface IConstraintProgrammingSolver {
    /// <summary>求解器描述符 / Solver descriptor.</summary>
    SolverDescriptor Descriptor { get; }

    /// <summary>一次性求解 / One-shot solve.</summary>
    /// <param name="model">CP 模型 / CP model.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <param name="cancellationToken">调用方取消令牌 / Caller cancellation token.</param>
    /// <returns>CP 输出或结构化错误 / CP output or a structured error.</returns>
    Task<Result<
        ConstraintProgrammingSolverOutput,
        ErrorCode,
        Error<ErrorCode>>> SolveAsync(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建可复用 session / Create a reusable session.</summary>
    /// <param name="model">CP 模型 / CP model.</param>
    /// <param name="options">求解选项 / Solve options.</param>
    /// <returns>CP session 或结构化错误 / CP session or a structured error.</returns>
    Result<
        IConstraintProgrammingSession,
        ErrorCode,
        Error<ErrorCode>> CreateSession(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolveOptions? options = null);
}
