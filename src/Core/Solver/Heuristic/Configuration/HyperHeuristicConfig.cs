// 超启发式配置 / Hyper-heuristic configuration.
// 与固定 Kotlin 基线 `hyper_heuristic/HyperHeuristicConfig.kt`（8838 B）对应。
// Mirrors the pinned baseline's `HyperHeuristicConfig.kt` (8838 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Configuration;

/// <summary>
/// 组件规格 / A component specification.
/// </summary>
/// <param name="Type">组件类型名 / The component type name.</param>
/// <param name="Parameters">参数 / The parameters.</param>
/// <param name="Version">版本 / The version.</param>
public sealed record ComponentSpec(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("parameters")] JsonObject? Parameters = null,
    [property: JsonPropertyName("version")] int Version = 1);

/// <summary>
/// 初始化模式 / The initialization mode.
/// </summary>
public enum InitializationMode {
    /// <summary>由模型初始化 / Initialize from the model.</summary>
    Model,

    /// <summary>由工作集初始化 / Initialize from the working set.</summary>
    WorkingSet,
}

/// <summary>
/// 导入模式 / The import mode.
/// </summary>
public enum ImportMode {
    /// <summary>原生导入 / Native import.</summary>
    Native,

    /// <summary>禁用导入 / Import disabled.</summary>
    Disabled,
}

/// <summary>
/// 算法配置 / An algorithm configuration.
/// </summary>
/// <param name="Id">标识 / The identity.</param>
/// <param name="Algorithm">算法组件 / The algorithm component.</param>
/// <param name="Size">种群规模 / The population size.</param>
/// <param name="Updates">更新算子 / The update operators.</param>
/// <param name="Mutations">变异算子 / The mutation operators.</param>
/// <param name="Neighborhoods">邻域算子 / The neighborhood operators.</param>
/// <param name="Parameters">参数源 / The parameter sources.</param>
/// <param name="Selection">选择策略 / The selection policy.</param>
/// <param name="Scalarization">标量化 / The scalarization.</param>
/// <param name="Children">子算法 / The child algorithms.</param>
/// <param name="Initialization">初始化模式 / The initialization mode.</param>
/// <param name="ImportMode">导入模式 / The import mode.</param>
/// <param name="Constraints">约束处理 / The constraint handling.</param>
public sealed record AlgorithmConfig(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("algorithm")] ComponentSpec Algorithm,
    [property: JsonPropertyName("size")] int Size = 20,
    [property: JsonPropertyName("updates")] IReadOnlyList<ComponentSpec>? Updates = null,
    [property: JsonPropertyName("mutations")] IReadOnlyList<ComponentSpec>? Mutations = null,
    [property: JsonPropertyName("neighborhoods")] IReadOnlyList<ComponentSpec>? Neighborhoods = null,
    [property: JsonPropertyName("parameters")] IReadOnlyDictionary<string, IReadOnlyList<ComponentSpec>>? Parameters = null,
    [property: JsonPropertyName("selection")] ComponentSpec? Selection = null,
    [property: JsonPropertyName("scalarization")] string? Scalarization = null,
    [property: JsonPropertyName("children")] IReadOnlyList<AlgorithmConfig>? Children = null,
    [property: JsonPropertyName("initialization")] InitializationMode Initialization = InitializationMode.Model,
    [property: JsonPropertyName("importMode")] ImportMode ImportMode = ImportMode.Native,
    [property: JsonPropertyName("constraints")] ConstraintHandlingConfig? Constraints = null);

/// <summary>
/// 执行模式 / The execution mode.
/// </summary>
public enum ExecutionMode {
    /// <summary>串行 / Serial.</summary>
    Serial,

    /// <summary>同步并行 / Synchronous parallelism.</summary>
    Synchronous,

    /// <summary>异步并行 / Asynchronous parallelism.</summary>
    Asynchronous,
}

/// <summary>
/// 学习模式 / The learning mode.
/// </summary>
public enum LearningMode {
    /// <summary>训练 / Train.</summary>
    Train,

    /// <summary>继续训练 / Continue.</summary>
    Continue,

    /// <summary>冻结 / Frozen.</summary>
    Frozen,
}

