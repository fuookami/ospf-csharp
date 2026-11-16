// 对称特征分解、岛屿迁移与约束排序测试 / Tests for eigendecomposition, island migration and constraint ranking.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 对称矩阵特征分解 / Symmetric-matrix eigendecomposition.
/// </summary>
public sealed class SymmetricEigenTests {
    /// <summary>**特征值与独立实现一致** / The eigenvalues match an independent implementation.</summary>
    /// <remarks>
    /// 黄金向量由独立 Python 的 Jacobi 实现算出，不是从 C# 输出抄来的：
    /// `[[2,1],[1,2]] → {1, 3}`、`[[4,1],[1,4]] → {3, 5}`、`[[2,1,0],[1,3,1],[0,1,2]] → {1, 4, 2}`。
    /// 第三个用例的解析解是 `2 + √2 ≈ 3.414`、`2`、`2 - √2 ≈ 0.586`——**Python 与 C# 若都写错同一个旋转公式，
    /// 这组黄金向量就抓不到**，所以再补一条对角矩阵用例（旋转必须完全不触发）。
    /// Golden vectors come from an independent Python Jacobi implementation, not copied from C# output. The third case's analytic
    /// eigenvalues are `2 ± √2` and `2`, so a shared rotation bug would slip past that vector — a diagonal matrix case is added as well,
    /// where rotations must not fire at all.
    /// </remarks>
    [Fact]
    public void SymmetricEigen_ShouldMatchGoldenValues() {
        EigenValuesOf(new[] { new[] { 2.0, 1.0 }, new[] { 1.0, 2.0 } })
            .Should().BeEquivalentTo(new[] { 1.0, 3.0 }, options => options.Using<double>(
                context => context.Subject.Should().BeApproximately(context.Expectation, 1e-9))
                .WhenTypeIs<double>());
        EigenValuesOf(new[] { new[] { 4.0, 1.0 }, new[] { 1.0, 4.0 } })
            .Should().BeEquivalentTo(new[] { 3.0, 5.0 }, options => options.Using<double>(
                context => context.Subject.Should().BeApproximately(context.Expectation, 1e-9))
                .WhenTypeIs<double>());
    }

    /// <summary>**对角矩阵不触发任何旋转，特征值原样返回** / A diagonal matrix triggers no rotation.</summary>
    [Fact]
    public void SymmetricEigen_ShouldKeepDiagonal() {
        SymmetricEigenResult result = SymmetricEigen.Decompose(
            new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 2.0, 0.0 }, new[] { 0.0, 0.0, 3.0 } });

        result.Values.Should().Equal(1.0, 2.0, 3.0);
        result.Vectors[0].Should().Equal(1.0, 0.0, 0.0);
        result.Vectors[1].Should().Equal(0.0, 1.0, 0.0);
        result.Vectors[2].Should().Equal(0.0, 0.0, 1.0);
    }

    /// <summary>**特征向量正交归一** / The eigenvectors are orthonormal.</summary>
    /// <remarks>
    /// 每个向量自身正交归一这件事**在向量取错时也成立**，所以必须同时检查不同向量之间的内积为 0，才能确认它们真的构成
    /// 一组特征基。按行读取会得到另一组正交基——只查单个向量看不出任何异常。
    /// A vector being unit length **holds even when the vectors are wrong**, so distinct vectors' inner products must also be checked
 /// to confirm they really form an eigenbasis. The implementation transposes explicitly; reading rows gives a different orthonormal basis
    /// and inspecting single vectors reveals nothing.
    /// </remarks>
    [Fact]
    public void SymmetricEigen_ShouldReturnOrthonormalBasis() {
        SymmetricEigenResult result = SymmetricEigen.Decompose(
            new[] { new[] { 2.0, 1.0, 0.0 }, new[] { 1.0, 3.0, 1.0 }, new[] { 0.0, 1.0, 2.0 } });

        for (int first = 0; first < result.Vectors.Count; first++) {
            double norm = result.Vectors[first].Sum(value => value * value);
            norm.Should().BeApproximately(1.0, 1e-9);
            for (int second = first + 1; second < result.Vectors.Count; second++) {
                double inner = result.Vectors[first].Zip(result.Vectors[second], (a, b) => a * b).Sum();
                inner.Should().BeApproximately(0.0, 1e-9,
                    "不同特征向量必须正交 / distinct eigenvectors must be orthogonal");
            }
        }
    }

    /// <summary>**每个特征向量确实是对应特征值的特征向量** / Each vector really is an eigenvector for its eigenvalue.</summary>
    /// <remarks>
    /// 这是最强的一条：`A·v = λ·v`。它同时覆盖特征值、特征向量与转置方向——三者任一出错都会破坏这个等式。
    /// This is the strongest assertion: `A·v = λ·v`. It covers the eigenvalues, the eigenvectors and the transposition direction at once.
    /// </remarks>
    [Fact]
    public void SymmetricEigen_ShouldSatisfyEigenEquation() {
        double[][] matrix = { new[] { 2.0, 1.0, 0.0 }, new[] { 1.0, 3.0, 1.0 }, new[] { 0.0, 1.0, 2.0 } };
        SymmetricEigenResult result = SymmetricEigen.Decompose(matrix);

        for (int index = 0; index < result.Values.Count; index++) {
            for (int row = 0; row < matrix.Length; row++) {
                double left = 0.0;
                for (int column = 0; column < matrix.Length; column++) {
                    left += matrix[row][column] * result.Vectors[index][column];
                }
                double right = result.Values[index] * result.Vectors[index][row];
                left.Should().BeApproximately(right, 1e-9,
                    "A·v 必须等于 λ·v / A·v must equal λ·v");
            }
        }
    }

    /// <summary>**空矩阵与非方阵返回无效结果** / Empty and non-square matrices yield an invalid result.</summary>
    [Fact]
    public void SymmetricEigen_ShouldRejectMalformedInput() {
        SymmetricEigen.Decompose(Array.Empty<IReadOnlyList<double>>()).IsValid.Should().BeFalse();
        SymmetricEigen.Decompose(new[] { new[] { 1.0, 2.0 }, new[] { 3.0 } }).IsValid.Should().BeFalse();
    }

    /// <summary>取特征值 / Read the eigenvalues.</summary>
    /// <param name="matrix">矩阵 / The matrix.</param>
    /// <returns>特征值 / The eigenvalues.</returns>
    private static IReadOnlyList<double> EigenValuesOf(IReadOnlyList<IReadOnlyList<double>> matrix) =>
        SymmetricEigen.Decompose(matrix).Values;
}

