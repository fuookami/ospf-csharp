using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Configuration;

/// <summary>
/// 内置算法元数据 / Built-in algorithm metadata.
/// </summary>
/// <remarks>
/// **`nativeIds` 是"由原生 runner 实现"的算法集合，它必须包含全部 `populationUpdates` 的键**。漏掉一个键会让该算法
/// 既不在原生集合里、又不在组合工厂里，**而查找失败的表现是"未知算法"而不是"未实现"**。
/// **`nativeIds` is the set of algorithms implemented by a native runner, and it must contain every `populationUpdates` key.**
/// Missing one key leaves that algorithm in neither the native set nor the portfolio factory, **and the lookup failure surfaces as
/// "unknown algorithm" rather than "not implemented"**.
/// **`slots` 返回"该算法需要哪些算子槽位"**：种群类算法需要 `update` 与 `mutation`，轨迹类算法只需要 `neighborhood`。
/// 种群类也返回 `neighborhood` 会让绑定层以为它需要一个邻域算子，**而该算子永远不会被调用**。
/// **`slots` returns "which operator slots this algorithm needs"**: population algorithms need `update` and `mutation`, trajectory
/// algorithms only `neighborhood`. Returning `neighborhood` for population algorithms too makes the binding layer believe it needs one,
/// **while that operator is never invoked**.
/// **未知类型的 `slots`/`parameters` 返回空集合，而不是抛异常或返回全集**。返回全集会让一次拼写错误被当作"该算法接受所有槽位"，
/// 而配置校验一路通过、运行时才在绑定层失败。
/// **`slots`/`parameters` return an empty set for unknown types rather than throwing or returning everything.** Returning everything makes
/// a typo look like "this algorithm accepts all slots", and configuration validation passes all the way through only to fail in the binding
/// layer at run time.
/// **`parameters` 是"算法自己声明的可调参数名"**，不含 `mutationRate`/`maxVelocity` 这类由工厂直接读的通用参数。
/// 混进来会让绑定层为它们创建池，**而池化后的参数值会被算法自身的默认值覆盖**。
/// **`parameters` is "the tunable parameter names the algorithm itself declares"**, excluding generic ones such as
/// `mutationRate`/`maxVelocity` that the factory reads directly. Mixing them in makes the binding layer create pools for them,
/// **and the pooled value is then overridden by the algorithm's own default**.
/// </remarks>
public static class BuiltInAlgorithms {
    /// <summary>
    /// 由种群更新算子实现的算法类型 / The algorithm types implemented by a population update operator.
    /// </summary>
    public static IReadOnlyList<string> PopulationUpdateTypes { get; } = new[] {
        "genetic", "gwo", "sca", "mvo", "hs", "wca", "soa", "warso", "evo", "gco", "de",
        "woa", "hho", "aoa", "tlbo", "ssa", "gsa", "fa", "cs", "ba", "mfo", "goa",
    };

    /// <summary>
    /// 由原生 runner 实现的算法类型 / The algorithm types implemented by a native runner.
    /// </summary>
    public static IReadOnlySet<string> NativeIds { get; } = BuildNativeIds();

    /// <summary>
    /// 返回算法需要的算子槽位 / The operator slots an algorithm requires.
    /// </summary>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <returns>槽位集合 / The slot set.</returns>
    public static IReadOnlySet<string> Slots(string type) {
        ArgumentNullException.ThrowIfNull(type);
        foreach (string candidate in PopulationUpdateTypes) {
            if (string.Equals(candidate, type, StringComparison.Ordinal)) {
                return PopulationSlots;
            }
        }
        if (string.Equals(type, "nsga-ii", StringComparison.Ordinal)
            || string.Equals(type, "nsga-iii", StringComparison.Ordinal)) {
            return PopulationSlots;
        }
        if (string.Equals(type, "hill-climbing", StringComparison.Ordinal)
            || string.Equals(type, "ns", StringComparison.Ordinal)
            || string.Equals(type, "random-walk", StringComparison.Ordinal)
            || string.Equals(type, "simulated-annealing", StringComparison.Ordinal)) {
            return TrajectorySlots;
        }
        return EmptySlots;
    }

