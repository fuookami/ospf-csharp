// CMA-ES 运行器测试 / Tests for the CMA-ES numerics and runner.

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
/// CMA-ES 数值辅助 / The CMA-ES numerical helpers.
/// </summary>
public sealed class CmaNumericsTests {
    /// <summary>**原生 Box–Muller 的第一个随机数被抬到 `1e-12` 以上** / The native Box–Muller lifts its first draw above `1e-12`.</summary>
    /// <remarks>
    /// 不抬会让 `ln(0)` 得到负无穷，**于是整条采样路径产出 `NaN`，而协方差在若干代之后才彻底失效**。
    /// Without it `ln(0)` yields negative infinity, **so the whole sampling path produces `NaN` and the covariance only collapses a few
    /// generations later**.
    /// </remarks>
    [Fact]
    public void Gaussian_ShouldClampFirstDraw() {
        var draws = new Queue<double>(new[] { 0.0, 0.0 });

        double sample = CmaNumericsSupport.Gaussian(
            CmaNumerics.Native, () => new Flt64(draws.Dequeue()));

        double expected = global::System.Math.Sqrt(-2.0 * global::System.Math.Log(1.0e-12));
        double.IsFinite(sample).Should().BeTrue("抬升后必须是有限值 / the lifted draw must stay finite");
        sample.Should().BeApproximately(expected, 1.0e-9);
    }

    /// <summary>**原生采样是 Box–Muller** / The native draw is Box–Muller.</summary>
    [Fact]
    public void Gaussian_ShouldFollowBoxMuller() {
        var draws = new Queue<double>(new[] { 0.25, 0.0 });

        double sample = CmaNumericsSupport.Gaussian(
            CmaNumerics.Native, () => new Flt64(draws.Dequeue()));

        double expected = global::System.Math.Sqrt(-2.0 * global::System.Math.Log(0.25));
        sample.Should().BeApproximately(expected, 1.0e-12,
            "cos(0) = 1 / cos(0) = 1");
    }

    /// <summary>**极坐标采样拒绝半径 0 与 1，并只用第一个分量** / Polar sampling rejects radii 0 and 1 and uses only the first component.</summary>
    /// <remarks>
    /// 包含 0 会让 `ln(radius)` 出现负无穷；包含 1 会让样本恰好为零并把协方差推向奇异。
    /// Including 0 makes `ln(radius)` infinite; including 1 produces a sample that is exactly zero and pushes the covariance toward
    /// singularity.
    /// </remarks>
    [Fact]
    public void Gaussian_ShouldRejectDegeneratePolarRadius() {
        // (0.5, 0.5) → x = y = 0 → 半径 0，被拒；(0.25, 0.5) → x = −0.5, y = 0 → 半径 0.25。
        // (0.5, 0.5) → x = y = 0 → radius 0, rejected; (0.25, 0.5) → x = −0.5, y = 0 → radius 0.25.
        var draws = new Queue<double>(new[] { 0.5, 0.5, 0.25, 0.5 });
        int consumed = 0;

        double sample = CmaNumericsSupport.Gaussian(CmaNumerics.PolarFdlibmV1, () => {
            consumed++;
            return new Flt64(draws.Dequeue());
        });

        consumed.Should().Be(4, "第一次采样因半径 0 被拒，第二次才成功 / the first pair is rejected, the second succeeds");
        double expected = -0.5 * global::System.Math.Sqrt(
            -2.0 * global::System.Math.Log(0.25) / 0.25);
        sample.Should().BeApproximately(expected, 1.0e-12);
        sample.Should().BeNegative("该分量保留了 x 的符号 / the component keeps the sign of x");
    }
}

/// <summary>
/// CMA-ES 运行器 / The CMA-ES runner.
/// </summary>
public sealed class CmaEsRunnerTests {
    /// <summary>**初始化给出单位协方差与初始步长** / Initialization yields an identity covariance and the initial step size.</summary>
    [Fact]
    public async Task Initialize_ShouldBuildDistribution() {
        CmaEsRunner<Flt64, Flt64> runner = BuildRunner(initialSigma: 0.3);
        var model = new CmaModel(new[] { new Flt64(3.0), new Flt64(-1.0) });

        CmaEsState<Flt64, Flt64> state = await runner.InitializeStep(model, new Iteration());

        state.Mean.Should().Equal(new[] { 3.0, -1.0 });
        state.Sigma.Should().Be(0.3);
        state.Covariance[0][0].Should().Be(1.0);
        state.Covariance[0][1].Should().Be(0.0);
        state.Covariance[1][1].Should().Be(1.0);
        state.Generation.Should().Be(0);
        state.Best.Should().NotBeNull();
    }

    /// <summary>**没有初始解时给出空的退化状态** / Without any initial solution the state is the empty degenerate one.</summary>
    [Fact]
    public async Task Initialize_ShouldHandleMissingInitialSolution() {
        CmaEsRunner<Flt64, Flt64> runner = BuildRunner();
        var model = new CmaModel(Array.Empty<Flt64>());

        CmaEsState<Flt64, Flt64> state = await runner.InitializeStep(model, new Iteration());

        state.Mean.Should().BeEmpty();
        state.Best.Should().BeNull();
        runner.StateMembers(state).Should().BeEmpty();
    }

