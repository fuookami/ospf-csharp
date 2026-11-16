// SHADE 运行器测试 / Tests for the SHADE memory and runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
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
/// SHADE 参数记忆 / The SHADE parameter memory.
/// </summary>
public sealed class ShadeMemoryTests {
    /// <summary>**默认记忆是 0.5 与游标 0** / The default memory is 0.5 with cursor 0.</summary>
    [Fact]
    public void Create_ShouldGiveDefaults() {
        var memory = ShadeMemory.Create(3);

        memory.MemoryF.Should().HaveCount(3);
        memory.MemoryCr.Should().HaveCount(3);
        memory.MemoryF[0].Should().Be(0.5);
        memory.MemoryCr[2].Should().Be(0.5);
        memory.Cursor.Should().Be(0);
    }

    /// <summary>**非正记忆长度被拒绝** / A non-positive memory length is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectNonPositiveSize() {
        Action act = () => ShadeMemory.Create(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>**采样按"槽下标 → 权重 → 三次交叉率"的顺序抽取** / Sampling draws slot index, weight, then three cross-rate draws.</summary>
    /// <remarks>
    /// 手算：所有随机数取 0.5。槽下标 = ⌊0.5×2⌋ = 1；权重 = 0.5 + 0.1×tan(π×(0.5−0.5)) = 0.5；
    /// 交叉率 = 0.5 + 0.1×((0.5+0.5+0.5)−1.5)×2 = 0.5。
    /// Hand-computed with every draw at 0.5: slot = ⌊0.5×2⌋ = 1; weight = 0.5 + 0.1×tan(0) = 0.5;
    /// cross rate = 0.5 + 0.1×((0.5+0.5+0.5)−1.5)×2 = 0.5.
    /// </remarks>
    [Fact]
    public void Sample_ShouldUseDrawOrder() {
        var memory = ShadeMemory.Create(2);
        int draws = 0;

        ShadeParameters parameters = memory.Sample(() => { draws++; return new Flt64(0.5); });

        parameters.MemoryIndex.Should().Be(1);
        parameters.DifferentialWeight.Should().Be(0.5);
        parameters.CrossRate.Should().Be(0.5);
        draws.Should().Be(5, "槽下标、权重与三次交叉率扰动共五次 / five draws in total");
    }

    /// <summary>**权重扰动落到非正值时回退到槽内原值，而不是截断到 0** / A non-positive perturbation falls back to the slot value rather than clamping to zero.</summary>
    /// <remarks>
    /// 截断会让差分权重停在 0，**于是变异向量退化成"父代 + 0"，整个种群停止移动**。
    /// Clamping parks the differential weight at zero, **so the mutant degenerates to "parent + 0" and the whole population stops moving**.
    /// </remarks>
    [Fact]
    public void Sample_ShouldFallBackOnNonPositiveWeight() {
        var memory = new ShadeMemory(new[] { 0.1, 0.2 }, new[] { 0.5, 0.5 }, 0);
        var draws = new Queue<double>(new[] { 0.0, 0.0, 0.5, 0.5, 0.5 });

        ShadeParameters parameters = memory.Sample(() => new Flt64(draws.Dequeue()));

        parameters.MemoryIndex.Should().Be(0);
        parameters.DifferentialWeight.Should().Be(0.1,
            "越界的负扰动必须回退到 0.1，而不是 0 / the out-of-range negative perturbation must fall back to 0.1, not 0");
    }

    /// <summary>**权重用 Lehmer 均值、交叉率用算术均值** / The weight uses the Lehmer mean and the cross rate the arithmetic mean.</summary>
    /// <remarks>
    /// 手算：`F = [0.4, 0.6]` → (0.16+0.36)/1.0 = **0.52**（算术均值会是 0.5）；`Cr = [0.8, 0.2]` → 0.5。
    /// Hand-computed: `F = [0.4, 0.6]` → (0.16+0.36)/1.0 = **0.52** (the arithmetic mean would be 0.5); `Cr = [0.8, 0.2]` → 0.5.
    /// </remarks>
    [Fact]
    public void Update_ShouldUseLehmerMeanForWeight() {
        var memory = ShadeMemory.Create(2);

        ShadeMemory updated = memory.Update(new[] { 0.4, 0.6 }, new[] { 0.8, 0.2 });

        updated.MemoryF[0].Should().Be(0.52);
        updated.MemoryCr[0].Should().Be(0.5);
        updated.Cursor.Should().Be(1);
    }

    /// <summary>**没有成功参数时记忆原样返回** / The memory is returned unchanged without any successful parameters.</summary>
    [Fact]
    public void Update_ShouldKeepMemoryWhenNoSuccess() {
        var memory = ShadeMemory.Create(2);

        ShadeMemory updated = memory.Update(Array.Empty<double>(), Array.Empty<double>());

        updated.Should().BeSameAs(memory);
    }

    /// <summary>**游标按模自增** / The cursor advances modulo the memory length.</summary>
    [Fact]
    public void Update_ShouldWrapCursor() {
        var memory = new ShadeMemory(new[] { 0.5, 0.5 }, new[] { 0.5, 0.5 }, 1);

        ShadeMemory updated = memory.Update(new[] { 0.5 }, new[] { 0.5 });

        updated.Cursor.Should().Be(0);
        updated.MemoryF[1].Should().Be(0.5);
    }
}

/// <summary>
/// SHADE 运行器 / The SHADE runner.
/// </summary>
public sealed class ShadeRunnerTests {
    /// <summary>**不差于的后代被接受并进入外部档案** / A not-worse offspring is accepted and enters the external archive.</summary>
    /// <remarks>
    /// 把接受判据改成"严格优于"会让等价后代被拒，**档案于是几乎永远为空，而收敛曲线只是"慢了一点"**。
    /// Making the predicate strict rejects equivalent offspring, **so the archive stays almost always empty while the convergence curve
    /// merely looks "a bit slower"**.
    ///
    /// 本用例把记忆设为"权重 0、交叉率 0"，于是变异向量等于父代、交叉后的试验解也等于父代：
    /// 后代与父代完全等价，正落在"不差于"与"严格优于"的分界上。
    /// This case sets the memory to "weight 0, cross rate 0", so the mutant equals the parent and the trial after crossover equals the
    /// parent too: the offspring is exactly equivalent, right on the boundary between "not worse" and "strictly better".
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldAcceptEquivalentOffspringIntoArchive() {
        ShadeRunner<Flt64, Flt64> runner = BuildRunner(size: 4, sizeRatio: new Flt64(1.0));

        ShadeState<Flt64, Flt64> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), State(new[] { 1.0, 2.0, 3.0, 4.0 }));

