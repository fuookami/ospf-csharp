#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 分段 Big-M：可修复的修复，不可修复的拒绝 / Repair what can be repaired, reject what cannot.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `resolvePwlBigM`。
///
/// **同一个非正值，自动路径修复、显式路径拒绝。**
///
/// adds
///
/// **The same non-positive value is repaired on the automatic path and rejected on the explicit one.**
/// </remarks>
public class PiecewiseBigMTests
{
    /// <summary>
    /// **同一个非正值，两条路径结果相反** / The same non-positive value splits by path.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据。**
    ///
    /// **自动 M 是推导出来的估计，可以合理地是 0 或负数，
    /// 规范化成安全的正值不改变任何语义。**
    ///
    /// **显式 M 是调用方的声明，声明一个非正值是矛盾：
    /// "所有违反情形的偏差都 ≤ 0"意味着那些约束没有放松余量。**
    ///
    /// **这时规范化会悄悄改掉调用方的声明，而规范化后的模型可能可行、求解成功——
    /// 只是它<b>不是</b>调用方要求的东西。**
    ///
    /// **判据对 `0` 与 `−5` 各测两条路径，四种组合都在。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **An automatic M is a derived estimate that may legitimately be zero or negative, and normalizing it changes no
    /// semantics.**
    ///
    /// **An explicit M is the caller's declaration, and a non-positive one is a contradiction: "every violating deviation
    /// is ≤ 0" leaves no relaxation slack.**
    ///
    /// **Normalizing would quietly rewrite the declaration, and the normalized model may be feasible and solve fine —
    /// it just is not what was asked for.**
    ///
    /// **The criterion tests `0` and `−5` on both paths — all four combinations.**
    /// </remarks>
    [Fact]
    public void TheNonPositiveCandidate_ShouldSplitByPath()
    {
        foreach (double candidate in new[] { 0.0, -5.0 }) {
            PiecewiseBigM.Resolve(new Flt64(candidate), @explicit: false, Identity).Should()
                .BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>(
                    $"an automatic candidate of {candidate} is repairable");

            PiecewiseBigM.Resolve(new Flt64(candidate), @explicit: true, Identity).Should()
                .BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
                    $"an explicit candidate of {candidate} is the caller's claim and must not be rewritten");
        }
    }

    /// <summary>
    /// **规范化后的值必须是正的，而修复出来的正是下界** / The normalized value is positive, and it is the floor.
    /// </summary>
    /// <remarks>
    /// **自动路径把 0 与负数夹到 `Minimum`。判据断言"结果为正"，而不是断言某个具体数——
    /// `Minimum` 是一个可以调整的实现常量，判据不该把它写死。**
    ///
    /// **The automatic path clamps zero and negatives to `Minimum`. The criterion asserts "positive" rather than a
    /// concrete value — `Minimum` is an implementation constant that may be tuned.**
    /// </remarks>
    [Fact]
    public void TheAutomaticRepair_ShouldProduceAPositiveValue()
    {
        Result<Flt64, ErrorCode, Error<ErrorCode>> repaired =
            PiecewiseBigM.Resolve(new Flt64(0.0), @explicit: false, Identity);

        repaired.Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
        repaired.Value.CompareTo(Flt64.Zero).Should().BeGreaterThan(
            0, "the repaired value must be strictly positive");
    }

    /// <summary>
    /// **正的候选值两条路径都原样通过** / A positive candidate passes unchanged on both paths.
    /// </summary>
    /// <remarks>
    /// **如果不测这一条，一个"永远拒绝"的实现能通过前两条判据里的后半段，
    /// 一个"永远夹到 Minimum"的实现能通过前半段。**
    ///
    /// **Without this, an implementation that always rejects passes the second half of the previous criteria, and one
    /// that always clamps to `Minimum` passes the first half.**
    /// </remarks>
    [Fact]
    public void ThePositiveCandidate_ShouldPassUnchanged()
    {
        foreach (bool isExplicit in new[] { false, true }) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> resolved =
                PiecewiseBigM.Resolve(new Flt64(1000.0), isExplicit, Identity);

            resolved.Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
            resolved.Value.ToDouble().Should().Be(
                1000.0, "a positive candidate is already usable and must not be rewritten");
        }
    }

    /// <summary>
    /// **不可用的候选值两条路径都拒绝** / An unusable candidate is rejected on both paths.
    /// </summary>
    /// <remarks>
    /// **`explicit` 只影响"非正"这一条，不影响"不可用"。**
    /// **如果一个实现把 `explicit` 的判断写成了"只有显式才检查可用性"，
    /// 那么自动路径上的 `NaN` 会被夹到 `Minimum` 而通过——那是一个凭空造出来的 Big-M。**
    ///
    /// **`explicit` affects only the non-positive rule, not usability.**
    /// **An implementation that reads `explicit` as "only check usability when explicit" would clamp a `NaN` to
    /// `Minimum` on the automatic path — inventing a Big-M out of nothing.**
    /// </remarks>
    [Fact]
    public void TheUnusableCandidate_ShouldBeRejectedOnBothPaths()
    {
        foreach (bool isExplicit in new[] { false, true }) {
            PiecewiseBigM.Resolve(new Flt64(double.NaN), isExplicit, Identity).Should()
                .BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
            PiecewiseBigM.Resolve(
                new Flt64(double.PositiveInfinity), isExplicit, Identity).Should()
                .BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
        }
    }

    private static IIntoValue<Flt64> Identity => IIntoValue<Flt64>.Identity;
}