    /// <summary>
    /// 返回算法声明的可调参数名 / The tunable parameter names an algorithm declares.
    /// </summary>
    /// <param name="type">算法类型 / The algorithm type.</param>
    /// <returns>参数名集合 / The parameter-name set.</returns>
    public static IReadOnlySet<string> Parameters(string type) {
        ArgumentNullException.ThrowIfNull(type);
        if (string.Equals(type, "pso", StringComparison.Ordinal)
            || string.Equals(type, "apso", StringComparison.Ordinal)) {
            return PsoParameters;
        }
        if (string.Equals(type, "clpso", StringComparison.Ordinal)) {
            return ClpsoParameters;
        }
        if (string.Equals(type, "l-shade", StringComparison.Ordinal)) {
            return LShadeParameters;
        }
        if (string.Equals(type, "simulated-annealing", StringComparison.Ordinal)) {
            return AnnealingParameters;
        }
        return EmptySlots;
    }

    /// <summary>种群类算法的槽位 / The population algorithm slots.</summary>
    private static readonly IReadOnlySet<string> PopulationSlots =
        new HashSet<string>(new[] { "update", "mutation" }, StringComparer.Ordinal);

    /// <summary>轨迹类算法的槽位 / The trajectory algorithm slots.</summary>
    private static readonly IReadOnlySet<string> TrajectorySlots =
        new HashSet<string>(new[] { "neighborhood" }, StringComparer.Ordinal);

    /// <summary>空槽位 / No slots.</summary>
    private static readonly IReadOnlySet<string> EmptySlots =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>PSO 系列参数 / The PSO-family parameters.</summary>
    private static readonly IReadOnlySet<string> PsoParameters =
        new HashSet<string>(new[] { "inertia", "personalWeight", "socialWeight" }, StringComparer.Ordinal);

    /// <summary>CLPSO 参数 / The CLPSO parameters.</summary>
    private static readonly IReadOnlySet<string> ClpsoParameters =
        new HashSet<string>(new[] { "inertia" }, StringComparer.Ordinal);

    /// <summary>L-SHADE 参数 / The L-SHADE parameters.</summary>
    private static readonly IReadOnlySet<string> LShadeParameters =
        new HashSet<string>(new[] { "sizeRatio" }, StringComparer.Ordinal);

    /// <summary>退火参数 / The annealing parameters.</summary>
    private static readonly IReadOnlySet<string> AnnealingParameters =
        new HashSet<string>(new[] { "temperature" }, StringComparer.Ordinal);

