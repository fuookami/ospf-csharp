// 自动接受与自适应接受 / Automatic and adaptive acceptance.
// 与固定 Kotlin 基线 `memory/AutoAcceptance.kt`（4164 B）与 `memory/AdaptiveAcceptance.kt`（4765 B）对应。
// Mirrors the pinned baseline's `AutoAcceptance.kt` (4164 B) and `AdaptiveAcceptance.kt` (4765 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 自动接受状态 / The automatic-acceptance state.
/// </summary>
/// <param name="Version">版本 / The version.</param>
/// <param name="Horizon">视野 / The horizon.</param>
/// <param name="Capacity">容量 / The capacity.</param>
/// <param name="Span">延迟跨度 / The delay span.</param>
/// <param name="Decisions">决策数 / The decision count.</param>
/// <param name="Improvements">改进数 / The improvement count.</param>
/// <param name="History">历史 / The history.</param>
public sealed record AutoAcceptanceState(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("horizon")] long Horizon,
    [property: JsonPropertyName("capacity")] int Capacity,
    [property: JsonPropertyName("span")] int Span,
    [property: JsonPropertyName("decisions")] long Decisions,
    [property: JsonPropertyName("improvements")] int Improvements,
    [property: JsonPropertyName("history")] JsonElement History);

/// <summary>
/// 自动接受策略 / The automatic acceptance policy.
/// </summary>
/// <remarks>
/// **`history` 入队的是"被接受则候选目标，否则当前目标"**，不是候选目标本身。总是记录候选会让延迟比较基准变成一串
/// 从未被接受的解，**接受率因此与配置无关地漂移**。
/// **`history` enqueues "the candidate objective when accepted, otherwise the current one"**, not the candidate objective itself. Always
/// recording the candidate makes the delayed comparison baseline a run of never-accepted solutions, and **the acceptance rate drifts
/// independently of the configuration**.
/// **跨度自动调整只在"恰好经过 `capacity` 个决策"时发生**，且三个分支互斥：零改进则加倍，改进占比达 1/4 则减半，否则不变。
/// 每步都调整会让跨度在两个值之间抖动，延迟窗口失去意义。
/// **The span only auto-adjusts when exactly `capacity` decisions have elapsed**, and the three branches are exclusive: double on zero
/// improvements, halve when improvements reach a quarter, otherwise unchanged. Adjusting every step makes the span oscillate between two
/// values and the delay window meaningless.
/// **延迟基准取 `history[size - span]`（越界则取 0 号），历史为空时退化为当前目标**。取下标 `span` 会让窗口左端随历史增长而移动，
/// 延迟量变成"历史长度"而不是配置的 `span`。
/// **The delayed baseline is `history[size - span]` (clamped to index 0), degrading to the current objective when history is empty.**
/// Indexing by `span` instead makes the window's left edge move with history growth, so the delay becomes the history length rather than
/// the configured `span`.
/// **`load` 校验 `improvements` 落在 `[0, decisions % capacity]`**。放宽上界会让一份"改进数远超实际决策数"的损坏快照被接受，
/// 而跨度仍会按正常节奏调整。
/// **`load` requires `improvements` to lie in `[0, decisions % capacity]`.** Loosening the upper bound accepts a corrupt snapshot whose
/// improvement count far exceeds its decision count, while the span still adjusts on the normal cadence.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class AutoAcceptance<ObjValue, V> : IMemoryAcceptance<ObjValue, V>, IRecoverableComponent
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IDominance _dominance;
    private readonly Func<ObjValue, IReadOnlyList<Flt64>> _coordinates;
    private readonly ISearchCodec<List<ObjValue>> _codec;
    private readonly int _capacity;
    private readonly List<ObjValue> _history = new();
    private int _span;
    private long _decisions;
    private int _improvements;

    /// <summary>
    /// 构造策略 / Construct the policy.
    /// </summary>
    /// <param name="codec">目标编解码 / The objective codec.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="coordinates">目标坐标投影 / The objective-coordinate projection.</param>
    /// <param name="horizon">视野 / The horizon.</param>
    public AutoAcceptance(
        ISearchCodec<List<ObjValue>> codec,
        IDominance dominance,
        Func<ObjValue, IReadOnlyList<Flt64>> coordinates,
        long horizon = long.MaxValue) {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(dominance);
        ArgumentNullException.ThrowIfNull(coordinates);
        _codec = codec;
        _dominance = dominance;
        _coordinates = coordinates;
        Horizon = horizon;
        double raw = global::System.Math.Sqrt(global::System.Math.Max(horizon, 1L));
        _capacity = (int)global::System.Math.Clamp(raw, 8.0, 256.0);
        _span = global::System.Math.Max(_capacity / 2, 2);
    }

    /// <summary>视野 / The horizon.</summary>
    public long Horizon { get; }

    /// <summary>容量 / The capacity.</summary>
    public int Capacity => _capacity;

    /// <summary>延迟跨度 / The delay span.</summary>
    public int HistorySpan => _span;

    /// <summary>决策数 / The decision count.</summary>
    public long Decisions => _decisions;

    /// <summary>改进数 / The improvement count.</summary>
    public int Improvements => _improvements;

    /// <inheritdoc/>
    public bool Accept(IReadOnlyList<IIndividual<ObjValue, V>> working, IIndividual<ObjValue, V> candidate) {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(candidate);
        ObjValue current = working.Count > 0 ? working[0].Objective : candidate.Objective;
        int index = global::System.Math.Max(_history.Count - _span, 0);
        ObjValue delayed = _history.Count > index ? _history[index] : current;
        bool improved = _dominance.Dominates(_coordinates(candidate.Objective), _coordinates(current));
        bool accepted = !_dominance.Dominates(_coordinates(current), _coordinates(candidate.Objective))
            || !_dominance.Dominates(_coordinates(delayed), _coordinates(candidate.Objective));
        if (improved) {
            _improvements++;
        }
        _history.Add(accepted ? candidate.Objective : current);
        while (_history.Count > _capacity) {
            _history.RemoveAt(0);
        }
        _decisions++;
        if (_decisions % _capacity == 0L) {
            if (_improvements == 0) {
                _span = global::System.Math.Min(_span * 2, _capacity);
            } else if (_improvements * 4 >= _capacity) {
                _span = global::System.Math.Max(_span / 2, 2);
            }
            _improvements = 0;
        }
        return accepted;
    }

    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() {
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = _codec.Encode(_history);
        if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<JsonElement>(failed.Error);
        }
        var state = new AutoAcceptanceState(
            1, Horizon, _capacity, _span, _decisions, _improvements,
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);
        return Results.Ok(JsonSerializer.SerializeToElement(state));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) {
        Result<AutoAcceptanceState, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<AutoAcceptanceState>(state.GetRawText())!);
        if (decoded is Failed<AutoAcceptanceState, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        AutoAcceptanceState saved = ((Ok<AutoAcceptanceState, ErrorCode, Error<ErrorCode>>)decoded).Value;
        int improvementBound = (int)(saved.Decisions % _capacity);
        if (saved.Version != 1 || saved.Horizon != Horizon || saved.Capacity != _capacity
            || saved.Span < 2 || saved.Span > _capacity || saved.Decisions < 0
            || saved.Improvements < 0 || saved.Improvements > improvementBound) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "自动接受状态不兼容 / Incompatible automatic acceptance state");
        }
        Result<List<ObjValue>, ErrorCode, Error<ErrorCode>> values = _codec.Decode(saved.History);
        if (values is Failed<List<ObjValue>, ErrorCode, Error<ErrorCode>> decodeFailed) {
            return Results.Failed<Success>(decodeFailed.Error);
        }
        List<ObjValue> history = ((Ok<List<ObjValue>, ErrorCode, Error<ErrorCode>>)values).Value;
        int expected = (int)global::System.Math.Min(saved.Decisions, _capacity);
        if (history.Count != expected) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "自动接受历史不完整 / Incomplete automatic acceptance history");
        }
        _history.Clear();
        _history.AddRange(history);
        _span = saved.Span;
        _decisions = saved.Decisions;
        _improvements = saved.Improvements;
        return Results.Ok(new Success());
    }
}

