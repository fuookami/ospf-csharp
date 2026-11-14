// 预算分配与调度日志测试 / Tests for budget allocation and schedule logging.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 预算分配 / Budget allocation.
/// </summary>
public sealed class BudgetAllocatorTests {
    /// <summary>**固定分配取配置与剩余的较小者** / Fixed allocation takes the smaller of configured and remaining.</summary>
    [Fact]
    public void Fixed_ShouldTakeSmaller() {
        FixedBudgetAllocator.Instance.Allocate("a", 10, 100).Should().Be(10);
        FixedBudgetAllocator.Instance.Allocate("a", 100, 10).Should().Be(10);
        FixedBudgetAllocator.Instance.Allocate("a", 10, 10).Should().Be(10);
    }

    /// <summary>**分配结果永不超过剩余额度** / The allocation never exceeds the remaining budget.</summary>
    /// <remarks>
    /// 返回超过剩余的额度会让总消耗突破全局上限，而每片段的数字看起来都很正常。
    /// Returning more than the remaining budget breaks the global cap while every slice's number still looks normal.
    /// </remarks>
    [Fact]
    public void Fixed_ShouldNeverExceedRemaining() {
        for (long remaining = 0; remaining < 20; remaining++) {
            long allocated = FixedBudgetAllocator.Instance.Allocate("a", 100, remaining);
            allocated.Should().BeLessThanOrEqualTo(remaining);
        }
    }
}

