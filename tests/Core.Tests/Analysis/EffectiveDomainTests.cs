#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using System.Collections.Generic;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 活跃证据下的有效定义域 / The effective domain under active evidence.
/// </summary>
/// <remarks>
/// **`Boolean` 那一行最容易写错**：把它当成"上界 1、下界 0"的区间，就会在边界不活跃时
/// **放开成全域**——于是一个布尔变量被允许取 42。**那不是一个数值边界被移除，那是一个类型被抹掉。**
///
/// **The `Boolean` row is the easiest to get wrong**: treating it as an interval with bounds 0 and 1 would, when the bounds are inactive, **open it to the whole integer range** — letting a
/// boolean variable take 42. **That is not a numeric bound being removed, that is a type being erased.**
/// </remarks>
public class EffectiveDomainTests {
    private static readonly VariableId X = new("source:x");

    private static IntegerDomain Interval(long lower, long upper) =>
        new IntegerDomain.Interval(new Int64(lower), new Int64(upper));

    private static IReadOnlySet<DiagnosticSource> Sources(params DiagnosticSource[] sources) =>
        new HashSet<DiagnosticSource>(sources);

    /// <summary>**二值性是类型不变量，永不放开** / Binary-ness is a type invariant and is never released.</summary>
    [Fact]
    public void TheBooleanDomain_ShouldNeverBeReleased() {
        // **稀疏不活跃**才是能区分的那一态：若不变量被删，`Boolean` 会走
        // "稀疏不活跃 → 降级成区间并放开边界"那条路，**变成全域。**
        //
        // 第一版判据给的是"稀疏活跃"，那条路会原样返回 `Values`，
        // **于是删掉不变量也不红**——判据没在测它声称要测的东西。
        //
        // **An inactive sparse domain is the state that discriminates**: without the invariant, `Boolean` falls into "inactive value set degrades to an interval with released bounds" and
        // **becomes the whole range.** The first version passed an *active* sparse source, which returns `Values` either way, **so dropping the invariant stayed green.**
        IntegerDomain effective = EffectiveDomain.Compute(
            IntegerDomain.Boolean, X,
            Sources(new DiagnosticSource.VariableLowerBound(X)));

        effective.LowerBound.ToLong().Should().Be(0, "a boolean still starts at zero");
        effective.UpperBound.ToLong().Should().Be(1, "a boolean still ends at one");
    }

    /// <summary>**没有活跃证据集合时不放开任何边界** / No active set releases nothing.</summary>
    [Fact]
    public void TheNullActiveSet_ShouldReleaseNothing() {
        IntegerDomain effective = EffectiveDomain.Compute(Interval(3, 7), X, activeSources: null);

        effective.LowerBound.ToLong().Should().Be(3);
        effective.UpperBound.ToLong().Should().Be(7);
    }

    /// <summary>**不活跃的边界放开，活跃的保留** / Inactive bounds are released, active ones kept.</summary>
    /// <remarks>
    /// **反向对照**：只测"全放开"会让"全保留"的实现通过，**而那会让假设的边界永远生效。**
    ///
    /// **The reverse control**: testing only "all released" lets "all kept" pass, **and that keeps a hypothetical bound in force forever.**
    /// </remarks>
    [Fact]
    public void TheInactiveBound_ShouldBeReleased() {
        IntegerDomain onlyLower = EffectiveDomain.Compute(
            Interval(3, 7), X, Sources(new DiagnosticSource.VariableLowerBound(X)));

        onlyLower.LowerBound.ToLong().Should().Be(3, "the lower bound is active");
        onlyLower.UpperBound.ToLong().Should().Be(long.MaxValue, "the upper bound is not active");

        IntegerDomain onlyUpper = EffectiveDomain.Compute(
            Interval(3, 7), X, Sources(new DiagnosticSource.VariableUpperBound(X)));

        onlyUpper.LowerBound.ToLong().Should().Be(long.MinValue, "the lower bound is not active");
        onlyUpper.UpperBound.ToLong().Should().Be(7, "the upper bound is active");
    }

    /// <summary>**稀疏域不活跃时降级成区间** / An inactive sparse domain degrades to an interval.</summary>
    /// <remarks>
    /// **值集不成立时，它连"哪些值合法"都不再是约束**，只剩边界还可谈。
    ///
    /// **When the value set is inactive it no longer constrains which values are legal**, leaving only the bounds to discuss.
    /// </remarks>
    [Fact]
    public void TheInactiveSparseDomain_ShouldDegradeToAnInterval() {
        var sparse = new IntegerDomain.Values([new Int64(2), new Int64(5), new Int64(9)]);

        IntegerDomain effective = EffectiveDomain.Compute(
            sparse, X, Sources(new DiagnosticSource.VariableLowerBound(X)));

        effective.Should().BeOfType<IntegerDomain.Interval>(
            "an inactive value set stops constraining the value set");
        effective.LowerBound.ToLong().Should().Be(2, "the active lower bound survives the degradation");
        effective.UpperBound.ToLong().Should().Be(long.MaxValue, "the inactive upper bound is released");
    }

    /// <summary>**稀疏域活跃时原样保留** / An active sparse domain is kept as-is.</summary>
    [Fact]
    public void TheActiveSparseDomain_ShouldBeKept() {
        var sparse = new IntegerDomain.Values([new Int64(2), new Int64(5), new Int64(9)]);

        IntegerDomain effective = EffectiveDomain.Compute(
            sparse, X, Sources(new DiagnosticSource.SparseDomain(X)));

        effective.Should().BeOfType<IntegerDomain.Values>();
        ((IntegerDomain.Values)effective).Items.Should().HaveCount(3);
    }

    /// <summary>**复制不共享底层数组** / The copy does not share the underlying array.</summary>
    /// <remarks>
    /// 共享实例会让下游的"只读"变成"随时可能被别人改"。
    ///
    /// Sharing the instance would turn a downstream "read-only" into "someone else may change it".
    /// </remarks>
    [Fact]
    public void TheCopy_ShouldNotShareTheArray() {
        var sparse = new IntegerDomain.Values([new Int64(2), new Int64(5)]);

        IntegerDomain copy = EffectiveDomain.Copy(sparse);

        copy.Should().BeOfType<IntegerDomain.Values>();
        ReferenceEquals(((IntegerDomain.Values)copy).Items, ((IntegerDomain.Values)sparse).Items)
            .Should().BeFalse("a shared array is not a copy");
    }
}