    /// <summary>
    /// 构造原生标识集合 / Build the native identity set.
    /// </summary>
    /// <returns>原生标识集合 / The native identity set.</returns>
    private static HashSet<string> BuildNativeIds() {
        var ids = new HashSet<string>(PopulationUpdateTypes, StringComparer.Ordinal);
        // **这十个标识必须显式列出**：它们不由种群更新算子实现，但确实有原生 runner。
        // 漏掉任一个会让它在"原生集合"与"组合工厂"里都不存在。
        // **These ten identities must be listed explicitly**: they are not implemented by a population update operator, yet they do
        // have a native runner. Missing any one leaves it absent from both the native set and the portfolio factory.
        foreach (string id in new[] {
            "pso", "apso", "clpso", "shade", "l-shade", "cma-es",
            "hill-climbing", "ns", "random-walk", "simulated-annealing",
        }) {
            ids.Add(id);
        }
        return ids;
    }
    /// <summary>
    /// 按配置装配原生算法 / Assemble a native algorithm from its configuration.
    /// </summary>
    /// <remarks>
    /// **这里是"配置 → 运行器"的唯一入口**，因此三件事只能在这里做对：
    ///
    /// 1. **用户绑定的算子优先于默认算子**（`bindings`）。配置里点名了变异，就必须用那个，不能因为"分支里写死了默认值"而忽略它。
    /// 2. **`genetic` 的默认变异是高斯，其它种群算法的默认变异是"不变异"**。反过来会让所有算法都悄悄带上变异，
    ///    而"变异率 0.1"的配置看起来完全生效。
    /// 3. **未知的原生算法必须失败**。返回一个"最像的"运行器，会让拼错的算法名**运行出一个真实但错误的结果**。
    ///
    /// **This is the single "configuration to runner" entry point**, so exactly three things can only be got right here:
    ///
    /// 1. **User-bound operators win over defaults** (`bindings`). If the configuration names a mutation, that one must be used; a hard-coded
    ///    default inside a branch must not silently ignore it.
    /// 2. **`genetic` defaults to a Gaussian mutation while every other population algorithm defaults to "no mutation".** The reverse quietly gives
    ///    every algorithm a mutation, while a "mutation rate of 0.1" configuration looks perfectly in effect.
    /// 3. **An unknown native algorithm must fail.** Returning "the closest runner" makes a misspelt algorithm name **run to a real but wrong result**.
    /// </remarks>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="context">装配上下文 / The assembly context.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>搜索会话 / The search session.</returns>
    public static Result<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>> Create<O, V>(
        AlgorithmContext<O, V> context,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(converter);
        string type = context.Config.Algorithm.Type;
        SearchProblem<O, V> problem = context.Problem;
        SearchRandom random = context.Random;
        OperatorBindings bindings = context.Bindings;
        int size = context.Config.Size;
        Flt64 rate = new(Number(context.Config.Algorithm, "mutationRate", 0.1));
        var archive = new BestArchive<O, V>(100);

        if (PopulationUpdateTypes.Contains(type)) {
 // **早先在这里写过一条"22 个种群更新没有 `ISolutionUpdate` 形态"的缺口，那是错的。**
            // 它们一直都在（`Operator/Update/` 下 20 个 + `SinglePointCross`），我只是用 `grep "class X"` 去找，
            // 而它们是 `sealed record`——**用错误的声明形态搜索，得出的"缺失"结论必然是假的**。
            // 这里改为映射表补齐默认工厂，并把那条错误结论从文档里撤回。
            //
 // **An earlier pass recorded a "the 22 population updates have no `ISolutionUpdate`" gap here, and it was wrong.** They had always been there
            // (twenty under `Operator/Update/` plus `SinglePointCross`); I had searched with `grep "class X"` while they are `sealed record`s —
 // **searching with the wrong declaration shape necessarily yields a false "missing"**. This fills in the default factories from the
            // mapping table and retracts that wrong conclusion from the documents.
            ISolutionUpdate? built = PopulationUpdate(type);
            if (Single(bindings.Update) is ISolutionUpdate bound) {
                built = bound;
            }
            if (built is null) {
                return Failed<ISearchSession<O, V>>("unknown population update: " + type);
            }
            ISolutionUpdate chosen = built;
            IMutationOperator mutation = Single(bindings.Mutation)
                ?? (type == "genetic" ? new GaussianMutation() : (IMutationOperator)NoMutation.Instance);
            var runner = new PopulationRunner<O, V>(
                chosen,
                mutation,
                ElitistSurvivor.Instance,
                random.Invoke,
                archive,
                NeverStop.Instance,
                converter,
                size,
                rate,
                fitnessEvaluator: ObjectiveFitnessEvaluator.Instance);
            return Results.Ok<ISearchSession<O, V>>(new RunnerSearchSession<List<IIndividual<O, V>>, O, V>(
                runner,
                problem,
                RunnerStateCodecs.Population(problem),
                (state, incoming) => Results.Ok(
                    ElitistSurvivor.Instance.Select(problem.Model, state, incoming, size).ToList())));
        }

        if (type is "hill-climbing" or "ns" or "random-walk" or "simulated-annealing") {
            IAcceptance acceptance = type switch {
                "random-walk" => AlwaysAcceptance.Instance,
                "simulated-annealing" => new MetropolisAcceptance(
                    bindings.Parameter("temperature", new GeometricSource(
                        new Flt64(100.0), new Flt64(0.95))),
                    random.Invoke),
                _ => BetterOnlyAcceptance.Instance,
            };
            var runner = new TrajectoryRunner<O, V>(
                Single(bindings.Neighborhood) ?? new RandomPerturbation(1),
                random.Invoke,
                archive,
                NeverStop.Instance,
                converter,
                acceptance: acceptance,
                fitnessEvaluator: ObjectiveFitnessEvaluator.Instance);
            return Results.Ok<ISearchSession<O, V>>(
                new RunnerSearchSession<IIndividual<O, V>?, O, V>(
                    runner,
                    problem,
                    RunnerStateCodecs.Trajectory(problem),
                    (state, incoming) => Results.Ok(BestOf(problem, incoming) ?? state)));
        }

        if (type is "pso" or "apso") {
            double maxVelocity = Number(context.Config.Algorithm, "maxVelocity", 10000.0);
            IParameterSource inertia = bindings.Parameter("inertia", type == "apso"
                ? new LinearSource(new Flt64(0.9), new Flt64(0.4), 1000UL)
                : new ConstantSource(new Flt64(0.4)));
            var runner = new ParticleSwarmRunner<O, V>(
                random.Invoke, archive, NeverStop.Instance, converter, size,
                inertia,
                bindings.Parameter("personalWeight", new ConstantSource(new Flt64(2.0))),
                bindings.Parameter("socialWeight", new ConstantSource(new Flt64(2.0))),
                new Flt64(maxVelocity),
                fitnessEvaluator: ObjectiveFitnessEvaluator.Instance);
            var synchronizer = new ContextStateSynchronizer<O, V>(problem, maxVelocity);
            return Results.Ok<ISearchSession<O, V>>(
                new RunnerSearchSession<List<ParticleState<O, V>>, O, V>(
                    runner, problem, RunnerStateCodecs.Particles(problem),
                    (state, incoming) => Map(synchronizer.Particles(state, incoming))));
        }

        if (type == "clpso") {
            double maxVelocity = Number(context.Config.Algorithm, "maxVelocity", 10000.0);
            var runner = new ClpsoRunner<O, V>(
                random.Invoke, archive, NeverStop.Instance, converter, size,
                bindings.Parameter("inertia", new LinearSource(
                    new Flt64(0.9), new Flt64(0.4), 1000UL)),
                new Flt64(Number(context.Config.Algorithm, "learningWeight", 1.49445)),
                new Flt64(maxVelocity),
                (int)Number(context.Config.Algorithm, "refreshInterval", 7.0),
                (int)Number(context.Config.Algorithm, "stagnationLimit", 5.0),
                fitnessEvaluator: ObjectiveFitnessEvaluator.Instance);
            var synchronizer = new ContextStateSynchronizer<O, V>(problem, maxVelocity);
            return Results.Ok<ISearchSession<O, V>>(
                new RunnerSearchSession<List<ClpsoParticle<O, V>>, O, V>(
                    runner, problem, RunnerStateCodecs.Clpso(problem),
                    (state, incoming) => Map(synchronizer.Clpso(state, incoming))));
        }

        if (type is "shade" or "l-shade") {
            int minimumSize = (int)Number(context.Config.Algorithm, "minimumSize", 4.0);
            IParameterSource sizeRatio = bindings.Parameter("sizeRatio", type == "l-shade"
                ? new LinearSource(
                    new Flt64(1.0),
                    new Flt64(minimumSize / (double)size),
                    (ulong)Number(context.Config.Algorithm, "sizeReductionSteps", 1000.0))
                : new ConstantSource(new Flt64(1.0)));
            var runner = new ShadeRunner<O, V>(
                random.Invoke, archive, NeverStop.Instance, converter, size,
                (int)Number(context.Config.Algorithm, "memorySize", 5.0),
                Number(context.Config.Algorithm, "pBestRate", 0.2),
                sizeRatio,
                minimumSize,
                fitnessEvaluator: ObjectiveFitnessEvaluator.Instance);
            var synchronizer = new ContextStateSynchronizer<O, V>(problem);
            return Results.Ok<ISearchSession<O, V>>(
                new RunnerSearchSession<ShadeState<O, V>, O, V>(
                    runner, problem, RunnerStateCodecs.Shade(problem),
                    synchronizer.Shade));
        }

        if (type == "cma-es") {
            double sigma = Number(context.Config.Algorithm, "initialSigma", 0.3);
            var runner = new CmaEsRunner<O, V>(
                random.Invoke, archive, NeverStop.Instance, converter, sigma,
                ObjectiveFitnessEvaluator.Instance);
            bool restartOnImport = context.Config.Algorithm.Parameters is not null
                && context.Config.Algorithm.Parameters.ContainsKey("restartOnImport")
                && context.Config.Algorithm.Parameters["restartOnImport"]?.ToJsonString() == "true";
            var synchronizer = new ContextStateSynchronizer<O, V>(problem, initialSigma: sigma);
            return Results.Ok<ISearchSession<O, V>>(
                new RunnerSearchSession<CmaEsState<O, V>, O, V>(
                    runner, problem, RunnerStateCodecs.CmaEs(problem),
                    restartOnImport ? synchronizer.CmaEs : null));
        }

        return Failed<ISearchSession<O, V>>("unknown native algorithm: " + type);
    }