/// <summary>
/// 预算配置 / The budget configuration.
/// </summary>
/// <param name="Evaluations">评估上限 / The evaluation limit.</param>
/// <param name="Decisions">决策上限 / The decision limit.</param>
/// <param name="TimeMillis">时间上限（毫秒）/ The time limit in milliseconds.</param>
/// <param name="SliceEvaluations">片段评估额 / The slice evaluation allowance.</param>
/// <param name="SliceSteps">片段步数 / The slice step count.</param>
/// <param name="Allocator">预算分配器 / The budget allocator.</param>
/// <param name="Steps">步数上限 / The step limit.</param>
public sealed record BudgetConfig(
    [property: JsonPropertyName("evaluations")] long? Evaluations = null,
    [property: JsonPropertyName("decisions")] long? Decisions = null,
    [property: JsonPropertyName("timeMillis")] long? TimeMillis = null,
    [property: JsonPropertyName("sliceEvaluations")] long SliceEvaluations = 256L,
    [property: JsonPropertyName("sliceSteps")] long SliceSteps = 1L,
    [property: JsonPropertyName("allocator")] ComponentSpec? Allocator = null,
    [property: JsonPropertyName("steps")] long? Steps = null);

/// <summary>
/// 执行配置 / The execution configuration.
/// </summary>
/// <param name="Mode">执行模式 / The execution mode.</param>
/// <param name="Parallelism">并行度 / The parallelism.</param>
/// <param name="Seed">随机种子 / The random seed.</param>
public sealed record ExecutionConfig(
    [property: JsonPropertyName("mode")] ExecutionMode Mode = ExecutionMode.Synchronous,
    [property: JsonPropertyName("parallelism")] int Parallelism = 1,
    [property: JsonPropertyName("seed")] long Seed = 1L);

/// <summary>
/// 记忆配置 / The memory configuration.
/// </summary>
/// <param name="ArchiveCapacity">档案容量 / The archive capacity.</param>
/// <param name="WorkingCapacity">工作集容量 / The working-set capacity.</param>
/// <param name="Acceptance">接受策略 / The acceptance policy.</param>
/// <param name="Exchange">交换策略 / The exchange policy.</param>
public sealed record MemoryConfig(
    [property: JsonPropertyName("archiveCapacity")] int ArchiveCapacity = 100,
    [property: JsonPropertyName("workingCapacity")] int WorkingCapacity = 20,
    [property: JsonPropertyName("acceptance")] ComponentSpec? Acceptance = null,
    [property: JsonPropertyName("exchange")] ComponentSpec? Exchange = null);

/// <summary>
/// 学习配置 / The learning configuration.
/// </summary>
/// <param name="Mode">学习模式 / The learning mode.</param>
/// <param name="Features">特征提取器 / The feature extractor.</param>
/// <param name="Reward">奖励策略 / The reward policy.</param>
/// <param name="TrajectoryPath">轨迹路径 / The trajectory path.</param>
public sealed record LearningConfig(
    [property: JsonPropertyName("mode")] LearningMode Mode = LearningMode.Train,
    [property: JsonPropertyName("features")] ComponentSpec? Features = null,
    [property: JsonPropertyName("reward")] ComponentSpec? Reward = null,
    [property: JsonPropertyName("trajectoryPath")] string? TrajectoryPath = null);

/// <summary>
/// 持久化配置 / The persistence configuration.
/// </summary>
/// <param name="Enabled">是否启用 / Whether it is enabled.</param>
/// <param name="CheckpointPath">检查点路径 / The checkpoint path.</param>
/// <param name="EveryDecisions">检查点间隔 / The checkpoint interval.</param>
public sealed record PersistenceConfig(
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("checkpointPath")] string? CheckpointPath = null,
    [property: JsonPropertyName("everyDecisions")] long EveryDecisions = 0L);

