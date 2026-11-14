// NSGA-II 运行器测试 / Tests for the NSGA-II runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// NSGA-II 运行器 / The NSGA-II runner.
/// </summary>
public sealed class Nsga2RunnerTests {
    /// <summary>**无效目标向量在进入种群前被过滤** / Invalid objective vectors are filtered before entering the population.</summary>
    /// <remarks>
    /// 放进来会让支配比较在 `NaN` 上返回假，**于是 `NaN` 个体永远不会被支配、永远不会被淘汰，最终占据整个前沿而看起来"多样"**。
    /// Letting them in makes dominance comparisons false on `NaN`, **so the `NaN` individuals are never dominated, never culled, and end up
    /// occupying the whole front while looking "diverse"**.
    /// </remarks>
    [Fact]
    public async Task Initialize_ShouldFilterInvalidObjectiveVectors() {
        var model = new ParetoModel(new[] {
            new[] { new Flt64(double.NaN), new Flt64(1.0) },
            new[] { new Flt64(1.0), new Flt64(2.0) },
        });
        Nsga2Runner<Flt64> runner = BuildRunner(size: 4);

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> state =
            await runner.InitializeStep(model, new Iteration());

        state.Should().HaveCount(1, "含 NaN 的个体必须在入口就被丢弃 / the NaN individual must be dropped at the gate");
        state[0].Objective[0].ToDouble().Should().Be(1.0);
    }

    /// <summary>**空目标向量同样被判为失败** / An empty objective vector counts as a failure too.</summary>
    [Fact]
    public async Task Initialize_ShouldFilterEmptyObjectiveVectors() {
        var model = new ParetoModel(new[] {
            new[] { new Flt64(-1.0), new Flt64(5.0) },
            new[] { new Flt64(1.0), new Flt64(2.0) },
        });
        // 第一个分量小于 0 时目标向量为空，该个体必须在入口被丢弃。
        // The objective vector is empty when the first component is negative, so that individual must be dropped at the gate.
        Nsga2Runner<Flt64> runner = BuildRunner(size: 4);

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> state =
            await runner.InitializeStep(model, new Iteration());

        state.Should().HaveCount(1, "空目标向量的个体必须被丢弃 / the empty-objective individual must be dropped");
        state[0].Objective[0].ToDouble().Should().Be(1.0);
    }

    /// <summary>**被支配的个体先被淘汰** / Dominated individuals are culled first.</summary>
    [Fact]
    public void SelectImported_ShouldDropDominatedFirst() {
        Nsga2Runner<Flt64> runner = BuildRunner(size: 2);

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> selected =
            runner.SelectImported(new[] {
                Individual(0.0, 1.0),
                Individual(1.0, 1.0),
                Individual(2.0, 2.0),
            });

        selected.Should().HaveCount(2);
        selected[0].Objective[0].ToDouble().Should().Be(0.0, "第一层只有一个个体 / the first front holds one individual");
        selected[1].Objective[0].ToDouble().Should().Be(1.0,
            "(1,1) 支配 (2,2)，因此第二个名额归它 / (1,1) dominates (2,2), so it takes the second slot");
    }

    /// <summary>**同一层内按拥挤距离逐个挑选** / Within one front the largest crowding distances are picked one at a time.</summary>
    /// <remarks>
    /// 四个互不支配的个体 (0,10)、(1,9)、(2,8)、(3,7) 同属第一层。两个端点的拥挤距离为无穷，
    /// 中间的有限，因此 `amount = 2` 必须选中两个端点。
    ///
    /// Four mutually non-dominated individuals (0,10), (1,9), (2,8), (3,7) share the first front. The two extremes have infinite
    /// crowding distance and the middle two finite ones, so `amount = 2` must pick the two extremes.
    /// </remarks>
    [Fact]
    public void SelectImported_ShouldKeepFrontExtremes() {
        Nsga2Runner<Flt64> runner = BuildRunner(size: 2);

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> selected =
            runner.SelectImported(new[] {
                Individual(0.0, 10.0),
                Individual(1.0, 9.0),
                Individual(2.0, 8.0),
                Individual(3.0, 7.0),
            });

        selected.Should().HaveCount(2);
        selected[0].Objective[0].ToDouble().Should().Be(0.0, "左端点必须保留 / the left extreme must be kept");
        selected[1].Objective[0].ToDouble().Should().Be(3.0, "右端点必须保留 / the right extreme must be kept");
    }

