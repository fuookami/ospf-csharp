#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 分析缓存的键 / The analysis cache key.
/// </summary>
/// <remarks>
/// 基线的键是三段：**分析器实例 + 目标 + 选项**。
/// **第三段最容易漏，而它漏了以后不会报错——只会让两个后端共用一个结论。**
///
/// The baseline key is three segments: **analyzer instance, target, options**. **The first is the easiest to omit, and omitting it raises nothing — it just lets two backends share one conclusion.**
/// </remarks>
public class AnalysisCacheKeyTests {
    /// <summary>一个只用来表示"某个分析器"的占位对象 / A stand-in that only means "some analyzer".</summary>
    private sealed class Analyzer { }

    private static ObjectiveTarget Target(string id = "objective:main") =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId(id), new Flt64(1.0));

    /// <summary>**不同分析器不能共用一个键** / Two analyzers never share a key.</summary>
    /// <remarks>
    /// **会话缓存是以 `object` 为键的字典，它只比相等性，不看键的形状。**
    /// 一个不含分析器身份的键，**在两台后端上看起来完全一样**——
    /// **于是第二个分析器会拿到第一个后端算出的结论，而且一路顺利。**
    ///
    /// **The session cache is an `object`-keyed dictionary that only compares equality.** A key without the analyzer identity **looks identical across two backends** — **so the second analyzer receives the first
    /// backend's conclusion, and nothing complains.**
    /// </remarks>
    [Fact]
    public void TheTwoAnalyzers_ShouldNotShareAKey() {
        var first = new Analyzer();
        var second = new Analyzer();
        var options = new ConflictAnalysisOptions();

        var a = AnalysisCacheKey.ForConflict(first, Target(), options);
        var b = AnalysisCacheKey.ForConflict(second, Target(), options);

        a.Should().NotBe(b, "two different backends must never read each other's cached verdict");
    }

    /// <summary>**同一个分析器、同样的输入给出同一个键** / Same analyzer and inputs give the same key.</summary>
    /// <remarks>
    /// **反向对照**：只测上一条会让"每个键都不同"通过，
    /// **而那等于缓存永远不命中——比冲突更隐蔽的性能坑。**
    ///
    /// **The reverse control**: testing only the case above lets "every key is distinct" pass, **which means the cache never hits — a quieter performance hole than a collision.**
    /// </remarks>
    [Fact]
    public void TheSameInputs_ShouldHitTheSameKey() {
        var analyzer = new Analyzer();
        var options = new ConflictAnalysisOptions();

        AnalysisCacheKey.ForConflict(analyzer, Target(), options)
            .Should().Be(AnalysisCacheKey.ForConflict(analyzer, Target(), options),
                "a cache that never hits is a cache that only costs");
    }

    /// <summary>**换目标就换键** / A different target is a different key.</summary>
    [Fact]
    public void TheDifferentTarget_ShouldBeADifferentKey() {
        var analyzer = new Analyzer();
        var options = new ConflictAnalysisOptions();

        AnalysisCacheKey.ForConflict(analyzer, Target("objective:a"), options)
            .Should().NotBe(AnalysisCacheKey.ForConflict(analyzer, Target("objective:b"), options),
                "two objectives are two questions");
    }

    /// <summary>**换选项就换键** / Different options are a different key.</summary>
    /// <remarks>
    /// **选项决定求解行为和预算**，所以旧选项下的结论不能拿来回答新选项。
    ///
    /// **Options decide solver behaviour and budget**, so a verdict reached under the old ones cannot answer the new ones.
    /// </remarks>
    [Fact]
    public void TheDifferentOptions_ShouldBeADifferentKey() {
        var analyzer = new Analyzer();

        AnalysisCacheKey.ForConflict(analyzer, Target(), new ConflictAnalysisOptions(MaxDeletionChecks: 4))
            .Should().NotBe(
                AnalysisCacheKey.ForConflict(analyzer, Target(), new ConflictAnalysisOptions(MaxDeletionChecks: 8)),
                "a four-check verdict is not an eight-check verdict");
    }

    /// <summary>**两种键不会互相命中** / The two key shapes never collide.</summary>
    /// <remarks>
    /// 目标键装的是求解选项，冲突键装的是冲突选项，**两个类型不同的选项对象不相等**——
    /// 所以即使分析器与目标相同，两种键也是分开的。
    ///
    /// The target key holds solve options while the conflict key holds conflict options, **and two objects of different types are not equal** — so even with the same analyzer and target, the two keys stay apart.
    /// </remarks>
    [Fact]
    public void TheTwoKeyFactories_ShouldNotCollide() {
        var analyzer = new Analyzer();

        AnalysisCacheKey.ForTarget(analyzer, Target(), new ConstraintProgrammingSolveOptions())
            .Should().NotBe(AnalysisCacheKey.ForConflict(analyzer, Target(), new ConflictAnalysisOptions()),
                "a feasibility verdict and a conflict explanation answer different questions");
    }
}