/// <summary>
/// 超启发式配置 / A hyper-heuristic configuration.
/// </summary>
/// <remarks>
/// **`AlgorithmConfig.Size` 必须为正，且算法标识必须唯一并符合 `[A-Za-z0-9][A-Za-z0-9._-]*`**。只检查唯一性会让标识
/// 里出现路径分隔符或空白，而它在持久化时会被当作键、在日志里会被当作分隔符，**两处都以"标识看起来正常"的方式出错**。
/// **`AlgorithmConfig.Size` must be positive, and algorithm identities must be unique and match `[A-Za-z0-9][A-Za-z0-9._-]*`**.
/// Checking uniqueness alone lets identities contain path separators or blanks; they are then used as storage keys and log delimiters,
/// **and both fail in ways where "the identity looks normal"**.
/// **四个全局停止条件至少要给出一个**。全都留空会让搜索没有任何终止依据，而配置本身"看起来完整"——运行会一直跑到
/// 调用方放弃，**而不是报配置错误**。
/// **At least one of the four global stopping conditions must be given.** Leaving them all empty gives the search no termination
/// basis while the configuration "looks complete" — the run simply keeps going until the caller gives up, **instead of reporting a
/// configuration error**.
/// **自动检查点要求"已启用持久化"且"给出路径"**。只检查间隔会让一次自动检查点在运行时才失败，
/// **而失败点距离配置错误已经很远**。
/// **Automatic checkpoints require both "persistence enabled" and "a destination path".** Checking only the interval defers the failure
/// to run time, **far from the configuration mistake**.
/// **子算法不递归校验**。基线的 `validate` 只检查顶层；递归校验会让一份"顶层合法、子层有问题"的配置在父层报错，
/// **而错误信息指向的是父层标识**。这是与基线一致的**已知行为**，不是遗漏。
/// **Child algorithms are not validated recursively.** The baseline's `validate` checks only the top level; recursive validation would
/// report a "top-level legal, child broken" configuration against the parent, **with the error message pointing at the parent identity**.
/// This is **known behaviour** matching the baseline, not an omission.
/// </remarks>
public sealed record HyperHeuristicConfig(
    [property: JsonPropertyName("problem")] ComponentSpec Problem,
    [property: JsonPropertyName("algorithms")] IReadOnlyList<AlgorithmConfig> Algorithms,
    [property: JsonPropertyName("budget")] BudgetConfig Budget,
    [property: JsonPropertyName("selection")] ComponentSpec? Selection = null,
    [property: JsonPropertyName("execution")] ExecutionConfig? Execution = null,
    [property: JsonPropertyName("memory")] MemoryConfig? Memory = null,
    [property: JsonPropertyName("learning")] LearningConfig? Learning = null,
    [property: JsonPropertyName("persistence")] PersistenceConfig? Persistence = null,
    [property: JsonPropertyName("version")] int Version = 1) {
    private static readonly Regex IdentifierPattern =
        new("[A-Za-z0-9][A-Za-z0-9._-]*", RegexOptions.CultureInvariant);

    /// <summary>
    /// 校验配置 / Validate the configuration.
    /// </summary>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Version != 1) {
            return Invalid($"unsupported configuration version: {Version}");
        }
        // **反序列化缺失的必填字段会变成 `null`**：`Problem` 与 `Algorithms` 在记录上声明为不可空，但 `System.Text.Json`
        // 不会因此拒绝一份缺少它们的文档。必须先判空，否则 `Validate` 在 `Algorithms.Count` 上抛 `NullReferenceException`，
        // **调用方拿到的是未处理异常而不是"配置非法"**。
        // **Deserializing a missing required field yields `null`**: `Problem` and `Algorithms` are declared non-nullable on the record, but
        // `System.Text.Json` does not reject a document lacking them. They must be null-checked first, otherwise `Validate` throws
        // `NullReferenceException` on `Algorithms.Count`, **and the caller gets an unhandled exception instead of "illegal configuration"**.
        if (Problem is null || Algorithms is null) {
            return Invalid("configuration requires a problem and an algorithm list");
        }
        if (Algorithms.Count == 0) {
            return Invalid("algorithm IDs must be nonempty and unique; sizes must be positive");
        }
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (AlgorithmConfig algorithm in Algorithms) {
            // **`IsMatch` 是整体匹配还是前缀匹配取决于锚点**：正则里没有 `^`/`$` 时 `IsMatch` 只要求"含有一个合法片段"，
            // 于是 `"a b"` 会被判定为合法。必须显式加锚点。
            // **Whether `IsMatch` is a whole-string match depends on the anchors**: without `^`/`$` it only requires "contains one legal
            // fragment", so `"a b"` would pass. The anchors must be explicit.
            if (algorithm.Size <= 0
                || !IdentifierPattern.IsMatch(algorithm.Id)
                || !IdentifierPattern.Match(algorithm.Id).Value.Equals(algorithm.Id, StringComparison.Ordinal)
                || !identifiers.Add(algorithm.Id)) {
                return Invalid("algorithm IDs must be nonempty and unique; sizes must be positive");
            }
        }

        BudgetConfig budget = Budget;
        if (budget.Evaluations is null && budget.Decisions is null
            && budget.TimeMillis is null && budget.Steps is null) {
            return Invalid("a global stopping limit is required");
        }
        bool negative = false;
        foreach (long? limit in new[] { budget.Evaluations, budget.Decisions, budget.TimeMillis, budget.Steps }) {
            if (limit is not null && limit.Value < 0L) {
                negative = true;
            }
        }
        if (negative || budget.SliceEvaluations <= 0L || budget.SliceSteps <= 0L) {
            return Invalid("invalid budget");
        }

        ExecutionConfig execution = Execution ?? new ExecutionConfig();
        MemoryConfig memory = Memory ?? new MemoryConfig();
        if (execution.Parallelism <= 0 || memory.ArchiveCapacity <= 0 || memory.WorkingCapacity <= 0) {
            return Invalid("invalid capacities");
        }

        PersistenceConfig persistence = Persistence ?? new PersistenceConfig();
        if (persistence.EveryDecisions < 0L
            || (persistence.EveryDecisions > 0L
                && (!persistence.Enabled || persistence.CheckpointPath is null))) {
            return Invalid("automatic checkpoints require persistence and a destination");
        }
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 构造配置错误 / Build a configuration error.
    /// </summary>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Invalid(string message) =>
        SearchCodecSupport.SearchInvalid<Success>(message);
}

