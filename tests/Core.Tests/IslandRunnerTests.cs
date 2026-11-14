// 岛屿运行器测试 / Tests for the island runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 岛屿运行器 / The island runner.
/// </summary>
public sealed class IslandRunnerTests {
    /// <summary>**每座岛各自开出一个种群** / Each island opens its own population.</summary>
    [Fact]
    public async Task Initialize_ShouldOpenEveryIsland() {
        IslandRunner<Flt64, Flt64> runner = BuildRunner(
            new[] { 2, 3 }, migrationPeriod: 5UL, migration: new RecordingMigration<Flt64, Flt64>());

        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> state =
            await runner.InitializeStep(Model(), new Iteration());

        state.Should().HaveCount(2);
        state[0].Should().HaveCount(2);
        state[1].Should().HaveCount(3);
    }

    /// <summary>**成员是各岛种群的拼接** / Members are the concatenation of the islands.</summary>
    [Fact]
    public async Task MembersOf_ShouldFlattenIslands() {
        IslandRunner<Flt64, Flt64> runner = BuildRunner(new[] { 2, 3 });
        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> state =
            await runner.InitializeStep(Model(), new Iteration());

        runner.StateMembers(state).Should().HaveCount(5);
    }

    /// <summary>**空状态原样返回** / An empty state is returned unchanged.</summary>
    [Fact]
    public async Task Advance_ShouldReturnEmptyStateUnchanged() {
        IslandRunner<Flt64, Flt64> runner = BuildRunner(new[] { 2 });
        var state = new List<IReadOnlyList<IIndividual<Flt64, Flt64>>>();

        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced.Should().BeSameAs(state);
    }