/// <summary>
/// 延迟接受状态 / The late-acceptance state.
/// </summary>
/// <param name="Length">历史长度 / The history length.</param>
/// <param name="Cursor">游标 / The cursor.</param>
/// <param name="History">历史 / The history.</param>
public sealed record LateAcceptanceState(
    [property: JsonPropertyName("length")] int Length,
    [property: JsonPropertyName("cursor")] int Cursor,
    [property: JsonPropertyName("history")] JsonElement History);

/// <summary>
/// 延迟接受策略 / The late acceptance policy.
/// </summary>
/// <remarks>
/// **多目标下直接拒绝**，而不是退化为"只看当前目标"。多目标没有唯一的"当前最优"，用一个个体充当基准会让接受判据取决于
/// 工作集的排列顺序，**而策略看起来仍在正常工作**。
/// **Multi-objective search rejects outright** rather than degrading to "compare against the current objective". Multi-objective search
/// has no unique current best, and using one individual as the baseline makes acceptance depend on the working set's order **while the
/// policy still looks like it works**.
/// **历史只在第一次调用时用当前目标填满**，之后按游标原地替换。每次调用都重新填充会让延迟窗口永远等于当前目标，退化成爬山法。
/// **The history is filled with the current objective only on the first call**, then replaced in place at the cursor. Refilling on every
/// call keeps the delay window equal to the current objective forever, degenerating into hill climbing.
/// **被接受时写入候选目标，否则写入当前目标**。总是写候选会让历史记录一串未接受的解。
/// **The candidate objective is written when accepted, the current objective otherwise.** Always writing the candidate records a run of
/// rejected solutions.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class LateAcceptance<ObjValue, V> : IMemoryAcceptance<ObjValue, V>, IRecoverableComponent
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IDominance _dominance;
    private readonly Func<ObjValue, IReadOnlyList<Flt64>> _coordinates;
    private readonly ISearchCodec<List<ObjValue>> _codec;
    private readonly bool _multiObjective;
    private List<ObjValue> _history = new();
    private int _cursor;

    /// <summary>
    /// 构造策略 / Construct the policy.
    /// </summary>
    /// <param name="codec">目标编解码 / The objective codec.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="coordinates">目标坐标投影 / The objective-coordinate projection.</param>
    /// <param name="multiObjective">是否多目标 / Whether it is multi-objective.</param>
    /// <param name="length">历史长度 / The history length.</param>
    public LateAcceptance(
        ISearchCodec<List<ObjValue>> codec,
        IDominance dominance,
        Func<ObjValue, IReadOnlyList<Flt64>> coordinates,
        bool multiObjective = false,
        int length = 50) {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(dominance);
        ArgumentNullException.ThrowIfNull(coordinates);
        _codec = codec;
        _dominance = dominance;
        _coordinates = coordinates;
        _multiObjective = multiObjective;
        Length = length;
    }

    /// <summary>历史长度 / The history length.</summary>
    public int Length { get; }

    /// <summary>游标 / The cursor.</summary>
    public int Cursor => _cursor;

    /// <inheritdoc/>
    public bool Accept(IReadOnlyList<IIndividual<ObjValue, V>> working, IIndividual<ObjValue, V> candidate) {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(candidate);
        if (_multiObjective || Length <= 0) {
            return false;
        }
        ObjValue current = working.Count > 0 ? working[0].Objective : candidate.Objective;
        if (_history.Count == 0) {
            _history = new List<ObjValue>(Length);
            for (int index = 0; index < Length; index++) {
                _history.Add(current);
            }
        }
        bool accepted = !_dominance.Dominates(_coordinates(current), _coordinates(candidate.Objective))
            || !_dominance.Dominates(_coordinates(_history[_cursor]), _coordinates(candidate.Objective));
        _history[_cursor] = accepted ? candidate.Objective : current;
        _cursor = (_cursor + 1) % Length;
        return accepted;
    }

    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() {
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = _codec.Encode(_history);
        if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<JsonElement>(failed.Error);
        }
        var state = new LateAcceptanceState(
            Length, _cursor, ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);
        return Results.Ok(JsonSerializer.SerializeToElement(state));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) {
        Result<LateAcceptanceState, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<LateAcceptanceState>(state.GetRawText())!);
        if (decoded is Failed<LateAcceptanceState, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        LateAcceptanceState saved = ((Ok<LateAcceptanceState, ErrorCode, Error<ErrorCode>>)decoded).Value;
        if (saved.Length != Length || Length <= 0 || saved.Cursor < 0 || saved.Cursor >= Length) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid late acceptance definition");
        }
        Result<List<ObjValue>, ErrorCode, Error<ErrorCode>> values = _codec.Decode(saved.History);
        if (values is Failed<List<ObjValue>, ErrorCode, Error<ErrorCode>> decodeFailed) {
            return Results.Failed<Success>(decodeFailed.Error);
        }
        List<ObjValue> history = ((Ok<List<ObjValue>, ErrorCode, Error<ErrorCode>>)values).Value;
        if (history.Count != 0 && history.Count != Length) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid late acceptance history");
        }
        _history = history;
        _cursor = saved.Cursor;
        return Results.Ok(new Success());
    }
}

