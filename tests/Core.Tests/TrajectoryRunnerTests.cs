// 运行器基类测试 / Tests for the runner base class and the trajectory runner.

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
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 轨迹运行器 / The trajectory runner.
/// </summary>
public sealed class TrajectoryRunnerTests {
    /// <summary>**初始化产出单个成员** / Initialization yields exactly one member.</summary>
    [Fact]
    public async Task Initialize_ShouldYieldSingleMember() {
        StubModelCore<object?> model = BuildModel();
        TrajectoryRunner<Flt64, Flt64> runner = BuildRunner(model, new FixedNeighborhood());

        IIndividual<Flt64, Flt64>? state = await runner.InitializeStep(model, new Iteration());

        state.Should().NotBeNull();
        runner.StateMembers(state!).Count.Should().Be(1);
    }

    /// <summary>**邻域失败时保留当前个体** / A failed neighbourhood keeps the current individual.</summary>
    /// <remarks>
    /// 置空会让档案在下一步变成空集合并触发 `NoCandidate`，**而搜索会"成功结束"在一个明显还没收敛的位置**。
    /// Clearing makes the archive empty on the next step and triggers `NoCandidate`, **so the search "terminates successfully" at a
    /// point that is clearly not converged**.
    ///
    /// **这条测试必须用引用类型的目标值**。`ObjValue?` 在无 `struct` 约束的泛型里只是可空注解：当 `ObjValue` 是结构体（如
    /// `Flt64`）时，`Evaluate` 返回的 `default` 是 `0.0` 而**不是** `null`，`if (candidate is null)` 永远不成立。
    /// 第一版用 `Flt64` 作目标值，变异测试（把分支改成 `return null`）报告"已通过"——**fixture 从未进入该分支**。
    /// **This test must use a reference objective value.** In an unconstrained generic, `ObjValue?` is only a nullable annotation: when
    /// `ObjValue` is a struct (such as `Flt64`), the `default` returned by `Evaluate` is `0.0`, **not** `null`, so
    /// `if (candidate is null)` never holds. The first version used `Flt64` as the objective value, and the mutation test (rewriting the
    /// branch to `return null`) reported a pass — **the fixture never entered the branch**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldKeepStateWhenNeighbourhoodFails() {
        var model = new ReferenceObjectiveModel();
        var runner = new TrajectoryRunner<int[], Flt64>(
            new FailingNeighborhood(),
            () => new Flt64(0.5),
            new BestArchive<int[], Flt64>(10),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            initializer: new FixedInitializer(),
            fitnessEvaluator: new ForwardFitnessEvaluator());

        IIndividual<int[], Flt64>? state = await runner.InitializeStep(model, new Iteration());
        IIndividual<int[], Flt64>? advanced =
            await runner.AdvanceStep(model, new Iteration(), state);