        advanced.Archive.Should().HaveCount(4,
            "四个等价后代都应进入档案 / all four equivalent offspring must enter the archive");
        advanced.Population.Should().HaveCount(4);
    }

    /// <summary>**规模缩减保留最优的个体** / A size reduction keeps the best individuals.</summary>
    /// <remarks>
    /// 基线保留的是"最优"而不是"最近"或"随机"，这正是"规模缩减"而非"随机淘汰"的意义。
    /// The code keeps the best, not the most recent nor a random subset; that is what makes it a size reduction rather than random
    /// culling.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldKeepBestOnSizeReduction() {
        ShadeRunner<Flt64, Flt64> runner = BuildRunner(
            size: 4, sizeRatio: new Flt64(0.5), minimumSize: 2);

        ShadeState<Flt64, Flt64> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), State(new[] { 1.0, 2.0, 3.0, 4.0 }));

        advanced.Population.Should().HaveCount(2, "目标规模 = ⌊4×0.5⌋ = 2 / target size = ⌊4×0.5⌋ = 2");
        advanced.Population[0].Objective.ToDouble().Should().Be(1.0);
        advanced.Population[1].Objective.ToDouble().Should().Be(2.0);
        advanced.Archive.Should().HaveCount(2, "档案随种群一起裁剪 / the archive is trimmed with the population");
    }

    /// <summary>**成员取当前种群** / Members are the current population.
    /// </summary>
    [Fact]
    public void MembersOf_ShouldReturnPopulation() {
        ShadeRunner<Flt64, Flt64> runner = BuildRunner(size: 2);
        ShadeState<Flt64, Flt64> state = State(new[] { 5.0, 7.0 });

        IReadOnlyList<IIndividual<Flt64, Flt64>> members = runner.StateMembers(state);

        members.Should().HaveCount(2);
        members[0].Objective.ToDouble().Should().Be(5.0);
    }

    /// <summary>**空种群原样返回** / An empty population is returned unchanged.</summary>
    [Fact]
    public async Task Advance_ShouldReturnEmptyPopulationUnchanged() {
        ShadeRunner<Flt64, Flt64> runner = BuildRunner(size: 4);
        var state = new ShadeState<Flt64, Flt64>(
            Array.Empty<IIndividual<Flt64, Flt64>>(), ShadeMemory.Create(5),
            Array.Empty<Solution<Flt64>>());

        ShadeState<Flt64, Flt64> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced.Population.Should().BeEmpty();
    }

    /// <summary>**初始化给出满规模种群、默认记忆与空档案** / Initialization yields a full population, the default memory and an empty archive.</summary>
    [Fact]
    public async Task Initialize_ShouldProducePopulation() {
        ShadeRunner<Flt64, Flt64> runner = BuildRunner(size: 3, memorySize: 4);

        ShadeState<Flt64, Flt64> state = await runner.InitializeStep(Model(), new Iteration());

        state.Population.Should().HaveCount(3);
        state.Memory.MemoryF.Should().HaveCount(4);
        state.Memory.MemoryF[3].Should().Be(0.5);
        state.Archive.Should().BeEmpty();
    }

    /// <summary>**非法构造参数被拒绝** / Invalid constructor arguments are rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectInvalidConfiguration() {
        Action zeroSize = () => BuildRunner(size: 0);
        Action zeroMemory = () => BuildRunner(size: 4, memorySize: 0);
        Action badRate = () => BuildRunner(size: 4, pBestRate: 0.0);

        zeroSize.Should().Throw<ArgumentOutOfRangeException>();
        zeroMemory.Should().Throw<ArgumentOutOfRangeException>();
        badRate.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>构造一个状态 / Build a state.</summary>
    /// <param name="objectives">目标值序列 / The objective values.</param>
    /// <returns>状态 / The state.</returns>
    private static ShadeState<Flt64, Flt64> State(IReadOnlyList<double> objectives) {
        var population = new List<IIndividual<Flt64, Flt64>>(objectives.Count);
        foreach (double objective in objectives) {
            population.Add(new SolutionWithFitness<Flt64, Flt64>(
                new[] { new Flt64(objective) }, new Flt64(objective), 0.0));
        }
        // 权重与交叉率全 0：变异与交叉都不会改变父代，后代因此与父代完全等价。
        // Weight and cross rate are all zero: neither mutation nor crossover changes the parent, so the offspring is exactly equivalent.
        var memory = new ShadeMemory(new[] { 0.0, 0.0 }, new[] { 0.0, 0.0 }, 0);
        return new ShadeState<Flt64, Flt64>(population, memory, Array.Empty<Solution<Flt64>>());
    }

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="size">规模 / The size.</param>
    /// <param name="memorySize">记忆长度 / The memory length.</param>
    /// <param name="pBestRate">最优池比例 / The best-pool rate.</param>
    /// <param name="sizeRatio">规模比例 / The size ratio.</param>
    /// <param name="minimumSize">规模下限 / The minimum size.</param>
    /// <returns>运行器 / The runner.</returns>
    private static ShadeRunner<Flt64, Flt64> BuildRunner(
        int size,
        int memorySize = ShadeMemory.DefaultSize,
        double pBestRate = 0.2,
        Flt64? sizeRatio = null,
        int minimumSize = 4) =>
        new(() => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(20),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size,
            memorySize: memorySize,
            pBestRate: pBestRate,
            sizeRatio: new ConstantSource(sizeRatio ?? Flt64.One),
            minimumSize: minimumSize,
            initializer: new RampInitializer());

    /// <summary>构造模型 / Build the model.</summary>
    /// <returns>模型 / The model.</returns>
    private static IAbstractCallBackModelInterface<object?, Flt64, Flt64> Model() => new ShadeModel();

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

    /// <summary>SHADE 测试模型 / A SHADE test model.</summary>
    private sealed class ShadeModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
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
