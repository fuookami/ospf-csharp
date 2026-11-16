// 搜索注册表 / The search registry.
// 与固定 Kotlin 基线 `configuration/SearchRegistry.kt`（43259 B）的类骨架、组件字典、注册与查询方法、
// 以及内建组件注册对应；算法的装配与策略查询留到后续批次。
// Mirrors the pinned baseline's `configuration/SearchRegistry.kt` (43259 B): the class skeleton, the component dictionaries, the register and
// lookup methods, and the built-in component registrations. Algorithm assembly and the policy lookups follow in later batches.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Configuration;

/// <summary>
/// 算法装配所需的上下文 / The context an algorithm needs while being assembled.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Config">算法配置 / The algorithm configuration.</param>
/// <param name="Problem">问题边界 / The problem boundary.</param>
/// <param name="Random">随机源 / The random source.</param>
/// <param name="Bindings">算子绑定 / The operator bindings.</param>
public sealed record AlgorithmContext<O, V>(
    AlgorithmConfig Config,
    SearchProblem<O, V> Problem,
    SearchRandom Random,
    OperatorBindings Bindings)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 一个组件注册项 / One registered component.
/// </summary>
/// <typeparam name="T">组件类型 / The component type.</typeparam>
/// <param name="Stateless">是否无状态 / Whether it carries no state.</param>
/// <param name="Encodings">适用的编码 / The encodings it applies to.</param>
/// <param name="Factory">工厂 / The factory.</param>
public sealed record RegistryEntry<T>(
    bool Stateless,
    IReadOnlySet<SearchEncoding> Encodings,
    Func<ComponentSpec, Result<T, ErrorCode, Error<ErrorCode>>> Factory);