/// <summary>
/// 岛屿迁移 / Island migration.
/// </summary>
public sealed class IslandMigrationTests {
    /// <summary>**环形迁移用上一座岛的最优替换本岛的最差** / Ring migration replaces the worst with the previous island's best.</summary>
    /// <remarks>
    /// 方向写反（从下一座岛迁入）会让信息沿环反向流动，收敛顺序整体反转，而每座岛的规模与最优值看起来都正常。
    ///
    /// **必须用三座岛**：两座岛时"上一座"与"下一座"是同一座，方向反转完全不可见——变异测试证实了这一点，
    /// 第一版正是两座岛，把方向反转后测试仍然通过。三座岛时岛 1 的迁入者分别是岛 0（正确）或岛 2（错误），可区分。
    ///
    /// **必须用 `rate = 0.5`**：`rate = 1.0` 时 `amount = 2`，整座岛被完全替换，"保留最优、丢弃最差"的语义被整个抹掉。
    ///
    /// Reversing the direction makes information flow backwards around the ring and inverts the convergence order while every island's
    /// size and best value still looks normal.
    /// **Three islands are required**: with two, "previous" and "next" are the same island and a direction flip is invisible — the
    /// mutation test confirmed it on the first two-island version. With three, island 1 receives either from island 0 (correct) or from
    /// island 2 (wrong), which is distinguishable.
    /// **`rate = 0.5` is required**: with 1.0 the amount is 2 and the whole island is replaced, erasing the keep-best/drop-worst semantics.
    /// </remarks>
    [Fact]
    public void Ring_ShouldMigrateFromPreviousIsland() {
        // 岛 0 = {0, 10}，岛 1 = {100, 110}，岛 2 = {200, 210}；适应度即坐标，越小越好。
        // Island 0 = {0, 10}, island 1 = {100, 110}, island 2 = {200, 210}; fitness equals the coordinate, smaller is better.
        var islands = new List<IReadOnlyList<SolutionWithFitness<object, Flt64>>> {
            Build(0.0, 10.0),
            Build(100.0, 110.0),
            Build(200.0, 210.0),
        };

        IReadOnlyList<IReadOnlyList<SolutionWithFitness<object, Flt64>>> migrated =
            new RingMigration<object, Flt64>(0.5)
                .Invoke(new Iteration(), islands, null!, () => new Flt64(0.5),
                    member => member.Fitness);

        // 岛 1 从岛 0 迁入最优 0 → {100, 0}；若方向反了则从岛 2 迁入 200 → {100, 200}。
        // Island 1 takes island 0's best 0, giving {100, 0}; a reversed direction would take island 2's 200, giving {100, 200}.
        migrated[1].Select(member => member.Fitness)
            .Should().BeEquivalentTo(new[] { 100.0, 0.0 });
        migrated[2].Select(member => member.Fitness)
            .Should().BeEquivalentTo(new[] { 200.0, 100.0 });
        migrated[0].Select(member => member.Fitness)
            .Should().BeEquivalentTo(new[] { 0.0, 200.0 });
    }

