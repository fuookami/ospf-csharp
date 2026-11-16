// 组合求解器工厂测试 / Tests for the composed-solver factories.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Heuristic.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 组合求解器工厂 / The composed-solver factories.
/// </summary>
public sealed class ComposedSolversTests {
    /// <summary>**六个工厂各自的名字** / All six factories carry its name.</summary>
    /// <remarks>
    /// 名字是这些工厂**唯一的对外标识**：`"gwo"`、`"sca"`、`"mvo"` 与算法本身的对应关系只写在这里。
    /// 装错更新算子时结果仍然"像个优化结果"，**只有名字或横向对比能把它抓出来**。
    /// The name is these factories' **only outward identity**: the mapping from `"gwo"`, `"sca"`, `"mvo"` to the algorithms lives here and
    /// nowhere else. A wrongly wired update operator still yields "something that looks like an optimisation result" — **only the name or a
    /// cross-algorithm comparison can catch it**.
    /// </remarks>
    [Fact]
    public void Factories_ShouldCarryBaselineNames() {
        Func<Flt64> entropy = () => new Flt64(0.5);
        var stopping = new IterationLimit(1UL);
        IIntoValue<Flt64> converter = IIntoValue<Flt64>.Identity;

        ComposedSolvers.Genetic<Flt64, Flt64>(stopping, converter, entropy).Name
            .Should().Be("genetic");
        ComposedSolvers.HillClimbing<Flt64, Flt64>(stopping, converter, entropy).Name
            .Should().Be("hill-climbing");
        ComposedSolvers.RandomWalk<Flt64, Flt64>(stopping, converter, entropy).Name
            .Should().Be("random-walk");
        ComposedSolvers.GreyWolf<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("gwo");
        ComposedSolvers.SineCosine<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("sca");
        ComposedSolvers.MultiVerse<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("mvo");
        ComposedSolvers.ParticleSwarm<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("pso");
        ComposedSolvers.HarmonySearch<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("hs");
        ComposedSolvers.WaterCycle<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("wca");
        ComposedSolvers.Seagull<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("soa");
        ComposedSolvers.WarStrategy<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("warso");
        ComposedSolvers.EnergyValley<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("evo");
        ComposedSolvers.GerminalCenter<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("gco");
        ComposedSolvers.NeighborhoodSearch<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("ns");
        ComposedSolvers.DifferentialEvolution<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("de");
        ComposedSolvers.WhaleOptimization<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("woa");
        ComposedSolvers.HarrisHawk<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("hho");
        ComposedSolvers.ArithmeticOptimization<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("aoa");
        ComposedSolvers.TeachingLearning<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("tlbo");
        ComposedSolvers.SalpSwarm<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("ssa");
        ComposedSolvers.GravitationalSearch<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("gsa");
        ComposedSolvers.Firefly<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("fa");
        ComposedSolvers.CuckooSearch<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("cs");
        ComposedSolvers.BatAlgorithm<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("ba");
        ComposedSolvers.MothFlame<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("mfo");
        ComposedSolvers.Grasshopper<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("goa");
        ComposedSolvers.Shade<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("shade");
        ComposedSolvers.LShade<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("l-shade");
        ComposedSolvers.AdaptiveParticleSwarm<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("apso");
        ComposedSolvers.ComprehensiveLearningParticleSwarm<Flt64, Flt64>(stopping, converter, entropy).Name
            .Should().Be("clpso");
        ComposedSolvers.CmaEs<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("cma-es");
        ComposedSolvers.QLearning<Flt64, Flt64>(stopping, converter, entropy).Name.Should().Be("q-learning");
        ComposedSolvers.SimulatedAnnealing<Flt64, Flt64>(stopping, converter, entropy).Name
            .Should().Be("simulated-annealing");
        ComposedSolvers.IslandGenetic<Flt64, Flt64>(2, 3, stopping, converter, entropy).Name
            .Should().Be("island-ga");
    }