    /// <summary>**候选不超过目标数量时原样返回** / Candidates within the target amount are returned as-is.</summary>
    [Fact]
    public void SelectImported_ShouldReturnAllWhenWithinAmount() {
        Nsga2Runner<Flt64> runner = BuildRunner(size: 8);
        IIndividual<IReadOnlyList<Flt64>, Flt64>[] candidates =
            new[] { Individual(0.0, 1.0), Individual(1.0, 0.0) };

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> selected =
            runner.SelectImported(candidates);

        selected.Should().BeSameAs(candidates);
    }

    /// <summary>**一步推进产出不超过种群规模的个体** / One step produces no more individuals than the population size.</summary>
    [Fact]
    public async Task Advance_ShouldKeepPopulationSize() {
        var model = new ParetoModel(new[] {
            new[] { new Flt64(0.0), new Flt64(4.0) },
            new[] { new Flt64(4.0), new Flt64(0.0) },
            new[] { new Flt64(1.0), new Flt64(3.0) },
        });
        Nsga2Runner<Flt64> runner = BuildRunner(size: 3);
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> state =
            await runner.InitializeStep(model, new Iteration());

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> advanced =
            await runner.AdvanceStep(model, new Iteration(), state);

        advanced.Should().HaveCount(3);
    }

    /// <summary>**空种群原样返回** / An empty population is returned unchanged.</summary>
    [Fact]
    public async Task Advance_ShouldReturnEmptyPopulationUnchanged() {
        Nsga2Runner<Flt64> runner = BuildRunner(size: 4);
        var state = new List<IIndividual<IReadOnlyList<Flt64>, Flt64>>();

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> advanced =
            await runner.AdvanceStep(new ParetoModel(Array.Empty<Flt64[]>()), new Iteration(), state);

        advanced.Should().BeSameAs(state);
    }

    /// <summary>**传了约束处理就要真的去问违约度** / With constraint handling the violation callback really runs.</summary>
    /// <remarks>
    /// **只看结果个数证明不了接线**：接线前后种群规模都是 `Size`，看起来一样"多样"。唯一能分辨的是违约度回调有没有被调用，
    /// 所以判据的锚点是一个计数回调。
    ///
    /// **Counting results cannot prove the wiring**: the population is `Size` before and after and looks equally "diverse". The only distinguishing signal is whether
    /// the violation callback runs, so the criterion anchors on a counting callback.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldConsultTheConstraintHandling() {
        int calls = 0;
        ConstrainedSearch<IReadOnlyList<Flt64>, Flt64> handling = ConstrainedSearch(() => calls = calls + 1);
        Nsga2Runner<Flt64> runner = BuildRunner(size: 3, constraints: handling);
        var model = new ParetoModel(new[] {
            new[] { new Flt64(0.0), new Flt64(4.0) },
            new[] { new Flt64(4.0), new Flt64(0.0) },
            new[] { new Flt64(1.0), new Flt64(3.0) },
        });
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> state =
            await runner.InitializeStep(model, new Iteration());

        await runner.AdvanceStep(model, new Iteration(), state);

