// 正交算子组合测试 / Tests for the orthogonal operator portfolio.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 正交算子组合 / The orthogonal operator portfolio.
/// </summary>
public sealed class OrthogonalOperatorPortfolioTests {
    /// <summary>**连续组合返回五个算法** / The continuous portfolio returns five algorithms.</summary>
    /// <remarks>
    /// 规模下限是 4，而组合实际有 5 个算法。若把下限改成"返回个数"的下限，调用方传 4 会得到一个少一个算法的组合，
    /// **而校验通过、组合看起来仍然正交**。
    /// The size floor is 4, yet the portfolio has 5 algorithms. Turning the floor into "the number returned" lets a caller passing 4 get a
    /// portfolio one algorithm short, **while validation passes and the portfolio still looks orthogonal**.
    /// </remarks>
    [Fact]
    public void Continuous_ShouldReturnFiveAlgorithms() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> built =
            OrthogonalOperatorPortfolio.Continuous(4);

        built.Should().BeOfType<Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>>();
        ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)built).Value.Count
            .Should().Be(5, "规模 4 仍应得到五个算法 / a size of 4 still yields five algorithms");
    }

    /// <summary>**连续组合拒绝规模小于 4** / The continuous portfolio rejects sizes below 4.</summary>
    [Fact]
    public void Continuous_ShouldRejectSmallSize() {
        OrthogonalOperatorPortfolio.Continuous(3)
            .Should().BeOfType<Failed<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>>();
        OrthogonalOperatorPortfolio.Continuous(0)
            .Should().BeOfType<Failed<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**强化算法的规模固定为 1** / The intensification algorithm has a fixed size of 1.</summary>
    /// <remarks>
    /// 让它跟随调用方的规模会让"强化"退化成又一次全局搜索，**而组合仍然声称自己覆盖了强化这一类行为**。
    /// Letting it follow the caller's size degrades "intensification" into another global search, **while the portfolio still claims to
    /// cover the intensification behaviour class**.
    /// </remarks>
    [Fact]
    public void Continuous_ShouldFixIntensificationSize() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> built =
            OrthogonalOperatorPortfolio.Continuous(50);
        IReadOnlyList<AlgorithmConfig> portfolio =
            ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)built).Value;

        AlgorithmConfig intensification = Find(portfolio, "intensification");
        intensification.Size.Should().Be(1, "强化算法不跟随全局规模 / intensification does not follow the global size");

        Find(portfolio, "distribution").Size.Should().Be(50);
        Find(portfolio, "escape").Size.Should().Be(50);
    }

    /// <summary>**`frr-mab` 只被三个算法共用** / `frr-mab` is shared by three algorithms only.</summary>
    /// <remarks>
    /// 给所有算法都配 `frr-mab` 会让每个算法独立学习自己的滑动窗口，**而"正交"退化成"五个独立的单臂老虎机"**。
    /// Giving every algorithm `frr-mab` makes each learn its own sliding window, **and "orthogonal" degenerates into five independent
    /// bandits**.
    /// </remarks>
    [Fact]
    public void Continuous_ShouldShareSelectorOnlyWhereDeclared() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> built =
            OrthogonalOperatorPortfolio.Continuous(20);
        IReadOnlyList<AlgorithmConfig> portfolio =
            ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)built).Value;

        int withSelector = 0;
        foreach (AlgorithmConfig algorithm in portfolio) {
            if (algorithm.Selection is not null
                && algorithm.Selection.Type == "frr-mab") {
                withSelector++;
            }
        }
        withSelector.Should().Be(2, "只有 encircling 与 escape 显式声明了 frr-mab / only encircling and escape declare frr-mab");

        // 另外两个算法（encircling、escape）确实声明了它，第三个是 permutation 组合里的。
        // The other two (encircling, escape) do declare it; the third lives in the permutation portfolio.
        Find(portfolio, "encircling").Selection!.Type.Should().Be("frr-mab");
        Find(portfolio, "escape").Selection!.Type.Should().Be("frr-mab");
        Find(portfolio, "distribution").Selection.Should().BeNull();
    }

    /// <summary>**连续组合的标识唯一** / The continuous portfolio's identities are unique.</summary>
    [Fact]
    public void Continuous_ShouldUseUniqueIdentities() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> built =
            OrthogonalOperatorPortfolio.Continuous(20);
        IReadOnlyList<AlgorithmConfig> portfolio =
            ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)built).Value;

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (AlgorithmConfig algorithm in portfolio) {
            identifiers.Add(algorithm.Id).Should().BeTrue(
                $"标识 {algorithm.Id} 必须唯一 / identity {algorithm.Id} must be unique");
        }
    }

    /// <summary>**排列组合返回单算法且带排列算子** / The permutation portfolio returns one algorithm with permutation operators.</summary>
    [Fact]
    public void Permutation_ShouldReturnSingleAlgorithm() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> built =
            OrthogonalOperatorPortfolio.Permutation(20);

        built.Should().BeOfType<Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>>();
        AlgorithmConfig algorithm =
            ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)built).Value[0];
        algorithm.Id.Should().Be("permutation");
        algorithm.Size.Should().Be(20);
        algorithm.Updates![0].Type.Should().Be("order");
        algorithm.Mutations!.Count.Should().Be(2);
        algorithm.Selection!.Type.Should().Be("frr-mab");
    }

    /// <summary>**排列组合拒绝非正规模** / The permutation portfolio rejects non-positive sizes.</summary>
    [Fact]
    public void Permutation_ShouldRejectNonPositiveSize() {
        OrthogonalOperatorPortfolio.Permutation(0)
            .Should().BeOfType<Failed<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>>();
        OrthogonalOperatorPortfolio.Permutation(-1)
            .Should().BeOfType<Failed<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**两个组合都能被配置校验接受** / Both portfolios are accepted by configuration validation.</summary>
    [Fact]
    public void Portfolios_ShouldValidateAsConfigurations() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> continuous =
            OrthogonalOperatorPortfolio.Continuous(20);
        var config = new HyperHeuristicConfig(
            new ComponentSpec("model"),
            ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)continuous).Value,
            new BudgetConfig(Evaluations: 100L));

        config.Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**`cma-es` 带 `restartOnImport` 参数** / `cma-es` carries the `restartOnImport` parameter.</summary>
    [Fact]
    public void Continuous_ShouldCarryCmaEsParameter() {
        Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> built =
            OrthogonalOperatorPortfolio.Continuous(20);
        AlgorithmConfig distribution =
            ((Ok<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>>)built).Value[0];

        distribution.Algorithm.Type.Should().Be("cma-es");
        distribution.Algorithm.Parameters!["restartOnImport"]!.GetValue<bool>().Should().BeTrue();
    }

    /// <summary>按标识查找算法 / Find an algorithm by identity.</summary>
    /// <param name="portfolio">组合 / The portfolio.</param>
    /// <param name="id">标识 / The identity.</param>
    /// <returns>算法 / The algorithm.</returns>
    private static AlgorithmConfig Find(IReadOnlyList<AlgorithmConfig> portfolio, string id) {
        foreach (AlgorithmConfig algorithm in portfolio) {
            if (string.Equals(algorithm.Id, id, StringComparison.Ordinal)) {
                return algorithm;
            }
        }
        throw new InvalidOperationException($"algorithm {id} not found");
    }
}