    /// <summary>**状态里多出来的岛原样保留** / Islands present in the state but not configured are carried over unchanged.</summary>
    /// <remarks>
    /// 丢弃会让恢复后的第 2 代凭空少掉几座岛，**而总个体数只是"变少了"**。
    /// Dropping them makes the second generation after a restore lose islands out of nowhere, **while the total individual count merely
    /// "got smaller"**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldCarryOverExtraIslands() {
        IslandRunner<Flt64, Flt64> runner = BuildRunner(new[] { 2 });
        var extra = new List<IIndividual<Flt64, Flt64>> { Individual(42.0) };
        var state = new List<IReadOnlyList<IIndividual<Flt64, Flt64>>> {
            new List<IIndividual<Flt64, Flt64>> { Individual(1.0) },
            extra,
        };

        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced.Should().HaveCount(2, "第二座岛没有对应的运行器，必须原样保留 / the second island has no runner and must be kept");
        advanced[1].Should().BeSameAs(extra);
    }

    /// <summary>**只有迭代数是迁移周期的整数倍时才迁移** / Migration happens only on multiples of the period.</summary>
    /// <remarks>
    /// 用 `(iteration.Current + 1)` 判断会让迁移错位一代，**而每座岛的规模与最优值都仍然正常**。
    /// Using `(iteration.Current + 1)` shifts which generation migrates, **while every island's size and best value still look normal**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldMigrateOnlyOnPeriod() {
        var migration = new RecordingMigration<Flt64, Flt64>();
        IslandRunner<Flt64, Flt64> runner = BuildRunner(
            new[] { 2, 2 }, migrationPeriod: 2UL, migration: migration);
        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> state =
            await runner.InitializeStep(Model(), new Iteration());

        await runner.AdvanceStep(Model(), At(1), state);
        migration.Calls.Should().Be(0, "第 1 代不是 2 的整数倍 / generation 1 is not a multiple of 2");

        await runner.AdvanceStep(Model(), At(2), state);
        migration.Calls.Should().Be(1, "第 2 代应当迁移 / generation 2 must migrate");
    }

    /// <summary>**周期为 0 表示永不迁移** / A period of 0 means never migrate.</summary>
    [Fact]
    public async Task Advance_ShouldNeverMigrateWhenPeriodIsZero() {
        var migration = new RecordingMigration<Flt64, Flt64>();
        IslandRunner<Flt64, Flt64> runner = BuildRunner(
            new[] { 2, 2 }, migrationPeriod: 0UL, migration: migration);
        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> state =
            await runner.InitializeStep(Model(), new Iteration());

        await runner.AdvanceStep(Model(), At(2), state);
        await runner.AdvanceStep(Model(), At(4), state);

        migration.Calls.Should().Be(0, "0 表示不迁移 / 0 means never");
    }

    /// <summary>构造指定代数的迭代状态 / Build the iteration state at a given generation.</summary>
    /// <param name="current">迭代数 / The generation.</param>
    /// <returns>迭代状态 / The iteration state.</returns>
    private static Iteration At(int current) {
        var iteration = new Iteration();
        iteration.Restore(current, 0, TimeSpan.Zero);
        return iteration;
    }

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="value">位置与目标值 / The position and objective.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double value) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(value) }, new Flt64(value), value);

    /// <summary>构造岛屿运行器 / Build the island runner.</summary>
    /// <param name="sizes">各岛规模 / The per-island sizes.</param>
    /// <param name="migrationPeriod">迁移周期 / The migration period.</param>
    /// <param name="migration">迁移策略 / The migration policy.</param>
    /// <returns>运行器 / The runner.</returns>
    private static IslandRunner<Flt64, Flt64> BuildRunner(
        IReadOnlyList<int> sizes,
        ulong migrationPeriod = 5UL,
        IIslandMigration<Flt64, Flt64>? migration = null) {
        var islands = new List<PopulationRunner<Flt64, Flt64>>(sizes.Count);
        foreach (int size in sizes) {
            islands.Add(new PopulationRunner<Flt64, Flt64>(
                new PassThroughUpdate(),
                new IdentityMutation(),
                GenerationalSurvivor.Instance,
                () => new Flt64(0.5),
                new BestArchive<Flt64, Flt64>(20),
                NeverStop.Instance,
                IIntoValue<Flt64>.Identity,
                size: size,
                initializer: new RampInitializer()));
        }
        return new IslandRunner<Flt64, Flt64>(
            islands,
            () => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(20),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            migrationPeriod: migrationPeriod,
            migration: migration);
    }

    /// <summary>构造模型 / Build the model.</summary>
    /// <returns>模型 / The model.</returns>
    private static IAbstractCallBackModelInterface<object?, Flt64, Flt64> Model() => new IslandModel();

    /// <summary>原样返回父代的更新算子 / An update operator that returns its parents unchanged.</summary>
    private sealed class PassThroughUpdate : ISolutionUpdate {
        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
            Iteration iteration,
            IReadOnlyList<T> parents,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> {
            var result = new List<Solution<V>>(parents.Count);
            foreach (T parent in parents) {
                result.Add(new Solution<V>(parent.Solution));
            }
            return result;
        }
    }

    /// <summary>原样返回个体的变异算子 / A mutation operator that returns its individual unchanged.</summary>
    private sealed class IdentityMutation : IMutationOperator {
        /// <inheritdoc/>
        public Solution<V> Invoke<V, TObj, TObjValue, T>(
            Iteration iteration,
            T individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy,
            Flt64 mutationRate)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> =>
            new(individual.Solution);
    }

    /// <summary>记录迁移调用次数 / Record how often migration is invoked.</summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    private sealed class RecordingMigration<ObjValue, V> : IIslandMigration<ObjValue, V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        /// <summary>调用次数 / The number of invocations.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public IReadOnlyList<IReadOnlyList<T>> Invoke<T>(
            Iteration iteration,
            IReadOnlyList<IReadOnlyList<T>> islands,
            IAbstractCallBackModelInterface<object, ObjValue, V> model,
            Func<Flt64> entropy,
            Func<T, double> fitness)
            where T : IIndividual<ObjValue, V> {
            Calls++;
            return islands;
        }
    }

    /// <summary>递增初始化器 / A ramp initializer.</summary>
    private sealed class RampInitializer : IInitializer {
        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
            Iteration iteration,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            ulong amount,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> {
            var result = new List<Solution<V>>((int)amount);
            for (int index = 0; index < (int)amount; index++) {
                result.Add(new Solution<V>(new[] { converter.IntoValue(new Flt64(index)) }));
            }
            return result;
        }
    }

    /// <summary>岛屿测试模型 / An island test model.</summary>
    private sealed class IslandModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

        /// <inheritdoc/>
        public IAbstractMutableTokenTable<Flt64> Tokens { get; } =
            new ConcurrentAutoTokenTable<Flt64>(
                new Fuookami.Ospf.Math.Symbol.LinearCategory(),
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (values => true, "c1") };

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, object?> Extractor, string Name)> ObjectiveFunctions =>
            new (Func<IReadOnlyList<Flt64>, object?>, string)[] { (values => null, "f1") };

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) =>
            solution.Count == 0 ? DefaultObjective : solution[0];

        /// <inheritdoc/>
        public Order CompareObjective(Flt64 lhs, Flt64 rhs) =>
            OrderHelpers.OrderOf(lhs.ToDouble().CompareTo(rhs.ToDouble()));

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
