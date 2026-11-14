// 种群运行器测试 / Tests for the population runner.

using System;
using System.Collections.Generic;
using System.Linq;
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
/// 种群运行器 / The population runner.
/// </summary>
public sealed class PopulationRunnerTests {
    /// <summary>**初始化产出恰好 `size` 个个体** / Initialization yields exactly `size` individuals.</summary>
    [Fact]
    public async Task Initialize_ShouldProduceSizeIndividuals() {
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 5);

        List<IIndividual<Flt64, Flt64>> state = await runner.InitializeStep(TwoValueModel(), new Iteration());

        state.Should().HaveCount(5);
        runner.StateMembers(state).Should().HaveCount(5);
    }

    /// <summary>**空种群原样返回** / An empty population is returned unchanged.</summary>
    /// <remarks>
    /// 继续推进会让初始化器被反复调用，**而"种群为空"这件事被伪装成"这一步没有进展"**。
    /// Advancing anyway would call the initializer repeatedly, **disguising "the population is empty" as "no progress this step"**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldReturnEmptyStateUnchanged() {
        var recorder = new RecordingUpdate();
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 5, update: recorder);
        var empty = new List<IIndividual<Flt64, Flt64>>();

        List<IIndividual<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(TwoValueModel(), new Iteration(), empty);

        advanced.Should().BeEmpty();
        recorder.Calls.Should().Be(0, "空种群不应触发更新算子 / an empty population must not invoke the update operator");
    }

    /// <summary>**邻域父代按优度升序交给更新算子** / Neighbourhood parents reach the update operator in ascending quality order.</summary>
    /// <remarks>
    /// 位置更新类算子（GWO 的 α/β/δ 等）依赖"前 k 名即头狼"的约定；不排序会让同一份种群喂给不同算子时得到语义不同的结果，
    /// **而更新算子看不出任何异常**。
    /// Position-update operators (GWO's α/β/δ, …) rely on "the first k are the leaders"; without the ranking the same population means
    /// different things to different operators, **and the update operator notices nothing**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldRankNeighboursBeforeUpdate() {
        var recorder = new RecordingUpdate();
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 3, update: recorder);
        List<IIndividual<Flt64, Flt64>> state = new() {
            Individual(3.0), Individual(1.0), Individual(2.0),
        };

        await runner.AdvanceStep(TwoValueModel(), new Iteration(), state);

        recorder.FirstParentObjectives.Should().Equal(
            new[] { 1.0, 2.0, 3.0 },
            "父代必须按优度升序 / parents must be in ascending quality order");
    }

    /// <summary>**变异作用在整份父代上** / Mutation applies to the whole parent generation.</summary>
    /// <remarks>
    /// 只变异子代会让"变异率"退化成"对交叉产物的二次扰动"，**而参数名仍然叫变异率**。
    /// Mutating only the offspring degrades "mutation rate" into "a second perturbation of the crossover products", **while the parameter is
    /// still called the mutation rate**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldMutateEveryParent() {
        var mutation = new CountingMutation();
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 3, mutation: mutation);
        List<IIndividual<Flt64, Flt64>> state = new() {
            Individual(1.0), Individual(2.0), Individual(3.0),
        };

        await runner.AdvanceStep(TwoValueModel(), new Iteration(), state);

        mutation.Calls.Should().Be(3,
            "变异次数必须等于父代规模 / the mutation count must equal the parent count");
    }

    /// <summary>**幸存者选择拿到父代、子代与变异体的并集** / The survivor selection receives parents, offspring and mutants.</summary>
    /// <remarks>
    /// 只传入子代会让父代无法存活超过一代，**而"精英保留"这一类选择策略因此永远不会被触发**。
    /// Passing only the offspring prevents any parent from surviving more than one generation, **so elite-preserving strategies are never
    /// exercised**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldPassUnionToSurvivor() {
        var survivor = new RecordingSurvivor();
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 3, survivor: survivor);
        List<IIndividual<Flt64, Flt64>> state = new() {
            Individual(1.0), Individual(2.0), Individual(3.0),
        };

        await runner.AdvanceStep(TwoValueModel(), new Iteration(), state);

        survivor.LastParentCount.Should().Be(3);
        survivor.LastOffspringCount.Should().BeGreaterThan(0,
            "子代池必须包含更新与变异的产物 / the offspring pool must contain both update and mutation products");
        survivor.LastAmount.Should().Be(3);
    }

    /// <summary>**幸存者选择的结果被采纳** / The survivor selection's result is adopted.</summary>
    [Fact]
    public async Task Advance_ShouldAdoptSurvivorResult() {
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 2);
        List<IIndividual<Flt64, Flt64>> state = new() { Individual(1.0), Individual(2.0) };

        List<IIndividual<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(TwoValueModel(), new Iteration(), state);

        advanced.Should().HaveCount(2, "下一代规模等于 `size` / the next generation has `size` members");
    }

    /// <summary>**非正种群规模被拒绝** / A non-positive population size is rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectNonPositiveSize() {
        Action act = () => BuildRunner(size: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>**岛的打开与步进复用同一套语义** / Opening and stepping an island reuse the same semantics.</summary>
    [Fact]
    public async Task Island_ShouldReuseInitializeAndAdvance() {
        PopulationRunner<Flt64, Flt64> runner = BuildRunner(size: 4);
        IAbstractCallBackModelInterface<object?, Flt64, Flt64> model = TwoValueModel();

        List<IIndividual<Flt64, Flt64>> island = await runner.OpenIsland(model, new Iteration());
        List<IIndividual<Flt64, Flt64>> next = await runner.StepIsland(model, new Iteration(), island);

        island.Should().HaveCount(4);
        next.Should().HaveCount(4);
    }

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="size">规模 / The size.</param>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="survivor">幸存者选择 / The survivor selection.</param>
    /// <returns>运行器 / The runner.</returns>
    private static PopulationRunner<Flt64, Flt64> BuildRunner(
        int size,
        ISolutionUpdate? update = null,
        IMutationOperator? mutation = null,
        ISurvivorSelection? survivor = null) =>
        new(update ?? new ConstantUpdate(),
            mutation ?? new ConstantMutation(),
            survivor ?? new ElitistSurvivor(),
            () => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(20),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size,
            initializer: new SequentialInitializer(),
            topology: new AllNeighbours());

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), 0.0);

    /// <summary>构造双值模型 / Build a two-value model.</summary>
    /// <returns>模型 / The model.</returns>
    private static IAbstractCallBackModelInterface<object?, Flt64, Flt64> TwoValueModel() =>
        new PopulationModel();

    /// <summary>顺序初始化器 / A sequential initializer.</summary>
    private sealed class SequentialInitializer : IInitializer {
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

    /// <summary>所有个体互为邻居 / Every individual neighbours every other.</summary>
    private sealed class AllNeighbours : ITopology {
        /// <inheritdoc/>
        public IReadOnlySet<int> Neighbours(int index, int size) {
            var result = new HashSet<int>();
            for (int candidate = 0; candidate < size; candidate++) {
                result.Add(candidate);
            }
            return result;
        }
    }

    /// <summary>返回常量解的更新算子 / An update operator returning a constant solution.</summary>
    private class ConstantUpdate : ISolutionUpdate {
        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
            Iteration iteration,
            IReadOnlyList<T> parents,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> =>
            new[] { new Solution<V>(new[] { converter.IntoValue(new Flt64(1.0)) }) };
    }

    /// <summary>
    /// 记录父代顺序的更新算子 / An update operator recording the parent order.
    /// </summary>
    /// <remarks>
    /// **必须是独立的接口实现，不能用 `new` 隐藏基类方法**。接口方法由基类实现，用 `new` 隐藏后通过接口调用仍然走基类版本，
    /// 记录器**永远不会被调到，而测试会"通过"在一个恒为空的结果上**。
    /// **This must be a standalone interface implementation, never a `new`-hidden override.** The interface method is implemented by the base
    /// class, and hiding it with `new` leaves interface dispatch calling the base version, so the recorder **is never invoked and the test
    /// "passes" on a permanently empty result**.
    /// </remarks>
    private sealed class RecordingUpdate : ISolutionUpdate {
        public int Calls { get; private set; }

        public IReadOnlyList<double> FirstParentObjectives { get; private set; } =
            Array.Empty<double>();

        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
            Iteration iteration,
            IReadOnlyList<T> parents,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> {
            Calls++;
            if (parents.Count > 0) {
                var recorded = new List<double>(parents.Count);
                foreach (T parent in parents) {
                    recorded.Add(parent.Objective is Flt64 objective ? objective.ToDouble() : double.NaN);
                }
                FirstParentObjectives = recorded;
            }
            return new[] { new Solution<V>(new[] { converter.IntoValue(new Flt64(1.0)) }) };
        }
    }

    /// <summary>返回常量解的变异算子 / A mutation operator returning a constant solution.</summary>
    private class ConstantMutation : IMutationOperator {
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
            new(new[] { converter.IntoValue(new Flt64(2.0)) });
    }

    /// <summary>
    /// 计数变异算子 / A counting mutation operator.
    /// </summary>
    private sealed class CountingMutation : IMutationOperator {
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public Solution<V> Invoke<V, TObj, TObjValue, T>(
            Iteration iteration,
            T individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy,
            Flt64 mutationRate)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> {
            Calls++;
            return new Solution<V>(new[] { converter.IntoValue(new Flt64(2.0)) });
        }
    }

    /// <summary>记录入参的幸存者选择 / A survivor selection that records its inputs.</summary>
    private sealed class RecordingSurvivor : ISurvivorSelection {
        public int LastParentCount { get; private set; }

        public int LastOffspringCount { get; private set; }

        public int LastAmount { get; private set; }

        /// <inheritdoc/>
        public IReadOnlyList<T> Select<TObj, TObjValue, V, T>(
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IReadOnlyList<T> parents,
            IReadOnlyList<T> offspring,
            int amount)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> {
            LastParentCount = parents.Count;
            LastOffspringCount = offspring.Count;
            LastAmount = amount;
            var union = new List<T>(parents);
            union.AddRange(offspring);
            union.Sort((lhs, rhs) =>
                HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));
            return union.Count <= amount ? union : union.GetRange(0, amount);
        }
    }

    /// <summary>双值测试模型 / A two-value test model.</summary>
    private sealed class PopulationModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
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
