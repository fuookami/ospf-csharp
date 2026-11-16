// SHADE 运行器 / The SHADE runner.
// 对应（`ShadeNumerics` 除外，见注释）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// SHADE 采样参数 / The SHADE sampled parameters.
/// </summary>
/// <param name="MemoryIndex">被采样的记忆槽 / The sampled memory slot.</param>
/// <param name="DifferentialWeight">差分权重 / The differential weight.</param>
/// <param name="CrossRate">交叉率 / The cross rate.</param>
public sealed record ShadeParameters(int MemoryIndex, double DifferentialWeight, double CrossRate);

/// <summary>
/// SHADE 参数记忆 / The SHADE parameter memory.
/// </summary>
/// <remarks>
/// **两个向量必须等长且非空**。不等长会让 `Sample` 用 `MemoryF` 的下标去索引 `MemoryCr`，在越界时才失败，
/// **而失败点距离配置错误已经很远**；空向量则让"采样"无槽可取。
/// **The two vectors must be non-empty and of equal length.** A length mismatch lets `Sample` index `MemoryCr` with an index derived from
/// `MemoryF` and fail only on the out-of-range access, **far from the configuration mistake**; an empty pair leaves "sampling" with no
/// slot to draw from.
/// **Lehmer 均值在 `ΣF ≤ 0` 时回退到当前槽的旧值**。直接做除法会得到 `NaN`，而 `NaN` 一旦进入记忆就再也不能被"比较"出来，
/// **整代之后的采样全部失效却仍然产出一个看起来正常的种群**。
/// **The Lehmer mean falls back to the slot's previous value when `ΣF ≤ 0`.** Dividing outright yields `NaN`, and once `NaN` enters the
/// memory it can never be surfaced by a comparison again, **so every later sample is silently invalid while the population still looks
/// normal**。
/// **交叉率用算术均值，不是 Lehmer 均值**。两者混用会让 `Cr` 系统性地偏离其定义，**而算法名字与产出的种群都看不出区别**。
/// **The cross rate uses the arithmetic mean, not the Lehmer mean.** Mixing them up biases `Cr` away from its definition, **with neither
/// the algorithm's name nor the resulting population showing any difference**.
/// **两个均值都要截断到 0 到 1 之间**。越界值会让一次采样直接产出不可能的权重，**而后续的差分向量照样算得出来**。
/// **Both means are clamped between 0 and 1.** Out-of-range values make one sample produce an impossible weight, **while the resulting
/// difference vector still evaluates fine**.
/// **游标按模自增**。不取模会在写入最后一槽之后越界。
/// **The cursor advances modulo the memory length.** Without the modulo it runs past the last slot.
/// </remarks>
/// <param name="MemoryF">差分权重记忆 / The differential-weight memory.</param>
/// <param name="MemoryCr">交叉率记忆 / The cross-rate memory.</param>
/// <param name="Cursor">下一个写入槽 / The next slot to write.</param>
public sealed record ShadeMemory(
    IReadOnlyList<double> MemoryF,
    IReadOnlyList<double> MemoryCr,
    int Cursor) {
    /// <summary>默认记忆长度 / The default memory length.</summary>
    public const int DefaultSize = 5;

    /// <summary>
    /// 构造默认记忆 / Construct the default memory.
    /// </summary>
    /// <param name="size">记忆长度 / The memory length.</param>
    /// <returns>记忆 / The memory.</returns>
    public static ShadeMemory Create(int size = DefaultSize) {
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "memory size must be positive");
        }
        var memoryF = new List<double>(size);
        var memoryCr = new List<double>(size);
        for (int index = 0; index < size; index++) {
            memoryF.Add(0.5);
            memoryCr.Add(0.5);
        }
        return new ShadeMemory(memoryF, memoryCr, 0);
    }

    /// <summary>
    /// 采样一组参数 / Sample one parameter set.
    /// </summary>
    /// <remarks>
    /// **抽取顺序是"槽下标 → 权重扰动 → 三次交叉率扰动"**，共五次。改动顺序不改变分布，却会改变同一随机流下的轨迹。
    /// **The draw order is "slot index → weight perturbation → three cross-rate perturbations"**, five draws in total. Changing the order
    /// alters no distribution yet changes the trajectory under the same random stream.
    /// **权重扰动后若 ≤ 0 则回退到槽内原值**，而不是截断到 0：截断会让差分权重停在 0，
    /// **于是变异向量退化成"父代 + 0"，整个种群停止移动**。
    /// **A perturbation that lands ≤ 0 falls back to the slot's value** rather than clamping to 0: clamping parks the differential weight at
    /// zero, **so the mutant degenerates to "parent + 0" and the whole population stops moving**.
    /// </remarks>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <returns>采样参数 / The sampled parameters.</returns>
    public ShadeParameters Sample(Func<Flt64> entropy) {
        ArgumentNullException.ThrowIfNull(entropy);
        if (MemoryF.Count == 0 || MemoryF.Count != MemoryCr.Count) {
            throw new InvalidOperationException("inconsistent SHADE memory");
        }
        int index = global::System.Math.Clamp(
            (int)(entropy().ToDouble() * MemoryF.Count), 0, MemoryF.Count - 1);
        double differentialWeight = MemoryF[index]
            + (0.1 * global::System.Math.Tan(
                global::System.Math.PI * (entropy().ToDouble() - 0.5)));
        if (differentialWeight <= 0.0) {
            differentialWeight = MemoryF[index];
        }
        differentialWeight = global::System.Math.Clamp(differentialWeight, 0.0, 1.0);

        double firstDraw = entropy().ToDouble();
        double secondDraw = entropy().ToDouble();
        double thirdDraw = entropy().ToDouble();
        double crossRate = MemoryCr[index]
            + (0.1 * (firstDraw + secondDraw + thirdDraw - 1.5) * 2.0);
        crossRate = global::System.Math.Clamp(crossRate, 0.0, 1.0);
        return new ShadeParameters(index, differentialWeight, crossRate);
    }

    /// <summary>
    /// 用成功参数更新记忆 / Update the memory with the successful parameters.
    /// </summary>
    /// <param name="successfulF">成功的差分权重 / The successful differential weights.</param>
    /// <param name="successfulCr">成功的交叉率 / The successful cross rates.</param>
    /// <returns>新记忆 / The new memory.</returns>
    public ShadeMemory Update(IReadOnlyList<double> successfulF, IReadOnlyList<double> successfulCr) {
        ArgumentNullException.ThrowIfNull(successfulF);
        ArgumentNullException.ThrowIfNull(successfulCr);
        if (successfulF.Count == 0) {
            return this;
        }
        if (successfulCr.Count != successfulF.Count) {
            throw new ArgumentException(
                "the successful parameter vectors must have the same length", nameof(successfulCr));
        }

        double weightedSum = 0.0;
        double totalSum = 0.0;
        foreach (double value in successfulF) {
            weightedSum += value * value;
            totalSum += value;
        }
        double crossRateSum = 0.0;
        foreach (double value in successfulCr) {
            crossRateSum += value;
        }

        double lehmerMean = totalSum > 0.0
            ? global::System.Math.Clamp(weightedSum / totalSum, 0.0, 1.0)
            : MemoryF[Cursor];
        double arithmeticMeanCr = global::System.Math.Clamp(
            crossRateSum / successfulCr.Count, 0.0, 1.0);

        var nextF = new List<double>(MemoryF) { };
        var nextCr = new List<double>(MemoryCr) { };
        nextF[Cursor] = lehmerMean;
        nextCr[Cursor] = arithmeticMeanCr;
        return new ShadeMemory(nextF, nextCr, (Cursor + 1) % MemoryF.Count);
    }
}

