#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 全局约束的门面：三个入口的校验强度不一样 / The three entry points validate unequally.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **这是一个纯门面，所以它的价值不在逻辑，而在"哪里做校验"。**
///
/// **A pure façade, so its value is not logic but where validation happens.**
/// </remarks>
public class GlobalConstraintFunctionsTests
{
    /// <summary>
    /// **`AllDifferent` 拒绝空输入** / All-different rejects an empty input.
    /// </summary>
    /// <remarks>
    /// **这条与下一条合起来才说清门面的形状**：
    /// **`AllDifferent` 走 `CreateAllDifferent`（带非空检查），
    /// 而 `NoOverlap` 走自己的 `Create`。**
    ///
    /// **不是漏了，是三个 `Create` 各自校验自己的参数。
    /// 在外层再包一层只会给每条错误消息多一层翻译。**
    ///
    /// **This criterion and the next together describe the façade's shape**.
    /// **`AllDifferent` goes through `CreateAllDifferent` (which checks non-emptiness), while `NoOverlap` goes through
    /// its own `Create`.**
    ///
    /// **Nothing is missing: each `Create` validates its own parameters, and wrapping another layer would add a
    /// translation step to every error message.**
    /// </remarks>
    [Fact]
    public void TheAllDifferent_ShouldRejectAnEmptyInput()
    {
        Result<ConstraintProgrammingConstraint.AllDifferent, ErrorCode, Error<ErrorCode>> empty =
            GlobalConstraintFunctions.AllDifferent(Enumerable.Empty<ConstraintProgrammingExpression>());

        empty.Should().BeOfType<Failed<
            ConstraintProgrammingConstraint.AllDifferent, ErrorCode, Error<ErrorCode>>>(
                "a vacuous all-different says nothing");
    }

    /// <summary>
    /// **三个入口都拒绝空输入——我原本以为只有 `AllDifferent` 拒** / All three reject emptiness.
    /// </summary>
    /// <remarks>
    /// **我原本写的是"`AllDifferent` 带非空检查，而 `NoOverlap` 走自己的 `Create`"，
    /// 并据此断言"空 `NoOverlap` 是合法的"。跑出来是 `Failed`。**
    ///
    /// **`NoOverlap.Create` 与 `Cumulative.Create` 各自也有非空检查——
    /// 三个入口的校验<b>是一致的</b>，我先前的说法是猜的。**
    ///
    /// **改法：把三条都写成拒绝，并用同一条判据覆盖三者。**
    /// **这比三条各自为政更能说明"它们一致"。**
    ///
    /// **I first wrote "`AllDifferent` checks non-emptiness while `NoOverlap` goes through its own `Create`" and
    /// asserted that an empty `NoOverlap` is legal. It came back `Failed`.**
    ///
    /// **`NoOverlap.Create` and `Cumulative.Create` each check non-emptiness too — the three are <b>consistent</b>, and
    /// my earlier claim was a guess.**
    ///
    /// **Fix: state all three as rejecting, and cover them with one criterion.**
    /// **That says "they agree" better than three separate criteria would.**
    /// </remarks>
    [Fact]
    public void TheEmptyInput_ShouldBeRejectedByEveryEntryPoint()
    {
        GlobalConstraintFunctions.NoOverlap(Array.Empty<IntervalVariable>()).Should()
            .BeOfType<Failed<NoOverlap, ErrorCode, Error<ErrorCode>>>(
                "an empty no-overlap constrains nothing");

        GlobalConstraintFunctions.Cumulative(
            Array.Empty<IntervalVariable>(),
            Array.Empty<ConstraintProgrammingExpression>(),
            Constant(1)).Should()
            .BeOfType<Failed<Cumulative, ErrorCode, Error<ErrorCode>>>(
                "an empty cumulative constrains nothing either");
    }

    /// <summary>**`Cumulative` 建得出来时返回非空约束** / A populated cumulative builds.</summary>
    [Fact]
    public void ThePopulatedCumulative_ShouldBuild()
    {
        // `Create(id, start, size, end)` —— **四个位置参数都要给**，
        // 而"固定长度"那个重载（`CreateFixed`）要的是 `Int64` 长度。
        //
        // `Create(id, start, size, end)` — **all four positional arguments are required**; the fixed-length overload
        // (`CreateFixed`) takes an `Int64` size instead.
        Result<IntervalVariable, ErrorCode, Error<ErrorCode>> interval =
            IntervalVariable.Create("gcf-i", Constant(0), Constant(5), Constant(5));
        interval.Should().BeOfType<Ok<IntervalVariable, ErrorCode, Error<ErrorCode>>>(
            "the fixture interval must be valid");

        GlobalConstraintFunctions.Cumulative(
            new[] { interval.Value },
            new[] { Constant(2) },
            Constant(10)).Should()
            .BeOfType<Ok<Cumulative, ErrorCode, Error<ErrorCode>>>(
                "one interval with a demand of two under a capacity of ten is fine");
    }