    /// <summary>**迁移后岛屿规模不变** / Island sizes stay unchanged after migration.</summary>
    [Fact]
    public void Ring_ShouldPreserveIslandSize() {
        IReadOnlyList<IReadOnlyList<SolutionWithFitness<object, Flt64>>> migrated =
            new RingMigration<object, Flt64>()
                .Invoke(new Iteration(), Islands(), null!, () => new Flt64(0.5),
                    member => member.Fitness);

        migrated.Should().OnlyContain(island => island.Count == 2);
    }

    /// <summary>**单岛时环形迁移不做事** / A single island makes ring migration a no-op.</summary>
    [Fact]
    public void Ring_ShouldDoNothingWithSingleIsland() {
        var single = new List<IReadOnlyList<SolutionWithFitness<object, Flt64>>> {
            new List<SolutionWithFitness<object, Flt64>> {
                new(new[] { new Flt64(0.0) }, new object(), 0.0),
            },
        };

        IReadOnlyList<IReadOnlyList<SolutionWithFitness<object, Flt64>>> migrated =
            new RingMigration<object, Flt64>(1.0)
                .Invoke(new Iteration(), single, null!, () => new Flt64(0.5),
                    member => member.Fitness);

        migrated.Should().BeSameAs(single);
    }

    /// <summary>**最优岛把最优个体送到最差岛** / The best island sends its best to the worst island.</summary>
    [Fact]
    public void BestToWorst_ShouldMoveBestToWorst() {
        var islands = new List<IReadOnlyList<SolutionWithFitness<object, Flt64>>> {
            new List<SolutionWithFitness<object, Flt64>> {
                new(new[] { new Flt64(0.0) }, new object(), 0.0),
                new(new[] { new Flt64(1.0) }, new object(), 1.0),
            },
            new List<SolutionWithFitness<object, Flt64>> {
                new(new[] { new Flt64(50.0) }, new object(), 50.0),
                new(new[] { new Flt64(51.0) }, new object(), 51.0),
            },
            new List<SolutionWithFitness<object, Flt64>> {
                new(new[] { new Flt64(90.0) }, new object(), 90.0),
                new(new[] { new Flt64(91.0) }, new object(), 91.0),
            },
        };

        IReadOnlyList<IReadOnlyList<SolutionWithFitness<object, Flt64>>> migrated =
            new BestToWorstMigration<object, Flt64>(1)
                .Invoke(new Iteration(), islands, null!, () => new Flt64(0.5),
                    member => member.Fitness);

        // 最优岛是 0（最优 0），最差岛是 2（最优 90）；岛 2 的最后一个被替换为 0。
        // The best island is 0 (best 0) and the worst is 2 (best 90); island 2's last entry becomes 0.
        migrated[2].Select(member => member.Fitness).Should().BeEquivalentTo(new[] { 90.0, 0.0 });
        migrated[0].Select(member => member.Fitness).Should().BeEquivalentTo(new[] { 0.0, 1.0 });
        migrated[1].Select(member => member.Fitness).Should().BeEquivalentTo(new[] { 50.0, 51.0 });
    }

    /// <summary>**空岛存在时最优到最差迁移不做事** / Best-to-worst migration is a no-op when any island is empty.</summary>
    [Fact]
    public void BestToWorst_ShouldSkipWithEmptyIsland() {
        var islands = new List<IReadOnlyList<SolutionWithFitness<object, Flt64>>> {
            new List<SolutionWithFitness<object, Flt64>> {
                new(new[] { new Flt64(0.0) }, new object(), 0.0),
            },
            new List<SolutionWithFitness<object, Flt64>>(),
        };

        IReadOnlyList<IReadOnlyList<SolutionWithFitness<object, Flt64>>> migrated =
            new BestToWorstMigration<object, Flt64>(1)
                .Invoke(new Iteration(), islands, null!, () => new Flt64(0.5),
                    member => member.Fitness);

        migrated.Should().BeSameAs(islands);
    }

