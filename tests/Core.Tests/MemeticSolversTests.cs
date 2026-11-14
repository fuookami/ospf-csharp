// 文化与模因求解器测试 / Tests for the memetic solver.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 文化与模因求解器 / The memetic solver.
/// </summary>
public sealed class MemeticSolversTests {
    /// <summary>**全局搜索失败原样向外传** / A global-search failure propagates unchanged.</summary>
    [Fact]
    public async Task Invoke_ShouldPropagateGlobalFailure() {
        IHeuristicSolver<Flt64, Flt64> polish = StubSolver.Ok(0.1, name: "polish");
        var marker = new PolishMarker(polish);
        var solver = new MemeticSolver<Flt64, Flt64>(
            "memetic", StubSolver.Fail("global broke"), marker.Build);

        Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.Invoke(new SolverModel());

        result.Should().BeOfType<Failed<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        marker.Invocations.Should().Be(0, "全局失败时不应该去打磨 / the polish must not run when the global search failed");
    }

    /// <summary>**打磨失败被吞掉，退回全局结果** / A polish failure is swallowed and the global result returned.</summary>
    /// <remarks>
    /// 全局搜索是主线，打磨只是"锦上添花"。让打磨的失败把全局结果一起扔掉，
    /// 会让**已经算出来的好解因为一次没必要的局部搜索而消失**，而上层只看到"求解失败"。
    /// The global search is the main line and the polish is a bonus. Letting a polish failure discard the global result makes
    /// **a good solution already found vanish because of an unnecessary local search**, while the layer above only sees "solve failed".
    /// </remarks>
    [Fact]
    public async Task Invoke_ShouldSwallowPolishFailure() {
        var marker = new PolishMarker(StubSolver.Fail("polish broke"));
        var solver = new MemeticSolver<Flt64, Flt64>(
            "memetic", StubSolver.Ok(2.0), marker.Build);

        HeuristicResult<Flt64, Flt64> result = Unwrap(await solver.Invoke(new SolverModel()));

        result.BestObjective!.ToDouble().Should().Be(2.0, "打磨失败时保留全局结果 / the global result stands when the polish fails");
        marker.Invocations.Should().Be(1);
    }

    /// <summary>**只有严格更好才采用打磨结果** / Only a strictly better polish result is kept.</summary>
    /// <remarks>
    /// 相等时替换会让**同一个解因为一次无意义的替换而改变来源**，
    /// 让"打磨到底有没有起作用"再也无法从结果上判断。
    /// Replacing on a tie makes **an identical solution change provenance because of a meaningless swap**,
    /// so whether the polish did anything at all can no longer be told from the result.
    /// </remarks>
    [Fact]
    public async Task Invoke_ShouldKeepGlobalUnlessStrictlyBetter() {
        var better = new MemeticSolver<Flt64, Flt64>(
            "better", StubSolver.Ok(2.0), new PolishMarker(StubSolver.Ok(1.0)).Build);
        // 相等时两条分支都返回 2.0，只有**解向量本身**能区分来源：打磨带回 7.0，全局带回 2.0。
        // On a tie both branches return 2.0, so only **the solution vector itself** tells the provenance apart: the polish carries 7.0,
        // the global result carries 2.0.
        var tie = new MemeticSolver<Flt64, Flt64>(
            "tie", StubSolver.Ok(2.0), new PolishMarker(StubSolver.Ok(2.0, startValue: 7.0)).Build);
        var worse = new MemeticSolver<Flt64, Flt64>(
            "worse", StubSolver.Ok(2.0), new PolishMarker(StubSolver.Ok(3.0)).Build);

        Unwrap(await better.Invoke(new SolverModel())).BestObjective!.ToDouble().Should().Be(1.0);
        HeuristicResult<Flt64, Flt64> tieResult = Unwrap(await tie.Invoke(new SolverModel()));
        tieResult.BestObjective!.ToDouble().Should().Be(2.0);
        tieResult.BestSolution![0].ToDouble().Should().Be(2.0,
            "相等时保留全局结果，而不是打磨的解 / a tie keeps the global result, not the polish's");
        Unwrap(await worse.Invoke(new SolverModel())).BestObjective!.ToDouble().Should().Be(2.0);
    }

    /// <summary>**没有起点时原样返回，且不调用打磨** / Without a start point the result is returned unchanged and the polish is not called.</summary>
    [Fact]
    public async Task Invoke_ShouldSkipPolishWithoutStartPoint() {
        var marker = new PolishMarker(StubSolver.Ok(0.1));
        var solver = new MemeticSolver<Flt64, Flt64>(
            "memetic", StubSolver.Ok(2.0, hasSolution: false), marker.Build);

        HeuristicResult<Flt64, Flt64> result = Unwrap(await solver.Invoke(new SolverModel()));

        result.BestSolution.Should().BeNull();
        result.BestObjective!.ToDouble().Should().Be(2.0);
        marker.Invocations.Should().Be(0, "没有起点就没有可打磨的东西 / there is nothing to polish without a start point");
    }