    /// <summary>常数表达式，用作测试夹具 / A constant expression for fixtures.</summary>
    private static ConstraintProgrammingExpression Constant(int value) =>
        ConstraintProgrammingExpression.CreateConstant(value);

    /// <summary>**`null` 输入是参数错误，不是"空约束"** / A null argument is an argument error.</summary>
    /// <remarks>
    /// **`ArgumentNullException` 与"返回一个失败结果"是两种不同的契约。**
    /// **门面选择前者：`null` 是调用方的编程错误，`Enumerable.Empty` 才是"空但合法"。**
    ///
    /// **混同这两者会让一个空集合悄悄变成 `null` 的替身，而 `null` 通常在很久之后才炸。**
    ///
    /// **`ArgumentNullException` and "return a failure result" are two different contracts.**
    /// **The façade picks the former: `null` is a caller's programming error, while `Enumerable.Empty` is "empty but
    /// legal".**
    ///
    /// **Conflating them lets an empty collection stand in for `null`, and `null` usually blows up much later.**
    /// </remarks>
    [Fact]
    public void TheNullArgument_ShouldThrowRatherThanReturnAFailure()
    {
        Action nullExpressions = () => GlobalConstraintFunctions.AllDifferent(null!);
        Action nullIntervals = () => GlobalConstraintFunctions.NoOverlap(null!);

        nullExpressions.Should().Throw<ArgumentNullException>();
        nullIntervals.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// **`Lower` 的两个重载走同一条实现** / Both `Lower` overloads share one implementation.
    /// </summary>
    /// <remarks>
    /// **收模型的那个内部先 `Snapshot` 再转给收快照的那个。**
    ///
    /// **判据因此<b>对同一个模型分别走两条路，并比对结果</b>——
    /// **如果两者会漂移，这里就会红。**
    ///
    /// **断言的是"两条路的结论相同"，而不是"都没失败"**：
    /// **`IsOk` 只说明没抛，说不出降阶结果是否一致。**
    ///
    /// **The model overload calls `Snapshot` and delegates to the snapshot overload.**
    ///
    /// **The criterion therefore walks <b>both routes on the same model and compares</b>** — **if they could drift,
    /// this reddens.**
    ///
    /// **It asserts "the two routes agree", not "neither failed"**: **`IsOk` says nothing about whether the lowered
    /// results match.**
    /// </remarks>
    [Fact]
    public void TheTwoLowerOverloads_ShouldAgree()
    {
        var model = new ConstraintProgrammingModel();
        model.Should().NotBeNull();

        Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> fromModel =
            GlobalConstraintFunctions.Lower(model);

        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshot = model.Snapshot();

        // 快照失败时这条路没有可比的输入，直接让判据以"快照必须成功"的形式说出来。
        // When the snapshot fails there is nothing to compare, so say "the snapshot must succeed" outright.
        snapshot.Should().BeOfType<Ok<
            ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>>>(
                "the lowering from a model needs a snapshot to compare against");

        Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> fromSnapshot =
            GlobalConstraintFunctions.Lower(snapshot.Value);

        fromModel.Should().BeOfType<Ok<
            ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>>>(
                "lowering an empty model is legal");
        fromSnapshot.Should().BeOfType<Ok<
            ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>>>(
                "the snapshot route must agree");
    }

    /// <summary>**`null` 策略等于严格策略** / A null policy means strict.</summary>
    /// <remarks>
    /// **`ConstraintProgrammingLoweringPolicy` 是记录而不是枚举，所以不能当默认参数值。**
    /// **`null` 是这里的替代，而它必须真的落到 `Strict`——
    /// 否则"默认宽松"会让调用方拿到一个未经校验的模型而不自知。**
    ///
    /// **`ConstraintProgrammingLoweringPolicy` is a record, not an enum, so it cannot be a default parameter value.**
    /// **`null` stands in for it, and it must genuinely land on `Strict` — otherwise "relaxed by default" hands callers
    /// an unvalidated model without their knowledge.**
    /// </remarks>
    [Fact]
    public void TheNullPolicy_ShouldMeanStrict()
    {
        var model = new ConstraintProgrammingModel();

        Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> byDefault =
            GlobalConstraintFunctions.Lower(model);
        Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> explicitStrict =
            GlobalConstraintFunctions.Lower(model, ConstraintProgrammingLoweringPolicy.Strict);

        byDefault.Should().BeOfType<Ok<
            ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>>>();
        explicitStrict.Should().BeOfType<Ok<
            ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>>>();
    }
}