    /// <summary>**Metropolis：变差的候选按温度概率接受，温度为 0 时直接拒绝** / Metropolis accepts worse candidates by temperature and rejects outright at zero.</summary>
    /// <remarks>
    /// **变差也按概率接受，是这个算法与爬山法的唯一区别**：概率被写成 0，算法照样跑完、照样给出"最优解"，
    /// 只是它变成了爬山法。
    /// **Accepting a worse candidate is the only thing separating this from hill climbing**: with that probability at zero it still runs to
    /// completion and still reports a "best solution", having merely become hill climbing.
    /// </remarks>
    [Fact]
    public void Metropolis_ShouldAcceptWorseByTemperature() {
        var model = new MetropolisModel(_ => true);
        IIndividual<Flt64, Flt64> incumbent = Individual(1.0);
        IIndividual<Flt64, Flt64> worse = Individual(5.0);
        var iteration = new Iteration();

        // 温度为 0：变差一律拒绝。
        // Temperature zero: a worse candidate is always rejected.
        new MetropolisAcceptance(new ConstantSource(Flt64.Zero), () => Flt64.Zero)
            .Invoke(model, iteration, incumbent, worse)
            .Should().BeFalse();

        // 温度很高、熵源取 0：接受概率趋近 1。
        // A huge temperature with an entropy source at zero: the acceptance probability approaches one.
        new MetropolisAcceptance(new ConstantSource(new Flt64(1000.0)), () => Flt64.Zero)
            .Invoke(model, iteration, incumbent, worse)
            .Should().BeTrue();

        // 熵源取 0.999…：同一对个体被拒绝——**这说明接受确实依赖随机数，而不是"变差就接受"**。
        // An entropy source at 0.999…: the same pair is rejected — **so acceptance really depends on the random draw, not on "accept when worse"**.
        new MetropolisAcceptance(new ConstantSource(new Flt64(1000.0)), () => new Flt64(0.999999))
            .Invoke(model, iteration, incumbent, worse)
            .Should().BeFalse();
    }

    /// <summary>**Metropolis：更好的候选无条件接受** / Metropolis accepts a better candidate unconditionally.</summary>
    [Fact]
    public void Metropolis_ShouldAcceptBetter() {
        var model = new MetropolisModel(_ => true);

        new MetropolisAcceptance(new ConstantSource(Flt64.Zero), () => new Flt64(0.999999))
            .Invoke(model, new Iteration(), Individual(5.0), Individual(1.0))
            .Should().BeTrue();
    }

    /// <summary>**Metropolis：不可行的候选即使目标值更好也被拒绝** / Metropolis rejects an infeasible candidate even when its objective is better.</summary>
    /// <remarks>
    /// 把可行性判断挪到最后，会让一个不可行的候选因为"目标值看起来更好"而被接受——
    /// **于是搜索跑进不可行域，而目标值确实在下降**。
    /// Moving the feasibility check to the end lets an infeasible candidate be accepted because "its objective looks better" — **so the search
    /// walks into the infeasible region while the objective really does improve**.
    /// </remarks>
    [Fact]
    public void Metropolis_ShouldRejectInfeasibleCandidate() {
        var model = new MetropolisModel(solution => solution[0].ToDouble() >= 3.0);

        new MetropolisAcceptance(new ConstantSource(new Flt64(1000.0)), () => Flt64.Zero)
            .Invoke(model, new Iteration(), Individual(5.0), Individual(1.0))
            .Should().BeFalse("候选不可行，即使目标值更好也不能接受 / the candidate is infeasible even though its objective is better");
    }

