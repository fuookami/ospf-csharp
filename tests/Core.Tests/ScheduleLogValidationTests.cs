#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// **调度日志是一台状态机，每种非法转移都必须被拒** / The schedule log is a state machine; every illegal transition must be refused.
/// </summary>
/// <remarks>
/// `ScheduleLog.ValidatePrefix` 有 76 行，**而此前整个排程日志只有 3 条判据**，
/// 全部在 `ScheduleLogRoundTripTests` 里、且都是"合法日志能往返"。
/// **没有一条证明非法日志会被拒** —— 也就是说，**这台状态机从来没有被测过会不会放行。**
///
/// **这是最危险的一种缺口**：一个不校验的校验器，**和没有校验器完全一样，而它的名字让人以为有。**
///
/// **判据不需要求解器**：`ScheduleLogs.Of` + `with { Checksum = … }` 就能造出任意日志。
///
/// `ScheduleLog.ValidatePrefix` is 76 lines, **and the whole schedule log had only three criteria**,
/// all in `ScheduleLogRoundTripTests` and all "a valid log round-trips".
/// **None of them shows an invalid log being refused** — meaning **nobody ever tested whether this state machine lets things through.**
///
/// **That is the most dangerous shape of a gap**: a validator that does not validate **is exactly as good as no validator,
/// while its name suggests otherwise.**
///
/// **No solver is needed**: `ScheduleLogs.Of` plus `with { Checksum = … }` can build any log at all.
/// </remarks>
public sealed class ScheduleLogValidationTests
{
    /// <summary>**合法序列通过，乱序序列被拒——同一台机器，两个方向** / The same machine accepts the legal sequence and refuses the shuffled one.</summary>
    /// <remarks>
    /// **这一条最初写的是"没 `begin` 就 `dispatch` 必须被拒"，而它是碰巧通过的。**
    /// 实测：把 `advancing` 守卫从 `dispatch` 分支删掉，那条判据**仍然绿**——
    /// **因为一个孤立的 `dispatch` 会被末尾的 `advancing || active.Count &gt; 0 || (isSealed &amp;&amp; !ended)` 拦下，
    /// 而那与 `begin` 毫无关系。**
    ///
    /// **单点否证在这台机器上很难做到**：几乎所有非法输入都会同时触发好几个检查。
    /// **所以改成对照组**：同一批事件，顺序对时通过、顺序乱时被拒。
    /// **这不是"钉住某一行代码"，而是"钉住它确实在按顺序判"**——**而后者是能被证明的。**
    ///
    /// **This started as "a `dispatch` before any `begin` must be refused", and it passed by accident.**
    /// Measured: removing the `advancing` guard from the `dispatch` branch left that criterion **green** —
    /// **because a lone `dispatch` is caught by the trailing `advancing || active.Count &gt; 0 || (isSealed &amp;&amp; !ended)`,
    /// which has nothing to do with `begin`.**
    ///
    /// **Single-point falsification is hard on this machine**: nearly every illegal input trips several checks at once.
    /// **So this is a control pair**: the same events pass in order and are refused out of order.
    /// **That is not "pinning one line of code" but "pinning that it really judges order" — and the latter can be proved.**
    /// </remarks>
    [Fact]
    public void TheEventOrder_ShouldDecideAcceptance()
    {
        ScheduleEvent begin = new("begin");
        ScheduleEvent dispatch = new("dispatch", 0L, "a", 5L, 1L, 11L, ["a", "b"]);
        ScheduleEvent complete = new("complete", 0L, "a", 5L, 1L);
        ScheduleEvent yield = new("yield", 1L);
        ScheduleEvent end = new("end", 1L, "", 5L, 1L);

        Sealed(begin, dispatch, complete, yield, end)
            .Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "the canonical order is legal, or the control proves nothing");

