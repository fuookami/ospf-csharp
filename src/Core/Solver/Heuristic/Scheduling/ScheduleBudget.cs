// 预算分配与调度日志 / Budget allocation and schedule logging.
// 与固定 Kotlin （995 B）与（7761 B）对应。
// And (7761 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 可替换的预算分配规则 / A replaceable budget-allocation rule.
/// </summary>
/// <remarks>
/// **返回的额度必须为正且不超过剩余额度**。返回 0 会让"这一片段不分配预算"与"调度卡住"无法区分；返回超过剩余的额度会让
/// 总消耗突破全局上限，而每片段的数字看起来都很正常。
/// **The returned allowance must be positive and must not exceed the remaining budget.** Returning 0 makes "this slice gets no budget"
/// indistinguishable from "the schedule is stuck"; returning more than the remaining budget breaks the global cap while every slice's
/// number still looks normal.
/// </remarks>
public interface IBudgetAllocator {
    /// <summary>
    /// 分配一个片段的最大评估额度 / Allocate the maximum evaluations for one slice.
    /// </summary>
    /// <param name="algorithm">算法实例 ID / The algorithm instance ID.</param>
    /// <param name="configured">配置的片段额度 / The configured slice allowance.</param>
    /// <param name="remaining">尚未预留的全局额度 / The unreserved global allowance.</param>
    /// <returns>片段额度 / The slice allowance.</returns>
    long Allocate(string algorithm, long configured, long remaining);
}

/// <summary>
/// 固定片段额度 / Fixed slice allowance.
/// </summary>
public sealed class FixedBudgetAllocator : IBudgetAllocator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static FixedBudgetAllocator Instance { get; } = new();

    /// <inheritdoc/>
    public long Allocate(string algorithm, long configured, long remaining) =>
        global::System.Math.Min(configured, remaining);
}

/// <summary>
/// 调度事件 / A schedule event.
/// </summary>
/// <param name="Kind">事件种类 / The event kind.</param>
/// <param name="Decision">决策序号 / The decision index.</param>
/// <param name="Action">动作名 / The action name.</param>
/// <param name="Evaluations">评估额度 / The evaluation allowance.</param>
/// <param name="Steps">步数额度 / The step allowance.</param>
/// <param name="Random">随机流位置 / The random-stream position.</param>
/// <param name="Available">可选动作 / The available actions.</param>
/// <param name="Stopped">是否停止 / Whether it stopped.</param>
/// <param name="Failed">是否失败 / Whether it failed.</param>
public sealed record ScheduleEvent(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("decision")] long Decision = 0,
    [property: JsonPropertyName("action")] string Action = "",
    [property: JsonPropertyName("evaluations")] long Evaluations = 0,
    [property: JsonPropertyName("steps")] long Steps = 0,
    [property: JsonPropertyName("random")] long Random = 0,
    [property: JsonPropertyName("available")] IReadOnlyList<string>? Available = null,
    [property: JsonPropertyName("stopped")] bool Stopped = false,
    [property: JsonPropertyName("failed")] bool Failed = false);

