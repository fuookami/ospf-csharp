// 多目标求解器测试 / Tests for the multi-objective solvers.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Heuristic.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 多目标求解器 / The multi-objective solvers.
/// </summary>
public sealed class MultiObjectiveSolversTests {
    /// <summary>**名称从 runner 取，不从包装类型取** / The name comes from the runner, not the wrapper type.</summary>
    /// <remarks>
    /// II 与 III 共用同一个包装类型，区别只在 runner。若把名称写死成 `nsga-ii`，**用 NSGA-III 跑出来的东西会自称 NSGA-II，
    /// 而结果本身完全正常**——这是最难被发现的一类错误：**结果对、标签错**。
    /// II and III share one wrapper and differ only in the runner. Hard-coding the name makes **an NSGA-III run call itself NSGA-II while its
    /// results are perfectly normal** — the hardest kind of error to spot: **right results, wrong label**.
    /// </remarks>
    [Fact]
    public void Name_ShouldComeFromTheRunner() {
        IMultiObjectiveSolver<Flt64> second = MultiObjectiveSolvers.Nsga2(
            Objectives, new MinimizationDominance(2), new IterationLimit(1UL),
            IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 4);
        IMultiObjectiveSolver<Flt64> third = MultiObjectiveSolvers.Nsga3(
            Objectives, new MinimizationDominance(2), new IterationLimit(1UL),
            IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 4);

        second.Name.Should().Be("nsga-ii");
        third.Name.Should().Be("nsga-iii",
            "NSGA-III 必须自称 nsga-iii / NSGA-III must call itself nsga-iii");
    }

    /// <summary>**NSGA-III 工厂装的是 NSGA-III 运行器** / The NSGA-III factory wires an NSGA-III runner.</summary>
    [Fact]
    public void Nsga3_ShouldWrapNsga3Runner() {
        IMultiObjectiveSolver<Flt64> third = MultiObjectiveSolvers.Nsga3(
            Objectives, new MinimizationDominance(2), new IterationLimit(1UL),
            IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 4);

        ((Nsga2Solver<Flt64>)third).Runner.Should().BeOfType<Nsga3Runner<Flt64>>();
    }

    /// <summary>**空前沿以失败返回，而不是成功的空结果** / An empty front is a failure, not a successful empty result.</summary>
    /// <remarks>
    /// 把"一个个体都没产出"包装成成功，会让调用方以为**算法收敛到了一个空集**，
    /// 而真正的原因（模型没有给出任何合法解）再也不会出现在任何地方。
    /// Wrapping "not a single individual was produced" as a success makes the caller believe **the algorithm converged to the empty set**, and
    /// the real cause never surfaces again.
    /// </remarks>
    [Fact]
    public async Task Invoke_ShouldFailOnEmptyFront() {
        IMultiObjectiveSolver<Flt64> solver = MultiObjectiveSolvers.Nsga2(
            Objectives, new MinimizationDominance(2), new IterationLimit(1UL),
            IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 4);

        Result<MultiObjectiveResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.Invoke(new SolverModel(Array.Empty<Flt64[]>()));

        result.Should().BeOfType<Failed<MultiObjectiveResult<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**前沿与目标向量一一对应** / The front and the objective vectors correspond one to one.</summary>
    [Fact]
    public async Task Invoke_ShouldReturnMatchingFront() {
        IMultiObjectiveSolver<Flt64> solver = MultiObjectiveSolvers.Nsga2(
            Objectives, new MinimizationDominance(2), new IterationLimit(2UL),
            IIntoValue<Flt64>.Identity, () => new Flt64(0.5), size: 3);
        var model = new SolverModel(new[] {
            new[] { new Flt64(0.0), new Flt64(4.0) },
            new[] { new Flt64(4.0), new Flt64(0.0) },
            new[] { new Flt64(2.0), new Flt64(2.0) },
        });

        MultiObjectiveResult<Flt64> result = Unwrap(await solver.Invoke(model));

        result.Front.Should().HaveCount(result.Objectives.Count);
        result.Front.Should().NotBeEmpty();
        result.Front.Count.Should().BeLessThanOrEqualTo(3, "前沿不会超过种群规模 / the front never exceeds the population size");
        result.Iteration.Should().NotBeNull();
    }

    /// <summary>目标向量提取器 / The objective-vector extractor.</summary>
    /// <param name="solution">解 / The solution.</param>
    /// <returns>目标向量 / The objective vector.</returns>
    private static IReadOnlyList<Flt64> Objectives(Solution<Flt64> solution) =>
        new[] { solution.Values[0], solution.Values[1] };

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result: " + ((Failed<T, ErrorCode, Error<ErrorCode>>)(object)result).Error.Message);

    /// <summary>双目标测试模型 / A two-objective test model.</summary>
    private sealed class SolverModel : IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, Flt64> {
        private readonly IReadOnlyList<IReadOnlyList<Flt64>> _initial;

        public SolverModel(IReadOnlyList<IReadOnlyList<Flt64>> initial) {
            _initial = initial;
            var table = new ConcurrentAutoTokenTable<Flt64>(
                Fuookami.Ospf.Math.Symbol.LinearCategory.Instance,
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>(),
                null,
                true);
            for (int index = 0; index < 2; index++) {
                table.Add(new RealVar($"x{index}"));
            }
            Tokens = table;
        }

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> DefaultObjective => Array.Empty<Flt64>();

        /// <inheritdoc/>
        public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

        /// <inheritdoc/>
        /// <remarks>
        /// **必须真的注册两个 token**。`AbstractRunner.Run` 内部构造的问题边界没有显式给出维度判据，
        /// 于是回退到"解长度必须等于 solver 里 token 数"——**测试模型若一个 token 都不注册，所有候选都会被判为无效**，
        /// 整轮运行产出为空。**这条陷阱此前在 `Nsga2RunnerTests` 里没有暴露，因为那些测试直接调 `InitializeStep`/`AdvanceStep`，
        /// 绕过了会话。**
        /// **Two tokens must actually be registered.** `AbstractRunner.Run` builds its internal problem without an explicit dimension
        /// predicate, so it falls back to "the solution length must equal the token count" — **a test model that registers no token has every
        /// candidate rejected** and the whole run produces nothing. **This trap stayed hidden in `Nsga2RunnerTests` because those tests call
        /// `InitializeStep`/`AdvanceStep` directly and bypass the session.**
        /// </remarks>
        public IAbstractMutableTokenTable<Flt64> Tokens { get; }

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
        public IReadOnlyList<Flt64> Operation(IReadOnlyList<Flt64> lhs, IReadOnlyList<Flt64> rhs) => lhs;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public IReadOnlyList<Flt64> Objective(IReadOnlyList<Flt64> solution) =>
            solution.Count < 2 ? Array.Empty<Flt64>() : new[] { solution[0], solution[1] };

        /// <inheritdoc/>
        public Order? CompareObjective(IReadOnlyList<Flt64>? lhs, IReadOnlyList<Flt64>? rhs) =>
            OrderHelpers.OrderOf((lhs?.Count ?? 0).CompareTo(rhs?.Count ?? 0));

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