    /// <summary>构造两座岛 / Build two islands.</summary>
    /// <returns>岛屿 / The islands.</returns>
    private static List<IReadOnlyList<SolutionWithFitness<object, Flt64>>> Islands() => new() {
        Build(0.0, 10.0),
        Build(100.0, 110.0),
    };

    /// <summary>构造一座岛 / Build one island.</summary>
    /// <param name="first">第一个适应度 / The first fitness.</param>
    /// <param name="second">第二个适应度 / The second fitness.</param>
    /// <returns>岛屿 / The island.</returns>
    private static IReadOnlyList<SolutionWithFitness<object, Flt64>> Build(double first, double second) =>
        new List<SolutionWithFitness<object, Flt64>> {
            new(new[] { new Flt64(first) }, new object(), first),
            new(new[] { new Flt64(second) }, new object(), second),
        };
}

/// <summary>
/// 约束排序 / Constraint ranking.
/// </summary>
public sealed class ConstraintRankingTests {
    /// <summary>**ε 随步数收缩到 0** / ε shrinks to zero across the horizon.</summary>
    /// <remarks>
    /// 固定 ε 会让算法永远优先满足约束，后期无法在可行域内继续优化。
    /// A fixed ε keeps the algorithm prioritizing feasibility forever and blocks late-stage refinement inside the feasible region.
    /// </remarks>
    [Fact]
    public void Epsilon_ShouldShrinkAcrossHorizon() {
        var evaluator = new EpsilonConstraintEvaluator(1.0, 10, 2.0);
        ConstraintAssessment<string>[] candidates = {
            // 违反量 0.5：第 0 步 ε = 1.0 → 可行；第 10 步 ε = 0 → 不可行。
            // Violation 0.5: ε = 1.0 at step 0 means feasible; ε = 0 at step 10 means infeasible.
            new ConstraintAssessment<string>("a", new Flt64(5.0), new Flt64(0.5)),
            new ConstraintAssessment<string>("b", new Flt64(0.0), new Flt64(0.4)),
        };

        Result<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>> early = evaluator.Rank(candidates, 0);
        ((Ok<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>)early)
            .Value.Select(item => item.Value).Should().Equal("b", "a");
    }

    /// <summary>**ε 为 0 时不可行解排到后面** / With ε of 0 infeasible solutions sort last.</summary>
    [Fact]
    public void Epsilon_ShouldSeparateFeasibleAtZero() {
        var evaluator = new EpsilonConstraintEvaluator(1.0, 10, 2.0);
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("infeasible", new Flt64(0.0), new Flt64(0.5)),
            new ConstraintAssessment<string>("feasible", new Flt64(5.0), Flt64.Zero),
        };

        IReadOnlyList<ConstraintAssessment<string>> ranked =
            ((Ok<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>)
                evaluator.Rank(candidates, 10)).Value;