/// <summary>
/// 记录-记录接受状态 / The record-to-record acceptance state.
/// </summary>
/// <param name="Ratio">初始容差比 / The initial tolerance ratio.</param>
/// <param name="Horizon">视野 / The horizon.</param>
/// <param name="Clock">时钟 / The clock.</param>
/// <param name="Best">历史最优 / The historic best.</param>
/// <param name="Scale">量级 / The scale.</param>
public sealed record RecordToRecordState(
    [property: JsonPropertyName("ratio")] double Ratio,
    [property: JsonPropertyName("horizon")] long Horizon,
    [property: JsonPropertyName("clock")] long Clock,
    [property: JsonPropertyName("best")] double? Best,
    [property: JsonPropertyName("scale")] double Scale);

/// <summary>
/// 记录-记录接受策略 / The record-to-record acceptance policy.
/// </summary>
/// <remarks>
/// **`scale` 单调不减，取自"至今见过的坐标绝对值最大者"**。用当前批次的量级会让容差随窗口内容忽大忽小，
/// 而每一次判断看起来都合理。
/// **`scale` is monotone non-decreasing and taken from the largest absolute coordinate seen so far.** Using the current batch's
/// magnitude makes the tolerance lurch with the batch contents while every individual decision looks reasonable.
/// **容差随 `clock / horizon` 线性收紧**，且比值必须夹到 `[0, 1]`。不夹会让 `clock` 超过 `horizon` 之后容差变负，
/// **接受判据整体反转成"必须改进"**。
/// **The tolerance tightens linearly with `clock / horizon`, and the ratio must be clamped to `[0, 1]`.** Without the clamp the tolerance
/// turns negative once `clock` passes `horizon`, and **the acceptance predicate silently becomes "must improve"**.
/// **`best` 与 `scale` 是跨调用累积的，不是每次重算**。每次重算会让容差只反映当前批次，历史最优的意义完全消失。
/// **`best` and `scale` accumulate across calls rather than being recomputed.** Recomputing makes the tolerance reflect only the current
/// batch and destroys the point of a historic best.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class RecordToRecordAcceptance<ObjValue, V> : IMemoryAcceptance<ObjValue, V>, IRecoverableComponent
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Func<ObjValue, IReadOnlyList<Flt64>>? _coordinates;
    private readonly bool _multiObjective;
    private long _clock;
    private double? _best;
    private double _scale;

    /// <summary>
    /// 构造策略 / Construct the policy.
    /// </summary>
    /// <param name="coordinates">目标坐标投影；`null` 时一律拒绝 / The objective-coordinate projection; always rejects when `null`.</param>
    /// <param name="multiObjective">是否多目标 / Whether it is multi-objective.</param>
    /// <param name="initialRatio">初始容差比 / The initial tolerance ratio.</param>
    /// <param name="horizon">视野 / The horizon.</param>
    public RecordToRecordAcceptance(
        Func<ObjValue, IReadOnlyList<Flt64>>? coordinates,
        bool multiObjective = false,
        double initialRatio = 0.05,
        long horizon = 1000) {
        _coordinates = coordinates;
        _multiObjective = multiObjective;
        InitialRatio = initialRatio;
        Horizon = horizon;
    }

    /// <summary>初始容差比 / The initial tolerance ratio.</summary>
    public double InitialRatio { get; }

    /// <summary>视野 / The horizon.</summary>
    public long Horizon { get; }

    /// <summary>时钟 / The clock.</summary>
    public long Clock => _clock;

    /// <summary>历史最优 / The historic best.</summary>
    public double? Best => _best;

    /// <summary>量级 / The scale.</summary>
    public double Scale => _scale;

    /// <inheritdoc/>
    public bool Accept(IReadOnlyList<IIndividual<ObjValue, V>> working, IIndividual<ObjValue, V> candidate) {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(candidate);
        if (_coordinates is null) {
            return false;
        }
        if (_multiObjective || InitialRatio < 0.0 || InitialRatio > 1.0 || Horizon <= 0) {
            return false;
        }

        var points = new List<double>(working.Count + 1);
        foreach (IIndividual<ObjValue, V> member in working) {
            IReadOnlyList<Flt64> projected = _coordinates(member.Objective);
            if (projected.Count != 1 || !double.IsFinite(projected[0].ToDouble())) {
                return false;
            }
            points.Add(projected[0].ToDouble());
        }
        IReadOnlyList<Flt64> candidatePoint = _coordinates(candidate.Objective);
        if (candidatePoint.Count != 1 || !double.IsFinite(candidatePoint[0].ToDouble())) {
            return false;
        }
        points.Add(candidatePoint[0].ToDouble());

        double value = points[^1];
        double record = global::System.Math.Min(
            _best ?? double.PositiveInfinity, Minimum(points));
        _best = record;
        _scale = global::System.Math.Max(_scale, Maximum(points));
        double decay = global::System.Math.Clamp(1.0 - (_clock / (double)Horizon), 0.0, 1.0);
        double tolerance = InitialRatio * decay * _scale;
        _clock++;
        return value - record <= tolerance;
    }

    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() {
        var state = new RecordToRecordState(InitialRatio, Horizon, _clock, _best, _scale);
        return Results.Ok(JsonSerializer.SerializeToElement(state));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) {
        Result<RecordToRecordState, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<RecordToRecordState>(state.GetRawText())!);
        if (decoded is Failed<RecordToRecordState, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        RecordToRecordState saved = ((Ok<RecordToRecordState, ErrorCode, Error<ErrorCode>>)decoded).Value;
        if (saved.Ratio != InitialRatio || saved.Horizon != Horizon || saved.Clock < 0
            || !double.IsFinite(saved.Scale) || saved.Scale < 0.0
            || (saved.Best is not null && !double.IsFinite(saved.Best.Value))) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid record-to-record state");
        }
        _clock = saved.Clock;
        _best = saved.Best;
        _scale = saved.Scale;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 最小坐标 / The smallest coordinate.
    /// </summary>
    /// <param name="points">坐标 / The coordinates.</param>
    /// <returns>最小值 / The minimum.</returns>
    private static double Minimum(IReadOnlyList<double> points) {
        double value = points[0];
        for (int index = 1; index < points.Count; index++) {
            if (points[index] < value) {
                value = points[index];
            }
        }
        return value;
    }

    /// <summary>
    /// 坐标绝对值最大者 / The largest absolute coordinate.
    /// </summary>
    /// <param name="points">坐标 / The coordinates.</param>
    /// <returns>最大绝对值 / The maximum absolute value.</returns>
    private static double Maximum(IReadOnlyList<double> points) {
        double value = 0.0;
        foreach (double point in points) {
            double magnitude = global::System.Math.Abs(point);
            if (magnitude > value) {
                value = magnitude;
            }
        }
        return value;
    }
}
