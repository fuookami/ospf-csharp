#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 严格有限域 CP 门面；所有能力均通过精确线性 MIP 降阶实现。
/// SCIP facade for strict finite-domain CP; every supported feature is implemented by exact linear MIP lowering.
/// </summary>
public sealed class ScipConstraintProgrammingSolver : IConstraintProgrammingSolver {
    private const double IntegerTolerance = 1e-6;
    private const double MaxExactInteger = 9_007_199_254_740_991d;

    private static readonly HashSet<string> ManagedNativeParameters = new(StringComparer.Ordinal) {
        "limits/time",
        "limits/gap",
        "limits/absgap",
        "limits/nodes",
        "limits/solutions",
        "parallel/maxnthreads",
        "randomization/randomseedstrategy",
        "randomization/randomseedshift",
        "randomization/permutevars",
        "randomization/permuteconss",
    };

    private readonly ConstraintProgrammingToLinearModelLowerer _lowerer;
    private readonly ScipLinearSolver _linearSolver;

    /// <summary>创建 SCIP CP 门面 / Create a SCIP CP facade.</summary>
    /// <param name="policy">精确降阶策略 / Exact lowering policy.</param>
    public ScipConstraintProgrammingSolver(
        ConstraintProgrammingLoweringPolicy? policy = null) {
        _lowerer = new ConstraintProgrammingToLinearModelLowerer(
            policy ?? ConstraintProgrammingLoweringPolicy.Strict);
        _linearSolver = new ScipLinearSolver();
        Descriptor = new SolverDescriptor(
            SolverId: "scip-cp",
            BackendName: "SCIP",
            Capabilities: Capabilities(_lowerer.Policy));
    }

    /// <inheritdoc/>
    public SolverDescriptor Descriptor { get; }