/// <summary>
/// 搜索注册表 / The search registry.
/// </summary>
/// <remarks>
/// **注册表的核心不是"查表"，而是"未知的一律拒绝"**。查不到时返回一个默认值，会让一个拼错的组件名
/// **安静地退化成另一个算法**——最典型的是 `mutation: "gaussain"` 退回默认变异，而结果曲线只是"平滑一点"。
///
/// **The registry's core is not "look up a table" but "reject everything unknown".** Returning a default on a miss makes a misspelt component
/// name **quietly degrade into a different algorithm** — the classic case being `mutation: "gaussain"` falling back to the default mutation while
/// the curve merely looks "a bit smoother".
///
/// **"无状态"不是优化标记，而是恢复语义的一部分**：无状态组件在检查点恢复后可以就地重建，有状态的必须自带恢复能力
/// （`IRecoverableComponent`）；两者都没有的组件，**恢复后会带着一份"看起来正常但已经过期"的内部状态继续跑**。
/// **"Stateless" is not an optimisation flag but part of the restore semantics**: a stateless component can be rebuilt in place after a
/// checkpoint restore, a stateful one must supply its own recovery (`IRecoverableComponent`), and a component with neither **resumes on an
/// internal state that looks fine but is stale**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class SearchRegistry<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Dictionary<string, Func<ComponentSpec, SearchRandom,
        Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>>>> _problems =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _statelessProblems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _statelessComponents = new(StringComparer.Ordinal) {
        "features:progress", "reward:improvement", "reward:cost-aware", "acceptance:not-worse", "acceptance:always",
        "exchange:none", "exchange:periodic", "allocator:fixed", "reward:time-normalized", "reward:dominance",
    };
    private readonly Dictionary<string, Func<AlgorithmContext<O, V>,
        Result<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>>>> _algorithms =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _constraintAlgorithms;
    private readonly Dictionary<string, RegistryEntry<ISolutionUpdate>> _updates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegistryEntry<IMutationOperator>> _mutations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegistryEntry<INeighborhood>> _neighborhoods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegistryEntry<IParameterSource>> _parameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<ComponentSpec, IReadOnlyList<string>, bool,
        Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>>>> _selectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IFeatureExtractor>> _featureFactories = new(StringComparer.Ordinal) {
        ["progress"] = () => new ProgressFeatures(),
    };
    private readonly Dictionary<string, Func<IRewardPolicy>> _rewardFactories = new(StringComparer.Ordinal) {
        ["improvement"] = () => new ImprovementRewardPolicy(),
        ["cost-aware"] = () => new CostAwareRewardPolicy(),
    };
    /// <remarks>
    /// **`not-worse` 不在这里默认注册**：`NotWorseAcceptancePolicy` 需要支配判据与目标坐标投影，
    /// 这两样属于**问题**而不属于**注册表**。在这里凭空造一个默认支配关系，会让"按配置选择的接受策略"
    /// **悄悄用一个和问题无关的判据**——两者都不报错，只是接受的东西不再是它该接受的。
    /// **`not-worse` is not registered by default here**: `NotWorseAcceptancePolicy` needs a dominance predicate and an objective-coordinate
    /// projection, both of which belong to the **problem**, not to the **registry**. Inventing a default dominance relation here would make
    /// "the acceptance policy chosen by configuration" **quietly use a criterion unrelated to the problem** — neither errors, the policy merely
    /// accepts things it should not.
    /// </remarks>
    private readonly Dictionary<string, Func<IMemoryAcceptance<O, V>>> _acceptanceFactories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<ComponentSpec,
        Result<IExchangePolicy<O, V>, ErrorCode, Error<ErrorCode>>>> _exchangeFactories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<ComponentSpec,
        Result<IBudgetAllocator, ErrorCode, Error<ErrorCode>>>> _allocatorFactories =
        new(StringComparer.Ordinal) {
            ["fixed"] = spec => RegistryParameters.Empty(spec)
                .Map(_ => (IBudgetAllocator)new FixedBudgetAllocator()),
        };

    /// <summary>
    /// 构造注册表 / Construct the registry.
    /// </summary>
    /// <param name="converter">值转换器 / The value converter.</param>
    public SearchRegistry(IIntoValue<V> converter) {
        ArgumentNullException.ThrowIfNull(converter);
        Converter = converter;
        _constraintAlgorithms = new HashSet<string>(BuiltInAlgorithms.NativeIds, StringComparer.Ordinal) {
            "nsga-ii", "nsga-iii",
        };
        RegisterBuiltIns();
    }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>档案裁剪策略 / The archive-trimming policy.</summary>
    public Func<IReadOnlyList<IIndividual<O, V>>, int, IReadOnlyList<IIndividual<O, V>>> ArchiveTrimmer {
        get;
        set;
    } = (values, amount) => {
        var trimmed = new List<IIndividual<O, V>>(values.Count);
        for (int index = 0; index < values.Count && index < amount; index++) {
            trimmed.Add(values[index]);
        }
        return trimmed;
    };

    /// <summary>
    /// 判断一个已构造的组件能否在恢复后重建 / Decide whether a built component can be rebuilt after a restore.
    /// </summary>
    /// <param name="kind">组件种类 / The component kind.</param>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="component">已构造的组件 / The built component.</param>
    /// <returns>能否重建 / Whether it can be rebuilt.</returns>
    public bool ComponentRecoverable(string kind, ComponentSpec spec, object component) {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(spec);
        return _statelessComponents.Contains(kind + ":" + spec.Type)
            || component is IRecoverableComponent;
    }

    /// <summary>
    /// 判断一个问题边界能否在恢复后重建 / Decide whether a problem boundary can be rebuilt after a restore.
    /// </summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>能否重建 / Whether it can be rebuilt.</returns>
    public bool RecoverableProblem(ComponentSpec spec, SearchProblem<O, V> problem) {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(problem);
        return _statelessProblems.Contains(spec.Type) || problem.State is not null;
    }

    /// <summary>注册一个问题边界 / Register a problem boundary.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="statelessCallbacks">回调是否无状态 / Whether the call-backs are stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterProblem(
        string id,
        bool statelessCallbacks,
        Func<ComponentSpec, SearchRandom, Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>>> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_problems, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (statelessCallbacks) {
            _statelessProblems.Add(id);
        }
        return Ok();
    }

    /// <summary>注册一个算法 / Register an algorithm.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="supportsConstraints">是否支持约束 / Whether it supports constraints.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterAlgorithm(
        string id,
        bool supportsConstraints,
        Func<AlgorithmContext<O, V>, Result<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>>> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_algorithms, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (supportsConstraints) {
            _constraintAlgorithms.Add(id);
        }
        return Ok();
    }

    /// <summary>注册一个更新算子 / Register an update operator.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="encodings">适用的编码 / The encodings it applies to.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterUpdate(
        string id,
        bool stateless,
        IReadOnlySet<SearchEncoding> encodings,
        Func<ComponentSpec, Result<ISolutionUpdate, ErrorCode, Error<ErrorCode>>> factory) =>
        Register(_updates, id, new RegistryEntry<ISolutionUpdate>(stateless, encodings, factory));

    /// <summary>注册一个变异算子 / Register a mutation operator.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="encodings">适用的编码 / The encodings it applies to.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterMutation(
        string id,
        bool stateless,
        IReadOnlySet<SearchEncoding> encodings,
        Func<ComponentSpec, Result<IMutationOperator, ErrorCode, Error<ErrorCode>>> factory) =>
        Register(_mutations, id, new RegistryEntry<IMutationOperator>(stateless, encodings, factory));

    /// <summary>注册一个邻域算子 / Register a neighbourhood operator.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="encodings">适用的编码 / The encodings it applies to.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterNeighborhood(
        string id,
        bool stateless,
        IReadOnlySet<SearchEncoding> encodings,
        Func<ComponentSpec, Result<INeighborhood, ErrorCode, Error<ErrorCode>>> factory) =>
        Register(_neighborhoods, id, new RegistryEntry<INeighborhood>(stateless, encodings, factory));

    /// <summary>注册一个参数源 / Register a parameter source.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterParameter(
        string id,
        bool stateless,
        Func<ComponentSpec, Result<IParameterSource, ErrorCode, Error<ErrorCode>>> factory) =>
        Register(_parameters, id, new RegistryEntry<IParameterSource>(stateless, AllEncodings, factory));

    /// <summary>注册一个选择策略 / Register a selection policy.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterSelection(
        string id,
        Func<ComponentSpec, IReadOnlyList<string>, bool,
            Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>>> factory) =>
        Register(_selectors, id, factory);

    /// <summary>注册一个特征提取器 / Register a feature extractor.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterFeatures(
        string id, bool stateless, Func<IFeatureExtractor> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_featureFactories, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (stateless) {
            _statelessComponents.Add("features:" + id);
        }
        return Ok();
    }

    /// <summary>注册一个回报策略 / Register a reward policy.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterReward(
        string id, bool stateless, Func<IRewardPolicy> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_rewardFactories, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (stateless) {
            _statelessComponents.Add("reward:" + id);
        }
        return Ok();
    }

    /// <summary>注册一个接受策略 / Register an acceptance policy.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterAcceptance(
        string id, bool stateless, Func<IMemoryAcceptance<O, V>> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_acceptanceFactories, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (stateless) {
            _statelessComponents.Add("acceptance:" + id);
        }
        return Ok();
    }

    /// <summary>注册一个交换策略 / Register an exchange policy.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterExchange(
        string id, bool stateless,
        Func<ComponentSpec, Result<IExchangePolicy<O, V>, ErrorCode, Error<ErrorCode>>> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_exchangeFactories, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (stateless) {
            _statelessComponents.Add("exchange:" + id);
        }
        return Ok();
    }

    /// <summary>注册一个预算分配器 / Register a budget allocator.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="stateless">是否无状态 / Whether it is stateless.</param>
    /// <param name="factory">工厂 / The factory.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> RegisterAllocator(
        string id, bool stateless,
        Func<ComponentSpec, Result<IBudgetAllocator, ErrorCode, Error<ErrorCode>>> factory) {
        Result<Success, ErrorCode, Error<ErrorCode>> registered = Register(_allocatorFactories, id, factory);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            return registered;
        }
        if (stateless) {
            _statelessComponents.Add("allocator:" + id);
        }
        return Ok();
    }

    /// <summary>构造一个预算分配器 / Build a budget allocator.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>分配器 / The allocator.</returns>
    public Result<IBudgetAllocator, ErrorCode, Error<ErrorCode>> Allocator(ComponentSpec spec) =>
        Boundary(_allocatorFactories, spec, "allocator");

    /// <summary>构造一个问题边界 / Build a problem boundary.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <returns>问题边界 / The problem boundary.</returns>
    public Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> Problem(
        ComponentSpec spec, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Version != 1) {
            return Failed<SearchProblem<O, V>>("unsupported problem version");
        }
        return Boundary(_problems, spec, "problem", random);
    }

    /// <summary>构造一个特征提取器 / Build a feature extractor.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>特征提取器 / The feature extractor.</returns>
    public Result<IFeatureExtractor, ErrorCode, Error<ErrorCode>> Features(ComponentSpec spec) {
        ArgumentNullException.ThrowIfNull(spec);
        Result<Success, ErrorCode, Error<ErrorCode>> validated = RegistryParameters.Empty(spec);
        if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<IFeatureExtractor>(failure.Error);
        }
        if (!_featureFactories.TryGetValue(spec.Type, out Func<IFeatureExtractor>? factory)) {
            return Failed<IFeatureExtractor>("unknown features: " + spec.Type);
        }
        try {
            return Results.Ok(factory());
        } catch (Exception exception) {
            return Results.Failed<IFeatureExtractor>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
        }
    }

    /// <summary>构造一个交换策略 / Build an exchange policy.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>交换策略 / The exchange policy.</returns>
    public Result<IExchangePolicy<O, V>, ErrorCode, Error<ErrorCode>> Exchange(ComponentSpec spec) =>
        Boundary(_exchangeFactories, spec, "exchange");

    /// <summary>构造一个更新算子 / Build an update operator.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="encoding">当前编码 / The current encoding.</param>
    /// <returns>更新算子 / The update operator.</returns>
    public Result<ISolutionUpdate, ErrorCode, Error<ErrorCode>> Update(
        ComponentSpec spec, SearchEncoding encoding) =>
        EntryLookup(_updates, spec, encoding, "update");

    /// <summary>构造一个变异算子 / Build a mutation operator.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="encoding">当前编码 / The current encoding.</param>
    /// <returns>变异算子 / The mutation operator.</returns>
    public Result<IMutationOperator, ErrorCode, Error<ErrorCode>> Mutation(
        ComponentSpec spec, SearchEncoding encoding) =>
        EntryLookup(_mutations, spec, encoding, "mutation");

    /// <summary>构造一个邻域算子 / Build a neighbourhood operator.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="encoding">当前编码 / The current encoding.</param>
    /// <returns>邻域算子 / The neighbourhood operator.</returns>
    public Result<INeighborhood, ErrorCode, Error<ErrorCode>> Neighborhood(
        ComponentSpec spec, SearchEncoding encoding) =>
        EntryLookup(_neighborhoods, spec, encoding, "neighborhood");

    /// <summary>构造一个参数源 / Build a parameter source.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>参数源 / The parameter source.</returns>
    public Result<IParameterSource, ErrorCode, Error<ErrorCode>> Parameter(ComponentSpec spec) =>
        EntryLookup(_parameters, spec, null, "parameter");

    /// <summary>
    /// 查询一个带编码约束的注册项 / Look up an entry that carries an encoding restriction.
    /// </summary>
    /// <remarks>
    /// **编码不匹配必须失败，而不是"回退到默认算子"**。回退会让一个专为排列设计的交叉算子被用在连续问题上，
    /// **而它照样能产出一个解**——只是那个解的语义已经不对了。
    /// **An encoding mismatch must fail rather than "fall back to a default operator".** A fallback applies a permutation-only crossover to a
    /// continuous problem, **and it still produces a solution** — one whose meaning is no longer what it claims.
    /// </remarks>
    /// <typeparam name="T">组件类型 / The component type.</typeparam>
    /// <param name="table">字典 / The dictionary.</param>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="encoding">当前编码；空表示不限制 / The current encoding; null means unrestricted.</param>
    /// <param name="kind">种类名 / The kind name.</param>
    /// <returns>组件 / The component.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> EntryLookup<T>(
        Dictionary<string, RegistryEntry<T>> table,
        ComponentSpec spec,
        SearchEncoding? encoding,
        string kind) {
        ArgumentNullException.ThrowIfNull(spec);
        if (!table.TryGetValue(spec.Type, out RegistryEntry<T>? entry)) {
            return Failed<T>("unknown " + kind + ": " + spec.Type);
        }
        if (encoding is not null && !entry.Encodings.Contains(encoding.Value)) {
            return Failed<T>(kind + " " + spec.Type + " does not support " + encoding.Value + " encoding");
        }
        return entry.Factory(spec);
    }

    /// <summary>
    /// 查表并构造，未知时失败 / Look up and build, failing on a miss.
    /// </summary>
    /// <param name="table">字典 / The dictionary.</param>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="kind">种类名 / The kind name.</param>
    /// <returns>组件 / The component.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Boundary<T>(
        Dictionary<string, Func<ComponentSpec, Result<T, ErrorCode, Error<ErrorCode>>>> table,
        ComponentSpec spec, string kind) {
        ArgumentNullException.ThrowIfNull(spec);
        return table.TryGetValue(spec.Type,
                out Func<ComponentSpec, Result<T, ErrorCode, Error<ErrorCode>>>? factory)
            ? factory(spec)
            : Failed<T>("unknown " + kind + ": " + spec.Type);
    }

    /// <summary>
    /// 带随机源的查表 / Look up with a random source.
    /// </summary>
    /// <param name="table">字典 / The dictionary.</param>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="kind">种类名 / The kind name.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <typeparam name="T">组件类型 / The component type.</typeparam>
    /// <returns>组件 / The component.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Boundary<T>(
        Dictionary<string, Func<ComponentSpec, SearchRandom, Result<T, ErrorCode, Error<ErrorCode>>>> table,
        ComponentSpec spec, string kind, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(random);
        return table.TryGetValue(spec.Type,
                out Func<ComponentSpec, SearchRandom, Result<T, ErrorCode, Error<ErrorCode>>>? factory)
            ? factory(spec, random)
            : Failed<T>("unknown " + kind + ": " + spec.Type);
    }

    /// <summary>注册并拒绝重复标识 / Register, rejecting duplicate identifiers.</summary>
    /// <typeparam name="T">值类型 / The value type.</typeparam>
    /// <param name="table">字典 / The dictionary.</param>
    /// <param name="id">标识 / The identifier.</param>
    /// <param name="value">值 / The value.</param>
    /// <returns>注册结果 / The registration result.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Register<T>(
        Dictionary<string, T> table, string id, T value) {
        ArgumentNullException.ThrowIfNull(id);
        if (!table.TryAdd(id, value)) {
            return Failed<Success>("duplicate registration: " + id);
        }
        return Ok();
    }

    /// <summary>全部编码 / All encodings.</summary>
    private static IReadOnlySet<SearchEncoding> AllEncodings { get; } =
        new HashSet<SearchEncoding>(new[] {
            SearchEncoding.Continuous, SearchEncoding.Permutation, SearchEncoding.Custom,
        });

    /// <summary>只有连续编码 / Continuous only.</summary>
    private static IReadOnlySet<SearchEncoding> ContinuousOnly { get; } =
        new HashSet<SearchEncoding>(new[] { SearchEncoding.Continuous });

    /// <summary>成功 / Success.</summary>
    /// <returns>成功结果 / The success.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Ok() => Results.Ok(new Success());

    /// <summary>失败 / A failure.</summary>
    /// <typeparam name="T">值类型 / The value type.</typeparam>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>
    /// 注册内建组件 / Register the built-in components.
    /// </summary>
    /// <remarks>
    /// **这里的每个工厂都先过 `RegistryParameters` 再构造**。先构造后校验会让"参数写错"变成"构造出半个对象"，
    /// 而先校验后构造保证**要么得到一个参数完全合法的组件，要么一个都不要**。
    /// **Every factory here validates through `RegistryParameters` before constructing.** Constructing first turns a wrong parameter into
    /// "half an object", while validating first guarantees **either a component whose parameters are entirely legal, or none at all**.
    /// </remarks>
    private void RegisterBuiltIns() {
        RegisterUpdate("single-point", true, ContinuousOnly,
            spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)SinglePointCross.Instance));
        RegisterUpdate("arithmetic", true, ContinuousOnly,
            spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)ArithmeticCross.Instance));
        RegisterUpdate("order", true, AllEncodings,
            spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)OrderCross.Instance));
        RegisterUpdate("cycle", true, AllEncodings,
            spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)CycleCross.Instance));
        RegisterUpdate("sbx", true, ContinuousOnly, spec =>
            RegistryParameters.Numeric(spec, Keys("eta")).Map(_ => (ISolutionUpdate)new SimulatedBinaryCross(
                new Flt64(Number(spec, "eta", 20.0)))));
        RegisterUpdate("opposition", true, ContinuousOnly,
            spec => RegistryParameters.Empty(spec).Map(_ => (ISolutionUpdate)new OppositionUpdate()));

        RegisterMutation("none", true, AllEncodings,
            spec => RegistryParameters.Empty(spec).Map(_ => (IMutationOperator)NoMutation.Instance));
        RegisterMutation("gaussian", true, ContinuousOnly, spec =>
            RegistryParameters.Numeric(spec, Keys("sigma")).Map(_ => (IMutationOperator)new GaussianMutation(
                new Flt64(Number(spec, "sigma", 0.1)))));
        RegisterMutation("polynomial", true, ContinuousOnly,
            spec => RegistryParameters.Empty(spec).Map(_ => (IMutationOperator)new PolynomialMutation()));
        RegisterMutation("swap", true, AllEncodings,
            spec => RegistryParameters.Empty(spec).Map(_ => (IMutationOperator)new SwapMutation()));
        RegisterMutation("inversion", true, AllEncodings,
            spec => RegistryParameters.Empty(spec).Map(_ => (IMutationOperator)new InversionMutation()));
        RegisterMutation("levy", true, ContinuousOnly,
            spec => RegistryParameters.Empty(spec).Map(_ => (IMutationOperator)new LevyMutation()));

        RegisterNeighborhood("perturbation", true, ContinuousOnly, spec =>
            RegistryParameters.Numeric(spec, Keys("amount")).Map(_ => (INeighborhood)new RandomPerturbation(
                (int)Number(spec, "amount", 1.0))));

        RegisterParameter("constant", true, spec =>
            RegistryParameters.Numeric(spec, Keys("value")).Map(_ => (IParameterSource)new ConstantSource(
                new Flt64(Number(spec, "value", 1.0)))));
        RegisterParameter("linear", true, spec =>
            RegistryParameters.Numeric(spec, Keys("from", "to", "limit")).Map(_ => (IParameterSource)new LinearSource(
                new Flt64(Number(spec, "from", 1.0)),
                new Flt64(Number(spec, "to", 0.0)),
                (ulong)Number(spec, "limit", 100.0))));
        RegisterParameter("geometric", true, spec =>
            RegistryParameters.Numeric(spec, Keys("initial", "ratio")).Map(_ => (IParameterSource)new GeometricSource(
                new Flt64(Number(spec, "initial", 100.0)),
                new Flt64(Number(spec, "ratio", 0.95)))));

        RegisterReward("dominance", true, () => new DominanceReward());
        RegisterExchange("none", true, spec =>
            RegistryParameters.Empty(spec).Map(_ => (IExchangePolicy<O, V>)new NoExchangePolicy<O, V>()));
        RegisterExchange("periodic", true, spec => {
            Result<Success, ErrorCode, Error<ErrorCode>> validated =
                RegistryParameters.Numeric(spec, Keys("period", "amount"));
            if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<IExchangePolicy<O, V>>(failure.Error);
            }
            int period = (int)Number(spec, "period", 5.0);
            int amount = (int)Number(spec, "amount", 1.0);
            if (period <= 0 || amount <= 0) {
                return Failed<IExchangePolicy<O, V>>("invalid exchange period/amount");
            }
            return Results.Ok<IExchangePolicy<O, V>>(new PeriodicExchangePolicy<O, V>(period, amount));
        });
    }

    /// <summary>
    /// 构造一个选择策略 / Build a selection policy.
    /// </summary>
    /// <remarks>
    /// **未注册的类型不会失败，而是退化成表格策略**——这是基线里唯一的"回退"，也是唯一一处值得解释的例外：
    /// 表格策略本身就是"用通用表格近似任意选择器"，退化后仍然是**同一族行为**，且参数仍然全部受校验。
    /// 这与"未知组件退回默认算子"不同：后者是**换了一个语义完全不同的东西**。
    /// **An unregistered type does not fail but degrades into the tabular policy** — the only "fallback" in the baseline, and the only one worth
    /// explaining: the tabular policy is itself "approximate any selector with a generic table", so the degenerate case stays **in the same family**
    /// and its parameters are still fully validated. That differs from "an unknown component falls back to a default operator", which **swaps in
    /// something with entirely different semantics**.
    /// </remarks>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="actions">可选动作 / The available actions.</param>
    /// <param name="learning">是否学习 / Whether it learns.</param>
    /// <returns>选择策略 / The selection policy.</returns>
    public Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>> Selection(
        ComponentSpec spec, IReadOnlyList<string> actions, bool learning) {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(actions);
        if (spec.Version != 1) {
            return Failed<ISelectionPolicy>("unsupported selector version");
        }
        if (_selectors.TryGetValue(spec.Type, out Func<ComponentSpec, IReadOnlyList<string>, bool, Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>>>? factory)) {
            try {
                return factory(spec, actions, learning);
            } catch (Exception exception) {
                return Results.Failed<ISelectionPolicy>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> validated = RegistryParameters.Numeric(
            spec, Keys("epsilon", "alpha", "gamma", "states", "stagnationLimit"));
        if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<ISelectionPolicy>(failure.Error);
        }
        var policy = new TabularSelectionPolicy(
            spec.Type,
            actions,
            learning,
            Number(spec, "epsilon", 0.1),
            Number(spec, "alpha", 0.5),
            Number(spec, "gamma", 0.9),
            (int)Number(spec, "states", 64.0),
            (long)Number(spec, "stagnationLimit", 10.0));
        Result<Success, ErrorCode, Error<ErrorCode>> policyValid = policy.Validate();
        return policyValid is Failed<Success, ErrorCode, Error<ErrorCode>> policyFailure
            ? Results.Failed<ISelectionPolicy>(policyFailure.Error)
            : Results.Ok<ISelectionPolicy>(policy);
    }

    /// <summary>
    /// 构造一个接受策略 / Build an acceptance policy.
    /// </summary>
    /// <remarks>
    /// **支配判据由目标坐标重建，而不是取问题自带的 `Dominates` 闭包**。`Dominates` 接受的是 `ObjValue`，
    /// 而接受策略拿到的是坐标向量——**没有从向量回到 `ObjValue` 的通路**，硬要接就得把整个档案带着走。
    ///
    /// 因此这里按坐标做标准 Pareto 支配（全部分量不劣、且至少一个分量更优，按最小化）。
    /// **这与问题的判据在单目标下等价**（只有一个分量，退化就是"严格更小"），
    /// **在多目标下要求坐标确实是标准 Pareto 语义**；若问题用的是别的支配关系（例如带容差或加权），
    /// 必须在调用处显式注册自己的接受策略——**不在这里猜**。
    ///
    /// **The dominance predicate is rebuilt from the objective coordinates rather than taking the problem's own `Dominates` closure.**
    /// `Dominates` accepts `ObjValue`, while an acceptance policy holds coordinate vectors — **there is no path back from a vector to `ObjValue`**, and
    /// wiring one through would mean dragging the whole archive along.
    ///
    /// Standard Pareto dominance is therefore computed over the coordinates (every component no worse, at least one strictly better, minimizing).
    /// **This equals the problem's predicate for a scalar objective** (one component degenerates to "strictly smaller") and **requires the coordinates
    /// to carry standard Pareto semantics in the multi-objective case**; a problem using a different dominance relation (toleration, weighting) must
    /// register its own acceptance policy at the call site — **it is not guessed here**.
    /// </remarks>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="horizon">时域 / The horizon.</param>
    /// <returns>接受策略 / The acceptance policy.</returns>
    public Result<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>> Acceptance(
        ComponentSpec spec, SearchProblem<O, V> problem, long horizon = long.MaxValue) {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(problem);
        if (spec.Version != 1) {
            return Failed<IMemoryAcceptance<O, V>>("unsupported acceptance version");
        }
        switch (spec.Type) {
            case "auto": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Empty(spec);
                if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> autoFailure) {
                    return Results.Failed<IMemoryAcceptance<O, V>>(autoFailure.Error);
                }
                if (problem.ObjectiveCoordinates is not Func<O, IReadOnlyList<Flt64>> coordinates) {
                    return Failed<IMemoryAcceptance<O, V>>(
                        "auto acceptance requires objective coordinates");
                }
                return Results.Ok<IMemoryAcceptance<O, V>>(new AutoAcceptance<O, V>(
                    SearchCodecs.List(problem.ObjectiveCodec),
                    new AnyArityMinimizationDominance(),
                    coordinates,
                    horizon));
            }
            // **`not-worse` 是基线的默认接受策略**（`HyperHeuristicConfig` 的 `acceptance` 默认值），
            // 因此它必须在注册表里能构造出来，否则**默认配置根本跑不起来**。
            //
 // earlier/226 曾以"注册表不该替问题做主"为由把它留空。**那个理由站不住**：`Acceptance` 本来就接收
            // `SearchProblem`，支配判据与坐标投影都是就地可得的；留空反而让默认配置不可用。
            // **`not-worse` is the baseline's default acceptance policy** (`HyperHeuristicConfig`'s `acceptance` default), so it must be
            // constructible from the registry — otherwise **the default configuration cannot run at all**.
            //
 // earlier/226 left it out on the grounds that "the registry must not decide for the problem". **That reason does not hold**:
            // `Acceptance` already receives the `SearchProblem`, so the dominance predicate and the coordinate projection are right there; leaving it
            // out only made the default configuration unusable.
            case "not-worse": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Empty(spec);
                if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> notWorseFailure) {
                    return Results.Failed<IMemoryAcceptance<O, V>>(notWorseFailure.Error);
                }
                if (problem.ObjectiveCoordinates is not Func<O, IReadOnlyList<Flt64>> notWorseCoordinates) {
                    return Failed<IMemoryAcceptance<O, V>>(
                        "not-worse acceptance requires objective coordinates");
                }
                return Results.Ok<IMemoryAcceptance<O, V>>(
                    new NotWorseAcceptancePolicy<O, V>(
                        new AnyArityMinimizationDominance(), notWorseCoordinates));
            }
                        case "late": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Numeric(spec, Keys("length"));
                if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> lateFailure) {
                    return Results.Failed<IMemoryAcceptance<O, V>>(lateFailure.Error);
                }
                if (problem.MultiObjective) {
                    return Failed<IMemoryAcceptance<O, V>>(
                        "late acceptance requires a scalar objective");
                }
                if (problem.ObjectiveCoordinates is not Func<O, IReadOnlyList<Flt64>> lateCoordinates) {
                    return Failed<IMemoryAcceptance<O, V>>(
                        "late acceptance requires objective coordinates");
                }
                return Results.Ok<IMemoryAcceptance<O, V>>(new LateAcceptance<O, V>(
                    SearchCodecs.List(problem.ObjectiveCodec),
                    new AnyArityMinimizationDominance(),
                    lateCoordinates,
                    false,
                    (int)Number(spec, "length", 50.0)));
            }
            case "record-to-record": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Numeric(spec, Keys("initialRatio", "horizon"));
                if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> recordFailure) {
                    return Results.Failed<IMemoryAcceptance<O, V>>(recordFailure.Error);
                }
                double ratio = Number(spec, "initialRatio", 0.05);
                if (problem.MultiObjective
                    || problem.ObjectiveCoordinates is not Func<O, IReadOnlyList<Flt64>> recordCoordinates
                    || ratio < 0.0 || ratio > 1.0) {
                    return Failed<IMemoryAcceptance<O, V>>(
                        "record acceptance requires scalar coordinates and a ratio in [0,1]");
                }
                return Results.Ok<IMemoryAcceptance<O, V>>(new RecordToRecordAcceptance<O, V>(
                    recordCoordinates,
                    false,
                    ratio,
                    (long)Number(spec, "horizon", 1000.0)));
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> empty = RegistryParameters.Empty(spec);
        if (empty is Failed<Success, ErrorCode, Error<ErrorCode>> emptyFailure) {
            return Results.Failed<IMemoryAcceptance<O, V>>(emptyFailure.Error);
        }
        if (!_acceptanceFactories.TryGetValue(spec.Type, out Func<IMemoryAcceptance<O, V>>? registered)) {
            return Failed<IMemoryAcceptance<O, V>>("unknown acceptance: " + spec.Type);
        }
        try {
            return Results.Ok(registered());
        } catch (Exception exception) {
            return Results.Failed<IMemoryAcceptance<O, V>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
        }
    }

    /// <summary>
    /// 按配置装配一个搜索会话 / Assemble a search session from its configuration.
    /// </summary>
    /// <remarks>
    /// **先校验参数，再装配**。反过来会让"参数写错"变成"装配出一半"，而调用方拿到的错误会指向算法而不是配置。
    /// 装配本身只走 `BuiltInAlgorithms` 的原生分支；**组合式装配（把多个算法装成一个）不在本方法职责内**，
    /// 遇到组合标识时明确说"未实现"，而不是退化成原生算法。
    ///
    /// **Parameters are validated before assembly.** The other order turns a wrong parameter into "half an assembly", and the error the caller sees
    /// points at the algorithm rather than at the configuration. Assembly covers only `BuiltInAlgorithms`' native branches; **composed assembly
    /// (several algorithms into one) is not this method's job**, and a composed identifier is reported as "not implemented" rather than degraded into
    /// a native algorithm.
    /// </remarks>
    /// <param name="config">算法配置 / The algorithm configuration.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <param name="bindings">算子绑定 / The operator bindings.</param>
    /// <returns>搜索会话 / The search session.</returns>
    public Result<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>> Create(
        AlgorithmConfig config,
        SearchProblem<O, V> problem,
        SearchRandom random,
        OperatorBindings bindings) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(bindings);
        Result<Success, ErrorCode, Error<ErrorCode>> validated =
            RegistryParameters.ValidateAlgorithm(config);
        if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<ISearchSession<O, V>>(failure.Error);
        }
        string type = config.Algorithm.Type;
        if (!BuiltInAlgorithms.NativeIds.Contains(type)) {
            return Failed<ISearchSession<O, V>>(
                "algorithm '" + type + "' is not a native algorithm; composed assembly is not implemented");
        }
        return BuiltInAlgorithms.Create(
            new AlgorithmContext<O, V>(config, problem, random, bindings), Converter);
    }

    /// <summary>
    /// 构造一个回报策略 / Build a reward policy.
    /// </summary>
    /// <remarks>
    /// **`time-normalized` 的分母是"最小奖励时长"，它必须为正**。取 0 会让"每秒奖励"变成除零，
    /// **而回报表面上仍然是"一个数"**——只在极端情况下变成无穷大，随后所有比较都变成"不更好"。
    /// **`time-normalized`'s denominator is the "minimum reward duration" and must be positive.** Zero turns "reward per second" into a division
    /// by zero **while the reward still looks like "a number"** — it only becomes infinite in the extreme case, after which every comparison says
    /// "not better".
    /// </remarks>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>回报策略 / The reward policy.</returns>
    public Result<IRewardPolicy, ErrorCode, Error<ErrorCode>> Reward(ComponentSpec spec) {
        ArgumentNullException.ThrowIfNull(spec);
        switch (spec.Type) {
            case "time-normalized": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Numeric(spec, Keys("epsilonSeconds"));
                if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<IRewardPolicy>(failure.Error);
                }
                double epsilon = Number(spec, "epsilonSeconds", 1e-9);
                return epsilon <= 0.0
                    ? Failed<IRewardPolicy>("invalid minimum reward duration")
                    : Results.Ok<IRewardPolicy>(new TimeNormalizedReward(epsilon));
            }
            case "extreme-value": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Numeric(spec, Keys("window"));
                return validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure
                    ? Results.Failed<IRewardPolicy>(failure.Error)
                    : Results.Ok<IRewardPolicy>(new ExtremeValueReward(
                        (int)Number(spec, "window", 50.0)));
            }
            case "dominance": {
                Result<Success, ErrorCode, Error<ErrorCode>> validated =
                    RegistryParameters.Empty(spec);
                return validated is Failed<Success, ErrorCode, Error<ErrorCode>> failure
                    ? Results.Failed<IRewardPolicy>(failure.Error)
                    : Results.Ok<IRewardPolicy>(new DominanceReward());
            }
            case "hypervolume-delta": {
                return HypervolumeReward(spec);
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> empty = RegistryParameters.Empty(spec);
        if (empty is Failed<Success, ErrorCode, Error<ErrorCode>> emptyFailure) {
            return Results.Failed<IRewardPolicy>(emptyFailure.Error);
        }
        if (!_rewardFactories.TryGetValue(spec.Type, out Func<IRewardPolicy>? registered)) {
            return Failed<IRewardPolicy>("unknown reward: " + spec.Type);
        }
        try {
            return Results.Ok(registered());
        } catch (Exception exception) {
            return Results.Failed<IRewardPolicy>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
        }
    }

    /// <summary>
    /// 构造超体积回报 / Build the hypervolume-delta reward.
    /// </summary>
    /// <remarks>
    /// **参考点有两种写法且互斥**：整体数组 `reference`，或分量形式 `referenceX` / `referenceY`。
    /// 两种同时给出、只给其中一个分量、或把参考点写成非数组，都必须拒绝——
    /// **否则"参考点缺失"会安静地变成一个空参考点，超体积恒为 0，而回报曲线只是"一直是 0"**。
    /// **The reference point has two mutually exclusive spellings**: the whole array `reference`, or the component form `referenceX` /
    /// `referenceY`. Supplying both, only one component, or a non-array reference must all be rejected — **otherwise a missing reference point
    /// quietly becomes an empty one, the hypervolume stays zero, and the reward curve merely "reads zero throughout"**.
    /// </remarks>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>回报策略 / The reward policy.</returns>
    private static Result<IRewardPolicy, ErrorCode, Error<ErrorCode>> HypervolumeReward(
        ComponentSpec spec) {
        // **不能复用 `RegistryParameters.Numeric`**：它会要求每个值都是有限数值，而 `reference` 是一个数组。
        // 超体积有自己的一套校验：版本、未知键、以及除 `reference` 外的标量。
        // **`RegistryParameters.Numeric` cannot be reused here**: it demands every value be a finite number, while `reference` is an array.
        // Hypervolume has its own validation: the version, the unknown keys, and every scalar except `reference`.
        if (spec.Version != 1) {
            return Failed<IRewardPolicy>(
                "超体积配置无效 / Invalid hypervolume configuration");
        }
        System.Text.Json.Nodes.JsonObject values =
            spec.Parameters ?? new System.Text.Json.Nodes.JsonObject();
        IReadOnlySet<string> allowedKeys = Keys("reference", "referenceX", "referenceY", "mode",
            "maxWork", "samples", "seed", "calibrationObservations");
        foreach ((string key, System.Text.Json.Nodes.JsonNode? node) in values) {
            if (!allowedKeys.Contains(key)) {
                return Failed<IRewardPolicy>(
                    "超体积配置无效 / Invalid hypervolume configuration");
            }
            if (key != "reference" && key != "mode"
                && !TryParseDouble(node, out _)) {
                return Failed<IRewardPolicy>(
                    "超体积配置无效 / Invalid hypervolume configuration");
            }
        }
        bool hasReference = values.ContainsKey("reference");
        bool hasX = values.ContainsKey("referenceX");
        bool hasY = values.ContainsKey("referenceY");
        if (hasReference && (hasX || hasY)) {
            return Failed<IRewardPolicy>("超体积配置无效 / Invalid hypervolume configuration");
        }
        if (hasX != hasY) {
            return Failed<IRewardPolicy>("超体积配置无效 / Invalid hypervolume configuration");
        }
        var reference = new List<Flt64>();
        if (hasReference) {
            if (values["reference"] is not System.Text.Json.Nodes.JsonArray array) {
                return Failed<IRewardPolicy>(
                    "超体积参考点必须为数组 / Hypervolume reference must be an array");
            }
            foreach (System.Text.Json.Nodes.JsonNode? item in array) {
                if (!TryParseDouble(item, out double number)) {
                    return Failed<IRewardPolicy>(
                        "超体积参考点无效 / Invalid hypervolume reference");
                }
                reference.Add(new Flt64(number));
            }
        } else if (hasX) {
            foreach (string key in new[] { "referenceX", "referenceY" }) {
                if (!TryParseDouble(values[key], out double number)) {
                    return Failed<IRewardPolicy>(
                        "超体积参考点无效 / Invalid hypervolume reference");
                }
                reference.Add(new Flt64(number));
            }
        }
        string modeText = values["mode"] is null && !values.ContainsKey("mode")
            ? "exact"
            : values["mode"] is System.Text.Json.Nodes.JsonValue modeValue
                && modeValue.TryGetValue(out string? text) ? text ?? "invalid" : "invalid";
        HypervolumeMode mode;
        if (modeText == "exact") {
            mode = HypervolumeMode.Exact;
        } else if (modeText == "sampled") {
            mode = HypervolumeMode.Sampled;
        } else {
            return Failed<IRewardPolicy>("未知超体积模式 / Unknown hypervolume mode");
        }
        long work = (long)Number(spec, "maxWork", 1_000_000.0);
        long samples = (long)Number(spec, "samples", 16384.0);
        long calibration = (long)Number(spec, "calibrationObservations", 32.0);
        long seedValue = (long)Number(spec, "seed", 1.0);
        if (samples < 1L || samples > int.MaxValue || calibration < 1L || calibration > int.MaxValue) {
            return Failed<IRewardPolicy>(
                "超体积计数超限 / Hypervolume count out of range");
        }
        var reward = new HypervolumeDeltaReward(
            reference, mode, work, (int)samples, seedValue, (int)calibration);
        Result<Success, ErrorCode, Error<ErrorCode>> rewardValid = reward.Validate();
        return rewardValid is Failed<Success, ErrorCode, Error<ErrorCode>> rewardFailure
            ? Results.Failed<IRewardPolicy>(rewardFailure.Error)
            : Results.Ok<IRewardPolicy>(reward);
    }

    /// <summary>读一个有限的数值节点 / Read a finite numeric node.</summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="value">数值 / The number.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryParseDouble(System.Text.Json.Nodes.JsonNode? node, out double value) {
        value = 0.0;
        if (node is null) {
            return false;
        }
        if (!double.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed)) {
            return false;
        }
        if (!double.IsFinite(parsed)) {
            return false;
        }
        value = parsed;
        return true;
    }

    /// <summary>构造键集合 / Build a key set.</summary>
    /// <param name="keys">键 / The keys.</param>
    /// <returns>键集合 / The key set.</returns>
    private static IReadOnlySet<string> Keys(params string[] keys) =>
        new HashSet<string>(keys, StringComparer.Ordinal);

    /// <summary>读一个数值参数 / Read a numeric parameter.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <param name="key">键 / The key.</param>
    /// <param name="fallback">默认值 / The fallback.</param>
    /// <returns>数值 / The number.</returns>
    private static double Number(ComponentSpec spec, string key, double fallback) =>
        spec.Parameters is not null
        && spec.Parameters.TryGetPropertyValue(key, out System.Text.Json.Nodes.JsonNode? node)
        && node is not null
        && double.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;
}