/// <summary>
/// SHADE 运行状态 / The SHADE run state.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Population">当前种群 / The current population.</param>
/// <param name="Memory">参数记忆 / The parameter memory.</param>
/// <param name="Archive">外部档案 / The external archive.</param>
public sealed record ShadeState<ObjValue, V>(
    IReadOnlyList<IIndividual<ObjValue, V>> Population,
    ShadeMemory Memory,
    IReadOnlyList<Solution<V>> Archive)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// SHADE 运行器 / The SHADE runner.
/// </summary>
/// <remarks>
/// **`pBestPool` 只取排序后的前 `max(1, ⌊size × pBestRate⌋)` 个**。取整份种群会让方向项等于"朝最优的平均移动"，
/// **于是自适应差分退化成一条平滑的下降路径**。
/// **`pBestPool` keeps only the first `max(1, ⌊size × pBestRate⌋)` sorted individuals.** Using the whole population makes the direction
/// term "an average pull toward the best", **degrading adaptive differential evolution into one smooth descent path**.
/// **`r1` 取当前种群，`r2` 取"种群 + 外部档案"的并集**。外部档案保留了已被淘汰的父代位置，让差分向量在更长的历史里取点，
/// 这是 SHADE 相比经典 DE 的关键改动之一；把 `r2` 也限制在当前种群会让档案变成**只写不读**的死存储。
/// **`r1` comes from the current population and `r2` from "population + external archive"**. The archive keeps replaced parents so the
/// difference vector can draw on a longer history — a key SHADE change over classic DE; restricting `r2` to the population too leaves
/// the archive **written but never read**.
/// **`r1` 与当前个体下标相同时顺移一位**。否则差分向量的第一个基点就是父代本身，**而变异仍然"看起来正常"**。
/// **When `r1` collides with the current individual's index it shifts by one.** Otherwise the difference vector's first base is the parent
/// itself, **and the mutant still looks normal**.
/// **二项式交叉必须强制至少一个维度取自变异向量**。否则子代可能与父代完全相同，**于是这次评估白花一份预算且不改动任何东西**。
/// **Binomial crossover must force at least one dimension from the mutant.** Otherwise the trial may equal its parent, **wasting one
/// evaluation without changing anything**。
/// **接受判据是"不差于"（比较结果不大于 0），不是"严格优于"**。SHADE 的"成功"包含等价后代，因为等价解仍会替换位置并进入档案；
/// 改成严格优于会让档案几乎永远为空，**而收敛曲线只是"慢了一点"**。
/// **The acceptance predicate is "not worse" (comparison not greater than 0), not "strictly better"**. SHADE's "success" includes equivalent offspring, because an
/// equivalent solution still replaces its parent and enters the archive; making it strict leaves the archive almost always empty,
/// **and the convergence curve merely looks "a bit slower"**。
/// **规模缩减后按最优保留，而不是按"最近"或"随机"**。这正是"规模缩减"而非"随机淘汰"的意义。
/// **After the size reduction, the best individuals are kept** — not the most recent ones, not a random subset. That is what makes it a
/// size reduction rather than random culling.
/// **档案从头部裁剪**（`RemoveAt(0)`），保留最近被淘汰的父代。
/// **The archive is trimmed from the head** (`RemoveAt(0)`), retaining the most recently replaced parents.
/// **`ShadeNumerics` 只保留原生一支，fdlibm 协议在配置层明确拒绝**。`FdlibmV1` 依赖 Java 的 `StrictMath`（fdlibm 的 `tan`），
/// 本实现无法在 .NET 上复现其逐位结果；写一个"看起来一样"的枚举只会让调用方以为可复现，而**收下配置却按原生数值跑**更糟——
/// 同一份配置会对应两份轨迹。所以 `RegistryParameters` 对 `numerics: shade-fdlibm-v1` 直接失败，`Tangent` 恒用 `System.Math.Tan`。
/// **Only the native branch of `ShadeNumerics` is kept; the fdlibm protocol is refused at the configuration layer.** Its `FdlibmV1`
/// relies on Java's `StrictMath` (fdlibm's `tan`), whose bit-exact results cannot be reproduced on .NET; a "looks the same" enum would only make
/// callers believe it is reproducible, and **accepting the config while running native numerics** is worse — one config would map to two
/// trajectories. So `RegistryParameters` fails on `numerics: shade-fdlibm-v1` outright and `Tangent` always uses `System.Math.Tan`.
/// **没有了"模型自带约束排序"这条分支**。`keepBest` 先问模型有没有约束处理器；本实现把约束处理器放在问题旁边而非模型上
/// （既定形状差异），因此 `keepBest` 直接按严格比较排序。
/// **The "model-carried constraint ranking" branch is gone.** Its `keepBest` first asks the model for a constraint handler; this
/// port keeps the handler next to the problem rather than on the model (an established shape difference), so `keepBest` sorts by strict
/// comparison directly.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class ShadeRunner<ObjValue, V> : AbstractRunner<ShadeState<ObjValue, V>, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IFitnessEvaluator _fitnessEvaluator;
    private readonly IInitializer _initializer;
    private readonly IParameterSource _sizeRatio;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造 SHADE 运行器 / Construct the SHADE runner.
    /// </summary>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="size">初始种群规模 / The initial population size.</param>
    /// <param name="memorySize">记忆长度 / The memory length.</param>
    /// <param name="pBestRate">最优池比例 / The best-pool rate.</param>
    /// <param name="sizeRatio">规模缩减比例 / The size-ratio source.</param>
    /// <param name="minimumSize">规模下限 / The minimum size.</param>
    /// <param name="initializer">初始化器 / The initializer.</param>
    /// <param name="fitnessEvaluator">适应度评估器 / The fitness evaluator.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public ShadeRunner(
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        int size = 20,
        int memorySize = ShadeMemory.DefaultSize,
        double pBestRate = 0.2,
        IParameterSource? sizeRatio = null,
        int minimumSize = 4,
        IInitializer? initializer = null,
        IFitnessEvaluator? fitnessEvaluator = null,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(entropy);
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "population size must be positive");
        }
        if (memorySize <= 0) {
            throw new ArgumentOutOfRangeException(nameof(memorySize), "memory size must be positive");
        }
        if (pBestRate <= 0.0 || pBestRate > 1.0) {
            throw new ArgumentOutOfRangeException(nameof(pBestRate), "pBestRate must be in (0, 1]");
        }
        if (minimumSize <= 0) {
            throw new ArgumentOutOfRangeException(nameof(minimumSize), "minimum size must be positive");
        }
        _entropy = entropy;
        _sizeRatio = sizeRatio ?? new ConstantSource(Flt64.One);
        _initializer = initializer ?? ModelInitializer.Instance;
        _fitnessEvaluator = fitnessEvaluator ?? DefaultOnMissFitnessEvaluator.Instance;
        Size = size;
        MemorySize = memorySize;
        PBestRate = pBestRate;
        MinimumSize = minimumSize;
    }

    /// <summary>初始种群规模 / The initial population size.</summary>
    public int Size { get; }

    /// <summary>记忆长度 / The memory length.</summary>
    public int MemorySize { get; }

    /// <summary>最优池比例 / The best-pool rate.</summary>
    public double PBestRate { get; }

    /// <summary>规模下限 / The minimum size.</summary>
    public int MinimumSize { get; }

    /// <inheritdoc/>
    protected override Task<ShadeState<ObjValue, V>> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        IReadOnlyList<Solution<V>> solutions =
            _initializer.Generate<V, object?, ObjValue>(iteration, model, (ulong)Size, Converter);
        var population = new List<IIndividual<ObjValue, V>>(Size);
        foreach (Solution<V> solution in solutions) {
            IIndividual<ObjValue, V>? individual = ToIndividual(model, solution);
            if (individual is null) {
                continue;
            }
            population.Add(individual);
            if (population.Count == Size) {
                break;
            }
        }
        return Task.FromResult(new ShadeState<ObjValue, V>(
            population,
            ShadeMemory.Create(MemorySize),
            Array.Empty<Solution<V>>()));
    }

    /// <inheritdoc/>
    protected override Task<ShadeState<ObjValue, V>> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        ShadeState<ObjValue, V> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Population.Count == 0) {
            return Task.FromResult(state);
        }

        var sorted = new List<IIndividual<ObjValue, V>>(state.Population);
        sorted.Sort((lhs, rhs) =>
            HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));
        int pBestAmount = global::System.Math.Max(
            1, (int)(sorted.Count * PBestRate));
        List<IIndividual<ObjValue, V>> pBestPool =
            sorted.GetRange(0, global::System.Math.Min(pBestAmount, sorted.Count));

        var nextPopulation = new List<IIndividual<ObjValue, V>>(state.Population.Count);
        var nextArchive = new List<Solution<V>>(state.Archive);
        var successfulF = new List<double>();
        var successfulCr = new List<double>();

        for (int index = 0; index < state.Population.Count; index++) {
            IIndividual<ObjValue, V> target = state.Population[index];
            ShadeParameters parameters = state.Memory.Sample(_entropy);
            IIndividual<ObjValue, V> pBest = pBestPool[global::System.Math.Clamp(
                (int)(_entropy().ToDouble() * pBestPool.Count), 0, pBestPool.Count - 1)];

            int first = global::System.Math.Clamp(
                (int)(_entropy().ToDouble() * state.Population.Count),
                0, state.Population.Count - 1);
            if (first == index) {
                first = (index + 1) % state.Population.Count;
            }

            var secondPool = new List<IReadOnlyList<V>>(state.Population.Count + nextArchive.Count);
            foreach (IIndividual<ObjValue, V> member in state.Population) {
                secondPool.Add(member.Solution);
            }
            foreach (Solution<V> archived in nextArchive) {
                secondPool.Add(archived.Values);
            }
            int second = global::System.Math.Clamp(
                (int)(_entropy().ToDouble() * secondPool.Count), 0, secondPool.Count - 1);

            int dimension = target.Solution.Count;
            var mutant = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double baseValue = target.Solution[dim].ToFlt64().ToDouble();
                double towardBest = pBest.Solution[dim].ToFlt64().ToDouble() - baseValue;
                double difference = state.Population[first].Solution[dim].ToFlt64().ToDouble()
                    - secondPool[second][dim].ToFlt64().ToDouble();
                double value = baseValue
                    + (parameters.DifferentialWeight * towardBest)
                    + (parameters.DifferentialWeight * difference);
                double? lower = NumericSupport.LowerBoundOf(model, dim, Converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, Converter);
                mutant.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(value, lower, upper), Converter));
            }

            var trial = new List<V>(dimension);
            int forced = global::System.Math.Clamp(
                (int)(_entropy().ToDouble() * dimension), 0, dimension - 1);
            for (int dim = 0; dim < dimension; dim++) {
                bool takeMutant = _entropy().ToDouble() < parameters.CrossRate || dim == forced;
                trial.Add(takeMutant ? mutant[dim] : target.Solution[dim]);
            }

            IIndividual<ObjValue, V>? candidate =
                ToIndividual(model, new Solution<V>(trial));
            if (candidate is null) {
                nextPopulation.Add(target);
                continue;
            }
            if (HeuristicOrdering.CompareStrictly(
                    model, candidate.Objective, target.Objective) <= 0) {
                nextPopulation.Add(candidate);
                nextArchive.Add(new Solution<V>(target.Solution));
                successfulF.Add(parameters.DifferentialWeight);
                successfulCr.Add(parameters.CrossRate);
            } else {
                nextPopulation.Add(target);
            }
        }

        int targetSize = global::System.Math.Clamp(
            (int)(Size * _sizeRatio.Value(iteration).ToDouble()),
            global::System.Math.Min(MinimumSize, Size), Size);
        IReadOnlyList<IIndividual<ObjValue, V>> survived =
            nextPopulation.Count > targetSize
                ? KeepBest(model, nextPopulation, targetSize)
                : nextPopulation;

        while (nextArchive.Count > targetSize) {
            nextArchive.RemoveAt(0);
        }

        return Task.FromResult(new ShadeState<ObjValue, V>(
            survived,
            state.Memory.Update(successfulF, successfulCr),
            nextArchive));
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        ShadeState<ObjValue, V> state) => state.Population;

    /// <summary>
    /// 保留最优的若干个个体 / Keep the best individuals.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="individuals">候选个体 / The candidate individuals.</param>
    /// <param name="amount">保留数量 / The retained amount.</param>
    /// <returns>保留的个体 / The retained individuals.</returns>
    private static List<IIndividual<ObjValue, V>> KeepBest(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        IReadOnlyList<IIndividual<ObjValue, V>> individuals,
        int amount) {
        var remaining = new List<IIndividual<ObjValue, V>>(individuals);
        var kept = new List<IIndividual<ObjValue, V>>(amount);
        int taken = global::System.Math.Min(amount, remaining.Count);
        for (int round = 0; round < taken; round++) {
            int bestIndex = 0;
            for (int index = 1; index < remaining.Count; index++) {
                if (HeuristicOrdering.CompareStrictly(
                        model, remaining[index].Objective, remaining[bestIndex].Objective) < 0) {
                    bestIndex = index;
                }
            }
            kept.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }
        return kept;
    }

    /// <summary>
    /// 把一个解包装成个体 / Wrap a solution into an individual.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="solution">解 / The solution.</param>
    /// <returns>个体，评估失败时返回 null / The individual, or null when evaluation fails.</returns>
    private IIndividual<ObjValue, V>? ToIndividual(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Solution<V> solution) {
        ObjValue? objective = _fitnessEvaluator.Evaluate<object?, ObjValue, V>(model, solution);
        if (objective is null) {
            return null;
        }
        return new SolutionWithFitness<ObjValue, V>(solution.Values, objective, 0.0);
    }
}