    /// <summary>
    /// 按标识构造默认的种群更新算子 / Build the default population-update operator by identifier.
    /// </summary>
    /// <remarks>
    /// **这张表与`populationUpdates` 一一对应，包括默认参数**。表里漏一项的表现不是"报错"，而是
    /// **该算法悄悄退回别的算子**——所以本表由一条"22 个标识全部可装配"的测试守着。
    ///
    /// **This table corresponds one-to-one with its `populationUpdates`, default arguments included.** A missing entry does not surface as
    /// an error but as **that algorithm quietly falling back to a different operator** — which is why a test guards "all twenty-two identifiers
    /// assemble".
    /// </remarks>
    /// <param name="type">算法标识 / The algorithm identifier.</param>
    /// <returns>更新算子；未知为 <c>null</c> / The update operator, or <c>null</c> when unknown.</returns>
    public static ISolutionUpdate? PopulationUpdate(string type) => type switch {
        "genetic" => SinglePointCross.Instance,
        "gwo" => new GreyWolfUpdate(),
        "sca" => new SineCosineUpdate(),
        "mvo" => new MultiVerseUpdate(),
        "hs" => new HarmonySearchUpdate(),
        "wca" => new WaterCycleUpdate(),
        "soa" => new SeagullUpdate(),
        "warso" => new WarStrategyUpdate(),
        "evo" => new EnergyValleyUpdate(),
        "gco" => new GerminalCenterUpdate(),
        "de" => new DifferentialCross(),
        "woa" => new WhaleOptimizationUpdate(),
        "hho" => new HarrisHawkUpdate(),
        "aoa" => new ArithmeticOptimizationUpdate(),
        "tlbo" => new TeachingLearningUpdate(),
        "ssa" => new SalpSwarmUpdate(),
        "gsa" => new GravitationalSearchUpdate(),
        "fa" => new FireflyUpdate(),
        "cs" => new CuckooSearchUpdate(),
        "ba" => new BatAlgorithmUpdate(),
        "mfo" => new MothFlameUpdate(),
        "goa" => new GrasshopperUpdate(),
        _ => null,
    };

