// 三线更新算子测试 / Tests for the tier-3 update operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 三线更新算子 / The tier-3 update operators.
/// </summary>
public sealed class Tier3UpdateTests {
    /// <summary>**每个父代产一个子代且维数不变** / Each parent yields one child of the same dimension.</summary>
    [Fact]
    public void Update_ShouldPreserveCountAndDimension() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            IReadOnlyList<Solution<Flt64>> children = Run(update, Parents(5), seed: 2);

            children.Should().HaveCount(5);
            children.Should().OnlyContain(child => child.Values.Count == 2);
        }
    }

    /// <summary>**空父代组产生空结果** / An empty parent group yields nothing.</summary>
    [Fact]
    public void Update_ShouldHandleEmptyParents() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            Run(update, Array.Empty<EvaluatedIndividual<Flt64, Flt64>>(), seed: 1).Should().BeEmpty();
        }
    }

    /// <summary>**相同种子完全可重放** / Updates replay exactly under the same seed.</summary>
    [Fact]
    public void Update_ShouldReplayUnderSameSeed() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            Flatten(Run(update, Parents(4), seed: 7)).Should().Equal(Flatten(Run(update, Parents(4), seed: 7)));
        }
    }

    /// <summary>**带边界时被夹到区间内** / Children are clamped into the bounds.</summary>
    [Fact]
    public void Update_ShouldClampIntoBounds() {
        foreach (ISolutionUpdate update in AllUpdates()) {
            var random = new SearchRandom(3);
            update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                    new Iteration(), Parents(6), new BoundedStubModel<object?>(2, -10.0, 10.0),
                    IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()))
                .SelectMany(child => child.Values)
                .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
        }
    }

    /// <summary>列出全部三线算子 / Enumerate the tier-3 operators.</summary>
    /// <returns>算子集合 / The operator set.</returns>
    private static IEnumerable<ISolutionUpdate> AllUpdates() => new ISolutionUpdate[] {
        new CuckooSearchUpdate(),
        new BatAlgorithmUpdate(),
        new MothFlameUpdate(),
        new GrasshopperUpdate(),
    };

    /// <summary>执行一次更新 / Run one update.</summary>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="parents">父代组 / The parent group.</param>
    /// <param name="seed">种子 / The seed.</param>
    /// <returns>子代 / The children.</returns>
    private static IReadOnlyList<Solution<Flt64>> Run(
        ISolutionUpdate update,
        IReadOnlyList<EvaluatedIndividual<Flt64, Flt64>> parents,
        long seed) {
        var random = new SearchRandom(seed);
        return update.Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            new Iteration(), parents, new StubModel(value => value[0]), IIntoValue<Flt64>.Identity,
            () => new Flt64(random.NextDouble()));
    }

    /// <summary>摊平子代数值 / Flatten child values.</summary>
    /// <param name="children">子代 / The children.</param>
    /// <returns>摊平后的数值 / The flattened values.</returns>
    private static IEnumerable<double> Flatten(IReadOnlyList<Solution<Flt64>> children) =>
        children.SelectMany(child => child.Values).Select(value => value.ToDouble());

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <param name="count">父代数量 / The parent count.</param>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new EvaluatedIndividual<Flt64, Flt64>(
                new[] { new Flt64(index * 2.0), new Flt64(index * 2.0 + 1.0) }, new Flt64(index)))
            .ToArray();
}

/// <summary>
/// 布谷鸟搜索的 Γ 函数与莱维尺度 / The Γ function and Levy scale in cuckoo search.
/// </summary>
public sealed class CuckooSearchUpdateTests {
    /// <summary>**Lanczos Γ 与独立实现一致** / The Lanczos Γ matches an independent implementation.</summary>
    /// <remarks>
    /// 黄金向量由独立 Python 实现算出，不是从 C# 输出抄来的：
    /// `Γ(5) = 24`（精确整数）、`Γ(0.5) = √π = 1.772453850905516`（走 `value &lt; 0.5` 的反射分支）。
    /// 第二条尤其重要：反射分支只在 `value &lt; 0.5` 时触发，而算法主路径用到的自变量是 `1.0`、`1.5`、`2.0`、`1.25`、`0.75`，
    /// **没有一条会走到反射分支**——不显式测就完全没有覆盖。
    /// Golden vectors come from an independent Python implementation, not copied from C# output: `Γ(5) = 24` (an exact integer) and
    /// `Γ(0.5) = √π = 1.772453850905516` (taking the `value &lt; 0.5` reflection branch). The second matters most: the reflection branch
    /// only fires below 0.5, while the algorithm's arguments are 1.0, 1.5, 2.0, 1.25 and 0.75 — **none of them reaches that branch**,
    /// so without an explicit test it has no coverage at all.
    /// </remarks>
    [Fact]
    public void Cuckoo_ShouldMatchGoldenGamma() {
        CuckooSearchUpdate.Gamma(5.0).Should().BeApproximately(24.0, 1e-12);
        CuckooSearchUpdate.Gamma(0.5).Should().BeApproximately(1.772453850905516, 1e-12);
    }