    /// <inheritdoc/>
    public async Task<Result<
        ConstraintProgrammingSolverOutput,
        ErrorCode,
        Error<ErrorCode>>> SolveAsync(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolveOptions? options = null,
        CancellationToken cancellationToken = default) {
        Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> created =
            CreateSession(model, options);
        if (created is not Ok<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>> session) {
            return Propagate<ConstraintProgrammingSolverOutput, IConstraintProgrammingSession>(
                created);
        }

        using IConstraintProgrammingSession opened = session.Value;
        return await opened.SolveAsync(
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Result<
        IConstraintProgrammingSession,
        ErrorCode,
        Error<ErrorCode>> CreateSession(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolveOptions? options = null) {
        ArgumentNullException.ThrowIfNull(model);
        if (model.IsClosed) {
            return Failure<IConstraintProgrammingSession>(
                code: ErrorCode.ApplicationStopped,
                message: "CP 模型已关闭 / CP model is closed");
        }

        ConstraintProgrammingSolveOptions effectiveOptions =
            options ?? new ConstraintProgrammingSolveOptions();
        Result<Success, ErrorCode, Error<ErrorCode>> optionValidation =
            ValidateOptions(effectiveOptions);
        if (optionValidation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<IConstraintProgrammingSession, Success>(optionValidation);
        }

        Result<(SolverConfig Config, ScipSolverConfig Backend), ErrorCode, Error<ErrorCode>>
            runtime = BuildRuntimeOptions(effectiveOptions);
        if (runtime is not Ok<
                (SolverConfig Config, ScipSolverConfig Backend),
                ErrorCode,
                Error<ErrorCode>> configured) {
            return Propagate<
                IConstraintProgrammingSession,
                (SolverConfig Config, ScipSolverConfig Backend)>(runtime);
        }

        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            model.Snapshot();
        if (snapshot is not Ok<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>> frozen) {
            return Propagate<IConstraintProgrammingSession, ConstraintProgrammingModelSnapshot>(
                snapshot);
        }

        Result<string, ErrorCode, Error<ErrorCode>> encoded =
            ConstraintProgrammingSnapshotCodec.Encode(frozen.Value);
        if (encoded is not Ok<string, ErrorCode, Error<ErrorCode>> canonical) {
            return Propagate<IConstraintProgrammingSession, string>(encoded);
        }

        return Results.Ok<IConstraintProgrammingSession>(
            new Session(
                solver: this,
                model: model,
                snapshot: frozen.Value,
                options: effectiveOptions,
                config: configured.Value.Config,
                modelFingerprint: SolveFingerprinting.Sha256(canonical.Value)));
    }

    private static SolverCapabilities Capabilities(
        ConstraintProgrammingLoweringPolicy policy) {
        var features = new Dictionary<
            ConstraintProgrammingFeature,
            ConstraintProgrammingSupportLevel> {
            [ConstraintProgrammingFeature.BooleanLogic] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.Reification] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.SparseDomain] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.AllDifferent] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.Element] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.Table] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.Interval] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.OptionalInterval] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.NoOverlap] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.Cumulative] = policy.AllowCumulative
                ? ConstraintProgrammingSupportLevel.ExactLowering
                : ConstraintProgrammingSupportLevel.Unsupported,
            [ConstraintProgrammingFeature.Circuit] =
                ConstraintProgrammingSupportLevel.Unsupported,
            [ConstraintProgrammingFeature.Automaton] =
                ConstraintProgrammingSupportLevel.Unsupported,
            [ConstraintProgrammingFeature.Reservoir] =
                ConstraintProgrammingSupportLevel.Unsupported,
            [ConstraintProgrammingFeature.Assumption] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.ConflictCore] =
                ConstraintProgrammingSupportLevel.Unsupported,
            [ConstraintProgrammingFeature.SolutionHint] =
                ConstraintProgrammingSupportLevel.ExactLowering,
            [ConstraintProgrammingFeature.IncrementalSolve] =
                ConstraintProgrammingSupportLevel.ExactLowering,
        };
        return new SolverCapabilities(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Cp },
            WarmStart: true,
            Interrupt: true,
            ConstraintProgrammingFeatures: features);
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateOptions(
        ConstraintProgrammingSolveOptions options) {
        if (options.TimeLimit is { } timeLimit && timeLimit < TimeSpan.Zero) {
            return Illegal("SCIP CP timeLimit 不得为负 / SCIP CP timeLimit must not be negative");
        }
        if (options.NodeLimit == 0 || options.SolutionLimit == 0) {
            return Illegal("SCIP CP 节点/解数量上限必须为正 / SCIP CP node and solution limits must be positive");
        }
        if (options.NodeLimit > (ulong)long.MaxValue ||
            options.SolutionLimit > (ulong)long.MaxValue) {
            return Illegal(
                "SCIP CP 节点/解数量上限超出 long 范围 / " +
                "SCIP CP node or solution limit exceeds the long range");
        }
        if (options.ThreadCount is <= 0) {
            return Illegal("SCIP CP threadCount 必须为正 / SCIP CP threadCount must be positive");
        }
        if (options.Deterministic &&
            options.ThreadCount is { } deterministicThreads &&
            deterministicThreads != 1) {
            return Illegal(
                "SCIP CP deterministic 模式必须使用单线程 / " +
                "SCIP CP deterministic mode requires one thread");
        }
        if (options.RandomSeed is < int.MinValue or > int.MaxValue) {
            return Illegal(
                "SCIP CP randomSeed 超出原生整数范围 / " +
                "SCIP CP randomSeed exceeds the native integer range");
        }
        if (options.RelativeObjectiveGap is { } relativeGap &&
            (!double.IsFinite(relativeGap.ToDouble()) ||
             relativeGap.ToDouble() < 0 ||
             relativeGap.ToDouble() > 1)) {
            return Illegal(
                "SCIP CP relativeObjectiveGap 必须位于 [0, 1] / " +
                "SCIP CP relativeObjectiveGap must be in [0, 1]");
        }
        if (options.AbsoluteObjectiveGap is { } absoluteGap &&
            (!double.IsFinite(absoluteGap.ToDouble()) ||
             absoluteGap.ToDouble() < 0)) {
            return Illegal(
                "SCIP CP absoluteObjectiveGap 必须是非负有限值 / " +
                "SCIP CP absoluteObjectiveGap must be finite and non-negative");
        }
        if (options.CollectConflict ||
            options.ShrinkConflict ||
            options.ConflictShrinkLimit is not null ||
            options.ConflictActivationIds is not null) {
            return Unsupported(
                "SCIP 精确降阶门面不提供 CP 冲突核 / " +
                "The exact-lowering SCIP facade does not provide CP conflict cores");
        }
        if (options.LogEnabled) {
            return Unsupported(
                "SCIP 精确降阶门面尚未接入原生日志通道 / " +
                "The exact-lowering SCIP facade has no native log channel yet");
        }
        return Results.OkInstance;
    }

    private static Result<
        (SolverConfig Config, ScipSolverConfig Backend),
        ErrorCode,
        Error<ErrorCode>> BuildRuntimeOptions(
        ConstraintProgrammingSolveOptions options) {
        Result<ScipSolverConfig?, ErrorCode, Error<ErrorCode>> parsed =
            ParseBackendConfiguration(options.BackendConfiguration);
        if (parsed is not Ok<ScipSolverConfig?, ErrorCode, Error<ErrorCode>> backendResult) {
            return Propagate<
                (SolverConfig Config, ScipSolverConfig Backend),
                ScipSolverConfig?>(parsed);
        }

        ScipSolverConfig? backend = backendResult.Value;
        long? randomSeed = backend?.RandomSeed ?? options.RandomSeed;
        bool deterministic = options.Deterministic ||
            backend?.Deterministic == true;
        int threadCount = options.ThreadCount ?? 1;
        if (deterministic && threadCount != 1) {
            return Illegal<
                (SolverConfig Config, ScipSolverConfig Backend)>(
                "SCIP CP deterministic 模式必须使用单线程 / " +
                "SCIP CP deterministic mode requires one thread");
        }

        Dictionary<string, BackendParameterValue> native = backend is null
            ? new Dictionary<string, BackendParameterValue>(StringComparer.Ordinal)
            : new Dictionary<string, BackendParameterValue>(
                backend.NativeParameters,
                StringComparer.Ordinal);
        foreach (string name in native.Keys) {
            if (ManagedNativeParameters.Contains(name)) {
                return Illegal<
                    (SolverConfig Config, ScipSolverConfig Backend)>(
                    $"SCIP CP 参数 {name} 由门面管理，不能在 NativeParameters 中覆盖 / " +
                    $"SCIP CP parameter {name} is managed by the facade and cannot be overridden in NativeParameters");
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> nodeLimit = AddNativeParameter(
            native: native,
            name: "limits/nodes",
            value: options.NodeLimit,
            context: "nodeLimit");
        if (nodeLimit is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<
                (SolverConfig Config, ScipSolverConfig Backend),
                Success>(nodeLimit);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> solutionLimit = AddNativeParameter(
            native: native,
            name: "limits/solutions",
            value: options.SolutionLimit,
            context: "solutionLimit");
        if (solutionLimit is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<
                (SolverConfig Config, ScipSolverConfig Backend),
                Success>(solutionLimit);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> absoluteGap = AddNativeParameter(
            native: native,
            name: "limits/absgap",
            value: options.AbsoluteObjectiveGap,
            context: "absoluteObjectiveGap");
        if (absoluteGap is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<
                (SolverConfig Config, ScipSolverConfig Backend),
                Success>(absoluteGap);
        }

        var effectiveBackend = new ScipSolverConfig(
            Presolve: backend?.Presolve,
            RandomSeed: randomSeed,
            Deterministic: deterministic,
            NativeParameters: new ReadOnlyDictionary<string, BackendParameterValue>(native));
        var config = new SolverConfig {
            TimeLimit = options.TimeLimit ?? TimeSpan.MaxValue,
            ThreadCount = (ulong)threadCount,
            Gap = options.RelativeObjectiveGap ?? Flt64.Zero,
            BackendConfiguration = effectiveBackend,
        };
        Result<Success, ErrorCode, Error<ErrorCode>> validation = config.ValidateRuntime();
        return validation is Ok<Success, ErrorCode, Error<ErrorCode>>
            ? Results.Ok((config, effectiveBackend))
            : Propagate<
                (SolverConfig Config, ScipSolverConfig Backend),
                Success>(validation);
    }

    private static Result<ScipSolverConfig?, ErrorCode, Error<ErrorCode>>
        ParseBackendConfiguration(BackendConfigurationSnapshot? snapshot) {
        if (snapshot is null) {
            return Results.Ok<ScipSolverConfig?>(null);
        }
        if (!string.Equals(snapshot.Type, "scip", StringComparison.Ordinal)) {
            return Unsupported<ScipSolverConfig?>(
                $"SCIP CP 不能使用 {snapshot.Type} backend 配置 / " +
                $"SCIP CP cannot use backend configuration {snapshot.Type}");
        }

        bool? presolve = null;
        long? randomSeed = null;
        bool? deterministic = null;
        var native = new Dictionary<string, BackendParameterValue>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BackendParameter parameter in snapshot.Parameters) {
            if (parameter.Sensitive) {
                return Unsupported<ScipSolverConfig?>(
                    $"SCIP CP 无法从脱敏快照还原参数 {parameter.Name} / " +
                    $"SCIP CP cannot reconstruct sensitive parameter {parameter.Name} from a snapshot");
            }
            if (string.IsNullOrWhiteSpace(parameter.Name) || !seen.Add(parameter.Name)) {
                return Illegal<ScipSolverConfig?>(
                    $"SCIP CP backend 参数名无效或重复：{parameter.Name} / " +
                    $"SCIP CP backend parameter name is blank or duplicated: {parameter.Name}");
            }
            switch (parameter.Name) {
                case "presolve" when parameter.Value is BackendParameterValue.BooleanValue value:
                    presolve = value.Value;
                    break;
                case "randomSeed" when parameter.Value is BackendParameterValue.Integer value:
                    randomSeed = value.Value;
                    break;
                case "deterministic" when parameter.Value is BackendParameterValue.BooleanValue value:
                    deterministic = value.Value;
                    break;
                case "presolve":
                case "randomSeed":
                case "deterministic":
                    return Illegal<ScipSolverConfig?>(
                        $"SCIP CP backend 参数 {parameter.Name} 类型无效 / " +
                        $"SCIP CP backend parameter {parameter.Name} has an invalid type");
                default:
                    native[parameter.Name] = parameter.Value;
                    break;
            }
        }
        return Results.Ok<ScipSolverConfig?>(
            new ScipSolverConfig(
                Presolve: presolve,
                RandomSeed: randomSeed,
                Deterministic: deterministic,
                NativeParameters: new ReadOnlyDictionary<string, BackendParameterValue>(native)));
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> AddNativeParameter<T>(
        IDictionary<string, BackendParameterValue> native,
        string name,
        T? value,
        string context)
        where T : struct {
        if (value is null) {
            return Results.OkInstance;
        }
        if (value is ulong unsigned) {
            if (unsigned > long.MaxValue) {
                return Illegal(
                    $"SCIP CP {context} 超出 long 范围 / SCIP CP {context} exceeds the long range");
            }
            native[name] = new BackendParameterValue.Integer((long)unsigned);
            return Results.OkInstance;
        }
        if (value is Flt64 gap) {
            double number = gap.ToDouble();
            if (!double.IsFinite(number)) {
                return Illegal(
                    $"SCIP CP {context} 必须是有限值 / SCIP CP {context} must be finite");
            }
            native[name] = new BackendParameterValue.Decimal(
                number.ToString("R", CultureInfo.InvariantCulture));
            return Results.OkInstance;
        }
        return Illegal(
            $"SCIP CP {context} 类型不受支持 / SCIP CP {context} has an unsupported type");
    }

    private sealed class Session : IConstraintProgrammingSession {
        private readonly ScipConstraintProgrammingSolver _solver;
        private readonly ConstraintProgrammingModelSnapshot _snapshot;
        private readonly SolverConfig _config;
        private readonly AuditFingerprint _modelFingerprint;
        private bool _closed;

        internal Session(
            ScipConstraintProgrammingSolver solver,
            ConstraintProgrammingModel model,
            ConstraintProgrammingModelSnapshot snapshot,
            ConstraintProgrammingSolveOptions options,
            SolverConfig config,
            AuditFingerprint modelFingerprint) {
            _solver = solver;
            Model = model;
            _snapshot = snapshot;
            Options = options;
            _config = config;
            _modelFingerprint = modelFingerprint;
        }

        public ConstraintProgrammingModel Model { get; }

        public ConstraintProgrammingSolveOptions Options { get; }

        public bool IsClosed => _closed;

        public async Task<Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>>> SolveAsync(
            IReadOnlyList<BooleanLiteral>? assumptions = null,
            IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
            ConstraintProgrammingSolution? hints = null,
            CancellationToken cancellationToken = default) {
            if (_closed) {
                return Failure<ConstraintProgrammingSolverOutput>(
                    code: ErrorCode.ApplicationStopped,
                    message: "SCIP CP session 已关闭 / SCIP CP session is closed");
            }
            if (cancellationToken.IsCancellationRequested ||
                Options.CancellationToken?.IsCancellationRequested == true) {
                return Results.Ok<ConstraintProgrammingSolverOutput>(
                    UnknownOutput(
                        options: Options,
                        config: _config,
                        modelFingerprint: _modelFingerprint,
                        descriptor: _solver.Descriptor,
                        terminationReason: TerminationReason.Cancelled));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            Func<CancellationRecord, Result<Success, ErrorCode, Error<ErrorCode>>>? listener =
                null;
            if (Options.CancellationToken is { } solveToken) {
                listener = record => {
                    try {
                        linked.Cancel();
                        return Results.OkInstance;
                    }
                    catch (Exception error) when (
                        error is not OutOfMemoryException and not AccessViolationException) {
                        return Failure<Success>(
                            code: ErrorCode.Other,
                            message: $"SCIP CP 取消转发失败：{error.Message} / " +
                                $"SCIP CP cancellation forwarding failed: {error.Message}");
                    }
                };
                Result<Success, ErrorCode, Error<ErrorCode>> registered =
                    solveToken.Register(listener);
                if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return Propagate<ConstraintProgrammingSolverOutput, Success>(
                        registered);
                }
            }

            try {
                return await SolveCore(
                    assumptions: assumptions ?? Array.Empty<BooleanLiteral>(),
                    fixedValues: fixedValues ??
                        new Dictionary<VariableId, Int64>(),
                    hints: hints,
                    cancellationToken: linked.Token).ConfigureAwait(false);
            }
            finally {
                if (Options.CancellationToken is { } cleanupToken && listener is not null) {
                    cleanupToken.Unregister(listener);
                }
            }
        }

        public void Dispose() => _closed = true;

        private async Task<Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>>> SolveCore(
            IReadOnlyList<BooleanLiteral> assumptions,
            IReadOnlyDictionary<VariableId, Int64> fixedValues,
            ConstraintProgrammingSolution? hints,
            CancellationToken cancellationToken) {
            Result<Success, ErrorCode, Error<ErrorCode>> loweringProgress =
                ReportProgress(
                    progress: Options.Progress,
                    stage: "lowering",
                    progressInStage: 0,
                    overallProgress: 0);
            if (loweringProgress is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<ConstraintProgrammingSolverOutput, Success>(
                    loweringProgress);
            }

            Result<
                ConstraintProgrammingLoweredLinearModel,
                ErrorCode,
                Error<ErrorCode>> loweredResult = _solver._lowerer.Lower(
                    snapshot: _snapshot,
                    assumptions: assumptions,
                    fixedValues: fixedValues);
            if (loweredResult is Failed<
                    ConstraintProgrammingLoweredLinearModel,
                    ErrorCode,
                    Error<ErrorCode>> loweringFailure) {
                return Results.Failed<ConstraintProgrammingSolverOutput>(
                    loweringFailure.Error);
            }
            if (loweredResult is Fatal<
                    ConstraintProgrammingLoweredLinearModel,
                    ErrorCode,
                    Error<ErrorCode>> loweringFatal) {
                return new Fatal<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>(loweringFatal.Errors);
            }

            using ConstraintProgrammingLoweredLinearModel lowered = loweredResult.Value;
            Result<Success, ErrorCode, Error<ErrorCode>> hintsResult =
                ApplyHints(
                    snapshot: _snapshot,
                    lowered: lowered,
                    fixedValues: fixedValues,
                    hints: hints);
            if (hintsResult is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<ConstraintProgrammingSolverOutput, Success>(
                    hintsResult);
            }

            Result<Success, ErrorCode, Error<ErrorCode>> loweredProgress =
                ReportProgress(
                    progress: Options.Progress,
                    stage: "lowered",
                    progressInStage: 100,
                    overallProgress: 30);
            if (loweredProgress is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<ConstraintProgrammingSolverOutput, Success>(
                    loweredProgress);
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok<ConstraintProgrammingSolverOutput>(
                    UnknownOutput(
                        options: Options,
                        config: _config,
                        modelFingerprint: _modelFingerprint,
                        descriptor: _solver.Descriptor,
                        terminationReason: TerminationReason.Cancelled));
            }

            Result<
                (LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix),
                ErrorCode,
                Error<ErrorCode>> built = await lowered.Model
                .BuildLinearTriadModelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (built is not Ok<
                    (LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix),
                    ErrorCode,
                    Error<ErrorCode>> mechanism) {
                if (cancellationToken.IsCancellationRequested &&
                    built is Failed<
                        (LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix),
                        ErrorCode,
                        Error<ErrorCode>>) {
                    return Results.Ok<ConstraintProgrammingSolverOutput>(
                        UnknownOutput(
                            options: Options,
                            config: _config,
                            modelFingerprint: _modelFingerprint,
                            descriptor: _solver.Descriptor,
                            terminationReason: TerminationReason.Cancelled));
                }
                return Propagate<
                    ConstraintProgrammingSolverOutput,
                    (LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix)>(built);
            }

            using LinearMechanismModel<Flt64> builtModel = mechanism.Value.Model;
            using LinearTriadModel matrix = mechanism.Value.Matrix;
            Result<Success, ErrorCode, Error<ErrorCode>> solvingProgress =
                ReportProgress(
                    progress: Options.Progress,
                    stage: "solving",
                    progressInStage: 0,
                    overallProgress: 30);
            if (solvingProgress is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<ConstraintProgrammingSolverOutput, Success>(
                    solvingProgress);
            }

            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
                await _solver._linearSolver.InvokeReportAsync(
                    model: matrix,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> solved) {
                return Propagate<ConstraintProgrammingSolverOutput, SolveReport<Flt64>>(
                    report);
            }

            Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> mapped =
                MapReport(
                    snapshot: _snapshot,
                    lowered: lowered,
                    options: Options,
                    config: _config,
                    modelFingerprint: _modelFingerprint,
                    descriptor: _solver.Descriptor,
                    columns: matrix.Variables,
                    report: solved.Value);
            if (mapped is not Ok<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>) {
                return mapped;
            }
            Result<Success, ErrorCode, Error<ErrorCode>> completedProgress =
                ReportProgress(
                    progress: Options.Progress,
                    stage: "completed",
                    progressInStage: 100,
                    overallProgress: 100);
            return completedProgress is Ok<Success, ErrorCode, Error<ErrorCode>>
                ? mapped
                : Propagate<ConstraintProgrammingSolverOutput, Success>(
                    completedProgress);
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ApplyHints(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweredLinearModel lowered,
        IReadOnlyDictionary<VariableId, Int64> fixedValues,
        ConstraintProgrammingSolution? hints) {
        if (hints is null ||
            (hints.Values.Count == 0 && hints.Intervals.Count == 0)) {
            return Results.OkInstance;
        }

        var values = new Dictionary<VariableId, Int64>(fixedValues);
        var initial = new Dictionary<IVariableItem, Flt64>(
            ReferenceEqualityComparer.Instance);
        foreach ((VariableId id, Int64 value) in hints.Values) {
            Result<Success, ErrorCode, Error<ErrorCode>> added = AddHintValue(
                snapshot: snapshot,
                lowered: lowered,
                values: values,
                initial: initial,
                id: id,
                value: value);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }

        foreach ((IntervalId id, IntervalValue hint) in hints.Intervals) {
            IntervalVariable? interval = snapshot.Interval(id);
            if (interval is null) {
                return Failure<Success>(
                    code: ErrorCode.DataNotFound,
                    message: $"CP hint 引用了未知 interval：{id.Value} / " +
                        $"CP hint references an unknown interval: {id.Value}");
            }
            if (interval.Presence?.VariableId is { } presenceId) {
                Int64 presence = hint.Present ? Int64.One : Int64.Zero;
                Result<Success, ErrorCode, Error<ErrorCode>> added = AddHintValue(
                    snapshot: snapshot,
                    lowered: lowered,
                    values: values,
                    initial: initial,
                    id: presenceId,
                    value: presence);
                if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return added;
                }
            }
            if (hint.Present) {
                Result<Success, ErrorCode, Error<ErrorCode>> start = AddExpressionHint(
                    snapshot: snapshot,
                    lowered: lowered,
                    values: values,
                    initial: initial,
                    expression: interval.Start,
                    value: hint.Start);
                if (start is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return start;
                }
                Result<Success, ErrorCode, Error<ErrorCode>> end = AddExpressionHint(
                    snapshot: snapshot,
                    lowered: lowered,
                    values: values,
                    initial: initial,
                    expression: interval.End,
                    value: hint.End);
                if (end is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return end;
                }
                Result<Success, ErrorCode, Error<ErrorCode>> size = AddExpressionHint(
                    snapshot: snapshot,
                    lowered: lowered,
                    values: values,
                    initial: initial,
                    expression: interval.Size,
                    value: hint.Size);
                if (size is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return size;
                }
            }
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                interval.Evaluate(values);
            if (evaluated is not Ok<IntervalValue, ErrorCode, Error<ErrorCode>> valid) {
                return Propagate<Success, IntervalValue>(evaluated);
            }
            if (valid.Value != hint) {
                return Failure<Success>(
                    code: ErrorCode.ORSolutionInvalid,
                    message: $"CP interval hint 不满足 interval 定义：{id.Value} / " +
                        $"CP interval hint does not satisfy the interval definition: {id.Value}");
            }
        }
        lowered.Model.Tokens.SetSolverSolution(initial);
        return Results.OkInstance;
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> AddExpressionHint(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweredLinearModel lowered,
        Dictionary<VariableId, Int64> values,
        Dictionary<IVariableItem, Flt64> initial,
        ConstraintProgrammingExpression expression,
        Int64 value) =>
        expression is ConstraintProgrammingExpression.Variable variable
            ? AddHintValue(
                snapshot: snapshot,
                lowered: lowered,
                values: values,
                initial: initial,
                id: variable.VariableId,
                value: value)
            : Results.OkInstance;

    private static Result<Success, ErrorCode, Error<ErrorCode>> AddHintValue(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweredLinearModel lowered,
        Dictionary<VariableId, Int64> values,
        Dictionary<IVariableItem, Flt64> initial,
        VariableId id,
        Int64 value) {
        ConstraintProgrammingVariableSnapshot? definition = snapshot.Variable(id);
        if (definition is null) {
            return Failure<Success>(
                code: ErrorCode.DataNotFound,
                message: $"CP hint 引用了未知变量：{id.Value} / " +
                    $"CP hint references an unknown variable: {id.Value}");
        }
        if (!definition.Domain.Contains(value)) {
            return Failure<Success>(
                code: ErrorCode.ORSolutionInvalid,
                message: $"CP hint 超出变量值域：{id.Value}={value} / " +
                    $"CP hint is outside the variable domain: {id.Value}={value}");
        }
        if (values.TryGetValue(id, out Int64 existing) && existing != value) {
            return Failure<Success>(
                code: ErrorCode.ORSolutionInvalid,
                message: $"CP hint 与固定值或 interval hint 冲突：{id.Value} / " +
                    $"CP hint conflicts with a fixed value or interval hint: {id.Value}");
        }
        long raw = value.ToLong();
        if (raw < -MaxExactInteger || raw > MaxExactInteger) {
            return Failure<Success>(
                code: ErrorCode.ORSolutionInvalid,
                message: $"CP hint 超出精确 double 范围：{id.Value} / " +
                    $"CP hint exceeds the exact double range: {id.Value}");
        }
        values[id] = value;
        if (lowered.Variables.TryGetValue(id, out IVariableItem? variable)) {
            initial[variable] = new Flt64(raw);
        }
        return Results.OkInstance;
    }

    private static Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>
        MapReport(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweredLinearModel lowered,
        ConstraintProgrammingSolveOptions options,
        SolverConfig config,
        AuditFingerprint modelFingerprint,
        SolverDescriptor descriptor,
        IReadOnlyList<ModelViewVariable> columns,
        SolveReport<Flt64> report) {
        return report.ProblemStatus switch {
            ProblemStatus.Infeasible => Results.Ok<ConstraintProgrammingSolverOutput>(
                new ConstraintProgrammingInfeasibleOutput(
                    proofStatus: report.Proof.Status,
                    report: ToExactReport(
                        source: report,
                        snapshot: snapshot,
                        options: options,
                        config: config,
                        modelFingerprint: modelFingerprint,
                        descriptor: descriptor,
                        solution: null,
                        exactObjective: null))),
            ProblemStatus.Feasible => MapFeasible(
                snapshot: snapshot,
                lowered: lowered,
                options: options,
                config: config,
                modelFingerprint: modelFingerprint,
                descriptor: descriptor,
                columns: columns,
                report: report),
            _ => Results.Ok<ConstraintProgrammingSolverOutput>(
                new ConstraintProgrammingUnknownOutput(
                    terminationReason: report.TerminationReason,
                    report: ToExactReport(
                        source: report,
                        snapshot: snapshot,
                        options: options,
                        config: config,
                        modelFingerprint: modelFingerprint,
                        descriptor: descriptor,
                        solution: null,
                        exactObjective: null))),
        };
    }

    private static Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>
        MapFeasible(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweredLinearModel lowered,
        ConstraintProgrammingSolveOptions options,
        SolverConfig config,
        AuditFingerprint modelFingerprint,
        SolverDescriptor descriptor,
        IReadOnlyList<ModelViewVariable> columns,
        SolveReport<Flt64> report) {
        SolveSolution<Flt64>? source = report.Solution;
        if (source is null) {
            return Failure<ConstraintProgrammingSolverOutput>(
                code: ErrorCode.ORSolutionInvalid,
                message: "SCIP CP 报告缺少可行解 / SCIP CP report has no feasible solution");
        }

        Result<ConstraintProgrammingSolution, ErrorCode, Error<ErrorCode>> extracted =
            ExtractSolution(
                snapshot: snapshot,
                lowered: lowered,
                columns: columns,
                linearSolution: source.Values);
        if (extracted is not Ok<
                ConstraintProgrammingSolution,
                ErrorCode,
                Error<ErrorCode>> solution) {
            return Propagate<
                ConstraintProgrammingSolverOutput,
                ConstraintProgrammingSolution>(extracted);
        }

        Result<Success, ErrorCode, Error<ErrorCode>> valid = ValidateSolution(
            snapshot: snapshot,
            solution: solution.Value);
        if (valid is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<ConstraintProgrammingSolverOutput, Success>(valid);
        }

        Int64? exactObjective = null;
        if (snapshot.Objectives.Count > 0) {
            Result<Int64, ErrorCode, Error<ErrorCode>> evaluated =
                snapshot.Objectives[0].Expression.Evaluate(solution.Value.Values);
            if (evaluated is not Ok<Int64, ErrorCode, Error<ErrorCode>> objective) {
                return Propagate<ConstraintProgrammingSolverOutput, Int64>(evaluated);
            }
            exactObjective = objective.Value;
        }

        SolverStatus status = report.SolutionPresence == SolutionPresence.Optimal
            ? SolverStatus.Optimal
            : SolverStatus.Feasible;
        return Results.Ok<ConstraintProgrammingSolverOutput>(
            new ConstraintProgrammingFeasibleOutput(
                solution: solution.Value,
                objective: exactObjective is { } exact
                    ? ConstraintProgrammingSolution.ToCompatibilityFlt64(exact)
                    : null,
                bestBound: report.Statistics.BestBound,
                status: status,
                proofStatus: report.Proof.Status,
                report: ToExactReport(
                    source: report,
                    snapshot: snapshot,
                    options: options,
                    config: config,
                    modelFingerprint: modelFingerprint,
                    descriptor: descriptor,
                    solution: solution.Value,
                    exactObjective: exactObjective),
                exactObjective: exactObjective));
    }

    private static Result<ConstraintProgrammingSolution, ErrorCode, Error<ErrorCode>>
        ExtractSolution(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweredLinearModel lowered,
        IReadOnlyList<ModelViewVariable> columns,
        Solution<Flt64> linearSolution) {
        var values = new Dictionary<VariableId, Int64>(snapshot.Variables.Count);
        foreach (ConstraintProgrammingVariableSnapshot definition in snapshot.Variables) {
            if (!lowered.Variables.TryGetValue(
                    definition.Id,
                    out IVariableItem? sourceVariable)) {
                return Failure<ConstraintProgrammingSolution>(
                    code: ErrorCode.DataNotFound,
                    message: $"降阶结果缺少 CP 变量：{definition.Id.Value} / " +
                        $"Lowered result misses CP variable: {definition.Id.Value}");
            }
            ModelViewVariable? column = null;
            int matches = 0;
            foreach (ModelViewVariable candidate in columns) {
                if (!ReferenceEquals(candidate.Origin, sourceVariable)) {
                    continue;
                }
                column = candidate;
                ++matches;
            }
            if (matches != 1 || column is null) {
                return Failure<ConstraintProgrammingSolution>(
                    code: ErrorCode.ORSolutionInvalid,
                    message: $"SCIP CP 源变量投影数量无效：{definition.Id.Value}={matches} / " +
                        $"SCIP CP source-variable projection count is invalid: {definition.Id.Value}={matches}");
            }
            if (column.Index < 0 || column.Index >= linearSolution.Length) {
                return Failure<ConstraintProgrammingSolution>(
                    code: ErrorCode.ORSolutionInvalid,
                    message: $"SCIP CP 线性解缺少变量：{definition.Id.Value} / " +
                        $"SCIP CP linear solution misses variable: {definition.Id.Value}");
            }

            double raw = linearSolution[column.Index].ToDouble();
            double rounded = System.Math.Round(raw);
            if (!double.IsFinite(raw) ||
                System.Math.Abs(raw - rounded) > IntegerTolerance ||
                System.Math.Abs(rounded) > MaxExactInteger) {
                return Failure<ConstraintProgrammingSolution>(
                    code: ErrorCode.ORSolutionInvalid,
                    message: $"SCIP CP 返回非整数或超范围解：{definition.Id.Value}={raw} / " +
                        $"SCIP CP returned a non-integral or out-of-range value: {definition.Id.Value}={raw}");
            }
            var value = new Int64((long)rounded);
            if (!definition.Domain.Contains(value)) {
                return Failure<ConstraintProgrammingSolution>(
                    code: ErrorCode.ORSolutionInvalid,
                    message: $"SCIP CP 解超出源值域：{definition.Id.Value}={value} / " +
                        $"SCIP CP solution is outside the source domain: {definition.Id.Value}={value}");
            }
            values[definition.Id] = value;
        }

        var intervals = new Dictionary<IntervalId, IntervalValue>(
            lowered.Intervals.Count);
        foreach ((IntervalId id, IntervalVariable interval) in lowered.Intervals) {
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                interval.Evaluate(values);
            if (evaluated is not Ok<IntervalValue, ErrorCode, Error<ErrorCode>> valid) {
                return Propagate<ConstraintProgrammingSolution, IntervalValue>(
                    evaluated);
            }
            intervals[id] = valid.Value;
        }
        return Results.Ok<ConstraintProgrammingSolution>(
            new ConstraintProgrammingSolution(
                values: new ReadOnlyDictionary<VariableId, Int64>(values),
                intervals: new ReadOnlyDictionary<IntervalId, IntervalValue>(intervals)));
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateSolution(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution solution) {
        foreach (IntervalVariable interval in snapshot.Intervals) {
            Result<IntervalValue, ErrorCode, Error<ErrorCode>> evaluated =
                interval.Evaluate(solution.Values);
            if (evaluated is not Ok<IntervalValue, ErrorCode, Error<ErrorCode>>) {
                return Propagate<Success, IntervalValue>(evaluated);
            }
        }
        foreach (ConstraintProgrammingConstraintSnapshot constraint in snapshot.Constraints) {
            Result<bool, ErrorCode, Error<ErrorCode>> satisfied =
                constraint.Constraint.IsSatisfied(solution.Values);
            if (satisfied is not Ok<bool, ErrorCode, Error<ErrorCode>> checkedValue) {
                return Propagate<Success, bool>(satisfied);
            }
            if (!checkedValue.Value) {
                return Failure<Success>(
                    code: ErrorCode.ORSolutionInvalid,
                    message: $"SCIP CP 解违反源约束：{constraint.Id.Value} / " +
                        $"SCIP CP solution violates source constraint: {constraint.Id.Value}");
            }
        }
        return Results.OkInstance;
    }

    private static SolveReport<Int64> ToExactReport(
        SolveReport<Flt64> source,
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolveOptions options,
        SolverConfig config,
        AuditFingerprint modelFingerprint,
        SolverDescriptor descriptor,
        ConstraintProgrammingSolution? solution,
        Int64? exactObjective) {
        SolverProvenance provenance = config.Provenance(descriptor);
        if (source.Provenance is { } linearProvenance) {
            provenance = provenance with {
                NativeVersion = linearProvenance.NativeVersion ?? provenance.NativeVersion,
                IgnoredParameters = Merge(
                    provenance.IgnoredParameters,
                    linearProvenance.IgnoredParameters),
                EnvironmentSummary = Merge(
                    provenance.EnvironmentSummary,
                    linearProvenance.EnvironmentSummary),
            };
        }
        AuditFingerprint configurationFingerprint =
            options.ConfigurationFingerprint?.Value ??
            SolveFingerprinting.Configuration(provenance.EffectiveParameters);
        SolveSolution<Int64>? exactSolution = solution is null
            ? null
            : new SolveSolution<Int64>(
                Values: new Solution<Int64>(
                    snapshot.Variables
                        .Select(variable => solution.Values[variable.Id])
                        .ToList()),
                Objective: exactObjective);
        return new SolveReport<Int64> {
            SchemaVersion = source.SchemaVersion,
            RunId = source.RunId,
            ProblemStatus = source.ProblemStatus,
            TerminationReason = source.TerminationReason,
            SolutionPresence = source.SolutionPresence,
            Solution = exactSolution,
            Proof = source.Proof with { Kind = "scip-cp" },
            Statistics = source.Statistics,
            Diagnostics = new SolveDiagnostics<Int64>(
                InfeasibilityEvidence: source.Diagnostics.InfeasibilityEvidence,
                Warnings: source.Diagnostics.Warnings,
                Errors: source.Diagnostics.Errors),
            Provenance = provenance,
            Fingerprints = new SolveFingerprints(
                Model: modelFingerprint,
                Configuration: configurationFingerprint,
                Solver: SolveFingerprinting.Solver(descriptor)),
        };
    }

    private static ConstraintProgrammingUnknownOutput UnknownOutput(
        ConstraintProgrammingSolveOptions options,
        SolverConfig config,
        AuditFingerprint modelFingerprint,
        SolverDescriptor descriptor,
        TerminationReason terminationReason) {
        SolverProvenance provenance = config.Provenance(descriptor);
        return new ConstraintProgrammingUnknownOutput(
            terminationReason: terminationReason,
            report: new SolveReport<Int64> {
                ProblemStatus = ProblemStatus.Unknown,
                TerminationReason = terminationReason,
                SolutionPresence = SolutionPresence.None,
                Provenance = provenance,
                Fingerprints = new SolveFingerprints(
                    Model: modelFingerprint,
                    Configuration: options.ConfigurationFingerprint?.Value ??
                        SolveFingerprinting.Configuration(
                            provenance.EffectiveParameters),
                    Solver: SolveFingerprinting.Solver(descriptor)),
            });
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ReportProgress(
        IProgress<ConstraintProgrammingProgress>? progress,
        string stage,
        int progressInStage,
        int overallProgress) {
        if (progress is null) {
            return Results.OkInstance;
        }
        try {
            progress.Report(new ConstraintProgrammingProgress(
                Stage: stage,
                ProgressInStage: progressInStage,
                OverallProgress: overallProgress));
            return Results.OkInstance;
        }
        catch (Exception error) when (
            error is not OutOfMemoryException and not AccessViolationException) {
            return Failure<Success>(
                code: ErrorCode.Other,
                message: $"SCIP CP 进度回调失败：{error.Message} / " +
                    $"SCIP CP progress callback failed: {error.Message}");
        }
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in left) {
            merged[key] = value;
        }
        foreach ((string key, string value) in right) {
            merged[key] = value;
        }
        return new ReadOnlyDictionary<string, string>(merged);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, U>(
        Result<U, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<U, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<U, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                code: ErrorCode.ApplicationError,
                message: "SCIP CP 结果状态无效 / Invalid SCIP CP result state"),
        };

    private static Result<T, ErrorCode, Error<ErrorCode>> Illegal<T>(string message) =>
        Failure<T>(code: ErrorCode.IllegalArgument, message: message);

    private static Result<Success, ErrorCode, Error<ErrorCode>> Illegal(string message) =>
        Illegal<Success>(message);

    private static Result<T, ErrorCode, Error<ErrorCode>> Unsupported<T>(string message) =>
        Failure<T>(code: ErrorCode.Other, message: $"Unsupported: {message}");

    private static Result<Success, ErrorCode, Error<ErrorCode>> Unsupported(
        string message) =>
        Unsupported<Success>(message);

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}