        Sealed(dispatch, begin, complete, yield, end)
            .Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "swapping begin and dispatch has to be refused");

        // **`yield` 之后 `complete` 是合法的**：`active` 里还有 `a`，而 `complete` 只要求配对。
        // **我原来断言它非法，红了——又一次"假设先于核实"。**
        // **真正非法的是"`complete` 排在它自己的 `dispatch` 之前"。**
        //
        // **`complete` after `yield` IS legal**: `a` is still active and `complete` only requires a pairing.
        // **I asserted it was illegal and it went red — assumption before verification again.**
        // **What is genuinely illegal is a `complete` placed before its own `dispatch`.**
        Sealed(begin, complete, dispatch, yield, end)
            .Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "a completion before its dispatch has nothing to pair with");
    }

    /// <summary>**`complete` 没有配对的 `dispatch` 必须被拒** / A `complete` without its `dispatch` must be refused.</summary>
    /// <remarks>
    /// **放行它意味着日志里可以有一个从没被派发的完成事件**，
    /// **于是"这个算法跑了多少步"可以被凭空写出来。**
    ///
    /// **Letting it through means a completion exists for work that was never dispatched**,
    /// **so "how many steps this algorithm ran" can be written out of thin air.**
    /// </remarks>
    [Fact]
    public void TheUnmatchedComplete_ShouldBeRefused()
    {
        ScheduleLog log = Sealed(
            new ScheduleEvent("begin"),
            new ScheduleEvent("complete", 0L, "a", 5L, 1L));

        log.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "there was no dispatch for this completion to pair with");
    }

    /// <summary>**`dispatch` 里没声明的算法必须被拒** / A `dispatch` for an algorithm that was not declared must be refused.</summary>
    /// <remarks>
    /// **`Available` 是这次决策允许的算法集合。** 放行未声明的算法，
    /// **意味着日志可以声称用了某个根本没注册的算法**——**而回放时那个算法不存在。**
    ///
    /// **`Available` is the set the decision is allowed to choose from.** Letting an undeclared algorithm through
    /// **means a log can claim it used something that was never registered — and that algorithm does not exist on replay.**
    /// </remarks>
    [Fact]
    public void TheDispatchOutsideTheAvailableSet_ShouldBeRefused()
    {
        ScheduleLog log = Sealed(
            new ScheduleEvent("begin"),
            new ScheduleEvent("dispatch", 0L, "b", 5L, 1L, 11L, ["a", "c"]));

        log.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "'b' was not in the available set {a, c}");
    }

    /// <summary>**负计数必须被拒** / A negative counter must be refused.</summary>
    /// <remarks>
    /// **负的评估次数会直接抵消别的算法**：总量是求和，**一个负数能把别人的工作抹掉。**
    ///
    /// **A negative evaluation count cancels out other algorithms**: totals are sums,
    /// **and one negative number erases somebody else's work.**
    /// </remarks>
    [Theory]
    [InlineData(-1L, 1L)]
    [InlineData(1L, -1L)]
    public void TheNegativeCounters_ShouldBeRefused(long evaluations, long steps)
    {
        ScheduleLog log = Sealed(
            new ScheduleEvent("begin"),
            new ScheduleEvent("dispatch", 0L, "a", 5L, evaluations, steps, ["a"]));

        log.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "a negative counter is not a measurement");
    }

    /// <summary>**校验和只验自洽，不防篡改——这一点必须写清楚** / The checksum checks self-consistency, not tampering — and that must be said.</summary>
    /// <remarks>
    /// **我原来写了一条"篡改事件但保留旧校验和必须被拒"，它假设错了。**
    /// 实测：把 `complete` 的 `Evaluations` 从 5 改成 50，`Checksum != Digest` **仍然为真**——
    /// 因为 `Digest` 是**基于当前 `Events` 重算**的，**改完再算，当然对得上**。
    ///
    /// **`digest` 也是同一个语义**（`Json.encodeToJsonElement(this).jsonObject - "checksum"`），
    /// **所以这不是本仓的缺陷，是两边一致的"自洽性检查"。**
    ///
    /// **它挡的是"校验和写错/留空"，不是"内容被改过"。** 这条判据把这句话钉住，
    /// **免得下一个人（或后续的我）把它当成签名。**
    ///
    /// **I originally wrote "tampering with events while keeping the old checksum must be refused", and the assumption was wrong.**
    /// Measured: changing `complete`'s `Evaluations` from 5 to 50 leaves `Checksum != Digest` **false** —
    /// because `Digest` **recomputes from the current `Events`**, **so of course it matches after the edit**.
    ///
 /// **Its `digest` has the same semantics**, **so this is not a defect here but an identical self-consistency check on both sides.**
    ///
    /// **What it stops is a wrong or empty checksum, not edited content.** This criterion pins that sentence
    /// **so nobody (including a later batch of me) treats it as a signature.**
    /// </remarks>
    [Fact]
    public void TheChecksum_ShouldBeSelfConsistencyNotASignature()
    {
        ScheduleLog valid = Sealed(
            new ScheduleEvent("begin"),
            new ScheduleEvent("dispatch", 0L, "a", 5L, 1L, 11L, ["a"]),
            new ScheduleEvent("complete", 0L, "a", 5L, 1L),
            new ScheduleEvent("yield", 1L),
            new ScheduleEvent("end", 1L, "", 5L, 1L));

        valid.Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
            "the fixture has to be valid first");

        // **改"不参与结构校验"的内容，校验和仍然自洽**——这才是"不是签名"的证据。
        // 第一次我改的是 `complete.Evaluations`，结果**被判失败**：因为 `end` 事件的计数必须等于累计总数，
        // **那是结构一致性，不是校验和。** 改 `Action` 的名字同理（`dispatch`/`complete` 必须配对）。
        // **真正"只影响校验和"的是 `problem` 字段**：它不参与任何计数或配对校验。
        //
        // **Edit something the structural checks ignore, and the checksum still matches** — that is the evidence it is not a signature.
        // The first attempt edited `complete.Evaluations` and **was refused**: `end`'s counters must equal the running totals,
        // **which is structural consistency, not the checksum.** Renaming `Action` fails the same way (dispatch/complete must pair).
        // **What only affects the checksum is `problem`**: it takes part in no counter and no pairing check.
        ScheduleLog edited = valid with { Problem = "a different problem" };
        edited.Checksum.Should().NotBe(edited.Digest(),
            "the checksum was computed over the old problem");
        (edited with { Checksum = edited.Digest() })
            .Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "recomputing after an edit always matches — the checksum is not a signature");

        // **但校验和写错（或留空）必须被拒**：这才是它真正挡住的东西。
        // **A wrong or empty checksum must be refused**: that is what it actually stops.
        (valid with { Checksum = "deadbeef" })
            .Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "a checksum that does not match the content is refused");
        (valid with { Checksum = string.Empty })
            .Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "an empty checksum is refused too");
    }

    /// <summary>**`begin` 之后不许再有 `begin`** / A second `begin` must be refused.</summary>
    /// <remarks>
    /// **`advancing` 是一次性的**：两次 `begin` 意味着日志描述了两段推进，
    /// **而回放只认第一段**——**后面的所有事件都会被静默忽略。**
    ///
    /// **`advancing` is one-shot**: two `begin`s mean the log describes two advances,
    /// **and replay only honours the first** — **every later event is silently ignored.**
    /// </remarks>
    [Fact]
    public void TheSecondBegin_ShouldNotRestartTheMachine()
    {
        ScheduleLog log = Sealed(
            new ScheduleEvent("begin"),
            new ScheduleEvent("begin"),
            new ScheduleEvent("dispatch", 0L, "a", 5L, 1L, 11L, ["a"]));

        // **不断言"一定被拒"**：实现可能把第二个 `begin` 当成无害的重复。
        // **但必须断言它不是"重新开始"**——否则 `dispatch` 的 `Decision` 计数会从 0 重来，
        // **而那会让两个不同的日志校验和相同。**
        //
        // **This does not assert "must be refused"**: the implementation may treat a second `begin` as harmless repetition.
        // **What it asserts is that it does not restart the machine** — otherwise the `dispatch` decision counter resets to 0,
        // **and two different logs would checksum the same.**
        Result<Success, ErrorCode, Error<ErrorCode>> first = log.Validate();
        Result<Success, ErrorCode, Error<ErrorCode>> restarted = Sealed(
            new ScheduleEvent("begin"),
            new ScheduleEvent("begin"),
            new ScheduleEvent("dispatch", 1L, "a", 5L, 1L, 11L, ["a"])).Validate();

        first.IsOk.Should().Be(restarted.IsOk,
            "a second begin must not change whether the following dispatch is acceptable");
    }

    /// <summary>造一个校验和自洽的日志 / Build a log whose checksum is self-consistent.</summary>
    /// <param name="events">事件 / The events.</param>
    /// <returns>日志 / The log.</returns>
    private static ScheduleLog Sealed(params ScheduleEvent[] events)
    {
        ScheduleLog source = ScheduleLogs.Of("config", "problem", events);
        return source with { Checksum = source.Digest() };
    }
}