    /// <summary>**打磨拿到的是全新的迭代状态，名字带全局前缀** / The polish gets a fresh iteration and the name carries the global prefix.</summary>
    /// <remarks>
    /// 沿用全局的迭代状态会让打磨的停止判据从全局已经用掉的预算开始数，
    /// **于是打磨要么一步都不走，要么走得比配置的多**。
    /// Reusing the global iteration makes the polish's stopping criterion count from the budget the global search already consumed,
    /// **so the polish either never takes a step or takes more than configured**.
    /// </remarks>
    [Fact]
    public async Task Invoke_ShouldGivePolishAFreshIteration() {
        var marker = new PolishMarker(StubSolver.Ok(0.5));
        var solver = new MemeticSolver<Flt64, Flt64>(
            "memetic", StubSolver.Ok(2.0, iteration: new Iteration()), marker.Build);

        await solver.Invoke(new SolverModel(), new Iteration());

        marker.Iterations.Should().HaveCount(1);
        marker.Iterations[0].Should().BeNull(
            "打磨必须从零开始计预算 / the polish must count its budget from zero");
        marker.StartPoints[0][0].Should().Be(2.0, "打磨起点是全局最优解 / the polish starts from the global best");
    }

    /// <summary>**工厂把名称拼成 `memetic-` 加全局名称，并给出打磨求解器名称** / The factory prefixes the global name and names the polish solver.</summary>
    [Fact]
    public async Task Memetic_ShouldNameBothSolvers() {
        IHeuristicSolver<Flt64, Flt64> solver = MemeticSolvers.Memetic(
            StubSolver.Ok(2.0, name: "ga"),
            new IterationLimit(5UL),
            IIntoValue<Flt64>.Identity,
            () => new Flt64(0.5),
            new RandomPerturbation(1),
            NotWorseAcceptance.Instance);

        solver.Name.Should().Be("memetic-ga");

        await solver.Invoke(new SolverModel());

        IHeuristicSolver<Flt64, Flt64> unwrapped = ((MemeticSolver<Flt64, Flt64>)solver).Global;
        unwrapped.Name.Should().Be("ga");
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");

    /// <summary>记录打磨调用的工厂 / A polish factory that records its calls.</summary>
    private sealed class PolishMarker {
        private readonly IHeuristicSolver<Flt64, Flt64> _polish;

        public PolishMarker(IHeuristicSolver<Flt64, Flt64> polish) => _polish = polish;

        public int Invocations { get; private set; }

        public List<Iteration?> Iterations { get; } = new();

        public List<IReadOnlyList<double>> StartPoints { get; } = new();

        public IHeuristicSolver<Flt64, Flt64> Build(IReadOnlyList<double> values) {
            Invocations++;
            StartPoints.Add(values);
            return new RecordingSolver(_polish, Iterations);
        }
    }

    /// <summary>记录迭代状态的求解器 / A solver that records its iteration state.</summary>
    private sealed class RecordingSolver : IHeuristicSolver<Flt64, Flt64> {
        private readonly IHeuristicSolver<Flt64, Flt64> _inner;
        private readonly List<Iteration?> _iterations;

        public RecordingSolver(IHeuristicSolver<Flt64, Flt64> inner, List<Iteration?> iterations) {
            _inner = inner;
            _iterations = iterations;
        }

        /// <inheritdoc/>
        public string Name => _inner.Name;

        /// <inheritdoc/>
        public Task<Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> Invoke(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model,
            Iteration? iteration = null) {
            _iterations.Add(iteration);
            return _inner.Invoke(model, iteration);
        }
    }

    /// <summary>返回固定结果的求解器 / A solver with a fixed outcome.</summary>
    private sealed class StubSolver : IHeuristicSolver<Flt64, Flt64> {
        private readonly Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> _result;

        public StubSolver(
            string name,
            Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result) {
            Name = name;
            _result = result;
        }

        /// <inheritdoc/>
        public string Name { get; }

        /// <summary>成功结果 / A successful outcome.</summary>
        /// <param name="objective">目标值 / The objective value.</param>
        /// <param name="hasSolution">是否带回解向量 / Whether a solution vector is carried.</param>
        /// <param name="iteration">迭代状态 / The iteration state.</param>
        /// <param name="name">名称 / The name.</param>
        /// <param name="startValue">解向量里的取值，默认等于目标值 / The value inside the solution vector, defaulting to the objective.</param>
        /// <returns>求解器 / The solver.</returns>
        public static IHeuristicSolver<Flt64, Flt64> Ok(
            double objective,
            bool hasSolution = true,
            Iteration? iteration = null,
            string name = "stub",
            double? startValue = null) {
            IReadOnlyList<Flt64>? values = hasSolution
                ? new[] { new Flt64(startValue ?? objective) }
                : null;
            return new StubSolver(name, Results.Ok(new HeuristicResult<Flt64, Flt64>(
                values, new Flt64(objective), HeuristicSolutionStatus.Feasible,
                iteration ?? new Iteration())));
        }

        /// <summary>失败结果 / A failing outcome.</summary>
        /// <param name="message">消息 / The message.</param>
        /// <returns>求解器 / The solver.</returns>
        public static IHeuristicSolver<Flt64, Flt64> Fail(string message, string name = "stub") =>
            new StubSolver(name, Results.Failed<HeuristicResult<Flt64, Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, message)));

        /// <inheritdoc/>
        public Task<Result<HeuristicResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> Invoke(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model,
            Iteration? iteration = null) =>
            Task.FromResult(_result);
    }

    /// <summary>求解器测试模型 / A solver test model.</summary>
    private sealed class SolverModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
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
