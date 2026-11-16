#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Success = Fuookami.Ospf.Utils.Functional.Success;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 分析会话 / The analysis session.
/// </summary>
/// <remarks>
/// **实现了 `CriticalConstraintAnalysisSession`**，并更正先前记错的账。
///
/// 记的是"持有活的求解器实例，要真实后端"。**那是 grep 代理读错了**：唯一带 `ConstraintProgrammingSolver`
/// 的参数**只用来读 `Descriptor`**。所以它**完全不需要后端**——判据里的第一条就是证明这一点。
///
/// **This implements `CriticalConstraintAnalysisSession` and corrects the earlier ledger**, which recorded "holds a live solver instance, needs a real backend".
/// **That was the grep proxy misleading the reading**: the one parameter typed `ConstraintProgrammingSolver` **is used only to read its `Descriptor`**. It therefore **needs no
/// backend at all**, and the first criterion here is the proof.
/// </remarks>
public class CriticalConstraintAnalysisSessionTests {
    private static (ConstraintProgrammingModelSnapshot Snapshot, VariableId Id) BuildModel(Int64 lower, Int64 upper) {
        using var model = new ConstraintProgrammingModel(name: "session", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(lower, upper).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull("the variable registered");
        model.RegisterExpression("x", expression).Value.Should().NotBeNull("the expression registered");
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, lower).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull("the constraint registered");
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should()
            .NotBeNull("the objective registered");

        return (model.Snapshot().Value, id);
    }

    /// <summary>**没有求解器也能建会话** / A session builds without a solver.</summary>
    /// <remarks>
    /// 这是对 那条账的直接反驳：**只给快照，什么都不缺。**
    ///
    /// This directly refutes the earlier entry: **given only a snapshot, nothing is missing.**
    /// </remarks>
    [Fact]
    public void TheSession_ShouldNeedNoSolver() {
        (ConstraintProgrammingModelSnapshot snapshot, _) = BuildModel(Int64.Zero, new Int64(3));

        using var session = CriticalConstraintAnalysisSession.FromSnapshot(snapshot);
        session.SolverDescriptor.Should().BeNull("no solver was supplied and none is required");
        session.Validation.Value.Should()
            .BeOfType<Fuookami.Ospf.Utils.Functional.Success>("a well-formed snapshot validates clean");
        session.Snapshot.Should().BeSameAs(snapshot, "the snapshot alias points at the same immutable baseline");
        session.IsClosed.Should().BeFalse();
    }

    /// <summary>**给了目标值就必须同时给解，而且两者要一致** / A supplied objective needs a solution that agrees with it.</summary>
    /// <remarks>
    /// **只给一个数字无法校验，而"未校验的目标值"会被下游当成事实。**
    ///
    /// **A bare number cannot be checked, and an unverified objective is treated as fact downstream.**
    /// </remarks>
    [Fact]
    public void TheObjective_ShouldHaveToAgreeWithTheSolution() {
        (ConstraintProgrammingModelSnapshot snapshot, VariableId id) = BuildModel(Int64.Zero, new Int64(3));
        var values = new Dictionary<VariableId, Int64> { [id] = new Int64(2) };

        // 目标值与解一致：通过。
        var agreed = CriticalConstraintAnalysisSession.FromSnapshot(
            snapshot,
            new ConstraintProgrammingSolution(values),
            new Fuookami.Ospf.Math.Algebra.Number.Flt64(2.0));
        // **说清结果是什么，而不是只说"没失败"**：成功时携带的是 `Success` 值。
        // **Say what the result is, not just that nothing failed**: success carries the `Success` value.
        agreed.Validation.Value.Should().BeOfType<Success>(
            "the objective matches what the solution evaluates to");

        // 目标值与解不一致：拒。
        var disagreed = CriticalConstraintAnalysisSession.FromSnapshot(
            snapshot,
            new ConstraintProgrammingSolution(values),
            new Fuookami.Ospf.Math.Algebra.Number.Flt64(3.0));
        disagreed.Validation.IsOk.Should().BeFalse(
            "an objective that disagrees with the solution would be believed downstream");

        // 只给目标值不给解：拒。
        var bare = CriticalConstraintAnalysisSession.FromSnapshot(
            snapshot, baselineSolution: null,
            baselineObjectiveValue: new Fuookami.Ospf.Math.Algebra.Number.Flt64(2.0));
        bare.Validation.IsOk.Should().BeFalse("a bare objective cannot be checked at all");

        // **没有目标值时不需要解**，否则上面三条只是"一律拒绝"。
        // **With no objective, no solution is needed**, or the three above only prove "everything is refused".
        CriticalConstraintAnalysisSession.FromSnapshot(snapshot, baselineSolution: null)
            .Validation.Value.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Success>();
    }