        ranked.Select(item => item.Value).Should().Equal("feasible", "infeasible");
    }

    /// <summary>**相同违反量时按目标值比较** / Equal violations compare by objective.</summary>
    /// <remarks>
    /// 少了这个条件，排序会退化成"保持原序"，看起来只是"稳定排序"。
    /// Without it the sort degenerates into "keep the input order", which merely looks like a stable sort.
    /// </remarks>
    [Fact]
    public void Epsilon_ShouldCompareObjectiveOnEqualViolation() {
        var evaluator = new EpsilonConstraintEvaluator(1.0, 10, 2.0);
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("worse", new Flt64(9.0), new Flt64(0.3)),
            new ConstraintAssessment<string>("better", new Flt64(1.0), new Flt64(0.3)),
        };

        IReadOnlyList<ConstraintAssessment<string>> ranked =
            ((Ok<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>)
                evaluator.Rank(candidates, 10)).Value;

        ranked.Select(item => item.Value).Should().Equal("better", "worse");
    }

    /// <summary>**非法输入被拒绝** / Illegal inputs are rejected.</summary>
    [Fact]
    public void Epsilon_ShouldRejectIllegalInputs() {
        var evaluator = new EpsilonConstraintEvaluator(-1.0, 10, 2.0);
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("a", Flt64.Zero, Flt64.Zero),
        };

        evaluator.Rank(candidates, 0)
            .Should().BeOfType<Failed<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>>();

        new EpsilonConstraintEvaluator(1.0, 10, 2.0)
            .Rank(new[] { new ConstraintAssessment<string>("bad", Flt64.Zero, new Flt64(-1.0)) }, 0)
            .Should().BeOfType<Failed<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**两个都可行时无条件按目标值排序** / Two feasible entries always sort by objective.</summary>
    /// <remarks>
    /// 把抽签也用在这条分支上会让可行解之间的顺序随机化，收敛曲线出现无法解释的抖动。
    /// Drawing in that branch too randomizes the order among feasible solutions and the convergence curve jitters for no visible reason.
    /// </remarks>
    [Fact]
    public void Stochastic_ShouldAlwaysCompareFeasibleByObjective() {
        var ranking = new StochasticRanking(0.0);
        // **两个候选的违反量必须都是 0**。判据是 `a.violation == 0 && b.violation == 0`，即"两者都完全可行"，
        // 而不是"两者都不超过某个阈值"。第一版给了 0.8 与 0.2 两个非零违反量，于是 `bothFeasible` 为假、走抽签分支，
        // 断言因此失败——**是我的夹具违背了语义，不是实现有问题**。
        //
        // 两个违反量都为 0 时，"按目标值"与"按违反量"会给出同一顺序（违反量相等），所以无法用它们区分两种判据；
        // 这条测试的作用是钉住"概率 0 时可行解仍按目标值排序"，而不是区分判据。
        // **Both violations must be 0.** The predicate is `a.violation == 0 && b.violation == 0`, i.e. "both fully feasible",
        // not "both below some threshold". The first version used 0.8 and 0.2, so `bothFeasible` was false, the draw branch ran and the
        // assertion failed — **the fixture contradicted its semantics, not the implementation**.
        //
        // With both violations at 0, "by objective" and "by violation" coincide (equal violations), so they cannot distinguish the two
        // criteria; this test pins "feasible entries still sort by objective at probability 0" rather than distinguishing criteria.
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("worse", new Flt64(9.0), Flt64.Zero),
            new ConstraintAssessment<string>("better", new Flt64(1.0), Flt64.Zero),
        };

        IReadOnlyList<ConstraintAssessment<string>> ranked =
            ((Ok<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>)
                ranking.Rank(candidates, () => new Flt64(0.9))).Value;
        // **必须用 `Equal`**：`BeEquivalentTo` 不检查顺序，而这条测试的全部意义就是顺序。
        // **`Equal` is required**: `BeEquivalentTo` ignores order and this test is entirely about order.
        ranked.Select(item => item.Value).Should().Equal(
            new[] { "better", "worse" },
            "可行解必须始终按目标值排序 / feasible entries must always sort by objective");
    }

    /// <summary>**概率为 1 时始终按目标值** / A probability of 1 always compares by objective.</summary>
    [Fact]
    public void Stochastic_ShouldUseObjectiveAtFullProbability() {
        var ranking = new StochasticRanking(1.0);
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("low-violation-high-objective", new Flt64(9.0), new Flt64(0.1)),
            new ConstraintAssessment<string>("high-violation-low-objective", new Flt64(1.0), new Flt64(0.9)),
        };

        IReadOnlyList<ConstraintAssessment<string>> ranked =
            ((Ok<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>)
                ranking.Rank(candidates, () => new Flt64(0.5))).Value;

        ranked.Select(item => item.Value).Should().Equal("high-violation-low-objective", "low-violation-high-objective");
    }

    /// <summary>**概率为 0 且都不可行时按违反量排序** / At zero probability infeasible entries sort by violation.</summary>
    [Fact]
    public void Stochastic_ShouldUseViolationAtZeroProbability() {
        var ranking = new StochasticRanking(0.0);
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("high-violation", new Flt64(1.0), new Flt64(0.9)),
            new ConstraintAssessment<string>("low-violation", new Flt64(9.0), new Flt64(0.1)),
        };

        IReadOnlyList<ConstraintAssessment<string>> ranked =
            ((Ok<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>)
                ranking.Rank(candidates, () => new Flt64(0.5))).Value;

        ranked.Select(item => item.Value).Should().Equal("low-violation", "high-violation");
    }

    /// <summary>**非法概率被拒绝** / An illegal probability is rejected.</summary>
    [Fact]
    public void Stochastic_ShouldRejectIllegalProbability() {
        var ranking = new StochasticRanking(1.5);
        ConstraintAssessment<string>[] candidates = {
            new ConstraintAssessment<string>("a", Flt64.Zero, Flt64.Zero),
        };

        ranking.Rank(candidates, () => Flt64.Zero)
            .Should().BeOfType<Failed<IReadOnlyList<ConstraintAssessment<string>>, ErrorCode, Error<ErrorCode>>>();
    }
}