    /// <summary>
    /// 取绑定的单个算子 / Take the single bound operator.
    /// </summary>
    /// <remarks>
    /// **只有恰好绑定一个候选时才算"用户点名了这个算子"**。绑定了多个时任一选择都需要一条配置没有写明的规则，
    /// 因此交给运行器自己的池调度，而不是在这里随便挑第一个。
    /// **Only a pool with exactly one candidate counts as "the user named this operator".** With several bound, picking one would need a rule the
    /// configuration never states, so it is left to the runner's own pool scheduling instead of taking the first here.
    /// </remarks>
    /// <typeparam name="T">算子类型 / The operator type.</typeparam>
    /// <param name="pool">算子池 / The pool.</param>
    /// <returns>算子 / The operator.</returns>
    private static T? Single<T>(OperatorPool<T>? pool) where T : class {
        if (pool is null || pool.Candidates.Count != 1) {
            return null;
        }
        foreach (T candidate in pool.Candidates.Values) {
            return candidate;
        }
        return null;
    }

    /// <summary>把"只读列表"结果转成"列表"结果 / Turn a read-only-list result into a list result.</summary>
    /// <remarks>
    /// 运行器状态用 `List&lt;T&gt;`，同步器返回 `IReadOnlyList&lt;T&gt;`；**这里只做形状转换，不做任何判断**——
    /// 判断留在同步器里，免得"转换的时候顺手补一个默认值"把无效导入变成有效导入。
    /// The runner state uses `List&lt;T&gt;` while the synchroniser returns `IReadOnlyList&lt;T&gt;`; **this only reshapes, it never decides** — the
    /// decision stays in the synchroniser, so that "quietly filling in a default while reshaping" cannot turn an invalid import into a valid one.
    /// </remarks>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>列表结果 / The list result.</returns>
    private static Result<List<T>, ErrorCode, Error<ErrorCode>> Map<T>(
        Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> failure
            ? Results.Failed<List<T>>(failure.Error)
            : Results.Ok(((Ok<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>)result).Value.ToList());

    /// <summary>取一组个体里最好的那个 / Take the best of a group of individuals.</summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="individuals">个体 / The individuals.</param>
    /// <returns>最好的个体 / The best individual.</returns>
    private static IIndividual<O, V>? BestOf<O, V>(
        SearchProblem<O, V> problem, IReadOnlyList<IIndividual<O, V>> individuals)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IIndividual<O, V>? best = null;
        foreach (IIndividual<O, V> candidate in individuals) {
            if (best is null
                || HeuristicOrdering.CompareStrictly(problem.Model, candidate.Objective, best.Objective) < 0) {
                best = candidate;
            }
        }
        return best;
    }

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

    /// <summary>失败 / A failure.</summary>
    /// <typeparam name="T">值类型 / The value type.</typeparam>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