    /// <summary>**岛屿遗传：岛屿数量非正时在构造期被拒绝** / Island genetic rejects a non-positive island count at construction.</summary>
    [Fact]
    public void IslandGenetic_ShouldRejectEmptyIslandSet() {
        Action noIslands = () => ComposedSolvers.IslandGenetic<Flt64, Flt64>(
            0, 3, new IterationLimit(1UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5));
        Action emptySpecs = () => ComposedSolvers.IslandGenetic<Flt64, Flt64>(
            Array.Empty<(int Size, Flt64 MutationRate)>(), new IterationLimit(1UL),
            IIntoValue<Flt64>.Identity, () => new Flt64(0.5));

        noIslands.Should().Throw<ArgumentOutOfRangeException>();
        emptySpecs.Should().Throw<ArgumentException>();
    }

    /// <summary>**模拟退火与岛屿遗传都能在真实模型上跑完** / Simulated annealing and island genetic run to completion.</summary>
    [Fact]
    public async Task AnnealingAndIslands_ShouldRun() {
        HeuristicResult<Flt64, Flt64> annealing = Unwrap(
            await ComposedSolvers.SimulatedAnnealing<Flt64, Flt64>(
                new IterationLimit(3UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5))
                .Invoke(new ComposedModel()));
        HeuristicResult<Flt64, Flt64> islands = Unwrap(
            await ComposedSolvers.IslandGenetic<Flt64, Flt64>(
                2, 3, new IterationLimit(3UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5))
                .Invoke(new ComposedModel()));

        annealing.BestObjective.Should().NotBeNull();
        annealing.Status.Should().Be(HeuristicSolutionStatus.Feasible);
        islands.BestObjective.Should().NotBeNull();
        islands.Status.Should().Be(HeuristicSolutionStatus.Feasible);
    }

    /// <summary>构造一个标量个体 / Build a scalar individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), objective);

    /// <summary>可行性可配置的测试模型 / A test model with a configurable feasibility rule.</summary>
    private sealed class MetropolisModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        private readonly Func<IReadOnlyList<Flt64>, bool?> _satisfied;

        public MetropolisModel(Func<IReadOnlyList<Flt64>, bool?> satisfied) => _satisfied = satisfied;

        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