/// <summary>
/// 调度日志 / Schedule logging.
/// </summary>
public sealed class ScheduleLogTests {
    /// <summary>**合法日志通过校验** / A legal log validates.</summary>
    [Fact]
    public void ScheduleLog_ShouldValidateLegalSequence() {
        Result<Success, ErrorCode, Error<ErrorCode>> result = BuildLog().Validate();

        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**顺序错乱的日志被拒绝** / A misordered log is rejected.</summary>
    /// <remarks>
    /// 校验是逐事件的自动机，任何一条单独检查都通不过一个真正非法的日志。
    /// Validation is a per-event state machine; no single check rejects a genuinely illegal log.
    /// </remarks>
    [Fact]
    public void ScheduleLog_ShouldRejectMisorderedEvents() {
        // complete 出现在 dispatch 之前。
        // A complete appears before its dispatch.
        ScheduleEvent[] events = {
            Event("begin"),
            Event("complete", decision: 0, action: "a", evaluations: 1, steps: 1),
        };

        Seal(events).Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**校验和必须与内容自洽** / The checksum must be self-consistent.</summary>
    /// <remarks>
    /// 把 `checksum` 纳入摘要会让它无法自洽（写入什么值都算不对），而"校验总是失败"会被误读成"日志损坏"。
    /// Including `checksum` in the digest makes it unsatisfiable, and "validation always fails" would be misread as corruption.
    /// </remarks>
    [Fact]
    public void ScheduleLog_ShouldRejectTamperedChecksum() {
        // **`begin` 之后必须 `yield` 才能 `end`**：`end` 分支要求 `!advancing`，而 `begin` 把 `advancing` 置真。
        // **A `yield` is required between `begin` and `end`**: the `end` branch requires `!advancing` while `begin` sets it true.
        ScheduleLog log = Seal(new[] { Event("begin"), Event("yield"), Event("end") });
        log.Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        (log with { Checksum = "deadbeef" }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**篡改事件内容会破坏校验和** / Tampering with an event breaks the checksum.</summary>
    [Fact]
    public void ScheduleLog_ShouldRejectTamperedEvents() {
        ScheduleLog log = Seal(new[] { Event("begin"), Event("yield"), Event("end") });

        (log with { Events = new[] { Event("begin"), Event("yield"), Event("end", evaluations: 5) } }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**前缀校验不要求已结束** / Prefix validation does not require an end event.</summary>
    /// <remarks>
    /// 检查点保存的是前缀，此时 `end` 事件尚未出现；要求 `ended` 为真会让每次保存都失败。
    /// A checkpoint stores a prefix where `end` has not appeared; requiring `ended` would make every save fail.
    /// </remarks>
    [Fact]
    public void ScheduleLog_ShouldAllowUnsealedPrefix() {
        // **前缀必须在静默点结束**：`begin` 之后 `advancing` 为真，单独的 `begin` 连前缀都不合法。
        // **The prefix must end at a quiescent point**: `advancing` is true after `begin`, so `begin` alone is not even a legal prefix.
        ScheduleLog prefix = Seal(new[] {
            Event("begin"),
            Event("dispatch", decision: 0, action: "a", evaluations: 5, steps: 3, available: new[] { "a" }),
            Event("complete", decision: 0, action: "a", evaluations: 5, steps: 3),
            Event("yield"),
        });

        prefix.ValidatePrefix(false).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        prefix.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "完整校验要求已结束 / full validation requires an end event");
    }

    /// <summary>**空标识被拒绝** / An empty identity is rejected.</summary>
    [Fact]
    public void ScheduleLog_ShouldRejectEmptyIdentity() {
        ScheduleLog log = Seal(new[] { Event("begin"), Event("yield"), Event("end") });

        (log with { Configuration = "" }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**录制后导出的日志可再次校验** / A recorded export validates again.</summary>
    [Fact]
    public void ScheduleControl_ShouldRoundTripRecording() {
        var control = new ScheduleControl();
        control.Bind("cfg", "prob", null).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        foreach (ScheduleEvent evt in BuildLog().Events) {
            control.Event(evt).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        }

        Result<ScheduleLog, ErrorCode, Error<ErrorCode>> exported = control.Export();
        exported.Should().BeOfType<Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>>();
        ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)exported).Value.Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**分叉一旦发生就不再恢复** / Divergence never clears.</summary>
    /// <remarks>
    /// 重放与录制一旦分叉，后续事件即使"碰巧又对上了"也不能当作一致——中间那一步已经执行了不同的动作，状态早已不同。
    /// Once replay and recording diverge, later coincidental matches cannot count as consistent — the intermediate step already ran a
    /// different action, so the state already differs.
    /// </remarks>
    [Fact]
    public void ScheduleControl_ShouldStayDiverged() {
        ScheduleLog source = BuildLog();
        var control = new ScheduleControl();
        control.Bind(source.Configuration, source.Problem, source)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        // 第一个事件故意错开，然后喂回正确事件——仍必须处于分叉状态。
        // Deliberately mismatch the first event, then feed the correct ones back — it must stay diverged.
        control.Event(Event("dispatch", decision: 0, action: "zzz", evaluations: 1, steps: 1))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        control.Diverged.Should().BeTrue();

        // **必须再喂一个"正确"的事件并重新检查 `Diverged`**。只断言"喂之前为真"抓不到"成功后清除分叉"的变异——
        // 分叉后 `Event` 会走早退分支（在记录之前 return），因此 `_recorded.Add` 里的清除语句根本不会执行。
        // 这里喂第一个事件后立刻复查，才能覆盖那条语句。
        // **A "correct" event must be fed and `Diverged` re-checked.** Asserting only "true before feeding" cannot catch a mutation that
        // clears the flag on success — after divergence `Event` takes the early-return branch (before recording), so the clearing
        // statement inside `_recorded.Add` never runs. Re-checking right after feeding the first event covers that statement.
        control.Event(source.Events[0]).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        control.Diverged.Should().BeTrue("分叉不得被后续巧合匹配清除 / divergence must not clear");
        control.Snapshot().Should().BeOfType<Failed<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>>>(
            "分叉的重放不能存成检查点 / a diverged replay cannot be checkpointed");
    }

    /// <summary>**身份不匹配的重放被拒绝** / A replay with a mismatched identity is rejected.</summary>
    [Fact]
    public void ScheduleControl_ShouldRejectIncompatibleIdentity() {
        ScheduleLog source = BuildLog();
        var control = new ScheduleControl();

        control.Bind("other-config", source.Problem, source)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**恢复后必须先绑定才能继续记录** / After a restore, an explicit bind is required.</summary>
    [Fact]
    public void ScheduleControl_ShouldRequireBindAfterRestore() {
        var control = new ScheduleControl();
        control.Bind("cfg", "prob", null).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        // **快照必须落在静默点**：`begin` 之后 `advancing` 为真，前缀校验会失败。
        // **The snapshot must land at a quiescent point**: `advancing` is true right after `begin` and the prefix check fails.
        foreach (ScheduleEvent evt in new[] {
            Event("begin"),
            Event("dispatch", decision: 0, action: "a", evaluations: 5, steps: 3, available: new[] { "a" }),
            Event("complete", decision: 0, action: "a", evaluations: 5, steps: 3),
            Event("yield"),
        }) {
            control.Event(evt).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        }

        Result<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>> saved = control.Snapshot();
        saved.Should().BeOfType<Ok<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>>>();

        // **必须在同一个已绑定的对象上恢复**。新建一个 `ScheduleControl` 时 `_bound` 本来就是 false，
        // 于是"恢复后未绑定"在任何实现下都成立——第一版正是这样写的，变异测试显示"恢复后保持已绑定"仍然通过。
        // **The restore must happen on the same already-bound object.** A fresh `ScheduleControl` starts with `_bound == false`, so the
        // assertion holds under any implementation — the first version did exactly that and the mutation test reported a pass.
        control.Restore(((Ok<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>>)saved).Value)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        control.Event(Event("yield"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "恢复后未绑定不得继续 / recording must not continue before binding");
    }

    /// <summary>构造一份合法日志 / Build a legal log.</summary>
    /// <returns>合法日志 / A legal log.</returns>
    /// <remarks>
    /// **`end` 的额度必须等于前面 `complete` 的累计和**。校验要求 `evt.Evaluations == evaluationsTotal`；
    /// 第一版只 dispatch 了一次（累计 5/3）却在 `end` 写了 9/5，于是 `end` 分支的条件不成立、落到 else 报"顺序错误"——
    /// **夹具与校验逻辑不一致，而不是校验逻辑有错**。改为两次 dispatch，累计 5+4=9、3+2=5，与 `end` 一致。
    /// **The `end` allowance must equal the accumulated `complete` totals.** Validation requires
    /// `evt.Evaluations == evaluationsTotal`; the first version dispatched only once (totals 5/3) but wrote 9/5 in `end`, so the `end`
    /// branch's condition failed and fell through to "ordering" — **the fixture disagreed with the validator, not the validator with
    /// itself**. Two dispatches give totals 5+4=9 and 3+2=5, matching `end`.
    /// </remarks>
    private static ScheduleLog BuildLog() => Seal(new[] {
        Event("begin"),
        Event("dispatch", decision: 0, action: "a", evaluations: 5, steps: 3, available: new[] { "a", "b" }),
        Event("complete", decision: 0, action: "a", evaluations: 5, steps: 3),
        Event("dispatch", decision: 1, action: "b", evaluations: 4, steps: 2, available: new[] { "a", "b" }),
        Event("complete", decision: 1, action: "b", evaluations: 4, steps: 2),
        Event("yield"),
        Event("end", evaluations: 9, steps: 5),
    });

    /// <summary>给日志写入自洽的校验和 / Seal a log with a self-consistent checksum.</summary>
    /// <param name="events">事件 / The events.</param>
    /// <returns>已封存的日志 / The sealed log.</returns>
    private static ScheduleLog Seal(IReadOnlyList<ScheduleEvent> events) {
        var log = new ScheduleLog(1, "cfg", "prob", events, "");
        return log with { Checksum = log.Digest() };
    }

    /// <summary>构造一个事件 / Build one event.</summary>
    /// <param name="kind">种类 / The kind.</param>
    /// <param name="decision">决策序号 / The decision index.</param>
    /// <param name="action">动作 / The action.</param>
    /// <param name="evaluations">评估额度 / The evaluation allowance.</param>
    /// <param name="steps">步数额度 / The step allowance.</param>
    /// <param name="available">可选动作 / The available actions.</param>
    /// <returns>事件 / The event.</returns>
    private static ScheduleEvent Event(
        string kind, long decision = 0, string action = "", long evaluations = 0, long steps = 0,
        IReadOnlyList<string>? available = null) =>
        new(kind, decision, action, evaluations, steps, 0, available);
}