    /// <summary>**成员只返回"至今最优"** / Members are the best so far only.</summary>
    /// <remarks>
    /// 返回当前分布的样本会让档案记录"最近采样到了什么"，**而不是"搜索过程中找到过什么"**。
    /// Returning the current samples makes the archive record "what was sampled recently" **instead of "what was ever found"**.
    /// </remarks>
    [Fact]
    public async Task MembersOf_ShouldReturnBestOnly() {
        CmaEsRunner<Flt64, Flt64> runner = BuildRunner();
        var model = new CmaModel(new[] { new Flt64(2.0) });
        CmaEsState<Flt64, Flt64> state = await runner.InitializeStep(model, new Iteration());

        IReadOnlyList<IIndividual<Flt64, Flt64>> members = runner.StateMembers(state);

        members.Should().HaveCount(1);
        members[0].Objective.ToDouble().Should().Be(4.0, "目标为 2² = 4 / the objective is 2² = 4");
    }

    /// <summary>**一步推进增加代数、保持协方差对称、步长有限为正** / One step bumps the generation, keeps the covariance symmetric and the step size finite and positive.</summary>
    [Fact]
    public async Task Advance_ShouldUpdateDistribution() {
        CmaEsRunner<Flt64, Flt64> runner = BuildRunner();
        var model = new CmaModel(new[] { new Flt64(3.0), new Flt64(1.0) });
        CmaEsState<Flt64, Flt64> state = await runner.InitializeStep(model, new Iteration());

        CmaEsState<Flt64, Flt64> advanced =
            await runner.AdvanceStep(model, new Iteration(), state);

        advanced.Generation.Should().Be(1);
        advanced.Sigma.Should().BeGreaterThan(0.0);
        double.IsFinite(advanced.Sigma).Should().BeTrue();
        advanced.Covariance[0][1].Should().Be(advanced.Covariance[1][0],
            "协方差必须保持对称 / the covariance must stay symmetric");
        advanced.Mean.Should().HaveCount(2);
        advanced.Best.Should().NotBeNull();
    }

    /// <summary>**步长非正或非有限时原样返回** / A non-positive or non-finite step size returns the state unchanged.</summary>
    /// <remarks>
    /// 继续推进只会把 `NaN` 写进协方差，**而"运行仍在推进"这件事看起来完全正常**。
    /// Advancing only writes `NaN` into the covariance, **while "the run is still progressing" looks perfectly normal**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldReturnDegenerateStateUnchanged() {
        CmaEsRunner<Flt64, Flt64> runner = BuildRunner();
        var model = new CmaModel(new[] { new Flt64(1.0) });

        foreach (double sigma in new[] { 0.0, -1.0, double.NaN }) {
            var state = new CmaEsState<Flt64, Flt64>(
                new[] { 1.0 }, sigma, new[] { new[] { 1.0 } },
                new[] { 0.0 }, new[] { 0.0 }, 0,
                new SolutionWithFitness<Flt64, Flt64>(new[] { new Flt64(1.0) }, new Flt64(1.0), 0.0));

            CmaEsState<Flt64, Flt64> advanced =
                await runner.AdvanceStep(model, new Iteration(), state);

            advanced.Should().BeSameAs(state, "退化状态不得被推进 / a degenerate state must not advance");
        }
    }

    /// <summary>**零维状态原样返回** / A zero-dimensional state is returned unchanged.</summary>
    [Fact]
    public async Task Advance_ShouldReturnZeroDimensionUnchanged() {
        CmaEsRunner<Flt64, Flt64> runner = BuildRunner();
        var model = new CmaModel(Array.Empty<Flt64>());
        CmaEsState<Flt64, Flt64> state = await runner.InitializeStep(model, new Iteration());

        CmaEsState<Flt64, Flt64> advanced =
            await runner.AdvanceStep(model, new Iteration(), state);

        advanced.Should().BeSameAs(state);
    }

    /// <summary>**非法初始步长被拒绝** / An invalid initial step size is rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectInvalidSigma() {
        Action zero = () => BuildRunner(initialSigma: 0.0);
        Action negative = () => BuildRunner(initialSigma: -0.5);
        Action notFinite = () => BuildRunner(initialSigma: double.NaN);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
        notFinite.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="initialSigma">初始步长 / The initial step size.</param>
    /// <returns>运行器 / The runner.</returns>
    private static CmaEsRunner<Flt64, Flt64> BuildRunner(double initialSigma = 0.3) =>
        new(() => new Flt64(0.5),
            new BestArchive<Flt64, Flt64>(20),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            initialSigma: initialSigma);

    /// <summary>CMA-ES 测试模型 / A CMA-ES test model.</summary>
    private sealed class CmaModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        private readonly IReadOnlyList<Flt64> _initial;

        public CmaModel(IReadOnlyList<Flt64> initial) => _initial = initial;

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
        public IReadOnlyList<IReadOnlyList<Flt64>> InitialSolutions(
            ulong initialSolutionAmount = 1) =>
            _initial.Count == 0 ? Array.Empty<IReadOnlyList<Flt64>>() : new[] { _initial };

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) {
            // 平方和：最小值在原点，便于观察均值是否朝原点移动。
            // Sum of squares: the minimum is at the origin, which makes it easy to see whether the mean moves toward it.
            double total = 0.0;
            foreach (Flt64 value in solution) {
                double item = value.ToDouble();
                total += item * item;
            }
            return new Flt64(total);
        }

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