/// <summary>
/// 超启发式配置构造入口 / The hyper-heuristic configuration entry point.
/// </summary>
public static class HyperHeuristics {
    /// <summary>
    /// 从构建器构造配置 / Build a configuration from a builder.
    /// </summary>
    /// <param name="build">构建器回调 / The builder callback.</param>
    /// <returns>配置 / The configuration.</returns>
    public static Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> Create(
        Action<HyperHeuristicBuilder> build) {
        ArgumentNullException.ThrowIfNull(build);
        // **回调必须在边界内执行**（它可能抛异常），但 `Build` 已经返回 `Result`，不能再包一层。
        // 双层包装会得到 `Result<Result<...>>`，编译器只报"无法隐式转换"。
        // **The callback must run inside the boundary** (it may throw), but `Build` already returns a `Result` and must not be wrapped
        // again. Double wrapping yields `Result<Result<...>>`, and the compiler only reports "cannot convert implicitly".
        var builder = new HyperHeuristicBuilder();
        try {
            build(builder);
        } catch (Exception exception) {
            return Results.Failed<HyperHeuristicConfig>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                $"搜索边界失败 / Search boundary failed: {exception.Message}"));
        }
        return builder.Build();
    }
}

/// <summary>
/// 超启发式配置构建器 / A hyper-heuristic configuration builder.
/// </summary>
public sealed class HyperHeuristicBuilder {
    private ComponentSpec? _problem;
    private readonly List<AlgorithmConfig> _algorithms = new();
    private BudgetConfig? _budget;
    private ComponentSpec? _selection;
    private ExecutionConfig? _execution;
    private MemoryConfig? _memory;
    private LearningConfig? _learning;
    private PersistenceConfig? _persistence;

    /// <summary>设置问题 / Set the problem.</summary>
    /// <param name="type">问题类型 / The problem type.</param>
    /// <param name="parameters">参数 / The parameters.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder Problem(string type, JsonObject? parameters = null) {
        _problem = new ComponentSpec(type, parameters);
        return this;
    }

    /// <summary>追加一个算法 / Append an algorithm.</summary>
    /// <param name="config">算法配置 / The algorithm configuration.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder Algorithm(AlgorithmConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        _algorithms.Add(config);
        return this;
    }

    /// <summary>设置预算 / Set the budget.</summary>
    /// <param name="budget">预算配置 / The budget configuration.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder WithBudget(BudgetConfig budget) {
        ArgumentNullException.ThrowIfNull(budget);
        _budget = budget;
        return this;
    }

    /// <summary>设置选择策略 / Set the selection policy.</summary>
    /// <param name="selection">选择策略 / The selection policy.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder WithSelection(ComponentSpec selection) {
        ArgumentNullException.ThrowIfNull(selection);
        _selection = selection;
        return this;
    }

    /// <summary>设置执行配置 / Set the execution configuration.</summary>
    /// <param name="execution">执行配置 / The execution configuration.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder WithExecution(ExecutionConfig execution) {
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
        return this;
    }

    /// <summary>设置记忆配置 / Set the memory configuration.</summary>
    /// <param name="memory">记忆配置 / The memory configuration.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder WithMemory(MemoryConfig memory) {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        return this;
    }

    /// <summary>设置学习配置 / Set the learning configuration.</summary>
    /// <param name="learning">学习配置 / The learning configuration.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder WithLearning(LearningConfig learning) {
        ArgumentNullException.ThrowIfNull(learning);
        _learning = learning;
        return this;
    }

    /// <summary>设置持久化配置 / Set the persistence configuration.</summary>
    /// <param name="persistence">持久化配置 / The persistence configuration.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder WithPersistence(PersistenceConfig persistence) {
        ArgumentNullException.ThrowIfNull(persistence);
        _persistence = persistence;
        return this;
    }

    /// <summary>
    /// 构造并校验配置 / Build and validate the configuration.
    /// </summary>
    /// <returns>配置 / The configuration.</returns>
    public Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> Build() {
        // **必须给出 problem**：基线在 `build` 里直接拒绝缺失的 problem，而不是塞一个默认值。
        // 塞默认值会让一份"忘了声明问题"的配置被当成"用模型问题"的配置，**而运行时会拿一个空模型去求解**。
        // **A problem is required**: the baseline rejects a missing problem outright in `build` rather than substituting a default.
        // Substituting one turns "forgot to declare the problem" into "uses the model problem", **and the run then solves against an
        // empty model**.
        if (_problem is null) {
            return SearchCodecSupport.SearchInvalid<HyperHeuristicConfig>(
                "a registered problem is required");
        }
        var config = new HyperHeuristicConfig(
            _problem, new List<AlgorithmConfig>(_algorithms),
            _budget ?? new BudgetConfig(),
            _selection, _execution, _memory, _learning, _persistence);
        // **构建时即校验**：把校验推迟到调用点会让一份非法配置在构造阶段"成功"、在运行时才失败。
        // **Validate at build time**: deferring validation to the call site lets an illegal configuration "succeed" at construction and
        // only fail at run time.
        Result<Success, ErrorCode, Error<ErrorCode>> valid = config.Validate();
        return valid is Failed<Success, ErrorCode, Error<ErrorCode>> failed
            ? Results.Failed<HyperHeuristicConfig>(failed.Error)
            : Results.Ok(config);
    }

    /// <summary>
    /// 追加一个算法 / Append an algorithm.
    /// </summary>
    /// <param name="id">算法标识 / The algorithm identity.</param>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <param name="configure">算法构建回调 / The algorithm builder callback.</param>
    /// <returns>构建器 / The builder.</returns>
    public HyperHeuristicBuilder Algorithm(
        string id, string type, Action<AlgorithmBuilder>? configure = null) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(type);
        var builder = new AlgorithmBuilder(id, type);
        configure?.Invoke(builder);
        _algorithms.Add(builder.Build());
        return this;
    }
}