        advanced.Should().NotBeNull("邻域失败不应让状态消失 / a failed neighbourhood must not clear the state");
        runner.StateMembers(advanced!).Count.Should().Be(1);
    }

    /// <summary>**更好的候选被接受** / A better candidate is accepted.</summary>
    [Fact]
    public async Task Advance_ShouldAcceptImprovement() {
        StubModelCore<object?> model = BuildModel();
        TrajectoryRunner<Flt64, Flt64> runner = BuildRunner(model, new ImprovingNeighborhood());

        IIndividual<Flt64, Flt64>? state = await runner.InitializeStep(model, new Iteration());
        Flt64 before = state!.Objective;
        IIndividual<Flt64, Flt64>? advanced =
            await runner.AdvanceStep(model, new Iteration(), state);

        advanced!.Objective.ToDouble().Should().BeLessThan(before.ToDouble());
    }

    /// <summary>**更差的候选被拒绝** / A worse candidate is rejected.</summary>
    [Fact]
    public async Task Advance_ShouldRejectWorsening() {
        StubModelCore<object?> model = BuildModel();
        TrajectoryRunner<Flt64, Flt64> runner = BuildRunner(model, new WorseningNeighborhood());

        IIndividual<Flt64, Flt64>? state = await runner.InitializeStep(model, new Iteration());
        Flt64 before = state!.Objective;
        IIndividual<Flt64, Flt64>? advanced =
            await runner.AdvanceStep(model, new Iteration(), state);

        advanced!.Objective.ToDouble().Should().Be(before.ToDouble(),
            "严格改进才接受 / only strict improvement is accepted");
    }

    /// <summary>**空状态产出空成员列表** / A null state yields an empty member list.</summary>
    [Fact]
    public void StateMembers_ShouldReturnEmptyForNull() {
        StubModelCore<object?> model = BuildModel();
        TrajectoryRunner<Flt64, Flt64> runner = BuildRunner(model, new FixedNeighborhood());

        runner.StateMembers(null).Should().BeEmpty();
    }

    /// <summary>**观察者按顺序被重置** / Observers are reset in order.</summary>
    [Fact]
    public void Reset_ShouldReachEveryObserver() {
        CountingObserver first = new();
        CountingObserver second = new();
        StubModelCore<object?> model = BuildModel();
        TrajectoryRunner<Flt64, Flt64> runner = BuildRunner(model, new FixedNeighborhood(), new[] { first, second });

        foreach (IIterationObserverContract observer in runner.Observers) {
            observer.Reset();
        }

        first.Resets.Should().Be(1);
        second.Resets.Should().Be(1);
    }

    /// <summary>**观察者收到本步成员与改善标志** / Observers receive the members and the improvement flag.</summary>
    [Fact]
    public async Task Observe_ShouldReceiveMembersAndFlag() {
        CountingObserver observer = new();
        StubModelCore<object?> model = BuildModel();
        TrajectoryRunner<Flt64, Flt64> runner = BuildRunner(model, new FixedNeighborhood(), new[] { observer });

        IIndividual<Flt64, Flt64>? state = await runner.InitializeStep(model, new Iteration());
        foreach (IIterationObserverContract contract in runner.Observers) {
            contract.Observe(new Iteration(), runner.StateMembers(state!), true, runner.Converter);
        }

        observer.Observations.Should().Be(1);
        observer.LastImproved.Should().BeTrue();
        observer.LastMemberCount.Should().Be(1);
    }

    /// <summary>**默认初始化器与默认接受策略可用** / The default initializer and acceptance policy are usable.</summary>
    [Fact]
    public void Constructor_ShouldSupplyDefaults() {
        StubModelCore<object?> model = BuildModel();
        var runner = new TrajectoryRunner<Flt64, Flt64>(
            new FixedNeighborhood(),
            () => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(10),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity);

        runner.Neighborhood.Should().NotBeNull();
        runner.Observers.Should().BeEmpty();
        runner.Stopping.Should().BeSameAs(NeverStop.Instance);
    }

    /// <summary>**空引用参数被拒绝** / Null arguments are rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectNulls() {
        StubModelCore<object?> model = BuildModel();
        Action act = () => new TrajectoryRunner<Flt64, Flt64>(
            null!, () => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(10),
            NeverStop.Instance, IIntoValue<Flt64>.Identity);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="neighborhood">邻域算子 / The neighbourhood.</param>
    /// <param name="observers">观察者 / The observers.</param>
    /// <returns>运行器 / The runner.</returns>
    private static TrajectoryRunner<Flt64, Flt64> BuildRunner(
        StubModelCore<object?> model,
        INeighborhood neighborhood,
        IReadOnlyList<IIterationObserverContract>? observers = null,
        IFitnessEvaluator? fitnessEvaluator = null) =>
        new(neighborhood, () => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(10),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            initializer: new FixedInitializer(),
            acceptance: null,
            fitnessEvaluator: fitnessEvaluator,
            observers: observers);

    /// <summary>固定初始解 / A fixed initial solution.</summary>
    private sealed class FixedInitializer : IInitializer {
        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
            Iteration iteration,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            ulong amount,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> {
            var result = new List<Solution<V>> {
                new(new[] { converter.IntoValue(new Flt64(1.0)) }),
            };
            return result;
        }
    }

    /// <summary>固定邻域 / A fixed neighbourhood.</summary>
    private sealed class FixedNeighborhood : INeighborhood {
        /// <inheritdoc/>
        public Solution<V> Generate<TObj, TObjValue, V>(
            Iteration iteration,
            IIndividual<TObjValue, V> individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            Func<Flt64> entropy,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            new(new[] { converter.IntoValue(new Flt64(1.0)) });
    }

    /// <summary>
    /// 原样转发模型目标值的评估器 / A fitness evaluator that forwards the model objective without a fallback.
    /// </summary>
    private sealed class ForwardFitnessEvaluator : IFitnessEvaluator {
        /// <inheritdoc/>
        public TObjValue? Evaluate<TObj, TObjValue, V>(
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            Solution<V> solution)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            model.Objective(solution.Values);
    }

    /// <summary>
    /// 引用类型目标值的测试模型 / A test model with a reference objective value.
    /// </summary>
    private sealed class ReferenceObjectiveModel
        : IAbstractCallBackModelInterface<object?, int[], Flt64> {
        /// <inheritdoc/>
        public int[] DefaultObjective => Array.Empty<int>();

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
        public int[] Operation(int[] lhs, int[] rhs) => lhs;

        /// <inheritdoc/>
        public int[] ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public int[] ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public int[]? Objective(IReadOnlyList<Flt64> solution) =>
            solution.Count == 0 ? null : new[] { (int)solution[0].ToDouble() };

        /// <inheritdoc/>
        public Order? CompareObjective(int[]? lhs, int[]? rhs) {
            if (lhs is null) {
                return rhs is null ? new Order.Equal() : new Order.Greater(1);
            }
            if (rhs is null) {
                return new Order.Less(-1);
            }
            return OrderHelpers.OrderOf(lhs[0].CompareTo(rhs[0]));
        }

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }

    /// <summary>返回不可评估邻域的算子 / A neighbourhood whose result cannot be evaluated.</summary>
    private sealed class FailingNeighborhood : INeighborhood {
        /// <inheritdoc/>
        public Solution<V> Generate<TObj, TObjValue, V>(
            Iteration iteration,
            IIndividual<TObjValue, V> individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            Func<Flt64> entropy,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            new(Array.Empty<V>());
    }

    /// <summary>改进邻域 / An improving neighbourhood.</summary>
    private sealed class ImprovingNeighborhood : INeighborhood {
        /// <inheritdoc/>
        public Solution<V> Generate<TObj, TObjValue, V>(
            Iteration iteration,
            IIndividual<TObjValue, V> individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            Func<Flt64> entropy,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            new(new[] { converter.IntoValue(new Flt64(0.5)) });
    }

    /// <summary>变差邻域 / A worsening neighbourhood.</summary>
    private sealed class WorseningNeighborhood : INeighborhood {
        /// <inheritdoc/>
        public Solution<V> Generate<TObj, TObjValue, V>(
            Iteration iteration,
            IIndividual<TObjValue, V> individual,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            Func<Flt64> entropy,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            new(new[] { converter.IntoValue(new Flt64(5.0)) });
    }

    /// <summary>计数观察者 / A counting observer.</summary>
    private sealed class CountingObserver : IIterationObserverContract {
        public int Resets { get; private set; }

        public int Observations { get; private set; }

        public bool LastImproved { get; private set; }

        public int LastMemberCount { get; private set; }

        /// <inheritdoc/>
        public void Reset() => Resets++;

        /// <inheritdoc/>
        public void Observe<ObjValue, V>(
            Iteration iteration,
            IReadOnlyList<IIndividual<ObjValue, V>> members,
            bool improved,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> {
            Observations++;
            LastImproved = improved;
            LastMemberCount = members.Count;
        }
    }

    /// <summary>
    /// 构造测试模型 / Build the test model.
    /// </summary>
    /// <remarks>
    /// 空解返回一个极大目标值（结构体目标值无法表达"评估失败"，见上面"邻域失败"测试的说明）。
    /// An empty solution returns a very large objective (a struct objective cannot express "evaluation failed"; see the note on the
    /// "failed neighbourhood" test above).
    /// </remarks>
    /// <returns>模型 / The model.</returns>
    private static StubModelCore<object?> BuildModel() =>
        new(values => values.Count == 0 ? new Flt64(1e9) : values[0]);
}
