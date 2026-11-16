// 组合求解器工厂 / The composed-solver factories.
// 与固定 Kotlin 基线 `solver/ComposedSolvers.kt`（75223 B）的本文件全部工厂对应；该文件共约 35 个工厂，分批推进。
// Mirrors the first three groups of factories in the pinned baseline's `solver/ComposedSolvers.kt` (75223 B), which holds roughly 35 factories
// and is being ported in batches.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Solver;

/// <remarks>
/// **本类是基线启发式插件的直接对应物：** **基线 `GA.kt`／`HS.kt` 等文件里各只有一个工厂函数，
/// 没有类——所以"基线对应物"是这里的 35 个入口，而不是 `Core.Plugin.Heuristic` 里的 `HeuristicAlgorithm` 子类**
/// （后者是 C# 侧的等价算法封装面，同样保留、且每个都被测试构造）。
///
/// **This class is the baseline's direct counterpart : each baseline file such as `GA.kt` / `HS.kt` holds a single
/// factory function and no classes, so the baseline counterpart is these 35 entry points rather than the `HeuristicAlgorithm`
/// subclasses in `Core.Plugin.Heuristic`** (those are C#'s equivalent algorithm surface, also kept, and each is constructed by a
/// test).
/// </remarks>
/// <summary>
/// 组合求解器工厂 / Composed-solver factories.
/// </summary>
/// <remarks>
/// **每个工厂都做三件事：装配运行器、给它一个人能认出来的名字、把它包成求解器。** 没有第四件事——
/// 工厂里出现"顺手做点别的"（调参、预热、额外校验）就会让"基线里这个算法到底是怎么装的"变得无法对照。
/// **Every factory does three things: assemble a runner, give it a recognisable name, and wrap it into a solver.** There is no fourth thing —
/// a factory that "does a little extra" (tuning, warm-up, extra validation) makes it impossible to compare against how the baseline actually
/// wires that algorithm.
/// **变异率与拓扑是有语义的默认值，不是装饰**：蜂群类算法在基线里一律 `NoMutation` + 变异率 `0` + `GlobalTopology`。
/// 给它们配上非零变异率会让"标准的 GWO / SCA / MVO"**悄悄多了一个基线里不存在的随机扰动**，
/// 而收敛曲线只会"更抖一点"。
/// **The mutation rate and the topology are semantic defaults, not decoration**: the swarm algorithms are uniformly `NoMutation` with a zero
/// mutation rate and `GlobalTopology` in the baseline. Giving them a non-zero rate **quietly adds a random perturbation the baseline does not
/// have**, and the convergence curve merely looks "a bit noisier".
/// </remarks>
public static class ComposedSolvers {
    /// <summary>
    /// 组合遗传算法 / Compose a genetic algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="cross">交叉算子 / The crossover operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="mutationRate">变异率 / The mutation rate.</param>
    /// <param name="topology">邻域拓扑 / The neighbourhood topology.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> Genetic<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ICross? cross = null,
        IMutationOperator? mutation = null,
        ISurvivorSelection? survivor = null,
        Flt64? mutationRate = null,
        ITopology? topology = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("genetic", new PopulationRunner<ObjValue, V>(
            cross ?? SinglePointCross.Instance,
            mutation ?? new GaussianMutation(),
            survivor ?? ElitistSurvivor.Instance,
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            mutationRate ?? new Flt64(0.1),
            initializer,
            topology: topology ?? GlobalTopology.Instance));

