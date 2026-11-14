// 运行器求解器测试 / Tests for the runner solver.

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
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 运行器求解器 / The runner solver.
/// </summary>
public sealed class RunnerSolverTests {
    /// <summary>**搜不到任何个体时失败** / Producing no individual fails.</summary>
    /// <remarks>
    /// 返回一个"最优解为空"的成功结果，会让调用方拿到成功的返回值，
    /// **而"算法什么都没找到"这件事被包装成一个正常结果**，上一层再也无从区分"收敛"与"根本没跑"。
    /// Returning a successful result whose best solution is empty hands the caller a success value, **so "the algorithm found nothing" is
    /// dressed up as a normal result** and no layer above can tell "converged" from "never ran".
    /// </remarks>
    [Fact]
    public async Task Invoke_ShouldFailOnEmptyResult() {
        var solver = new RunnerSolver<Flt64, Flt64>(
            "empty", new StubRunner(Array.Empty<IIndividual<Flt64, Flt64>>()));

        Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.Invoke(new SolverModel(null));

        result.Should().BeOfType<Failed<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**只有"明确不可行"才标成不可行，"未知"算作可行** / Only an explicit "infeasible" is infeasible; "unknown" counts as feasible.</summary>
    /// <remarks>
    /// 把"未知"当成不可行会让所有没声明约束的模型一上来就被标成不可行，
    /// **而目标值与迭代数看起来完全正常**。
    /// Treating "unknown" as infeasible marks every model that declares no constraints as infeasible from the start, **while its objective and
    /// iteration count look perfectly normal**.
    /// </remarks>
    [Fact]
    public async Task Invoke_ShouldTreatUnknownFeasibilityAsFeasible() {
        var candidate = new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(1.0) }, new Flt64(1.0), 0.0);
        var solver = new RunnerSolver<Flt64, Flt64>(
            "unknown", new StubRunner(new[] { candidate }));

        HeuristicResult<Flt64, Flt64> unknown = Unwrap(
            await solver.Invoke(new SolverModel(null)));
        HeuristicResult<Flt64, Flt64> feasible = Unwrap(
            await solver.Invoke(new SolverModel(true)));
        HeuristicResult<Flt64, Flt64> infeasible = Unwrap(
            await solver.Invoke(new SolverModel(false)));

        unknown.Status.Should().Be(HeuristicSolutionStatus.Feasible, "未知算作可行 / unknown counts as feasible");
        feasible.Status.Should().Be(HeuristicSolutionStatus.Feasible);
        infeasible.Status.Should().Be(HeuristicSolutionStatus.Infeasible);
    }

    /// <summary>**结果取第一个个体并带回迭代状态** / The result takes the first individual and carries the iteration state.</summary>
    [Fact]
    public async Task Invoke_ShouldReturnBestIndividual() {
        var first = new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(2.0) }, new Flt64(2.0), 0.0);
        var second = new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(9.0) }, new Flt64(9.0), 0.0);
        var solver = new RunnerSolver<Flt64, Flt64>(
            "best", new StubRunner(new[] { first, second }));
        var iteration = new Iteration();

        HeuristicResult<Flt64, Flt64> result = Unwrap(
            await solver.Invoke(new SolverModel(null), iteration));

        result.BestObjective!.ToDouble().Should().Be(2.0);
        result.BestSolution![0].ToDouble().Should().Be(2.0);
        result.Iteration.Should().BeSameAs(iteration, "迭代状态原样带回 / the iteration state is carried through");
        solver.Name.Should().Be("best");
    }

    /// <summary>**运行器的失败原样向外传播** / A runner failure propagates unchanged.</summary>
    [Fact]
    public async Task Invoke_ShouldPropagateRunnerFailure() {
        var solver = new RunnerSolver<Flt64, Flt64>("broken", new FailingRunner());

        Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.Invoke(new SolverModel(null));

        result.Should().BeOfType<Failed<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");

    /// <summary>返回固定成员的运行器 / A runner that returns fixed members.</summary>
    private sealed class StubRunner : IHeuristicRunner<Flt64, Flt64> {
        private readonly IReadOnlyList<IIndividual<Flt64, Flt64>> _members;

        public StubRunner(IReadOnlyList<IIndividual<Flt64, Flt64>> members) => _members = members;

        /// <inheritdoc/>
        public Task<Result<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> Run(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model,
            Iteration? iteration = null,
            Func<Iteration, IReadOnlyList<IIndividual<Flt64, Flt64>>,
                Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null) =>
            Task.FromResult(
                Results.Ok<IReadOnlyList<IIndividual<Flt64, Flt64>>>(_members));
    }

    /// <summary>总是失败的运行器 / A runner that always fails.</summary>
    private sealed class FailingRunner : IHeuristicRunner<Flt64, Flt64> {
        /// <inheritdoc/>
        public Task<Result<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>> Run(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model,
            Iteration? iteration = null,
            Func<Iteration, IReadOnlyList<IIndividual<Flt64, Flt64>>,
                Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null) =>
            Task.FromResult(Results.Failed<IReadOnlyList<IIndividual<Flt64, Flt64>>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError, "runner broke")));
    }

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

    /// <summary>求解器测试模型 / A solver test model.</summary>
    private sealed class SolverModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        private readonly bool? _satisfied;

        public SolverModel(bool? satisfied) => _satisfied = satisfied;

        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

        /// <inheritdoc/>
        public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<Flt64> Tokens { get; } =
            new Fuookami.Ospf.Core.Token.ConcurrentAutoTokenTable<Flt64>(
                Fuookami.Ospf.Math.Symbol.LinearCategory.Instance,
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (values => _satisfied, "c1") };

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
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => _satisfied;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