/// <summary>
/// 调度日志 / A schedule log.
/// </summary>
/// <remarks>
/// **校验是逐事件的自动机，不是逐字段检查**。`begin → dispatch → complete → drain → yield → end` 的顺序、`decision` 的递增、
/// 动作必须出现在 `available` 且不能重复活跃、`complete` 的额度不得超过 `dispatch`、累加器不得溢出——
/// 这些约束彼此耦合，**任何一条单独检查都通不过一个真正非法的日志**。
/// **Validation is a per-event state machine, not per-field checking.** The `begin → dispatch → complete → drain → yield → end` order,
/// the incrementing `decision`, actions appearing in `available` and never being doubly active, `complete` allowances never exceeding
/// `dispatch`, and the accumulator never overflowing are mutually coupled — **no single check rejects a genuinely illegal log**.
/// **`checksum` 排除自身后计算**。把 `checksum` 也纳入摘要会让它无法自洽（写入什么值都算不对），而"校验总是失败"会被误读成
/// "日志损坏"。
/// **The checksum is computed over the document excluding itself.** Including `checksum` in the digest makes it unsatisfiable (no value
/// validates), and "validation always fails" would be misread as "the log is corrupt".
/// **`sealed` 区分"前缀合法"与"完整合法"**：检查点保存的是前缀，此时 `end` 事件尚未出现，要求 `ended` 为真会让每次保存都失败。
/// **`sealed` distinguishes a valid prefix from a complete log**: a checkpoint stores a prefix where the `end` event has not appeared
/// yet, so requiring `ended` would make every save fail.
/// </remarks>
public sealed record ScheduleLog(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("configuration")] string Configuration,
    [property: JsonPropertyName("problem")] string Problem,
    [property: JsonPropertyName("events")] IReadOnlyList<ScheduleEvent> Events,
    [property: JsonPropertyName("checksum")] string Checksum) {
    /// <summary>
    /// 计算摘要 / Compute the digest.
    /// </summary>
    /// <returns>摘要 / The digest.</returns>
    public string Digest() => ScheduleLogs.Fingerprint(this);

    /// <summary>
    /// 校验完整日志 / Validate a complete log.
    /// </summary>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() => ValidatePrefix(true);

    /// <summary>
    /// 校验前缀或完整日志 / Validate a prefix or a complete log.
    /// </summary>
    /// <param name="isSealed">是否要求已结束 / Whether an end event is required.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> ValidatePrefix(bool isSealed) {
        if (Version != 1 || string.IsNullOrEmpty(Configuration) || string.IsNullOrEmpty(Problem)
            || Checksum != Digest()) {
            return ScheduleLogs.Invalid("invalid schedule identity, version or checksum");
        }

        var active = new Dictionary<string, ScheduleEvent>(StringComparer.Ordinal);
        long decision = 0L;
        long evaluationsTotal = 0L;
        long stepsTotal = 0L;
        bool advancing = false;
        bool ended = false;
        foreach (ScheduleEvent evt in Events) {
            if (ended || evt.Decision < 0 || evt.Evaluations < 0 || evt.Steps < 0) {
                return ScheduleLogs.Invalid("invalid schedule counters or events after end");
            }
            bool available = evt.Available is not null
                && Contains(evt.Available, evt.Action);
            if (evt.Kind == "begin" && !advancing) {
                advancing = true;
            } else if (evt.Kind == "dispatch" && advancing && evt.Decision == decision
                && evt.Evaluations > 0 && evt.Steps > 0 && available && !active.ContainsKey(evt.Action)) {
                active[evt.Action] = evt;
                decision++;
            } else if (evt.Kind == "complete") {
                if (!active.Remove(evt.Action, out ScheduleEvent? dispatch)) {
                    return ScheduleLogs.Invalid("unexpected schedule completion");
                }
                if (dispatch.Decision != evt.Decision
                    || evt.Evaluations > dispatch.Evaluations
                    || evt.Steps > dispatch.Steps) {
                    return ScheduleLogs.Invalid("inconsistent schedule completion allowances");
                }
                if (long.MaxValue - evaluationsTotal < evt.Evaluations
                    || long.MaxValue - stepsTotal < evt.Steps) {
                    return ScheduleLogs.Invalid("schedule counter overflow");
                }
                evaluationsTotal += evt.Evaluations;
                stepsTotal += evt.Steps;
            } else if (evt.Kind == "drain" && active.Count > 0) {
                // 空分支：`drain` 只在还有活跃动作时合法，否则落到 else 分支报错。
                // Empty branch: `drain` is only legal while actions are active; otherwise the else branch reports it.
            } else if (evt.Kind == "yield" && advancing) {
                advancing = false;
            } else if (evt.Kind == "end" && !advancing && active.Count == 0
                && evt.Evaluations == evaluationsTotal && evt.Steps == stepsTotal) {
                ended = true;
            } else {
                return ScheduleLogs.Invalid("invalid schedule event ordering");
            }
        }

        return advancing || active.Count > 0 || (isSealed && !ended)
            ? ScheduleLogs.Invalid("truncated or non-quiescent schedule")
            : Results.Ok(new Success());
    }

    /// <summary>
    /// 动作是否在可选列表中 / Whether the action appears in the available list.
    /// </summary>
    /// <param name="available">可选动作 / The available actions.</param>
    /// <param name="action">动作名 / The action name.</param>
    /// <returns>是否包含 / Whether it is contained.</returns>
    private static bool Contains(IReadOnlyList<string> available, string action) {
        for (int index = 0; index < available.Count; index++) {
            if (string.Equals(available[index], action, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 调度日志辅助 / Schedule-log helpers.
/// </summary>
public static class ScheduleLogs {
    /// <summary>
    /// 以默认版本构造一份未封缄的日志 / Build an unsealed log with the default version.
    /// </summary>
    /// <remarks>
    /// **这里用静态工厂而不是第二个构造函数**：`ScheduleLog` 是带主构造函数的 record，再叠一个便捷构造函数会让
    /// `System.Text.Json` 无法选定反序列化目标，于是**序列化能成功、反序列化直接抛 `NotSupportedException`**。
 /// 那正是 earlier 记下的"往返损坏"的真正成因。
    ///
    /// **A static factory rather than a second constructor**: `ScheduleLog` is a record with a primary constructor, and stacking a convenience
    /// constructor on it leaves `System.Text.Json` unable to pick a deserialisation target, so **serialisation succeeds while deserialisation throws
 /// `NotSupportedException`**. That is the actual cause of the "broken round trip" recorded in earlier.
    /// </remarks>
    /// <param name="configuration">配置指纹 / The configuration fingerprint.</param>
    /// <param name="problem">问题指纹 / The problem fingerprint.</param>
    /// <param name="events">事件 / The events.</param>
    /// <returns>日志 / The log.</returns>
    public static ScheduleLog Of(
        string configuration, string problem, IReadOnlyList<ScheduleEvent> events) =>
        new(1, configuration, problem, events, string.Empty);

    /// <summary>
    /// 计算文档指纹（排除 `checksum`）/ Compute the document fingerprint excluding `checksum`.
    /// </summary>
    /// <param name="log">日志 / The log.</param>
    /// <returns>指纹 / The fingerprint.</returns>
    public static string Fingerprint(ScheduleLog log) {
        ArgumentNullException.ThrowIfNull(log);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["version"] = log.Version,
            ["configuration"] = log.Configuration,
            ["problem"] = log.Problem,
            ["events"] = log.Events,
        };
        string json = JsonSerializer.Serialize(payload);
        return SolveFingerprinting.Sha256(json).Value;
    }

    /// <summary>
    /// 构造一个搜索参数错误 / Build a search-argument error.
    /// </summary>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / A failed result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Invalid(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}

/// <summary>
/// 调度检查点 / A schedule checkpoint.
/// </summary>
/// <param name="Cursor">游标 / The cursor.</param>
/// <param name="Log">日志 / The log.</param>
public sealed record ScheduleCheckpoint(
    [property: JsonPropertyName("cursor")] int Cursor,
    [property: JsonPropertyName("log")] ScheduleLog Log);

/// <summary>
/// 调度控制 / Schedule control.
/// </summary>
/// <remarks>
/// **`diverged` 一旦置位就不再恢复**。重放与录制一旦分叉，后续事件即使"碰巧又对上了"也不能当作一致——因为中间那一步已经
/// 执行了不同的动作，状态早已不同。把 `diverged` 做成"可恢复的临时标记"会让分歧被静默吞掉。
/// **Once `diverged` is set it never clears.** After a replay and a recording diverge, later events matching by coincidence cannot count
/// as consistent — the intermediate step already executed a different action, so the state already differs. Making `diverged` a
/// recoverable temporary flag would silently swallow the divergence.
/// **`restore` 把 `bound` 置假**，要求调用方显式 `Bind` 之后才能继续。恢复出来的日志与新运行的身份（配置、问题）必须重新核对，
/// 否则用错检查点会在几百次迭代后才发现。
/// **`restore` clears `bound`**, requiring an explicit `Bind` before continuing. The restored log's identity (configuration, problem) must
/// be re-checked, otherwise a wrong checkpoint is only discovered hundreds of iterations later.
/// **`snapshot` 拒绝导出已分叉的重放**。分叉后的记录已经不是原轨迹，存成检查点会污染后续恢复。
/// **`snapshot` refuses to export a diverged replay.** After divergence the recording is no longer the original trajectory, and storing it
/// as a checkpoint would corrupt later restores.
/// </remarks>
public sealed class ScheduleControl {
    private ScheduleLog? _history;
    private ScheduleLog? _source;
    private readonly List<ScheduleEvent> _recorded = new();
    private bool _bound;

    /// <summary>是否已分叉 / Whether it diverged.</summary>
    public bool Diverged { get; private set; }

    /// <summary>录制是否已启用 / Whether recording is enabled.</summary>
    public bool Enabled => _history is not null;

    /// <summary>是否正在无分叉重放 / Whether it is replaying without divergence.</summary>
    public bool Replaying => _source is not null && !Diverged;

    /// <summary>下一个期望事件 / The next expected event.</summary>
    public ScheduleEvent? Peek =>
        _source is not null && _recorded.Count < _source.Events.Count ? _source.Events[_recorded.Count] : null;

    /// <summary>是否已结束 / Whether it ended.</summary>
    public bool Ended => _recorded.Count > 0 && _recorded[^1].Kind == "end";

    /// <summary>
    /// 绑定录制或重放 / Bind recording or replay.
    /// </summary>
    /// <param name="configuration">配置标识 / The configuration identity.</param>
    /// <param name="problem">问题标识 / The problem identity.</param>
    /// <param name="source">重放来源 / The replay source.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Bind(
        string configuration, string problem, ScheduleLog? source) {
        if (source is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> validated = source.Validate();
            if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<Success>(failed.Error);
            }
            if (!string.Equals(source.Configuration, configuration, StringComparison.Ordinal)
                || !string.Equals(source.Problem, problem, StringComparison.Ordinal)) {
                return ScheduleLogs.Invalid("incompatible schedule identity");
            }
        }

        ScheduleLog previous = _history
            ?? new ScheduleLog(1, configuration, problem, Array.Empty<ScheduleEvent>(), "");
        if (source is not null && !PrefixMatches(source, _recorded)) {
            return ScheduleLogs.Invalid("schedule checkpoint prefix mismatch");
        }
        _history = previous;
        _source = source;
        _bound = true;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 检查是否已绑定 / Check whether it is bound.
    /// </summary>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Ready() =>
        Enabled && !_bound
            ? ScheduleLogs.Invalid("bind recordSchedule or replaySchedule after checkpoint restoration")
            : Results.Ok(new Success());

    /// <summary>
    /// 记录或重放一个事件 / Record or replay one event.
    /// </summary>
    /// <param name="evt">事件 / The event.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Event(ScheduleEvent evt) {
        ArgumentNullException.ThrowIfNull(evt);
        Result<Success, ErrorCode, Error<ErrorCode>> ready = Ready();
        if (ready is Failed<Success, ErrorCode, Error<ErrorCode>> notReady) {
            return Results.Failed<Success>(notReady.Error);
        }
        if (_history is null || Diverged) {
            return Results.Ok(new Success());
        }
        ScheduleEvent? peek = Peek;
        if (_source is not null && !EventsEqual(peek, evt)) {
            Diverged = true;
            return ScheduleLogs.Invalid(
                $"schedule diverged at event {_recorded.Count}: expected {Describe(peek)}, actual {Describe(evt)}");
        }
        if (Ended) {
            return ScheduleLogs.Invalid("schedule already ended");
        }
        _recorded.Add(evt);
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 生成检查点 / Produce a checkpoint.
    /// </summary>
    /// <returns>检查点或失败 / A checkpoint or a failure.</returns>
    public Result<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>> Snapshot() {
        ScheduleLog? previous = _history;
        if (previous is null) {
            return Results.Ok<ScheduleCheckpoint?>(null);
        }
        if (Diverged) {
            return Results.Failed<ScheduleCheckpoint?>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "cannot checkpoint a diverged replay"));
        }
        ScheduleLog prefix = previous with { Events = new List<ScheduleEvent>(_recorded) };
        ScheduleLog log = prefix with { Checksum = prefix.Digest() };
        Result<Success, ErrorCode, Error<ErrorCode>> validated = log.ValidatePrefix(false);
        return validated is Failed<Success, ErrorCode, Error<ErrorCode>> failed
            ? Results.Failed<ScheduleCheckpoint?>(failed.Error)
            : Results.Ok<ScheduleCheckpoint?>(new ScheduleCheckpoint(log.Events.Count, log));
    }

    /// <summary>
    /// 从检查点恢复 / Restore from a checkpoint.
    /// </summary>
    /// <param name="saved">检查点 / The checkpoint.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(ScheduleCheckpoint? saved) {
        if (saved is null) {
            return Results.Ok(new Success());
        }
        Result<Success, ErrorCode, Error<ErrorCode>> validated = saved.Log.ValidatePrefix(false);
        if (validated is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        if (saved.Cursor != saved.Log.Events.Count) {
            return ScheduleLogs.Invalid("invalid schedule cursor");
        }
        _history = saved.Log with { Events = Array.Empty<ScheduleEvent>() };
        _recorded.Clear();
        _recorded.AddRange(saved.Log.Events);
        _source = null;
        _bound = false;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 导出完整日志 / Export the complete log.
    /// </summary>
    /// <returns>日志或失败 / The log or a failure.</returns>
    public Result<ScheduleLog, ErrorCode, Error<ErrorCode>> Export() {
        Result<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>> saved = Snapshot();
        if (saved is Failed<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<ScheduleLog>(failed.Error);
        }
        ScheduleLog? log = ((Ok<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>>)saved).Value?.Log;
        if (log is null) {
            return Results.Failed<ScheduleLog>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "schedule recording is not enabled"));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> validated = log.Validate();
        return validated is Failed<Success, ErrorCode, Error<ErrorCode>> invalid
            ? Results.Failed<ScheduleLog>(invalid.Error)
            : Results.Ok(log);
    }

    /// <summary>
    /// 来源前缀是否与已记录的一致 / Whether the source prefix matches what was recorded.
    /// </summary>
    /// <param name="source">来源 / The source.</param>
    /// <param name="recorded">已记录 / The recorded events.</param>
    /// <returns>是否一致 / Whether they match.</returns>
    private static bool PrefixMatches(ScheduleLog source, IReadOnlyList<ScheduleEvent> recorded) {
        if (source.Events.Count < recorded.Count) {
            return false;
        }
        for (int index = 0; index < recorded.Count; index++) {
            if (!EventsEqual(source.Events[index], recorded[index])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 两个事件是否相同 / Whether two events are equal.
    /// </summary>
    /// <param name="left">左事件 / The left event.</param>
    /// <param name="right">右事件 / The right event.</param>
    /// <returns>是否相同 / Whether they are equal.</returns>
    private static bool EventsEqual(ScheduleEvent? left, ScheduleEvent? right) =>
        left is not null && right is not null && Describe(left) == Describe(right);

    /// <summary>
    /// 事件的字符串描述 / A string description of an event.
    /// </summary>
    /// <param name="evt">事件 / The event.</param>
    /// <returns>描述 / The description.</returns>
    private static string Describe(ScheduleEvent? evt) => evt is null
        ? "<none>"
        : $"{evt.Kind}#{evt.Decision}:{evt.Action}@{evt.Evaluations}/{evt.Steps}";
}