    /// <summary>
    /// 组合爬山法（严格改进才接受）/ Compose hill climbing (accept only on strict improvement).
    /// </summary>
    /// <remarks>
    /// **接受判据是这个算法与随机游走的唯一区别**。两者共用同一套"从当前解扰动一个邻域"的循环，
    /// 把它换成"无条件接受"，**爬山法就会在结果上变成随机游走，而名字、参数、迭代数全部不变**。
    /// **The acceptance criterion is the only thing distinguishing this algorithm from a random walk.** Both share the same
    /// "perturb one neighbour of the current solution" loop; swapping it for "accept unconditionally" **turns hill climbing into a random walk in
    /// every observable way, while the name, parameters and iteration count stay identical**.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="neighborhood">邻域算子 / The neighbourhood operator.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> HillClimbing<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        INeighborhood? neighborhood = null,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("hill-climbing", new TrajectoryRunner<ObjValue, V>(
            neighborhood ?? new RandomPerturbation(1),
            entropy,
            new BestArchive<ObjValue, V>(1),
            stopping,
            converter,
            initializer,
            BetterOnlyAcceptance.Instance));

    /// <summary>
    /// 组合随机游走（无条件接受）/ Compose a random walk (accept unconditionally).
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="neighborhood">邻域算子 / The neighbourhood operator.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> RandomWalk<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        INeighborhood? neighborhood = null,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("random-walk", new TrajectoryRunner<ObjValue, V>(
            neighborhood ?? new RandomPerturbation(1),
            entropy,
            new BestArchive<ObjValue, V>(1),
            stopping,
            converter,
            initializer,
            AlwaysAcceptance.Instance));

    /// <summary>
    /// 组合灰狼算法 / Compose a grey-wolf optimiser.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> GreyWolf<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("gwo", stopping, converter, entropy, size,
            update ?? new GreyWolfUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合正余弦算法 / Compose a sine-cosine algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> SineCosine<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("sca", stopping, converter, entropy, size,
            update ?? new SineCosineUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合多元宇宙算法 / Compose a multi-verse optimiser.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> MultiVerse<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("mvo", stopping, converter, entropy, size,
            update ?? new MultiVerseUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 装配一个"蜂群式"运行器：只更新、不变异、全局拓扑 / Assemble a swarm-style runner: update only, no mutation, global topology.
    /// </summary>
    /// <remarks>
    /// 基线里 GWO / SCA / MVO 三个工厂的装配完全同构，只有名字与默认更新算子不同。把这段重复写成一处，
    /// **是为了让"三者装配一致"这件事由代码结构本身保证**，而不是靠三份逐字复制的参数表。
    /// The GWO / SCA / MVO factories are assembled identically in the baseline, differing only in the name and the default update operator.
    /// Factoring the repetition out **lets the code structure itself guarantee "all three are assembled alike"**, instead of relying on three
    /// hand-copied parameter lists.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="name">名称 / The name.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    private static IHeuristicSolver<ObjValue, V> Swarm<ObjValue, V>(
        string name,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size,
        ISolutionUpdate update,
        ISurvivorSelection? survivor,
        int archiveAmount,
        IInitializer? initializer)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>(name, new PopulationRunner<ObjValue, V>(
            update,
            NoMutation.Instance,
            survivor ?? ElitistSurvivor.Instance,
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            Flt64.Zero,
            initializer,
            topology: GlobalTopology.Instance));

    /// <summary>
    /// 组合粒子群算法 / Compose a particle-swarm optimiser.
    /// </summary>
    /// <remarks>
    /// **粒子群不走"更新算子"那条路**，它有自己的运行器：速度是显式状态，`maxVelocity` 是速度的硬上限。
    /// 把它塞进 `Swarm(...)` 会让速度被当成"一代一代的解"，**于是算法还能跑、还能收敛，但已经不是粒子群了**。
    /// **PSO does not go through the "update operator" route**: it has its own runner, velocity is explicit state, and `maxVelocity` is a hard
    /// cap on it. Folding it into `Swarm(...)` would treat the velocity as "one generation of solutions", **so the algorithm still runs and still
    /// converges, but it is no longer a particle swarm**.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="inertia">惯性权重 / The inertia weight.</param>
    /// <param name="personalWeight">个人权重 / The personal weight.</param>
    /// <param name="socialWeight">社会权重 / The social weight.</param>
    /// <param name="maxVelocity">速度上限 / The velocity cap.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> ParticleSwarm<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        IParameterSource? inertia = null,
        IParameterSource? personalWeight = null,
        IParameterSource? socialWeight = null,
        Flt64? maxVelocity = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("pso", new ParticleSwarmRunner<ObjValue, V>(
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            inertia ?? new ConstantSource(new Flt64(0.4)),
            personalWeight ?? new ConstantSource(new Flt64(2.0)),
            socialWeight ?? new ConstantSource(new Flt64(2.0)),
            maxVelocity ?? new Flt64(10000.0),
            initializer: initializer));

    /// <summary>
    /// 组合和声搜索 / Compose a harmony search.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> HarmonySearch<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("hs", stopping, converter, entropy, size,
            update ?? new HarmonySearchUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合水循环算法 / Compose a water-cycle algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> WaterCycle<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("wca", stopping, converter, entropy, size,
            update ?? new WaterCycleUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合海鸥算法 / Compose a seagull optimisation algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> Seagull<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("soa", stopping, converter, entropy, size,
            update ?? new SeagullUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合战争策略算法 / Compose a war-strategy optimiser.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> WarStrategy<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("warso", stopping, converter, entropy, size,
            update ?? new WarStrategyUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合能量谷算法 / Compose an energy-valley optimiser.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> EnergyValley<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("evo", stopping, converter, entropy, size,
            update ?? new EnergyValleyUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合生发中心算法 / Compose a germinal-centre optimiser.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> GerminalCenter<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("gco", stopping, converter, entropy, size,
            update ?? new GerminalCenterUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合邻域搜索（不更差才接受）/ Compose a neighbourhood search (accept when not worse).
    /// </summary>
    /// <remarks>
    /// **接受判据是"不更差"而不是"严格更好"**。看似只是放宽一点点，实际决定了算法**能不能在等价解之间横向平移**；
    /// 换成"严格更好"后，**在对称或平坦的目标上算法会立刻卡住**，而收敛曲线只是"提前变平"。
    /// **The acceptance criterion is "not worse" rather than "strictly better".** That looks like a small relaxation, but it decides whether the
    /// algorithm **can slide sideways across equivalent solutions**; with "strictly better" **it stalls immediately on symmetric or flat
    /// objectives**, while the convergence curve merely "flattens early"**.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="neighborhood">邻域算子 / The neighbourhood operator.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> NeighborhoodSearch<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        INeighborhood? neighborhood = null,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("ns", new TrajectoryRunner<ObjValue, V>(
            neighborhood ?? new RandomPerturbation(1),
            entropy,
            new BestArchive<ObjValue, V>(1),
            stopping,
            converter,
            initializer,
            NotWorseAcceptance.Instance));

    /// <summary>
    /// 组合差分进化 / a differential evolution / Compose a differential evolution.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> DifferentialEvolution<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("de", stopping, converter, entropy, size,
            update ?? new DifferentialCross(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合鲸鱼优化 / a whale optimisation / Compose a whale optimisation.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> WhaleOptimization<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("woa", stopping, converter, entropy, size,
            update ?? new WhaleOptimizationUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合哈里斯鹰优化 / a Harris-hawk optimisation / Compose a Harris-hawk optimisation.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> HarrisHawk<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("hho", stopping, converter, entropy, size,
            update ?? new HarrisHawkUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合算术优化 / an arithmetic optimisation / Compose an arithmetic optimisation.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> ArithmeticOptimization<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("aoa", stopping, converter, entropy, size,
            update ?? new ArithmeticOptimizationUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合教与学优化 / a teaching-learning optimisation / Compose a teaching-learning optimisation.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> TeachingLearning<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("tlbo", stopping, converter, entropy, size,
            update ?? new TeachingLearningUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合樽海鞘群算法 / a salp-swarm algorithm / Compose a salp-swarm algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> SalpSwarm<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("ssa", stopping, converter, entropy, size,
            update ?? new SalpSwarmUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合引力搜索 / a gravitational search / Compose a gravitational search.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> GravitationalSearch<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("gsa", stopping, converter, entropy, size,
            update ?? new GravitationalSearchUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合萤火虫算法 / a firefly algorithm / Compose a firefly algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> Firefly<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("fa", stopping, converter, entropy, size,
            update ?? new FireflyUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合布谷鸟搜索 / a cuckoo search / Compose a cuckoo search.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> CuckooSearch<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("cs", stopping, converter, entropy, size,
            update ?? new CuckooSearchUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合蝙蝠算法 / a bat algorithm / Compose a bat algorithm.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> BatAlgorithm<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("ba", stopping, converter, entropy, size,
            update ?? new BatAlgorithmUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合飞蛾扑火优化 / a moth-flame optimisation / Compose a moth-flame optimisation.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> MothFlame<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("mfo", stopping, converter, entropy, size,
            update ?? new MothFlameUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合蝗虫优化 / a grasshopper optimisation / Compose a grasshopper optimisation.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> Grasshopper<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Swarm<ObjValue, V>("goa", stopping, converter, entropy, size,
            update ?? new GrasshopperUpdate(), survivor, archiveAmount, initializer);

    /// <summary>
    /// 组合 SHADE / Compose SHADE.
    /// </summary>
    /// <remarks>
    /// **`memorySize` 与 `pBestRate` 不是调参旋钮，而是这个算法的定义的一部分**：SHADE 的全部优势来自"按历史成功参数采样、
    /// 并按比例取当前最优个体做差分基"。把 `pBestRate` 归零，**算法仍然会收敛，但已经不是 SHADE 了**。
    /// **`memorySize` and `pBestRate` are not tuning knobs but part of the algorithm's definition**: all of SHADE's advantage comes from
    /// "sample from the historically successful parameters and use a proportion of the current best as the difference base". Zeroing
    /// `pBestRate` **still converges, and is no longer SHADE**.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="memorySize">历史记忆容量 / The history-memory size.</param>
    /// <param name="pBestRate">最优基比例 / The best-base proportion.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> Shade<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        int memorySize = 5,
        double pBestRate = 0.2,
        int archiveAmount = 1)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("shade", new ShadeRunner<ObjValue, V>(
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            memorySize,
            pBestRate));

    /// <summary>
    /// 组合 L-SHADE / Compose L-SHADE.
    /// </summary>
    /// <remarks>
    /// **`sizeRatio` 是 L-SHADE 相对 SHADE 的唯一本质差别**：种群规模按线性计划缩减到 `minimumSize`。
    /// 不传它、或传一个恒为 1 的计划，**得到的就只是一份 SHADE**，而名字还叫 `l-shade`。
    /// **`sizeRatio` is the only essential difference from SHADE**: the population shrinks linearly down to `minimumSize`. Omitting it, or
    /// passing a plan pinned at one, **yields plain SHADE under the name `l-shade`**.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">初始种群规模 / The initial population size.</param>
    /// <param name="minimumSize">最小种群规模 / The minimum population size.</param>
    /// <param name="sizeRatio">种群规模比例计划 / The population-size ratio plan.</param>
    /// <param name="memorySize">历史记忆容量 / The history-memory size.</param>
    /// <param name="pBestRate">最优基比例 / The best-base proportion.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> LShade<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        int minimumSize = 4,
        IParameterSource? sizeRatio = null,
        int memorySize = 5,
        double pBestRate = 0.2,
        int archiveAmount = 1)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("l-shade", new ShadeRunner<ObjValue, V>(
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            memorySize,
            pBestRate,
            // 与基线一致：规模随时间线性缩到 5%。
            // As in the baseline: the size shrinks linearly down to 5%.
            sizeRatio ?? new LinearSource(Flt64.One, new Flt64(0.05), 100UL),
            minimumSize));

    /// <summary>
    /// 组合自适应粒子群 / Compose an adaptive particle swarm.
    /// </summary>
    /// <remarks>
    /// **与标准粒子群的区别全在三个权重源是"随迭代变化"的计划而不是常数**：惯性由 0.9 线性降到 0.4，
    /// 个人权重由 2.5 降到 0.5，社会权重反向由 0.5 升到 2.5——**前期的探索与后期的开发就是靠这一组反向计划实现的**。
    /// 换成三个常数，**算法照样收敛**，只是退化成普通粒子群而名字还叫 `apso`。
    /// **The difference from standard PSO is entirely that the three weight sources are iteration-varying plans rather than constants**: the
    /// inertia falls linearly from 0.9 to 0.4, the personal weight from 2.5 to 0.5, and the social weight rises the other way from 0.5 to 2.5 —
    /// **early exploration and late exploitation are exactly this pair of opposing plans**. Replacing them with constants **still converges**,
    /// merely degrading into plain PSO under the name `apso`.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="inertia">惯性权重计划 / The inertia-weight plan.</param>
    /// <param name="personalWeight">个人权重计划 / The personal-weight plan.</param>
    /// <param name="socialWeight">社会权重计划 / The social-weight plan.</param>
    /// <param name="maxVelocity">速度上限 / The velocity cap.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> AdaptiveParticleSwarm<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        IParameterSource? inertia = null,
        IParameterSource? personalWeight = null,
        IParameterSource? socialWeight = null,
        Flt64? maxVelocity = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("apso", new ParticleSwarmRunner<ObjValue, V>(
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            inertia ?? new LinearSource(new Flt64(0.9), new Flt64(0.4), 100UL),
            personalWeight ?? new LinearSource(new Flt64(2.5), new Flt64(0.5), 100UL),
            socialWeight ?? new LinearSource(new Flt64(0.5), new Flt64(2.5), 100UL),
            maxVelocity ?? new Flt64(10000.0),
            initializer: initializer));

    /// <summary>
    /// 组合综合学习粒子群 / Compose a comprehensive-learning particle swarm.
    /// </summary>
    /// <remarks>
    /// **`refreshInterval` 与 `stagnationLimit` 是 CLPSO 的核心机制**：每 `refreshInterval` 步重新分配"每一维向谁学"，
    /// 停滞超过 `stagnationLimit` 的粒子强制刷新。把两者设成 0 或极大值，**算法仍然能跑**，
    /// 但"逐维向不同的精英学习"这件事就消失了——而它正是 CLPSO 相对标准粒子群的全部意义。
    /// **`refreshInterval` and `stagnationLimit` are CLPSO's core mechanism**: every `refreshInterval` steps the per-dimension exemplars are
    /// reassigned, and particles stagnating past `stagnationLimit` are forced to refresh. Setting either to zero or to a huge value **still
    /// runs**, but "learn each dimension from a different elite" disappears — and that is the entire point of CLPSO over standard PSO.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="inertia">惯性权重计划 / The inertia-weight plan.</param>
    /// <param name="learningWeight">学习权重 / The learning weight.</param>
    /// <param name="maxVelocity">速度上限 / The velocity cap.</param>
    /// <param name="refreshInterval">学习对象刷新间隔 / The exemplar refresh interval.</param>
    /// <param name="stagnationLimit">停滞上限 / The stagnation limit.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> ComprehensiveLearningParticleSwarm<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        IParameterSource? inertia = null,
        Flt64? learningWeight = null,
        Flt64? maxVelocity = null,
        int refreshInterval = 7,
        int stagnationLimit = 5,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("clpso", new ClpsoRunner<ObjValue, V>(
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            inertia ?? new LinearSource(new Flt64(0.9), new Flt64(0.4), 100UL),
            learningWeight ?? new Flt64(1.49445),
            maxVelocity ?? new Flt64(10000.0),
            refreshInterval,
            stagnationLimit,
            initializer));

    /// <summary>
    /// 组合 CMA-ES / Compose CMA-ES.
    /// </summary>
    /// <remarks>
    /// **初始步长不是"学习率"**：它同时决定初始分布的尺度与后续协方差自适应的起点，`initialSigma` 取 0 会让采样退化成一个点，
    /// **于是协方差永远学不到任何东西，而迭代仍然照常进行**。
    /// **The initial step size is not a "learning rate"**: it sets both the scale of the initial distribution and the starting point of the
    /// covariance adaptation. `initialSigma` of zero degenerates sampling to a point, **so the covariance never learns anything while the
    /// iteration keeps going as usual**.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="initialSigma">初始步长 / The initial step size.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> CmaEs<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        double initialSigma = 0.3,
        int archiveAmount = 1)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("cma-es", new CmaEsRunner<ObjValue, V>(
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            initialSigma));

    /// <summary>
    /// 组合 Q 学习驱动的自适应求解器 / Compose a Q-learning-driven adaptive solver.
    /// </summary>
    /// <remarks>
    /// **同一个对象既是"解更新"又是"迭代观测者"，这两件事必须同时接上**：前者执行本轮选中的算子，
    /// 后者根据本轮结果更新 Q 表。**只接 `update` 不接 `observers`，算法照样跑、照样给出结果**——
    /// 只是 Q 表永远是初始值，**"自适应选择算子"这件事从来没有发生过**，而每一步都看起来正常。
    /// 这是本文件里唯一一个"少接一个参数就等于关掉整个机制"的工厂。
    ///
    /// **`candidates` 的数量就是动作数**：`actionAmount` 由它推出来，不是另一个独立参数。
    /// 传一个 `size = 0` 的动作表会让 `actionAmount` 变成 0，**于是在构造期就该被拒绝**，而不是等到第一次选择时越界。
    ///
    /// **The same object is both the "solution update" and the "iteration observer", and both must be wired**: the former applies the operator
    /// chosen for this round, the latter updates the Q table from this round's outcome. **Wiring only `update` and not `observers` still runs and
    /// still returns a result** — the table merely stays at its initial values, **so "adaptive operator selection" never happened**, while every
    /// step looks normal. This is the only factory in this file where omitting one argument silently disables the entire mechanism.
    ///
    /// **The number of `candidates` is the action count**: `actionAmount` is derived from it rather than being a separate parameter, so an empty
    /// operator list must be rejected at construction time instead of overrunning on the first selection.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="candidates">候选更新算子 / The candidate update operators.</param>
    /// <param name="discretizer">状态离散化 / The state discretiser.</param>
    /// <param name="selector">动作选择 / The action selector.</param>
    /// <param name="reward">回报函数 / The reward function.</param>
    /// <param name="alpha">学习率 / The learning rate.</param>
    /// <param name="gamma">折扣因子 / The discount factor.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> QLearning<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        IReadOnlyList<ISolutionUpdate>? candidates = null,
        IStateDiscretizer? discretizer = null,
        IActionSelector? selector = null,
        IReward? reward = null,
        Flt64? alpha = null,
        Flt64? gamma = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IReadOnlyList<ISolutionUpdate> updates = candidates
            ?? new ISolutionUpdate[] { new GreyWolfUpdate(), new SineCosineUpdate() };
        if (updates.Count == 0) {
            throw new ArgumentException(
                "候选算子不能为空 / the candidate operators must not be empty", nameof(candidates));
        }
        var agent = new QlearningAgent(
            discretizer ?? new DiversityStagnationDiscretizer(),
            updates.Count,
            selector ?? new EpsilonGreedyActionSelector(),
            reward ?? new ImprovementReward(),
            alpha ?? new Flt64(0.5),
            gamma ?? new Flt64(0.9),
            entropy);
        var update = new QlearningUpdate(updates, agent);
        return new RunnerSolver<ObjValue, V>("q-learning", new PopulationRunner<ObjValue, V>(
            update,
            NoMutation.Instance,
            survivor ?? ElitistSurvivor.Instance,
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            size,
            Flt64.Zero,
            initializer,
            topology: GlobalTopology.Instance,
            observers: new IIterationObserverContract[] { new ObserverContractAdapter(update) }));
    }

    /// <summary>
    /// 组合模拟退火 / Compose a simulated annealing.
    /// </summary>
    /// <remarks>
    /// **这个工厂的全部内容就是把 `MetropolisAcceptance` 接上去**。换成"严格更好才接受"，**算法照样跑完、照样给出最优解**——
    /// 只是它变成了爬山法，而名字还叫模拟退火。
    /// **Everything this factory does is wire in `MetropolisAcceptance`.** Swapping it for "accept only on strict improvement" **still runs to
    /// completion and still reports a best solution** — it has merely become hill climbing under the annealing name.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="neighborhood">邻域算子 / The neighbourhood operator.</param>
    /// <param name="temperature">温度计划 / The temperature plan.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> SimulatedAnnealing<ObjValue, V>(
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        INeighborhood? neighborhood = null,
        IParameterSource? temperature = null,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new RunnerSolver<ObjValue, V>("simulated-annealing", new TrajectoryRunner<ObjValue, V>(
            neighborhood ?? new RandomPerturbation(1),
            entropy,
            new BestArchive<ObjValue, V>(1),
            stopping,
            converter,
            initializer,
            new MetropolisAcceptance(
                temperature ?? new GeometricSource(new Flt64(100.0), new Flt64(0.95)),
                entropy)));

    /// <summary>
    /// 组合岛屿遗传算法（等规模岛屿）/ Compose an island genetic algorithm (equal-sized islands).
    /// </summary>
    /// <remarks>
    /// **`migrationPeriod` 与 `migration` 是岛屿模型的主体**：不迁移的"多个种群"与单种群在数学上没有区别——
    /// 只是把同一份计算切成了几块。传 `NoMigration` 时算法**照样跑完、照样给出结果**，
    /// 而"岛屿"这件事只剩下名字。
    /// **`migrationPeriod` and `migration` are the whole of the island model**: several populations with no migration are mathematically
    /// indistinguishable from one, merely split into pieces. With `NoMigration` the algorithm **still runs to completion and still returns a
    /// result**, and the only thing left of "islands" is the name.
    /// **每个岛屿自身的停止判据恒为真**（`IterationLimit(0)`）：步进由外层 `IslandRunner` 驱动，
    /// 岛屿若自带"跑到底"的判据，**一次步进就会把一个岛屿跑到收敛**，迁移再也没有机会发生。
    /// **Each island's own stopping criterion is always true** (`IterationLimit(0)`): stepping is driven by the outer `IslandRunner`, and an island
    /// that carried a "run to completion" criterion **would converge in a single step**, so migration would never get a chance to happen.
    /// </remarks>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="islandCount">岛屿数量 / The island count.</param>
    /// <param name="islandSize">每个岛屿的规模 / The size of each island.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="migrationPeriod">迁移周期 / The migration period.</param>
    /// <param name="migration">迁移策略 / The migration policy.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="mutationRate">变异率 / The mutation rate.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> IslandGenetic<ObjValue, V>(
        int islandCount,
        int islandSize,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        ulong migrationPeriod = 5UL,
        IIslandMigration<ObjValue, V>? migration = null,
        ISolutionUpdate? update = null,
        IMutationOperator? mutation = null,
        Flt64? mutationRate = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (islandCount <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(islandCount), "岛屿数量必须为正 / the island count must be positive");
        }
        var islands = new List<(int Size, Flt64 MutationRate)>(islandCount);
        for (int index = 0; index < islandCount; index++) {
            islands.Add((islandSize, mutationRate ?? new Flt64(0.1)));
        }
        return IslandGenetic(islands, stopping, converter, entropy, migrationPeriod, migration,
            update, mutation, survivor, archiveAmount);
    }

    /// <summary>
    /// 组合岛屿遗传算法（逐岛屿规格）/ Compose an island genetic algorithm (per-island specs).
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="islands">逐岛屿的规模与变异率 / The per-island size and mutation rate.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="migrationPeriod">迁移周期 / The migration period.</param>
    /// <param name="migration">迁移策略 / The migration policy.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="survivor">环境选择算子 / The survivor-selection operator.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="initializer">初始化算子 / The initialisation operator.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> IslandGenetic<ObjValue, V>(
        IReadOnlyList<(int Size, Flt64 MutationRate)> islands,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        ulong migrationPeriod = 5UL,
        IIslandMigration<ObjValue, V>? migration = null,
        ISolutionUpdate? update = null,
        IMutationOperator? mutation = null,
        ISurvivorSelection? survivor = null,
        int archiveAmount = 1,
        IInitializer? initializer = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(islands);
        if (islands.Count == 0) {
            throw new ArgumentException(
                "至少需要一个岛屿 / at least one island is required", nameof(islands));
        }
        var runners = new List<PopulationRunner<ObjValue, V>>(islands.Count);
        foreach ((int size, Flt64 rate) in islands) {
            runners.Add(new PopulationRunner<ObjValue, V>(
                update ?? ArithmeticCross.Instance,
                mutation ?? new GaussianMutation(),
                survivor ?? ElitistSurvivor.Instance,
                entropy,
                new BestArchive<ObjValue, V>(1),
                // 恒为真的岛屿判据：步进由外层驱动。
                // An always-true island criterion: stepping is driven from outside.
                new IterationLimit(0UL),
                converter,
                size,
                rate,
                initializer,
                topology: GlobalTopology.Instance));
        }
        return new RunnerSolver<ObjValue, V>("island-ga", new IslandRunner<ObjValue, V>(
            runners,
            entropy,
            new BestArchive<ObjValue, V>(archiveAmount),
            stopping,
            converter,
            migrationPeriod,
            migration));
    }
}