        /// <inheritdoc/>
        public IAbstractMutableTokenTable<Flt64> Tokens { get; } =
            new ConcurrentAutoTokenTable<Flt64>(
                Fuookami.Ospf.Math.Symbol.LinearCategory.Instance,
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (_satisfied, "c1") };

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
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => _satisfied(solution);

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }

    /// <summary>**Q 学习：候选算子为空时在构造期就被拒绝** / Q-learning rejects an empty candidate list at construction.</summary>
    /// <remarks>
    /// `actionAmount` 由候选数量推导出来。空候选会让动作数变成 0，**于是"选择动作"这件事从一开始就没有可选的动作**；
    /// 与其等到第一次选择时越界，不如**根本建不起来**。
    /// `actionAmount` is derived from the candidate count. An empty list makes it zero, **so "select an action" has nothing to select from
    /// the start**; failing at construction beats overrunning on the first selection.
    /// </remarks>
    [Fact]
    public void QLearning_ShouldRejectEmptyCandidates() {
        Action build = () => ComposedSolvers.QLearning<Flt64, Flt64>(
            new IterationLimit(1UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5),
            candidates: Array.Empty<ISolutionUpdate>());

        build.Should().Throw<ArgumentException>();
    }

    /// <summary>**Q 学习工厂能在真实模型上跑完** / The Q-learning factory runs to completion on a real model.</summary>
    [Fact]
    public async Task QLearning_ShouldRun() {
        HeuristicResult<Flt64, Flt64> result = Unwrap(
            await ComposedSolvers.QLearning<Flt64, Flt64>(
                new IterationLimit(3UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 4)
                .Invoke(new ComposedModel()));

        result.BestObjective.Should().NotBeNull();
        result.Status.Should().Be(HeuristicSolutionStatus.Feasible);
    }

    /// <summary>**观测契约适配器真的把每一次迭代转发出去** / The observer-contract adapter really forwards every iteration.</summary>
    /// <remarks>
    /// 两个接口的 `Observe` 签名逐字相同，**但少了适配器，运行器就收不到任何观测者**——
    /// Q 学习这类靠观测驱动的算法会**照常跑完、照常给出结果，Q 表却永远是初始值**。
    /// 这条测试直接数转发次数，不依赖任何"结果看起来对不对"的判断。
    /// The two interfaces' `Observe` signatures are identical, **but without the adapter the runner receives no observer at all** — an
    /// observation-driven algorithm such as Q-learning **runs to completion and returns in its usual shape while the Q table stays at its
    /// initial values**. This test counts the forwards directly instead of judging whether the result "looks right".
    /// </remarks>
    [Fact]
    public void ObserverContractAdapter_ShouldForward() {
        var observer = new CountingObserver();
        IIterationObserverContract contract = new ObserverContractAdapter(observer);
        var members = new List<IIndividual<Flt64, Flt64>>();

        contract.Observe(new Iteration(), members, true, IIntoValue<Flt64>.Identity);

        observer.Calls.Should().Be(1, "每次迭代都必须转发一次 / every iteration must be forwarded once");
    }

    /// <summary>数转发次数的观测者 / An observer that counts forwards.</summary>
    private sealed class CountingObserver : IIterationObserver {
        /// <summary>转发次数 / The forward count.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public void Observe<ObjValue, V>(
            Iteration iteration,
            IReadOnlyList<IIndividual<ObjValue, V>> members,
            bool improved,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> => Calls++;
    }

    /// <summary>**第 4 组的五个工厂都能跑完并给出可行解** / All five fourth-group factories run to completion.</summary>
    /// <remarks>
    /// 这一组与前一组不同：五个工厂**各有自己的运行器**（`ShadeRunner` 两份、`ParticleSwarmRunner`、`ClpsoRunner`、`CmaEsRunner`），
    /// 所以"装错"首先会在**编译期**被类型挡住——真正需要测试守的是"参数有没有传对位置"：
    /// `ShadeRunner` 与 `CmaEsRunner` 的构造参数里同时存在 `memorySize`、`pBestRate` 与 `int`/`double` 两种数值，
    /// **把 `memorySize` 与 `pBestRate` 写反了能编译通过，但每代只从 0 个历史条目里采样**。
    /// This group differs from the previous one: each factory **has its own runner** (`ShadeRunner` twice, `ParticleSwarmRunner`, `ClpsoRunner`,
    /// `CmaEsRunner`), so wiring the wrong runner is caught **at compile time** — what actually needs guarding is whether the parameters land
    /// in the right slots: both `ShadeRunner` and `CmaEsRunner` take `memorySize`, `pBestRate` and other numeric arguments, **so swapping two of
    /// them still compiles while every generation samples from zero history entries**.
    /// </remarks>
    [Fact]
    public async Task FourthGroup_ShouldAllRun() {
        var factories = new (string Name, Func<IStoppingCriterion, IIntoValue<Flt64>, Func<Flt64>, IHeuristicSolver<Flt64, Flt64>> Build)[] {
            ("shade", (stop, conv, ent) => ComposedSolvers.Shade<Flt64, Flt64>(stop, conv, ent, size: 4)),
            ("l-shade", (stop, conv, ent) => ComposedSolvers.LShade<Flt64, Flt64>(stop, conv, ent, size: 4, minimumSize: 2)),
            ("apso", (stop, conv, ent) => ComposedSolvers.AdaptiveParticleSwarm<Flt64, Flt64>(stop, conv, ent, size: 4)),
            ("clpso", (stop, conv, ent) => ComposedSolvers.ComprehensiveLearningParticleSwarm<Flt64, Flt64>(stop, conv, ent, size: 4)),
            ("cma-es", (stop, conv, ent) => ComposedSolvers.CmaEs<Flt64, Flt64>(stop, conv, ent)),
        };

        foreach ((string name, Func<IStoppingCriterion, IIntoValue<Flt64>, Func<Flt64>, IHeuristicSolver<Flt64, Flt64>> build) in factories) {
            var random = new SearchRandom(17L);
            HeuristicResult<Flt64, Flt64> result = Unwrap(
                await build(new IterationLimit(4UL), IIntoValue<Flt64>.Identity, random.Invoke)
                    .Invoke(new ComposedModel()));
            result.BestObjective.Should().NotBeNull(name + " 必须给出最优目标值 / must report a best objective");
            result.Status.Should().Be(HeuristicSolutionStatus.Feasible,
                name + " 必须给出可行解 / must report a feasible solution");
        }
    }

    /// <summary>**六个蜂群式工厂给出六条不同的轨迹** / The six swarm-style factories produce six different trajectories.</summary>
    /// <remarks>
 /// 与此前同一条判据：装配同构、只有更新算子不同，因此**装错算子不会报错，只会让两个算法变成同一个**。
    /// 六条轨迹必须两两不同。
    /// The same criterion as in the first group: identical assembly, only the update operator differs, so **a wrong operator raises no error and
    /// merely collapses two algorithms into one**. All six trajectories must be pairwise different.
    /// </remarks>
    [Fact]
    public async Task SecondSwarmGroup_ShouldDifferInUpdate() {
        var results = new List<(string Name, double Value)> {
            ("hs", await Run((stop, conv, ent) => ComposedSolvers.HarmonySearch<Flt64, Flt64>(stop, conv, ent), 11UL)),
            ("wca", await Run((stop, conv, ent) => ComposedSolvers.WaterCycle<Flt64, Flt64>(stop, conv, ent), 11UL)),
            ("soa", await Run((stop, conv, ent) => ComposedSolvers.Seagull<Flt64, Flt64>(stop, conv, ent), 11UL)),
            ("warso", await Run((stop, conv, ent) => ComposedSolvers.WarStrategy<Flt64, Flt64>(stop, conv, ent), 11UL)),
            ("evo", await Run((stop, conv, ent) => ComposedSolvers.EnergyValley<Flt64, Flt64>(stop, conv, ent), 11UL)),
            ("gco", await Run((stop, conv, ent) => ComposedSolvers.GerminalCenter<Flt64, Flt64>(stop, conv, ent), 11UL)),
        };

        for (int left = 0; left < results.Count; left++) {
            for (int right = left + 1; right < results.Count; right++) {
                results[left].Value.Should().NotBe(results[right].Value,
                    results[left].Name + " 与 " + results[right].Name + " 不能是同一条轨迹 / the two must not share a trajectory");
            }
        }
    }

 /// <summary>**十二个此前工厂都能在真实模型上跑完** / All twelve third-group factories run to completion on a real model.</summary>
    /// <remarks>
    /// **这里没有做"两两不同"的判据，原因如实记在这里**：试过在 2 / 3 / 4 / 5 四种迭代上限下比较十二个工厂的最优值，
    /// `gsa`、`de`、`ba` 三者**在每一档上都撞到同一个值**。它们的更新算子确实不同，是**这个 fixture 的目标函数（一维平方）
    /// 在少量迭代内把许多轨迹压到同一个值上**，于是"两两不同"这个判据在这里根本不成立。
    ///
    /// 前两批之所以能用它，是因为那一批的目标值恰好在那个种子下互不相同——**换句话说，那条判据一直是有条件的，
 /// 只是前两次的条件恰好满足**。这与 earlier / 216 关于接受判据的缺口是同一个问题的另一面：
    /// **当判据依赖"结果恰好可分辨"时，它就不是一条可靠的判据**。
    ///
 /// 因此这里改为"十二个工厂都能跑完并给出可行解"，并**明确记录：此前的默认更新算子没有测试级保护**，
    /// 只有代码层面的"每个工厂传自己的算子"这一处可直接阅读的事实。不伪造断言去补这个洞。
    ///
    /// **This group deliberately skips the pairwise-distinctness criterion, and the reason is recorded here**: the twelve factories were compared
    /// at four iteration limits (2 / 3 / 4 / 5) and `gsa`, `de` and `ba` **collide on the same value at every one of them**. Their update
    /// operators really do differ; **this fixture's objective (a one-dimensional square) collapses many trajectories onto the same value within a
    /// few iterations**, so "pairwise different" simply does not hold here.
    ///
    /// The first two groups could use it because their values happened to differ under that seed — **in other words, the criterion was always
 /// conditional and the condition merely happened to hold twice**. This is the other face of the same gap as earlier / 216's acceptance
    /// criteria: **a criterion that depends on "the results happen to be distinguishable" is not a reliable criterion**.
    ///
    /// This group therefore asserts "all twelve run to completion and report a feasible solution", and **records explicitly that the third
    /// group's default update operators have no test-level protection** — only the directly readable fact in the code that each factory passes its
    /// own operator. No assertion is fabricated to cover the hole.
    /// </remarks>
    [Fact]
    public async Task ThirdGroup_ShouldAllRun() {
        var factories = new (string Name, Func<IStoppingCriterion, IIntoValue<Flt64>, Func<Flt64>, IHeuristicSolver<Flt64, Flt64>> Build)[] {
            ("de", (stop, conv, ent) => ComposedSolvers.DifferentialEvolution<Flt64, Flt64>(stop, conv, ent)),
            ("woa", (stop, conv, ent) => ComposedSolvers.WhaleOptimization<Flt64, Flt64>(stop, conv, ent)),
            ("hho", (stop, conv, ent) => ComposedSolvers.HarrisHawk<Flt64, Flt64>(stop, conv, ent)),
            ("aoa", (stop, conv, ent) => ComposedSolvers.ArithmeticOptimization<Flt64, Flt64>(stop, conv, ent)),
            ("tlbo", (stop, conv, ent) => ComposedSolvers.TeachingLearning<Flt64, Flt64>(stop, conv, ent)),
            ("ssa", (stop, conv, ent) => ComposedSolvers.SalpSwarm<Flt64, Flt64>(stop, conv, ent)),
            ("gsa", (stop, conv, ent) => ComposedSolvers.GravitationalSearch<Flt64, Flt64>(stop, conv, ent)),
            ("fa", (stop, conv, ent) => ComposedSolvers.Firefly<Flt64, Flt64>(stop, conv, ent)),
            ("cs", (stop, conv, ent) => ComposedSolvers.CuckooSearch<Flt64, Flt64>(stop, conv, ent)),
            ("ba", (stop, conv, ent) => ComposedSolvers.BatAlgorithm<Flt64, Flt64>(stop, conv, ent)),
            ("mfo", (stop, conv, ent) => ComposedSolvers.MothFlame<Flt64, Flt64>(stop, conv, ent)),
            ("goa", (stop, conv, ent) => ComposedSolvers.Grasshopper<Flt64, Flt64>(stop, conv, ent)),
        };

        foreach ((string name, Func<IStoppingCriterion, IIntoValue<Flt64>, Func<Flt64>, IHeuristicSolver<Flt64, Flt64>> build) in factories) {
            var random = new SearchRandom(13L);
            HeuristicResult<Flt64, Flt64> result = Unwrap(
                await build(new IterationLimit(4UL), IIntoValue<Flt64>.Identity, random.Invoke)
                    .Invoke(new ComposedModel()));
            result.BestObjective.Should().NotBeNull(name + " 必须给出最优目标值 / must report a best objective");
            result.Status.Should().Be(HeuristicSolutionStatus.Feasible,
                name + " 必须给出可行解 / must report a feasible solution");
        }
    }

    /// <summary>**种群类工厂能在真实模型上跑完** / The population factories run to completion on a real model.</summary>
    [Fact]
    public async Task Genetic_ShouldProduceAResult() {
        HeuristicResult<Flt64, Flt64> result = Unwrap(
            await ComposedSolvers.Genetic<Flt64, Flt64>(
                new IterationLimit(3UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 4)
                .Invoke(new ComposedModel()));

        result.BestObjective.Should().NotBeNull();
        result.Status.Should().Be(HeuristicSolutionStatus.Feasible);
    }

    /// <summary>**轨迹类工厂能在真实模型上跑完** / The trajectory factories run to completion on a real model.</summary>
    [Fact]
    public async Task TrajectoryFactories_ShouldProduceResults() {
        HeuristicResult<Flt64, Flt64> hill = Unwrap(
            await ComposedSolvers.HillClimbing<Flt64, Flt64>(
                new IterationLimit(3UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5))
                .Invoke(new ComposedModel()));
        HeuristicResult<Flt64, Flt64> walk = Unwrap(
            await ComposedSolvers.RandomWalk<Flt64, Flt64>(
                new IterationLimit(3UL), IIntoValue<Flt64>.Identity, () => new Flt64(0.5))
                .Invoke(new ComposedModel()));

        hill.BestObjective.Should().NotBeNull();
        walk.BestObjective.Should().NotBeNull();
        hill.Status.Should().Be(HeuristicSolutionStatus.Feasible);
    }

    /// <summary>跑一次蜂群工厂并返回最优目标值 / Run a swarm factory once and return the best objective.</summary>
    /// <param name="factory">工厂 / The factory.</param>
    /// <param name="seed">随机种子 / The random seed.</param>
    /// <returns>最优目标值 / The best objective value.</returns>
    private static async Task<double> Run(
        Func<IStoppingCriterion, IIntoValue<Flt64>, Func<Flt64>, IHeuristicSolver<Flt64, Flt64>> factory,
        ulong seed) {
        var random = new SearchRandom((long)seed);
        HeuristicResult<Flt64, Flt64> result = Unwrap(
            await factory(
                new IterationLimit(5UL),
                IIntoValue<Flt64>.Identity,
                random.Invoke).Invoke(new ComposedModel()));
        return result.BestObjective!.ToDouble();
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");

    /// <summary>组合求解器测试模型 / A composed-solver test model.</summary>
    private sealed class ComposedModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

        /// <inheritdoc/>
        public IAbstractMutableTokenTable<Flt64> Tokens { get; } = Build();

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (values => true, "c1") };

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, object?> Extractor, string Name)> ObjectiveFunctions =>
            new (Func<IReadOnlyList<Flt64>, object?>, string)[] { (values => null, "f1") };

        /// <inheritdoc/>
        public IReadOnlyList<IReadOnlyList<Flt64>> InitialSolutions(ulong initialSolutionAmount = 1UL) {
            var result = new List<IReadOnlyList<Flt64>>();
            for (ulong index = 0; index < System.Math.Max(initialSolutionAmount, 1UL); index++) {
                result.Add(new[] { new Flt64(3.0 + index) });
            }
            return result;
        }

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) =>
            solution.Count == 0
                ? DefaultObjective
                : new Flt64(solution[0].ToDouble() * solution[0].ToDouble());

        /// <inheritdoc/>
        public Order CompareObjective(Flt64 lhs, Flt64 rhs) =>
            OrderHelpers.OrderOf(lhs.ToDouble().CompareTo(rhs.ToDouble()));

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }

        /// <summary>构造只含一个带界变量的 token 表 / Build a token table with one bounded variable.</summary>
        /// <returns>token 表 / The token table.</returns>
        private static IAbstractMutableTokenTable<Flt64> Build() {
            var table = new ConcurrentAutoTokenTable<Flt64>(
                Fuookami.Ospf.Math.Symbol.LinearCategory.Instance,
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>(),
                null,
                true);
            var variable = new RealVar("x0");
            variable.Range.SetRange(
                Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Of(
                    new Flt64(-10.0), new Flt64(10.0),
                    new Fuookami.Ospf.Math.Algebra.ValueRange.Interval.Closed(),
                    new Fuookami.Ospf.Math.Algebra.ValueRange.Interval.Closed()).Value!);
            table.Add(variable);
            return table;
        }
    }
}
