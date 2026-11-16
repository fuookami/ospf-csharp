// 岛屿运行器 / The island runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 岛屿运行器 / The island runner.
/// </summary>
/// <remarks>
/// **每座岛独立推进一代，岛屿之间没有共享可变状态**。这一点是"能不能并行"的全部依据：一旦某个岛在推进时读到另一座岛的种群，
/// 并行的顺序就会改变结果，**而串行实现看起来完全正常**（本实现是串行的，见下）。
/// **Each island advances one generation independently and islands share no mutable state.** That is the entire basis for "can this run in
/// parallel": as soon as one island reads another's population while advancing, the parallelism's scheduling changes the result,
/// **while the serial implementation looks perfectly normal** (this implementation is serial, see below).
/// **迁移只在 `iteration.Current` 是 `migrationPeriod` 的整数倍时发生，且 `migrationPeriod == 0` 表示"永不迁移"**。
/// 用 `(iteration.Current + 1)` 或跳过零值判断会让迁移多发生或少发生一次，**而每座岛的规模与最优值都仍然正常**。
/// **Migration happens only when `iteration.Current` is a multiple of `migrationPeriod`, and `migrationPeriod == 0` means "never migrate"**.
/// Using `(iteration.Current + 1)` or skipping the zero check shifts which generations migrate, **while every island's size and best value
/// still look normal**.
/// **状态里的岛数可能多于已配置的岛**（例如调用方恢复了一个更旧的检查点）。多出来的岛**原样保留**，而不是被丢弃。
/// 丢弃会让恢复后的第 2 代凭空少掉几座岛，**而总个体数只是"变少了"**。
/// **The state may hold more islands than are configured** (e.g. the caller restored an older checkpoint). The extra islands are
/// **carried over unchanged** rather than dropped. Dropping them makes the second generation after a restore lose islands out of nowhere,
/// **while the total individual count merely "got smaller"**.
/// **本实现是串行的**。并发 dispatcher 并行推进各岛；这里按索引顺序串行推进，理由是"同样的随机流必须给出同样的结果"。
/// 升级路径：按岛索引分片调度，判据是"并行与串行的结果逐位相同"。`ponytail:` 并发不是这里的目标，正确性优先。
/// **This implementation is serial.** The implementation advances the islands in parallel via a dispatcher; here they advance serially in index order, for the
/// reason that "the same random stream must give the same result". Upgrade path: index-sharded scheduling, gated on "parallel matches
/// serial bit for bit". `ponytail:` concurrency is out of scope; correctness first.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class IslandRunner<ObjValue, V>
    : AbstractRunner<IReadOnlyList<IReadOnlyList<IIndividual<ObjValue, V>>>, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyList<PopulationRunner<ObjValue, V>> _islands;
    private readonly IIslandMigration<ObjValue, V> _migration;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造岛屿运行器 / Construct the island runner.
    /// </summary>
    /// <param name="islands">各岛的运行器 / The per-island runners.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="migrationPeriod">迁移周期，0 表示不迁移 / The migration period; 0 means never.</param>
    /// <param name="migration">迁移策略 / The migration policy.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public IslandRunner(
        IReadOnlyList<PopulationRunner<ObjValue, V>> islands,
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        ulong migrationPeriod = 5UL,
        IIslandMigration<ObjValue, V>? migration = null,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(islands);
        ArgumentNullException.ThrowIfNull(entropy);
        _islands = islands;
        _entropy = entropy;
        _migration = migration ?? new NoMigration<ObjValue, V>();
        MigrationPeriod = migrationPeriod;
    }

    /// <summary>迁移周期 / The migration period.</summary>
    public ulong MigrationPeriod { get; }

    /// <inheritdoc/>
    protected override async Task<IReadOnlyList<IReadOnlyList<IIndividual<ObjValue, V>>>> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        var state = new List<IReadOnlyList<IIndividual<ObjValue, V>>>(_islands.Count);
        foreach (PopulationRunner<ObjValue, V> island in _islands) {
            state.Add(await island.OpenIsland(model, iteration));
        }
        return state;
    }

    /// <inheritdoc/>
    protected override async Task<IReadOnlyList<IReadOnlyList<IIndividual<ObjValue, V>>>> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        IReadOnlyList<IReadOnlyList<IIndividual<ObjValue, V>>> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Count == 0) {
            return state;
        }

        var advanced = new List<IReadOnlyList<IIndividual<ObjValue, V>>>(state.Count);
        for (int index = 0; index < state.Count; index++) {
            if (index >= _islands.Count) {
                advanced.Add(state[index]);
                continue;
            }
            advanced.Add(await _islands[index].StepIsland(
                model, iteration, new List<IIndividual<ObjValue, V>>(state[index])));
        }

        if (MigrationPeriod == 0UL || (ulong)iteration.Current % MigrationPeriod != 0UL) {
            return advanced;
        }

        return _migration.Invoke(
            iteration, advanced, model!, _entropy, FitnessOf);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        IReadOnlyList<IReadOnlyList<IIndividual<ObjValue, V>>> state) {
        var members = new List<IIndividual<ObjValue, V>>();
        foreach (IReadOnlyList<IIndividual<ObjValue, V>> island in state) {
            members.AddRange(island);
        }
        return members;
    }

    /// <summary>
    /// 取个体的适应度数值 / Read an individual's numeric fitness.
    /// </summary>
    /// <remarks>
    /// 目标值类型在本类里是不受约束的，无法直接变成 `double`；迁移策略需要的是一个可比较的数值。
    /// 因此这里只对 `SolutionWithFitness` 取它自带的 `Fitness` 字段，其它实现回退为 0——**迁移选择会退化成"按岛内顺序"**。
    /// 这一点是已知的窄化，不假装通用。
    /// The objective-value type is unconstrained here and cannot become a `double` directly, while the migration policy needs a comparable
    /// number. So this takes the `Fitness` field of `SolutionWithFitness` only and falls back to 0 for other implementations — **migration
    /// selection then degenerates to "island order"**. This is a known narrowing, not claimed to be general.
    /// </remarks>
    /// <param name="individual">个体 / The individual.</param>
    /// <returns>适应度数值 / The numeric fitness.</returns>
    private static double FitnessOf(IIndividual<ObjValue, V> individual) =>
        individual is SolutionWithFitness<ObjValue, V> solution ? solution.Fitness : 0.0;
}