        runner.Constraints.Should().NotBeNull("the runner must keep what it was given");
        calls.Should().BeGreaterThan(0, "the handler was passed in, so the violation callback must have run");
    }

    /// <summary>**导入入口也要问违约度** / The import entry asks for a violation too.</summary>
    /// <remarks>
    /// 只接 `Advance` 会有个隐蔽的后果：**外部导入的种群按拥挤距离筛，而内部每一代按可行性筛**——两处规则不同，
    /// 却都不报错，症状只会表现为"导入之后收敛变差"。所以两个入口各有一条判据，**不靠一条覆盖两处**。
    ///
    /// Wiring only `Advance` has a quiet consequence: **imported populations are filtered by crowding distance while each internal generation is filtered by
    /// feasibility** — two different rules that never complain, looking exactly like "convergence got worse after importing". Each entry gets its own criterion;
    /// **one does not cover both**.
    /// </remarks>
    [Fact]
    public void SelectImported_ShouldConsultTheConstraintHandling() {
        int calls = 0;
        ConstrainedSearch<IReadOnlyList<Flt64>, Flt64> handling = ConstrainedSearch(() => calls = calls + 1);
        Nsga2Runner<Flt64> runner = BuildRunner(size: 2, constraints: handling);
        List<IIndividual<IReadOnlyList<Flt64>, Flt64>> candidates = new() {
            Individual(0.0, 4.0), Individual(4.0, 0.0), Individual(1.0, 3.0),
        };

        runner.SelectImported(candidates);

        calls.Should().BeGreaterThan(0, "the import entry must apply the same feasibility rule as the advance entry");
    }

    /// <summary>**可行性必须真的改变选择，而不只是被读一次** / Feasibility must really change the selection, not just be read once.</summary>
    /// <remarks>
    /// 种群造得让**更好的目标恰好来自不可行个体**：`A(10,10)` 可行但目标最差，`B(0,0)`、`C(1,1)` 不可行且目标更好。
    /// 纯拥挤距离 / 非支配选择会挑 `{B, C}`（它们支配 `A`），可行性优先会先保 `A` 再取 `{B}`。
    ///
    /// **只钉"回调被调用"是不够的**：读一次违约度然后照样按目标挑，同样能通过那条判据。
    /// 这里钉的是**结果集合不同**，两条路各跑一遍、各自断言。
    ///
    /// The population is built so that **the better objectives belong to the infeasible individuals**: `A(10,10)` is feasible with the worst objectives, while
    /// `B(0,0)` and `C(1,1)` are infeasible with better ones. Plain non-dominated/crowding selection picks `{B, C}` (they dominate `A`); feasibility-first keeps `A`
    /// first and then takes `{B}` from the infeasible group. **Pinning "the callback ran" is not enough** — reading the violation once and then selecting by
    /// objective anyway would pass that criterion. This pins **a different result set**, running both paths and asserting on each.
    /// </remarks>
    [Fact]
    public void Feasibility_ShouldChangeWhichIndividualsSurvive() {
        List<IIndividual<IReadOnlyList<Flt64>, Flt64>> candidates = new() {
            Individual(10.0, 10.0), Individual(0.0, 0.0), Individual(1.0, 1.0),
        };

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> byObjective =
            BuildRunner(size: 2).SelectImported(candidates);
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> byFeasibility =
            BuildRunner(size: 2, constraints: FeasibilityFirstHandling()).SelectImported(candidates);

        System.Linq.Enumerable.Select(byObjective, individual => individual.Objective[0].ToDouble())
            .Should().NotContain(10.0, "plain selection keeps the dominating infeasible pair");
        System.Linq.Enumerable.Select(byFeasibility, individual => individual.Objective[0].ToDouble())
            .Should().Contain(10.0, "feasibility-first keeps the feasible individual even though it is dominated");
        byFeasibility.Should().NotBeEquivalentTo(byObjective,
            "if these two agree, reading the violation changed nothing");
    }

    /// <summary>构造"小解值即不可行"的可行性优先处理 / Build feasibility-first handling where small solution values are infeasible.</summary>
    /// <returns>约束处理 / The constraint handling.</returns>
    private static ConstrainedSearch<IReadOnlyList<Flt64>, Flt64> FeasibilityFirstHandling() {
        // **模型必须真的判不可行**：`ConstraintSatisfied` 恒为真时所有人都落在可行层，分组只剩一个桶。
        // **The model has to really report infeasibility**: with `ConstraintSatisfied` always true everyone lands in the feasible tier and grouping collapses.
        var model = new ParetoModel(
            new[] { new[] { new Flt64(0.0), new Flt64(0.0) } },
            feasible: values => values[0].ToDouble() >= 5.0);
        var problem = new SearchProblem<IReadOnlyList<Flt64>, Flt64>(
            "feasibility", "fp", model, SearchCodecs.Serialized<IReadOnlyList<Flt64>>(), SearchCodecs.Flt64,
            (lhs, rhs) => lhs[0].ToDouble() < rhs[0].ToDouble(),
            multiObjective: true,
            validSolution: _ => true,
            admissible: _ => true,
            constraintViolation: values =>
                Fuookami.Ospf.Utils.Functional.Results.Ok<Flt64>(new Flt64(values[0].ToDouble() < 5.0 ? 1.0 : 0.0)));
        Result<ConstrainedSearch<IReadOnlyList<Flt64>, Flt64>, ErrorCode, Error<ErrorCode>> attached =
            problem.WithConstraints(new ConstraintHandlingConfig(Mode: ConstraintMode.FeasibilityFirst), new SearchRandom(1));
        attached.IsOk.Should().BeTrue(attached.ToString());
        return attached.Value;
    }

    /// <summary>构造带计数违约度的约束处理 / Build constraint handling with a counted violation.</summary>
    /// <param name="onViolation">每次求违约度时调用 / Called on each violation request.</param>
    /// <returns>约束处理 / The constraint handling.</returns>
    private static ConstrainedSearch<IReadOnlyList<Flt64>, Flt64> ConstrainedSearch(Action onViolation) {
        var model = new ParetoModel(new[] {
            new[] { new Flt64(0.0), new Flt64(4.0) },
            new[] { new Flt64(4.0), new Flt64(0.0) },
        });
        var problem = new SearchProblem<IReadOnlyList<Flt64>, Flt64>(
            "wiring", "fp", model, SearchCodecs.Serialized<IReadOnlyList<Flt64>>(), SearchCodecs.Flt64,
            (lhs, rhs) => lhs[0].ToDouble() < rhs[0].ToDouble(),
            multiObjective: true,
            validSolution: _ => true,
            admissible: _ => true,
            constraintViolation: values => {
                onViolation();
                // 一半个体不可行，分组选择才有事可做 / Half the individuals are infeasible so grouped selection has work to do.
                return Fuookami.Ospf.Utils.Functional.Results.Ok<Flt64>(new Flt64(values[0].ToDouble() > 2.0 ? 1.0 : 0.0));
            });
        Result<ConstrainedSearch<IReadOnlyList<Flt64>, Flt64>, ErrorCode, Error<ErrorCode>> attached =
            problem.WithConstraints(new ConstraintHandlingConfig(), new SearchRandom(1));
        attached.IsOk.Should().BeTrue(attached.ToString());
        return attached.Value;
    }

    /// <summary>**成员取当前种群** / Members are the current population.</summary>
    [Fact]
    public void MembersOf_ShouldReturnPopulation() {
        Nsga2Runner<Flt64> runner = BuildRunner(size: 2);
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> state =
            new[] { Individual(1.0, 2.0) };

        runner.StateMembers(state).Should().BeSameAs(state);
    }

    /// <summary>**非法构造参数被拒绝** / Invalid constructor arguments are rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectInvalidConfiguration() {
        Action zeroSize = () => BuildRunner(size: 0);
        Action zeroArchive = () => BuildRunner(size: 2, archiveAmount: 0);

        zeroSize.Should().Throw<ArgumentOutOfRangeException>();
        zeroArchive.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="first">第一个目标 / The first objective.</param>
    /// <param name="second">第二个目标 / The second objective.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<IReadOnlyList<Flt64>, Flt64> Individual(
        double first, double second) =>
        new SolutionWithFitness<IReadOnlyList<Flt64>, Flt64>(
            new[] { new Flt64(first), new Flt64(second) },
            new[] { new Flt64(first), new Flt64(second) },
            0.0);

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <returns>运行器 / The runner.</returns>
    private static Nsga2Runner<Flt64> BuildRunner(
        int size,
        int archiveAmount = 100,
        ConstrainedSearch<IReadOnlyList<Flt64>, Flt64>? constraints = null) =>
        new(solution => solution.Values[0].ToDouble() < 0.0
                ? Array.Empty<Flt64>()
                : new[] { solution.Values[0], solution.Values[1] },
            new MinimizationDominance(2),
            new PassThroughUpdate(),
            new IdentityMutation(),
            () => new Flt64(0.5),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size,
            archiveAmount: archiveAmount,
            constraints: constraints);

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

    /// <summary>双目标测试模型 / A two-objective test model.</summary>
    private sealed class ParetoModel : IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, Flt64> {
        private readonly IReadOnlyList<IReadOnlyList<Flt64>> _initial;

        private readonly Func<IReadOnlyList<Flt64>, bool?>? _feasible;

        public ParetoModel(IReadOnlyList<IReadOnlyList<Flt64>> initial, Func<IReadOnlyList<Flt64>, bool?>? feasible = null) {
            _initial = initial;
            _feasible = feasible;
        }

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> DefaultObjective => Array.Empty<Flt64>();

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
        public IReadOnlyList<IReadOnlyList<Flt64>> InitialSolutions(
            ulong initialSolutionAmount = 1) => _initial;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> Operation(
            IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs) => lhs;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> Objective(IReadOnlyList<Flt64> solution) =>
            solution.Count < 2 || solution[0].ToDouble() < 0.0
                ? Array.Empty<Flt64>()
                : new[] { solution[0], solution[1] };

        /// <inheritdoc/>
        public Order? CompareObjective(IReadOnlyList<Flt64>? lhs, IReadOnlyList<Flt64>? rhs) =>
            OrderHelpers.OrderOf((lhs?.Count ?? 0).CompareTo(rhs?.Count ?? 0));

        /// <inheritdoc/>
        // **可行性由模型说了算，违约度回调只给"同层内的度量"**（B812 实测）：`ConstraintSatisfied` 恒为真时，
        // 所有个体都落在可行层，`groups` 只剩一个桶，违约度再大也分不开——**接线看起来生效了，实际没改变任何选择**。
        //
        // **Feasibility is decided by the model; the violation callback only supplies a within-tier measure** (measured by B812): with `ConstraintSatisfied`
        // always true every individual lands in the feasible tier, `Groups` collapses to one bucket, and no violation is large enough to split it — **the wiring
        // looks active while changing nothing**.
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => _feasible is null ? true : _feasible(solution);

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
