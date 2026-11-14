// NSGA-III 运行器与参考方向距离测试 / Tests for the NSGA-III runner and the reference-direction distance.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 参考方向距离 / The reference-direction distance.
/// </summary>
public sealed class ReferenceDistanceTests {
    /// <summary>**零参考方向的模长不大于阈值时返回正无穷** / A reference direction of near-zero norm yields positive infinity.</summary>
    /// <remarks>
    /// 返回 0 会让"零向量"看起来是所有个体里最贴近方向的那一个，**于是每轮补位都挑中同一批个体，
    /// 参考方向的多样性维护彻底失效而编号仍然正常递增**。
    /// Returning zero would make the "zero vector" look like the closest direction for every individual, **so every fill round picks the
    /// same batch and the diversity maintenance silently collapses while the indices still advance normally**.
    /// </remarks>
    [Fact]
    public void PerpendicularDistance_ShouldReturnInfinityForZeroReference() {
        double distance = ReferencePoints.PerpendicularDistance(
            new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 });

        distance.Should().Be(double.PositiveInfinity);
    }

    /// <summary>**参考方向上的点距离为 0** / A point on the reference direction has zero distance.</summary>
    [Fact]
    public void PerpendicularDistance_ShouldBeZeroOnTheDirection() {
        double distance = ReferencePoints.PerpendicularDistance(
            new[] { 3.0, 3.0 }, new[] { 1.0, 1.0 });

        distance.Should().BeApproximately(0.0, 1.0e-12);
    }

    /// <summary>**垂直距离按手算值** / The perpendicular distance matches the hand-computed value.</summary>
    /// <remarks>
    /// 手算：(1,0) 到方向 (1,1)：投影 = 1，模方 = 2，分量 = (1−0.5, 0−0.5) → sqrt(0.5) ≈ 0.7071067811865476。
    /// Hand-computed: (1,0) against the direction (1,1): projection = 1, squared norm = 2, components (1−0.5, 0−0.5) →
    /// sqrt(0.5) ≈ 0.7071067811865476.
    /// </remarks>
    [Fact]
    public void PerpendicularDistance_ShouldMatchHandComputedValue() {
        double distance = ReferencePoints.PerpendicularDistance(
            new[] { 1.0, 0.0 }, new[] { 1.0, 1.0 });

        distance.Should().BeApproximately(0.7071067811865476, 1.0e-12);
    }

    /// <summary>**长度不一致被拒绝** / Mismatched lengths are rejected.</summary>
    [Fact]
    public void PerpendicularDistance_ShouldRejectLengthMismatch() {
        Action act = () => ReferencePoints.PerpendicularDistance(
            new[] { 1.0, 0.0, 2.0 }, new[] { 1.0, 1.0 });

        act.Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// NSGA-III 运行器 / The NSGA-III runner.
/// </summary>
public sealed class Nsga3RunnerTests {
    /// <summary>**名称是 nsga-iii，且与 nsga-ii 区分开** / The name is "nsga-iii", distinct from "nsga-ii".</summary>
    [Fact]
    public void Name_ShouldDistinguishFromNsga2() {
        Nsga3Runner<Flt64> runner = BuildRunner(size: 2);
        Nsga2Runner<Flt64> baseRunner = BuildRunner2(size: 2);

        runner.Name.Should().Be("nsga-iii");
        baseRunner.Name.Should().Be("nsga-ii");
    }

    /// <summary>**最后一层按参考方向补位，而不是按拥挤距离取端点** / The last front is filled by reference directions, not by crowding extremes.</summary>
    /// <remarks>
    /// 四个互不支配个体 (0,10)、(1,9)、(2,8)、(3,7)，归一化后 min = (0,7)、max = (3,10)，极差 (3,3)，于是
    /// 归一化结果是 (0,1)、(1/3,2/3)、(2/3,1/3)、(1,0)。
    ///
    /// 参考方向取 2 目标 8 分割（9 个方向）。按垂直距离，四个归一化点分别归到方向 0、3、5、8，互不冲突。
    /// 补位第一轮挑"编号最小的空方向" 0，取归属该方向的 (0,10)；第二轮挑方向 3，取 (1,9)。
    /// 因此结果是前两个个体，**而 NSGA-II 的拥挤距离会挑两个端点 (0,10) 与 (3,7)** —— 这就是两条测试的分水岭。
    ///
    /// Four mutually non-dominated individuals (0,10), (1,9), (2,8), (3,7); normalisation uses min = (0,7), max = (3,10), spans (3,3), so
    /// the normalised points are (0,1), (1/3,2/3), (2/3,1/3), (1,0).
    ///
    /// With 2 objectives and 8 divisions (9 directions), the perpendicular distances map them onto directions 0, 3, 5 and 8 with no
    /// collision. The first fill round takes the lowest-numbered empty direction 0 and picks (0,10); the second takes direction 3 and picks
    /// (1,9). **NSGA-II's crowding distance would instead pick the two extremes (0,10) and (3,7)** — that is what separates the two tests.
    /// </remarks>
    [Fact]
    public void SelectImported_ShouldFillByReferenceDirection() {
        Nsga3Runner<Flt64> runner = BuildRunner(size: 2, divisions: 8);
        Nsga2Runner<Flt64> twoObjective = BuildRunner2(size: 2);
        // 输入顺序刻意反过来：这样"最近方向"的判据一旦出错，退化路径会挑到 available[0] = (3,7)，
        // 与正确结果 (1,9) 明显不同，而不会被"按原顺序取第一个"恰好掩盖。
        // The input order is deliberately reversed: if the nearest-direction predicate breaks, the fallback picks available[0] = (3,7),
        // clearly different from the correct (1,9), instead of being masked by "take the first in the original order".
        IIndividual<IReadOnlyList<Flt64>, Flt64>[] candidates = new[] {
            Individual(3.0, 7.0),
            Individual(2.0, 8.0),
            Individual(1.0, 9.0),
            Individual(0.0, 10.0),
        };

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> n3 = runner.SelectImported(candidates);
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> n2 = twoObjective.SelectImported(candidates);

        n3[0].Objective[0].ToDouble().Should().Be(0.0);
        n3[1].Objective[0].ToDouble().Should().Be(1.0,
            "NSGA-III 取方向 3 上的 (1,9)，而不是端点 (3,7) / NSGA-III takes (1,9) on direction 3, not the extreme (3,7)");
        var n2First = new List<double> { n2[0].Objective[0].ToDouble(), n2[1].Objective[0].ToDouble() };
        n2First.Should().BeEquivalentTo(new[] { 0.0, 3.0 },
            "对照组：NSGA-II 取两个端点 / control: NSGA-II takes the two extremes");
    }

    /// <summary>**候选不超过目标数量时原样返回** / Candidates within the target amount are returned as-is.</summary>
    [Fact]
    public void SelectImported_ShouldReturnAllWhenWithinAmount() {
        Nsga3Runner<Flt64> runner = BuildRunner(size: 8);
        IIndividual<IReadOnlyList<Flt64>, Flt64>[] candidates =
            new[] { Individual(0.0, 1.0), Individual(1.0, 0.0) };

        runner.SelectImported(candidates).Should().BeSameAs(candidates);
    }

    /// <summary>**既支配关系先淘汰，再谈补位** / Domination is resolved before any filling.</summary>
    [Fact]
    public void SelectImported_ShouldDropDominatedFirst() {
        Nsga3Runner<Flt64> runner = BuildRunner(size: 2);

        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, Flt64>> selected =
            runner.SelectImported(new[] {
                Individual(0.0, 1.0),
                Individual(1.0, 1.0),
                Individual(2.0, 2.0),
            });

        selected.Should().HaveCount(2);
        selected[0].Objective[0].ToDouble().Should().Be(0.0);
        selected[1].Objective[0].ToDouble().Should().Be(1.0);
    }

    /// <summary>**非正分割数被拒绝** / A non-positive division count is rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectInvalidDivisions() {
        Action act = () => BuildRunner(size: 2, divisions: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
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

    /// <summary>构造 NSGA-III 运行器 / Build an NSGA-III runner.</summary>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="divisions">分割数 / The divisions.</param>
    /// <returns>运行器 / The runner.</returns>
    private static Nsga3Runner<Flt64> BuildRunner(int size, int divisions = 8) =>
        new(solution => new[] { solution.Values[0], solution.Values[1] },
            new MinimizationDominance(2),
            new PassThroughUpdate(),
            new IdentityMutation(),
            () => new Flt64(0.5),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size,
            divisions: divisions);

    /// <summary>构造 NSGA-II 运行器作对照 / Build an NSGA-II runner as the control.</summary>
    /// <param name="size">种群规模 / The population size.</param>
    /// <returns>运行器 / The runner.</returns>
    private static Nsga2Runner<Flt64> BuildRunner2(int size) =>
        new(solution => new[] { solution.Values[0], solution.Values[1] },
            new MinimizationDominance(2),
            new PassThroughUpdate(),
            new IdentityMutation(),
            () => new Flt64(0.5),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size);

    /// <summary>原样返回父代的更新算子 / An update operator that returns its parents unchanged.</summary>
    private sealed class PassThroughUpdate : ISolutionUpdate {
        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
            Iteration iteration,
            IReadOnlyList<T> parents,
            Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
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
            Fuookami.Ospf.Core.Model.Callback.IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy,
            Flt64 mutationRate)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> =>
            new(individual.Solution);
    }
}