    /// <summary>**莱维尺度与独立实现一致** / The Levy scale matches the independent implementation.</summary>
    /// <remarks>
    /// 指数 1.0 时公式退化为 1.0、指数 2.0 时塌到 `1e-8` 量级，只有默认的 1.5 给出有意义的 `0.696574502557697`。
    /// 三个点一起测，才能防止"公式写反但仍然在某个指数上碰巧正确"。
    /// At exponent 1.0 the formula degenerates to 1.0 and at 2.0 it collapses to the `1e-8` scale; only the default 1.5 gives the
    /// meaningful `0.696574502557697`. Testing all three points prevents "the formula is inverted but happens to be right at one
    /// exponent".
    /// </remarks>
    [Fact]
    public void Cuckoo_ShouldMatchGoldenLevySigma() {
        CuckooSearchUpdate.LevySigma(1.5).Should().BeApproximately(0.696574502557697, 1e-12);
        CuckooSearchUpdate.LevySigma(1.0).Should().BeApproximately(1.0, 1e-12);
        CuckooSearchUpdate.LevySigma(2.0).Should().BeApproximately(9.884972e-9, 1e-15);
    }

    /// <summary>**全部弃巢时子代落在区间内** / When every nest is abandoned the children land inside the interval.</summary>
    [Fact]
    public void Cuckoo_ShouldRebuildInsideDomainWhenAbandoned() {
        var random = new SearchRandom(9);

        new CuckooSearchUpdate(1.5, 0.01, 1.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(),
                new[] { new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(5.0) }, new Flt64(0.0)) },
                new BoundedStubModel<object?>(1, -10.0, 10.0), IIntoValue<Flt64>.Identity,
                () => new Flt64(random.NextDouble()))
            .SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() >= -10.0 && value.ToDouble() <= 10.0);
    }
}

/// <summary>
/// 蝙蝠算法的步长方向 / The bat algorithm's step direction.
/// </summary>
public sealed class BatAlgorithmUpdateTests {
    /// <summary>**步长方向是远离最优，不是朝向最优** / The step points away from the best, not toward it.</summary>
    /// <remarks>
    /// 写成 `best - position` 会让蝙蝠朝最优跳跃，与"回声定位探索"的意图相反——收敛更快、更容易早熟，但没有任何数值异常。
    /// 构造：最优在 0、个体在 5，熵源恒返回 0.5 → 频率 `0 + 0.5*2 = 1.0`、步长系数 0.5，
    /// 于是 `step = (5 - 0) * 1.0 * 0.5 = 2.5`、子代为 `7.5`（远离）。反向实现会给出 `2.5`。
    /// Writing `best - position` makes the bat jump toward the best, contradicting the echolocation-exploration intent — faster
    /// convergence and premature stagnation with no numerical anomaly. Setup: best at 0, individual at 5, constant entropy 0.5 gives
    /// frequency `0 + 0.5*2 = 1.0` and step factor 0.5, so `step = 5 * 1.0 * 0.5 = 2.5` and the child is `7.5` (away). The inverted
    /// implementation would give `2.5`.
    /// </remarks>
    [Fact]
    public void Bat_ShouldStepAwayFromBest() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(5.0) }, new Flt64(1.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = new BatAlgorithmUpdate(0.0, 2.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(0.5));

        children[1].Values[0].ToDouble().Should().BeApproximately(7.5, 1e-12);
    }

    /// <summary>**零频率时个体完全不动** / A zero frequency leaves individuals untouched.</summary>
    [Fact]
    public void Bat_ShouldNotMoveAtZeroFrequency() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(0.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(5.0) }, new Flt64(1.0)),
        };
        var random = new SearchRandom(2);

        IReadOnlyList<Solution<Flt64>> children = new BatAlgorithmUpdate(0.0, 0.0)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        children.Select((child, index) => child.Values[0].ToDouble() - parents[index].Solution[0].ToDouble())
            .Should().OnlyContain(delta => delta == 0.0);
    }
}