/// <summary>
/// 算法配置构建器 / An algorithm configuration builder.
/// </summary>
public sealed class AlgorithmBuilder {
    private readonly string _id;
    private readonly List<AlgorithmConfig> _children = new();

    /// <summary>
    /// 构造算法构建器 / Construct the algorithm builder.
    /// </summary>
    /// <param name="id">标识 / The identity.</param>
    /// <param name="type">算法类型 / The algorithm type.</param>
    public AlgorithmBuilder(string id, string type) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(type);
        _id = id;
        Component = new ComponentSpec(type);
    }

    /// <summary>算法组件 / The algorithm component.</summary>
    public ComponentSpec Component { get; set; }

    /// <summary>种群规模 / The population size.</summary>
    public int Size { get; set; } = 20;

    /// <summary>选择策略 / The selection policy.</summary>
    public ComponentSpec? Selection { get; set; }

    /// <summary>更新算子 / The update operators.</summary>
    public IReadOnlyList<ComponentSpec>? Updates { get; set; }

    /// <summary>变异算子 / The mutation operators.</summary>
    public IReadOnlyList<ComponentSpec>? Mutations { get; set; }

    /// <summary>邻域算子 / The neighborhood operators.</summary>
    public IReadOnlyList<ComponentSpec>? Neighborhoods { get; set; }

    /// <summary>参数源 / The parameter sources.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ComponentSpec>>? Parameters { get; set; }

    /// <summary>标量化 / The scalarization.</summary>
    public string? Scalarization { get; set; }

    /// <summary>初始化模式 / The initialization mode.</summary>
    public InitializationMode Initialization { get; set; } = InitializationMode.Model;

    /// <summary>导入模式 / The import mode.</summary>
    public ImportMode ImportMode { get; set; } = ImportMode.Native;

    /// <summary>
    /// 追加一个子算法 / Append a child algorithm.
    /// </summary>
    /// <param name="id">标识 / The identity.</param>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <param name="configure">子算法构建回调 / The child builder callback.</param>
    /// <returns>构建器 / The builder.</returns>
    public AlgorithmBuilder Algorithm(
        string id, string type, Action<AlgorithmBuilder>? configure = null) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(type);
        var builder = new AlgorithmBuilder(id, type);
        configure?.Invoke(builder);
        _children.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// 构造算法配置 / Build the algorithm configuration.
    /// </summary>
    /// <returns>算法配置 / The algorithm configuration.</returns>
    public AlgorithmConfig Build() => new(
        _id, Component, Size, Updates, Mutations, Neighborhoods, Parameters,
        Selection, Scalarization, new List<AlgorithmConfig>(_children),
        Initialization, ImportMode);
}