    private static string Message(Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> validation) =>
        ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)validation).Error.Message;
    /// <summary>**解必须完整、已知、在值域内、且满足约束** / The solution must be complete, known, in-domain and satisfying.</summary>
    [Fact]
    public void TheSolution_ShouldBeCheckedAgainstTheSnapshot() {
        (ConstraintProgrammingModelSnapshot snapshot, VariableId id) = BuildModel(Int64.Zero, new Int64(3));

        // 未知变量。
        var unknown = CriticalConstraintAnalysisSession.FromSnapshot(snapshot, new ConstraintProgrammingSolution(
            new Dictionary<VariableId, Int64> {
                [id] = new Int64(1),
                [new VariableId("source:stranger")] = new Int64(1),
            }));
        unknown.Validation.IsOk.Should().BeFalse("a value for a variable this model never declared means another model's solution");
        // **不只是"失败了"，还要说清是哪一种失败。** 三种失败指向三个不同的修法：
        // 换错了解、补缺的赋值、改越界的值。**消息混用会把人送去修错的东西。**
        //
        // **Not just "it failed" but which failure.** Three failures point at three different fixes:
        // swap the wrong solution, fill the missing assignment, fix the out-of-range value. **A mixed-up message sends the reader to fix the wrong thing.**
        Message(unknown.Validation).Should().Contain("未知变量", "an extra value is not a missing one");
        Message(unknown.Validation).Should().Contain("source:stranger", "the message has to name the culprit");

        // 缺少变量。
        var missing = CriticalConstraintAnalysisSession.FromSnapshot(
            snapshot, new ConstraintProgrammingSolution(new Dictionary<VariableId, Int64>()));
        missing.Validation.IsOk.Should().BeFalse("an incomplete assignment cannot be evaluated");
        Message(missing.Validation).Should().Contain("缺少变量", "a missing assignment is not an unknown variable");
        Message(missing.Validation).Should().NotContain("未知变量", "nothing in this solution is unknown; something is absent");

        // 超出值域。
        var outside = CriticalConstraintAnalysisSession.FromSnapshot(snapshot, new ConstraintProgrammingSolution(
            new Dictionary<VariableId, Int64> { [id] = new Int64(9) }));
        outside.Validation.IsOk.Should().BeFalse("a value outside the domain is not a solution");
        Message(outside.Validation).Should().Contain("值域", "an out-of-range value is not a missing one");
        Message(outside.Validation).Should().NotContain("缺少变量", "the variable is there; its value is wrong");

        // 完整且合法。
        var good = CriticalConstraintAnalysisSession.FromSnapshot(snapshot, new ConstraintProgrammingSolution(
            new Dictionary<VariableId, Int64> { [id] = new Int64(1) }));
        good.Validation.Value.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Success>();
    }

    /// <summary>**关闭后写入必须失败，读到的只能是未命中** / A write after close fails, and reads miss.</summary>
    /// <remarks>
    /// **`_closed` 要在持锁后再查一次**：只在锁外查，会在"查到未关闭"和"拿到锁"之间被 `Dispose` 插进来，
    /// 于是**一个已关闭的 session 又多出一条缓存**。
    ///
    /// **`_closed` has to be checked again under the lock**: checking only outside lets `Dispose` slip in between the check and the lock, so **a closed session gains one more
    /// cache entry.**
    /// </remarks>
    [Fact]
    public void TheClosedSession_ShouldRefuseWrites() {
        (ConstraintProgrammingModelSnapshot snapshot, _) = BuildModel(Int64.Zero, new Int64(3));
        var session = CriticalConstraintAnalysisSession.FromSnapshot(snapshot);
        // **键现在是强类型的。** 裸 `object` 曾经让"两个分析器共用一条缓存"成为可能，
        // **而编译器不会拦，运行时也不会报。**
        // **The key is typed now.** A bare `object` used to make "two analyzers sharing one entry" possible, **with neither the compiler nor the runtime objecting.**
        var key = AnalysisCacheKey.ForConflict(
            new object(), ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:cache"), new Flt64(1.0)),
            new ConflictAnalysisOptions());

        session.Cache(AnalysisCacheKind.Activity, key, "first").Value
            .Should().BeOfType<Fuookami.Ospf.Utils.Functional.Success>();
        session.Cached<string>(AnalysisCacheKind.Activity, key).Should().Be("first");
        session.CacheSizes[AnalysisCacheKind.Activity].Should().Be(1);

        session.Close();
        session.IsClosed.Should().BeTrue();
        session.CacheSizes.Values.Should().OnlyContain(size => size == 0,
            "closing releases every bucket");

        session.Cached<string>(AnalysisCacheKind.Activity, key).Should().BeNull(
            "a closed session has no cache to read");
        session.Cache(AnalysisCacheKind.Activity, key, "second").IsOk.Should().BeFalse(
            "a closed session must not accept new entries");
        session.CacheSizes[AnalysisCacheKind.Activity].Should().Be(0,
            "and the refused write must not have landed");

        session.Close();
        session.IsClosed.Should().BeTrue("closing twice is a no-op, not an error");
    }

    /// <summary>**缓存按桶隔离，清空只影响该桶** / Buckets are separate, and clearing one leaves the rest.</summary>
    [Fact]
    public void TheCacheBuckets_ShouldBeSeparate() {
        (ConstraintProgrammingModelSnapshot snapshot, _) = BuildModel(Int64.Zero, new Int64(3));
        using var session = CriticalConstraintAnalysisSession.FromSnapshot(snapshot);

        // **同一个键在两个桶里是两条**——而键本身现在必须带齐分析器、目标、选项。
        // **One key in two buckets is two entries** — and the key itself now has to carry analyzer, target and options.
        var key = AnalysisCacheKey.ForConflict(
            new object(),
            ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:cache"), new Flt64(1.0)),
            new ConflictAnalysisOptions());
        session.Cache(AnalysisCacheKind.Activity, key, "activity");
        session.Cache(AnalysisCacheKind.Conflict, key, "conflict");

        session.Cached<string>(AnalysisCacheKind.Activity, key).Should().Be("activity");
        session.Cached<string>(AnalysisCacheKind.Conflict, key).Should().Be("conflict",
            "the same key in two buckets is two entries");
        session.Cached<string>(AnalysisCacheKind.Target, key).Should().BeNull(
            "the third bucket was never written");

        session.ClearCaches();
        session.CacheSizes.Values.Should().OnlyContain(size => size == 0);
        session.IsClosed.Should().BeFalse("clearing caches does not close the session");

        // **关闭后仍可查缓存大小**：诊断不该因为关闭而失效。
        // **Cache sizes stay readable after close**: diagnostics should not stop working just because it closed.
        session.Close();
        session.CacheSizes.Should().HaveCount(Enum.GetValues<AnalysisCacheKind>().Length);
    }
}