/// <summary>
/// 不交换 / No exchange.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class NoExchangePolicy<ObjValue, V> : IExchangePolicy<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <inheritdoc/>
    public IReadOnlyList<IIndividual<ObjValue, V>> Select(
        long decision, string target, IReadOnlyList<IIndividual<ObjValue, V>> working) =>
        Array.Empty<IIndividual<ObjValue, V>>();
}

/// <summary>
/// 周期性交换 / Periodic exchange.
/// </summary>
/// <remarks>
/// **`decision == 0` 不触发交换**（0 对任何周期取模都是 0）。把判断写成 `decision % period == 0`，会让**第一步就发生一次迁移**，
/// 而"迁移周期为 5"的配置看起来完全生效——只是第一次提前了。
/// **`decision == 0` must not trigger an exchange** (zero is divisible by every period). Writing only `decision % period == 0` makes the
/// **first step migrate**, while a "migration period of five" configuration looks perfectly in effect — the first one merely came early.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class PeriodicExchangePolicy<ObjValue, V> : IExchangePolicy<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 构造周期性交换 / Construct the periodic exchange.
    /// </summary>
    /// <param name="period">周期 / The period.</param>
    /// <param name="amount">每次交换的个体数 / The number of individuals per exchange.</param>
    public PeriodicExchangePolicy(int period, int amount) {
        if (period <= 0) {
            throw new ArgumentOutOfRangeException(nameof(period), "period must be positive");
        }
        if (amount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(amount), "amount must be positive");
        }
        Period = period;
        Amount = amount;
    }

    /// <summary>周期 / The period.</summary>
    public int Period { get; }

    /// <summary>每次交换的个体数 / The number of individuals per exchange.</summary>
    public int Amount { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IIndividual<ObjValue, V>> Select(
        long decision, string target, IReadOnlyList<IIndividual<ObjValue, V>> working) {
        ArgumentNullException.ThrowIfNull(working);
        if (decision <= 0 || decision % Period != 0) {
            return Array.Empty<IIndividual<ObjValue, V>>();
        }
        var selected = new List<IIndividual<ObjValue, V>>(Amount);
        for (int index = 0; index < working.Count && index < Amount; index++) {
            selected.Add(working[index]);
        }
        return selected;
    }
}
