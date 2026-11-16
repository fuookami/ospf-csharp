// 执行器目标项的判据 / Criteria for the executor objective term.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **"没配置提取器"与"该执行器不可定价"是两件事** / "No extractor configured" and "this executor cannot be priced" are different things.
/// </summary>
/// <remarks>
/// 代码写的是 `coefficient?.let {... }` 套 `it(e) ?: Flt64.infinity`，看起来只差一个 `?`。
/// 混为一谈会出两种相反的错：把"没配置"当成"禁止"会让**所有执行器都不可选**；
/// 把"不可定价"当成"权重一"会让本该禁止的选项变得很便宜。
///
/// The code writes `coefficient?.let {... }` around `it(e) ?: Flt64.infinity`, one `?` apart. Conflating them yields two opposite errors: "not configured" as "forbid"
/// makes **every executor unusable**; "unpriced" as "weight one" makes an option that should be forbidden very cheap.
/// </remarks>
public class ExecutorObjectiveTermTests {
    /// **没有提取器时不加目标** / No objective is added without an extractor.
    [Fact]
    public void ShouldAdd_ShouldBeFalseWithoutAnExtractor() =>
        ExecutorObjectiveTerm.ShouldAdd(hasExtractor: false, globalGate: true)
            .Should().BeFalse("a caller who configured no penalty must not get one");

    /// **全局开关关闭时不加目标** / No objective is added with the global switch off.
    [Fact]
    public void ShouldAdd_ShouldBeFalseWithTheGlobalGateClosed() =>
        ExecutorObjectiveTerm.ShouldAdd(hasExtractor: true, globalGate: false).Should().BeFalse();

    /// **两者都成立时才加目标** / The objective is added only when both hold.
    [Fact]
    public void ShouldAdd_ShouldBeTrueWhenBothHold() =>
        ExecutorObjectiveTerm.ShouldAdd(hasExtractor: true, globalGate: true).Should().BeTrue();

    /// **提取到数值时原样返回** / An extracted number is returned unchanged.
    [Fact]
    public void Weight_ShouldReturnTheExtractedValue() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> weight = ExecutorObjectiveTerm.Weight(new Flt64(3.5));

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)weight).Value.ToDouble().Should().BeApproximately(3.5, 1e-12);
    }

    /// <summary>
    /// **提取到空时失败，不是权重一** / An empty extraction fails rather than becoming weight one.
    /// </summary>
    /// <remarks>
    /// 是 `infinity`——该执行器的此项不可定价，即禁止。静默取 `1` 会把"禁止"变成"每次罚 1"，
    /// 于是本该被排除的执行器方案被轻松选中。
    ///
    /// The code yields `infinity` — the term cannot be priced for that executor, i.e. it is forbidden. Defaulting to `1` turns "forbid" into "penalty 1 each", so an executor
    /// assignment that should be excluded is chosen cheaply.
    /// </remarks>
    [Fact]
    public void Weight_ShouldFailWhenTheExtractionIsEmpty() {
        ExecutorObjectiveTerm.Weight(null)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// **零权重是合法的数值** / A zero weight is a legal value.
    [Fact]
    public void Weight_ShouldAcceptZero() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> weight = ExecutorObjectiveTerm.Weight(Flt64.Zero);

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)weight).Value.ToDouble().Should().BeApproximately(0.0, 1e-12,
            "zero means this term is not penalised, which is a configuration, not a missing value");
    }
}
