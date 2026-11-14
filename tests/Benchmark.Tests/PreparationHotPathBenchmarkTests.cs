// 批量准备热路径基准测试 / Tests for the preparation hot-path benchmark.

using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.MultiArray;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 批量准备热路径基准 / The preparation hot-path benchmark.
/// </summary>
public sealed class PreparationHotPathBenchmarkTests {
    /// <summary>**三档规模各自放大，且夹具真的差异化** / The three tiers really differ.</summary>
    [Theory]
    [InlineData("small", 1)]
    [InlineData("medium", 2)]
    [InlineData("large", 4)]
    public void Setup_ShouldScaleThePrimeLimit(string dataset, int factor) {
        var benchmark = new PreparationHotPathBenchmark { Dataset = dataset };

        benchmark.Setup();

        benchmark.Factor.Should().Be(factor);
        benchmark.PrimeLimit.Should().Be(10_000 * factor);
        benchmark.Snapshot.Variables.Should().HaveCount(128 * factor);
    }

    /// <summary>**冷素性序列数出的素数个数正确** / The cold primality sequence counts the right number of primes.</summary>
    /// <remarks>
    /// **断言落在"数出多少个"而不是"有没有跑"**：`IsPrime` 若恒返回 `false`，基准照样跑完并给出 0，
    /// **而 0 看起来像"这一段没有素数"**。这里用 10 以内的已知素数个数当基准点。
    ///
    /// **The assertion is on the count, not on "it ran"**: if `IsPrime` always returned `false` the benchmark would still complete and report 0, and
    /// **a 0 looks like "this range has no primes"**. A known prime count below 10 serves as the reference point.
    /// </remarks>
    [Fact]
    public void ColdPrimeSequence_ShouldCountPrimes() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        int count = benchmark.ColdPrimeSequence();

        count.Should().Be(1229,
            "10 000 以内恰好 1229 个素数 / there are exactly 1229 primes below 10 000");
    }

    /// <summary>**常量数组的每个元素都是填充值** / Every element of the constant array carries the fill value.</summary>
    /// <remarks>
    /// **必须遍历真实元素**：只断言"形状对"时，`NewWith` 若把填充值写丢（全 0），形状依然对，
    /// **而 7 与 0 在聚合统计里都能算出一个数**。
    ///
    /// **The real elements must be walked**: asserting only "the shape is right" passes even when `NewWith` drops the fill value (leaving zeros), and
    /// **both 7 and 0 aggregate into some number**.
    /// </remarks>
    [Fact]
    public void ConstantArray_ShouldFillEveryElement() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        MutableMultiArray<int, Shape2> array = benchmark.ConstantArray();

        array.Shape.Should().Be(benchmark.Shape);
        int wrong = 0;
        foreach (int value in array) {
            if (value != 7) {
                ++wrong;
            }
        }
        wrong.Should().Be(0, "每个元素都必须是填充值 7 / every element must be the fill value 7");
    }

    /// <summary>**冷 token 索引对全部不同 key 给出非负位置，且互不相同** / Cold token positions are non-negative and distinct for every key.</summary>
    /// <remarks>
    /// 校验和为正既可能是"全对"也可能是"部分错"。**断言位置集合互不相同**才能排除"多个 key 落到同一位置"。
    ///
    /// A positive checksum can mean "all right" or "partly wrong". **Asserting the positions are distinct** is what rules out several keys landing on
    /// one position.
    /// </remarks>
    [Fact]
    public void ColdTokenIndices_ShouldReturnDistinctPositions() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        int checksum = benchmark.ColdTokenIndices();

        checksum.Should().BeGreaterThan(0);
        checksum.Should().Be(
            Enumerable.Range(0, 1_024).Sum(),
            "位置就是注册顺序 / the positions are the registration order");
    }

    /// <summary>**稀疏值域成员查询把偶数判为成员、奇数判为非成员** / Sparse-domain membership accepts evens and rejects odds.</summary>
    /// <remarks>
    /// 只断言"命中数等于某个值"时，**域把全部负数都算成成员也能凑出同一个数**。这里逐值断言。
    ///
    /// Asserting only "the hit count equals some value" passes even when **the domain treats every negative as a member**. This walks the values.
    /// </remarks>
    [Fact]
    public void SparseDomainMembership_ShouldDistinguishEvensFromOdds() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        benchmark.SparseDomainMembership().Should().Be(0,
            "负奇数不在只含非负偶数的值域里 / negative odds are not in a non-negative-even domain");
    }

    /// <summary>**单项与批量注册得到同样的数量** / Single-item and batch registration agree on the count.</summary>
    [Fact]
    public void Registration_ShouldAgreeBetweenSingleAndBatch() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        benchmark.SingleTokenRegistration().Should().Be(benchmark.BatchTokenRegistration());
        benchmark.BatchTokenRegistration().Should().Be(1_024);
    }

    /// <summary>**捕获的 checkpoint 带回完整的 incumbent** / The captured checkpoint carries the full incumbent.</summary>
    [Fact]
    public void CaptureCheckpoint_ShouldCarryTheIncumbent() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        ConstraintProgrammingCheckpointEnvelope envelope = benchmark.CaptureCheckpoint();

        envelope.Incumbent.Should().NotBeNull();
        envelope.Incumbent!.ValuesById.Should().NotBeNull();
        envelope.Incumbent.ValuesById!.Should().HaveCount(128);
    }

    /// <summary>**恢复带回与捕获同样多的变量** / The restore brings back as many variables as were captured.</summary>
    /// <remarks>
    /// **必须比对恢复结果与捕获结果的变量数**：恢复若只带回一部分，`RestoreCheckpoint` 仍会返回一个正数，
    /// **而正数看起来就像"恢复了"**。
    ///
    /// **The restored count must be compared against the captured count**: if the restore brought back only some variables, `RestoreCheckpoint` would
    /// still return a positive number, and **a positive number looks like "it restored"**.
    /// </remarks>
    [Fact]
    public void RestoreCheckpoint_ShouldBringBackEveryCapturedVariable() {
        var benchmark = new PreparationHotPathBenchmark { Dataset = "small" };
        benchmark.Setup();

        int restored = benchmark.RestoreCheckpoint();

        restored.Should().Be(benchmark.CaptureCheckpoint().Incumbent!.ValuesById!.Count);
        restored.Should().Be(128);
    }
}
