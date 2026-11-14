// 算子共用样板测试 / Tests for the shared operator boilerplate.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 目标比较规范化 / Objective comparison normalisation.
/// </summary>
public sealed class HeuristicOrderingTests {
    /// <summary>严格比较器下左侧更优返回负数 / A strictly better left side yields a negative result.</summary>
    [Fact]
    public void CompareStrictly_ShouldReturnNegativeForBetterLeft() {
        var model = new StubModel(value => value[0]);

        HeuristicOrdering.CompareStrictly<object?, Flt64, Flt64>(model, new Flt64(1.0), new Flt64(2.0))
            .Should().BeLessThan(0);
    }

    /// <summary>**非严格比较器下持平返回 0** / A non-strict comparator yields 0 on a tie.</summary>
    /// <remarks>
    /// 模型注册 `&lt;=` 时 `CompareObjective(a, a)` 返回 `Less`。单向比较会把持平误判为"改进"，让搜索在平台上无限前进。
    /// When a model registers `&lt;=`, `CompareObjective(a, a)` returns `Less`. A one-way comparison misreads the tie as an
    /// improvement and lets the search march forever on a plateau.
    /// </remarks>
    [Fact]
    public void CompareStrictly_ShouldReportTieForNonStrictComparator() {
        var model = new StubModel(value => value[0]) {
            // 非严格：相等也返回 Less / non-strict: equal also yields Less
            Comparer = (lhs, rhs) => OrderHelpers.OrderOf(lhs.ToDouble() <= rhs.ToDouble() ? -1 : 1),
        };

        HeuristicOrdering.CompareStrictly<object?, Flt64, Flt64>(model, new Flt64(5.0), new Flt64(5.0))
            .Should().Be(0, "持平不能被误判为改进 / a tie must not be misread as an improvement");
    }

    /// <summary>非严格比较器下右侧更优返回正数 / A worse left side yields a positive result.</summary>
    [Fact]
    public void CompareStrictly_ShouldReturnPositiveForWorseLeft() {
        var model = new StubModel(value => value[0]);

        HeuristicOrdering.CompareStrictly<object?, Flt64, Flt64>(model, new Flt64(9.0), new Flt64(1.0))
            .Should().BeGreaterThan(0);
    }

    /// <summary>严格比较器下持平返回 0 / A strictly equal pair yields 0.</summary>
    [Fact]
    public void CompareStrictly_ShouldReturnZeroForEqual() {
        var model = new StubModel(value => value[0]);

        HeuristicOrdering.CompareStrictly<object?, Flt64, Flt64>(model, new Flt64(4.0), new Flt64(4.0)).Should().Be(0);
    }

    /// <summary>拒绝空模型 / A null model is rejected.</summary>
    [Fact]
    public void CompareStrictly_ShouldRejectNullModel() =>
        ((Func<int>)(() => HeuristicOrdering.CompareStrictly<object?, Flt64, Flt64>(null!, new Flt64(1.0), new Flt64(2.0))))
            .Should().Throw<ArgumentNullException>();
}

/// <summary>
/// 数值辅助 / Numeric support.
/// </summary>
public sealed class NumericSupportTests {
    /// <summary>**`NaN` 回落到下界** / `NaN` falls back to the lower bound.</summary>
    [Fact]
    public void ClampTo_ShouldReplaceNaNWithLower() => NumericSupport.ClampTo(double.NaN, 1.0, 9.0).Should().Be(1.0);

    /// <summary>**无下界时 `NaN` 回落到上界** / `NaN` falls back to the upper bound when no lower exists.</summary>
    [Fact]
    public void ClampTo_ShouldReplaceNaNWithUpper() => NumericSupport.ClampTo(double.NaN, null, 9.0).Should().Be(9.0);

    /// <summary>**无边界时 `NaN` 回落到 0** / `NaN` falls back to 0 when no bound exists.</summary>
    [Fact]
    public void ClampTo_ShouldReplaceNaNWithZero() => NumericSupport.ClampTo(double.NaN, null, null).Should().Be(0.0);