/// <summary>
/// 飞蛾扑火的火焰数量 / The moth-flame flame count.
/// </summary>
public sealed class MothFlameUpdateTests {
    /// <summary>**火焰数量递减到 1 后所有个体都以最优为火焰** / Once the flame count reaches 1 everyone uses the best as its flame.</summary>
    /// <remarks>
    /// 把 `flameAmount` 固定为 `size` 会让算法永远在探索阶段、收敛停滞。火焰比例为 `1e-9` 时 `flameAmount` 夹到 1，
    /// 于是所有个体都以 `parents[0]` 为火焰。
    ///
    /// **不能用"螺旋 = 0"来验证**：`distance * exp(0) * cos(0) + flame = |flame - current| + flame`，当 `current > flame` 时
    /// 这个式子恰好等于 `current`，子代原地不动——第一版夹具正是这样写的，得到 `{50, 90}` 而非预期的 2。它既不能证明
    /// `flameAmount == 1`，也不能证明螺旋公式。
    ///
    /// 改用熵源 0.25 → 螺旋 `0.25*2 - 1 = -0.5`、`exp(-0.5) = 0.6065306597…`、`cos(-π) = -1`，
    /// 于是 `value = 2 - 0.6065306597 * distance`。个体 1 距火焰 48 → `2 - 29.113471666 = -27.113471666`。
    /// 若 `flameAmount == size`，个体 1 的火焰就是它自己、距离为 0，子代恒为 50——两者可区分。
    ///
    /// Pinning `flameAmount` to `size` keeps the algorithm exploring forever and convergence stalls. With a flame ratio of `1e-9`
    /// `flameAmount` clamps to 1, so every individual uses `parents[0]` as its flame.
    /// **A spiral of 0 cannot verify this**: `distance * exp(0) * cos(0) + flame = |flame - current| + flame`, which equals `current`
    /// whenever `current > flame`, leaving the child untouched — the first fixture did exactly that and got `{50, 90}` instead of 2. It
    /// proves neither `flameAmount == 1` nor the spiral formula.
    /// With entropy 0.25 the spiral is `-0.5`, `exp(-0.5) = 0.6065306597…` and `cos(-π) = -1`, so
    /// `value = 2 - 0.6065306597 * distance`. Individual 1 is 48 away from the flame, giving `2 - 29.113471666 = -27.113471666`.
    /// If `flameAmount == size`, individual 1's flame is itself with distance 0 and the child stays at 50 — distinguishable.
    /// </remarks>
    [Fact]
    public void MothFlame_ShouldUseBestAsFlameWhenCountIsOne() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(2.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(50.0) }, new Flt64(1.0)),
            new(new[] { new Flt64(90.0) }, new Flt64(2.0)),
        };

        IReadOnlyList<Solution<Flt64>> children = new MothFlameUpdate(
                1.0, new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(new Flt64(1.0e-9)))
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(0.25));

        children[1].Values[0].ToDouble().Should().BeApproximately(-27.113471666206, 1e-9);
        children[2].Values[0].ToDouble().Should().BeApproximately(
            2.0 - (0.606530659712633 * 88.0), 1e-9);
    }
}

/// <summary>
/// 蝗虫更新的零距离与基准平移 / Zero distance and the best offset in grasshopper updates.
/// </summary>
public sealed class GrasshopperUpdateTests {
    /// <summary>**坐标全部相同时不产生 `NaN`，且子代落在最优处** / Identical coordinates never produce `NaN`.</summary>
    /// <remarks>
    /// "自己与自己"的距离恒为 0，`interaction / distance` 是除零；跳过判断是唯一防线。全部坐标相同时 `social = 0`，
    /// 于是子代恰好等于最优位置。
    /// The "self with self" distance is always 0 and `interaction / distance` divides by zero; the skip is the only guard. With all
    /// coordinates identical `social = 0`, so the child equals the best's position exactly.
    /// </remarks>
    [Fact]
    public void Grasshopper_ShouldHandleIdenticalCoordinates() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(5.0), new Flt64(5.0) }, new Flt64(1.0)),
        };
        var random = new SearchRandom(2);

        IReadOnlyList<Solution<Flt64>> children = new GrasshopperUpdate()
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        children.SelectMany(child => child.Values)
            .Should().OnlyContain(value => value.ToDouble() == 5.0);
    }

    /// <summary>**结果以最优为基准平移，不会滑向原点** / The result is offset from the best and does not slide to the origin.</summary>
    /// <remarks>
    /// 漏掉 `+ best[dim]` 会让整个种群滑向原点。最优取 100、其余在 101，收缩系数为 0（`coefficient² = 0`）时
    /// `social` 项被完全消掉，子代必须恰好是 100；漏掉平移项会得到 0。
    /// Omitting `+ best[dim]` slides the whole population toward the origin. With the best at 100 and the rest at 101, a shrink
    /// coefficient of 0 (so `coefficient² = 0`) eliminates the `social` term entirely and the child must be exactly 100; omitting the
    /// offset gives 0.
    /// </remarks>
    [Fact]
    public void Grasshopper_ShouldOffsetFromBest() {
        EvaluatedIndividual<Flt64, Flt64>[] parents = {
            new(new[] { new Flt64(100.0) }, new Flt64(0.0)),
            new(new[] { new Flt64(101.0) }, new Flt64(1.0)),
        };
        var random = new SearchRandom(2);

        IReadOnlyList<Solution<Flt64>> children = new GrasshopperUpdate(
                new Fuookami.Ospf.Core.Solver.Heuristic.Operator.ConstantSource(Flt64.Zero), 0.5, 1.5)
            .Update<object?, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), parents, new StubModel(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()));

        children.Select(child => child.Values[0].ToDouble()).Should().OnlyContain(value => value == 100.0);
    }
}