    /// <summary>**无穷回落到边界** / Infinity falls back to a bound.</summary>
    [Fact]
    public void ClampTo_ShouldReplaceInfinity() {
        NumericSupport.ClampTo(double.PositiveInfinity, 1.0, 9.0).Should().Be(1.0);
        NumericSupport.ClampTo(double.NegativeInfinity, 1.0, 9.0).Should().Be(1.0);
    }

    /// <summary>区间外的值被夹到边界 / Out-of-range values are clamped to the bounds.</summary>
    [Fact]
    public void ClampTo_ShouldClampOutOfRange() {
        NumericSupport.ClampTo(100.0, 1.0, 9.0).Should().Be(9.0);
        NumericSupport.ClampTo(-100.0, 1.0, 9.0).Should().Be(1.0);
    }

    /// <summary>区间内的值不变 / A value within range is unchanged.</summary>
    [Fact]
    public void ClampTo_ShouldKeepInRangeValue() => NumericSupport.ClampTo(5.0, 1.0, 9.0).Should().Be(5.0);

    /// <summary>**概率 1 无条件触发且不消耗随机数** / Probability 1 fires unconditionally and consumes no draw.</summary>
    [Fact]
    public void ShouldFire_ShouldNotConsumeEntropyAtProbabilityOne() {
        int calls = 0;
        Func<Flt64> entropy = () => {
            calls++;
            return new Flt64(0.99);
        };

        NumericSupport.ShouldFire(entropy, 1.0).Should().BeTrue();
        calls.Should().Be(0, "必触发不该扰动随机流 / an always-fire must not shift the random stream");
    }

    /// <summary>概率 0 从不触发 / Probability 0 never fires.</summary>
    [Fact]
    public void ShouldFire_ShouldNeverFireAtProbabilityZero() =>
        NumericSupport.ShouldFire(() => new Flt64(0.0), 0.0).Should().BeFalse();

    /// <summary>概率比较使用抽样值 / The probability test uses the drawn value.</summary>
    [Fact]
    public void ShouldFire_ShouldCompareAgainstDraw() {
        NumericSupport.ShouldFire(() => new Flt64(0.2), 0.5).Should().BeTrue();
        NumericSupport.ShouldFire(() => new Flt64(0.8), 0.5).Should().BeFalse();
    }

    /// <summary>**熵源返回 null 时回落到 0.5** / A null reading falls back to 0.5.</summary>
    [Fact]
    public void Unit_ShouldFallBackToHalf() => NumericSupport.Unit(() => (Flt64?)null).Should().Be(0.5);

    /// <summary>抽样值被夹到 [0,1] / The sampled value is clamped to [0,1].</summary>
    [Fact]
    public void Unit_ShouldClampOutOfRange() {
        NumericSupport.Unit(() => new Flt64(5.0)).Should().Be(1.0);
        NumericSupport.Unit(() => new Flt64(-5.0)).Should().Be(0.0);
    }

    /// <summary>两个父代按顺序取出 / Two parents come out in order.</summary>
    [Fact]
    public void PickTwo_ShouldReturnFirstTwo() {
        (int First, int Second)? pair = NumericSupport.PickTwo(new[] { 1, 2, 3 });

        pair!.Value.First.Should().Be(1);
        pair.Value.Second.Should().Be(2);
    }

    /// <summary>只有一个父代时复用 / A single parent is reused.</summary>
    [Fact]
    public void PickTwo_ShouldReuseSingleParent() {
        (int First, int Second)? pair = NumericSupport.PickTwo(new[] { 7 });

        pair!.Value.First.Should().Be(7);
        pair.Value.Second.Should().Be(7);
    }

    /// <summary>空列表返回 null / An empty list yields null.</summary>
    [Fact]
    public void PickTwo_ShouldReturnNullForEmpty() => NumericSupport.PickTwo(Array.Empty<int>()).Should().BeNull();

    /// <summary>双精度转回解值类型 / A double converts back to the solution value type.</summary>
    [Fact]
    public void AsValue_ShouldConvert() =>
        NumericSupport.AsValue(2.5, Fuookami.Ospf.Core.Solver.Value.IIntoValue<Flt64>.Identity).Should().Be(new Flt64(2.5));
}
