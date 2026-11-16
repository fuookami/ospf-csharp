// 超体积增量奖励 / Hypervolume-delta reward.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 超体积计算模式 / The hypervolume computation mode.
/// </summary>
public enum HypervolumeMode {
    /// <summary>精确递归 / Exact recursion.</summary>
    Exact,

    /// <summary>蒙特卡洛采样 / Monte-Carlo sampling.</summary>
    Sampled,
}

/// <summary>
/// 一次超体积测量 / One hypervolume measurement.
/// </summary>
/// <param name="Delta">增量 / The delta.</param>
/// <param name="Exact">是否精确 / Whether it is exact.</param>
/// <param name="Samples">采样数 / The sample count.</param>
/// <param name="StandardError">标准误 / The standard error.</param>
/// <param name="ErrorBound">误差上界 / The error bound.</param>
/// <param name="Work">几何计算量 / The geometric work.</param>
/// <param name="Calibrated">是否已校准 / Whether it is calibrated.</param>
public sealed record HypervolumeMeasurement(
    [property: JsonPropertyName("delta")] double Delta,
    [property: JsonPropertyName("exact")] bool Exact,
    [property: JsonPropertyName("samples")] int Samples = 0,
    [property: JsonPropertyName("standardError")] double StandardError = 0.0,
    [property: JsonPropertyName("errorBound")] double ErrorBound = 0.0,
    [property: JsonPropertyName("work")] long Work = 0,
    [property: JsonPropertyName("calibrated")] bool Calibrated = true);

/// <summary>
/// 超体积状态 / The hypervolume state.
/// </summary>
/// <param name="Definition">定义串 / The definition string.</param>
/// <param name="Reference">参考点 / The reference point.</param>
/// <param name="Minimum">坐标下界 / The coordinate minimum.</param>
/// <param name="Maximum">坐标上界 / The coordinate maximum.</param>
/// <param name="Observations">观测数 / The observation count.</param>
/// <param name="Measurement">最近测量 / The latest measurement.</param>
/// <param name="Version">版本 / The version.</param>
public sealed record HypervolumeState(
    [property: JsonPropertyName("definition")] string Definition,
    [property: JsonPropertyName("reference")] IReadOnlyList<double> Reference,
    [property: JsonPropertyName("minimum")] IReadOnlyList<double> Minimum,
    [property: JsonPropertyName("maximum")] IReadOnlyList<double> Maximum,
    [property: JsonPropertyName("observations")] int Observations,
    [property: JsonPropertyName("measurement")] HypervolumeMeasurement? Measurement,
    [property: JsonPropertyName("version")] int Version = 1);

/// <summary>
/// 超体积增量奖励 / The hypervolume-delta reward.
/// </summary>
/// <remarks>
/// **未配置显式参考点时先累积观测、再自动校准**，校准前一律返回 `delta = 0` 且 `calibrated = false`。跳过校准直接算
/// 会让参考点由此前候选决定，**而此前候选往往远离真实前沿，整段搜索的增量从此系统性地偏大**。
/// **Without an explicit reference point the reward accumulates observations first and calibrates afterwards**; before calibration it
/// always returns `delta = 0` with `calibrated = false`. Skipping calibration lets the first batch of candidates decide the reference
/// point, **and since that first batch is typically far from the true front, every later delta is systematically inflated**.
/// **校准点必须严格劣于（数值上大于）坐标上界**，否则溢出后清空参考点并报错。取"上界 + 一个正余量"看起来更自然，
/// 但当余量退化为 0 时参考点与上界重合，**体积恒为 0 而每一步都返回成功**。
/// **The calibrated point must be strictly worse (numerically larger) than the coordinate maximum**, otherwise it overflows, clears the
/// reference point and fails. "Maximum plus a positive margin" looks more natural, but when the margin degenerates to 0 the reference
/// point coincides with the maximum, **the volume stays 0 while every step returns success**.
/// **维度取自"显式参考点 → 已校准下界 → 第一个点"，三处都可能为空**。任取其一而不是按这个顺序回退，会让一次
/// "参考点为空但点非空"的调用被当成零维并静默返回 0。
/// **The dimension comes from "explicit reference point, then calibrated minimum, then the first point", any of which may be empty.**
/// Picking one of them instead of falling back in this order makes a "empty reference but non-empty points" call look zero-dimensional
/// and silently return 0.
/// **精确模式限制维数 ≤ 256**。递归深度按维数线性增长，超过会让栈溢出——**而栈溢出不是可捕获的失败，整个进程终止**。
/// **Exact mode caps the dimension at 256.** Recursion depth grows linearly with the dimension, and exceeding it overflows the stack —
/// **and a stack overflow is not a catchable failure; the whole process dies**.
/// **几何计算额度用尽时返回失败而不是截断结果**。截断会给出一个偏小的体积，**而调用方无法区分"前沿小"和"算力不够"**。
/// **Exhausting the geometric work budget fails rather than truncating the result.** Truncation yields a too-small volume, and
/// **the caller cannot distinguish "a small front" from "not enough compute"**.
/// **采样的随机流用固定种子重新构造**，而不是复用外部流。复用会让同一份快照在不同时刻给出不同的奖励，
/// **而快照本身看起来完全自洽**。
/// **The sampling random stream is rebuilt from a fixed seed** rather than reusing an external one. Reuse makes the same snapshot yield
/// different rewards at different times, **while the snapshot itself looks perfectly self-consistent**.
/// **采样用"覆盖判据"而不是"体积差"**：先判旧点是否覆盖样本，未覆盖时再判新点。直接比较两批的体积会让
/// 同一次采样同时计入两侧误差，**标准误因此被低估**。
/// **Sampling uses a coverage predicate rather than a volume difference**: it first asks whether an old point covers the sample and only
/// then whether a new one does. Comparing two volumes directly counts both sides' error into one sample, **understating the standard
/// error**.
/// </remarks>
public sealed class HypervolumeDeltaReward : IRewardPolicy, IRecoverableComponent {
    private readonly IReadOnlyList<Flt64> _referencePoint;
    private List<double> _reference;
    private List<double> _minimum = new();
    private List<double> _maximum = new();
    private int _observations;

    /// <summary>
    /// 构造奖励 / Construct the reward.
    /// </summary>
    /// <param name="referencePoint">参考点 / The reference point.</param>
    /// <param name="mode">计算模式 / The computation mode.</param>
    /// <param name="maxWork">几何计算额度 / The geometric work budget.</param>
    /// <param name="samples">采样数 / The sample count.</param>
    /// <param name="seed">随机种子 / The random seed.</param>
    /// <param name="calibrationObservations">校准观测数 / The calibration observation count.</param>
    public HypervolumeDeltaReward(
        IReadOnlyList<Flt64>? referencePoint = null,
        HypervolumeMode mode = HypervolumeMode.Exact,
        long maxWork = 1_000_000,
        int samples = 16384,
        long seed = 1,
        int calibrationObservations = 32) {
        _referencePoint = referencePoint ?? Array.Empty<Flt64>();
        _reference = new List<double>(_referencePoint.Count);
        foreach (Flt64 value in _referencePoint) {
            _reference.Add(value.ToDouble());
        }
        Mode = mode;
        MaxWork = maxWork;
        Samples = samples;
        Seed = seed;
        CalibrationObservations = calibrationObservations;
    }

    /// <summary>计算模式 / The computation mode.</summary>
    public HypervolumeMode Mode { get; }

    /// <summary>几何计算额度 / The geometric work budget.</summary>
    public long MaxWork { get; }

    /// <summary>采样数 / The sample count.</summary>
    public int Samples { get; }

    /// <summary>随机种子 / The random seed.</summary>
    public long Seed { get; }

    /// <summary>校准观测数 / The calibration observation count.</summary>
    public int CalibrationObservations { get; }

    /// <summary>最近一次测量 / The latest measurement.</summary>
    public HypervolumeMeasurement? LastMeasurement { get; private set; }

    /// <summary>当前参考点 / The current reference point.</summary>
    public IReadOnlyList<double> Reference => _reference;

    /// <inheritdoc/>
    public bool RequiresCoordinates => true;

    /// <summary>
    /// 校验参数 / Validate the parameters.
    /// </summary>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (MaxWork <= 0 || Samples <= 0 || CalibrationObservations <= 0) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "超体积参数无效 / Invalid hypervolume parameters");
        }
        foreach (Flt64 value in _referencePoint) {
            if (!double.IsFinite(value.ToDouble())) {
                return SearchCodecSupport.SearchInvalid<Success>(
                    "超体积参数无效 / Invalid hypervolume parameters");
            }
        }
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public Flt64 Reward(RewardContext context) {
        Result<Flt64, ErrorCode, Error<ErrorCode>> result = Evaluate(context);
        return result is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : new Flt64(double.NaN);
    }

    /// <inheritdoc/>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> Evaluate(RewardContext context) {
        Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> measured = Measure(context);
        if (measured is Failed<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Flt64>(failed.Error);
        }
        return Results.Ok(new Flt64(((Ok<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>>)measured).Value.Delta));
    }

    /// <summary>
    /// 测量一次超体积增量 / Measure one hypervolume delta.
    /// </summary>
    /// <param name="context">奖励上下文 / The reward context.</param>
    /// <returns>测量结果 / The measurement.</returns>
    public Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> Measure(RewardContext context) {
        ArgumentNullException.ThrowIfNull(context);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<HypervolumeMeasurement>(invalid.Error);
        }

        IReadOnlyList<IReadOnlyList<Flt64>> referencePoints =
            context.Reference ?? Array.Empty<IReadOnlyList<Flt64>>();
        IReadOnlyList<IReadOnlyList<Flt64>> candidatePoints =
            context.Candidates ?? Array.Empty<IReadOnlyList<Flt64>>();
        var points = new List<List<double>>(referencePoints.Count + candidatePoints.Count);
        foreach (IReadOnlyList<Flt64> point in referencePoints) {
            points.Add(ToDoubles(point));
        }
        foreach (IReadOnlyList<Flt64> point in candidatePoints) {
            points.Add(ToDoubles(point));
        }

        int dimension = _reference.Count > 0
            ? _reference.Count
            : _minimum.Count > 0
                ? _minimum.Count
                : points.Count > 0 ? points[0].Count : 0;
        foreach (List<double> point in points) {
            if (point.Count != dimension || point.Count == 0) {
                return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                    "超体积坐标维度或数值无效 / Invalid hypervolume coordinate dimensions or values");
            }
            foreach (double value in point) {
                if (!double.IsFinite(value)) {
                    return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                        "超体积坐标维度或数值无效 / Invalid hypervolume coordinate dimensions or values");
                }
            }
        }

        if (_reference.Count == 0) {
            // **观测数按"候选数"累加，不是按"点数"**：`points` 里还混着参考点，用点数会把一次调用当成两批。
            // **The observation count accumulates the candidate count, not the point count**: `points` also contains the reference
            // points, so counting points would treat one call as two batches.
            return Calibrate(points, candidatePoints.Count);
        }
        if (dimension == 0) {
            return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                "超体积参考点无效 / Invalid hypervolume reference");
        }
        foreach (double value in _reference) {
            if (!double.IsFinite(value)) {
                return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                    "超体积参考点无效 / Invalid hypervolume reference");
            }
        }

        var work = new GeometryWork(MaxWork);
        var oldPoints = new List<List<double>>(referencePoints.Count);
        foreach (IReadOnlyList<Flt64> point in referencePoints) {
            oldPoints.Add(ToDoubles(point));
        }
        var freshPoints = new List<List<double>>(candidatePoints.Count);
        foreach (IReadOnlyList<Flt64> point in candidatePoints) {
            freshPoints.Add(ToDoubles(point));
        }

        if (Mode == HypervolumeMode.Exact) {
            if (dimension > 256) {
                return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                    "精确超体积递归深度超限，请显式选择采样 / Exact HV recursion depth exceeded; explicitly select sampling");
            }
            Result<double, ErrorCode, Error<ErrorCode>> before =
                HypervolumeGeometry.ExactVolume(oldPoints, _reference, work);
            if (before is Failed<double, ErrorCode, Error<ErrorCode>> beforeFailed) {
                return Results.Failed<HypervolumeMeasurement>(beforeFailed.Error);
            }
            var combined = new List<List<double>>(oldPoints);
            combined.AddRange(freshPoints);
            Result<double, ErrorCode, Error<ErrorCode>> after =
                HypervolumeGeometry.ExactVolume(combined, _reference, work);
            if (after is Failed<double, ErrorCode, Error<ErrorCode>> afterFailed) {
                return Results.Failed<HypervolumeMeasurement>(afterFailed.Error);
            }
            double delta = global::System.Math.Max(
                ((Ok<double, ErrorCode, Error<ErrorCode>>)after).Value
                - ((Ok<double, ErrorCode, Error<ErrorCode>>)before).Value, 0.0);
            if (!double.IsFinite(delta)) {
                return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                    "超体积溢出 / Hypervolume overflow");
            }
            return Complete(new HypervolumeMeasurement(delta, true, Work: work.Used));
        }
        return Sampled(oldPoints, freshPoints, work);
    }

    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() {
        var state = new HypervolumeState(
            Definition(), _reference, _minimum, _maximum, _observations, LastMeasurement);
        return Results.Ok(JsonSerializer.SerializeToElement(state));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) {
        Result<HypervolumeState, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<HypervolumeState>(state.GetRawText())!);
        if (decoded is Failed<HypervolumeState, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        HypervolumeState saved = ((Ok<HypervolumeState, ErrorCode, Error<ErrorCode>>)decoded).Value;

        bool reject = saved.Version != 1
            || !string.Equals(saved.Definition, Definition(), StringComparison.Ordinal)
            || saved.Observations < 0 || saved.Observations > CalibrationObservations;
        foreach (double value in AllFiniteInputs(saved)) {
            if (!double.IsFinite(value)) {
                reject = true;
            }
        }
        if (saved.Minimum.Count != saved.Maximum.Count) {
            reject = true;
        }
        for (int index = 0; index < saved.Minimum.Count && index < saved.Maximum.Count; index++) {
            if (saved.Minimum[index] > saved.Maximum[index]) {
                reject = true;
            }
        }
        int dimension = saved.Reference.Count > 0 ? saved.Reference.Count : saved.Minimum.Count;
        if (saved.Minimum.Count > 0 && saved.Minimum.Count != dimension) {
            reject = true;
        }
        if (_referencePoint.Count > 0) {
            if (!VectorsEqual(saved.Reference, _reference)) {
                reject = true;
            }
            if (saved.Minimum.Count > 0 || saved.Observations != 0) {
                reject = true;
            }
        } else if (saved.Reference.Count > 0) {
            if (saved.Observations != CalibrationObservations || saved.Minimum.Count == 0) {
                reject = true;
            }
            for (int index = 0; index < saved.Reference.Count && index < saved.Maximum.Count; index++) {
                if (saved.Reference[index] <= saved.Maximum[index]) {
                    reject = true;
                }
            }
        }
        if (saved.Observations > 0 && saved.Minimum.Count == 0) {
            reject = true;
        }
        if (saved.Measurement is not null) {
            HypervolumeMeasurement measurement = saved.Measurement;
            bool bad = measurement.Delta < 0.0 || !double.IsFinite(measurement.Delta)
                || measurement.Samples < 0
                || measurement.Work < 0 || measurement.Work > MaxWork
                || measurement.Exact != (Mode == HypervolumeMode.Exact)
                || measurement.Calibrated != (saved.Reference.Count > 0)
                || measurement.Samples != 0 && measurement.Samples != (Mode == HypervolumeMode.Sampled ? Samples : 0)
                || !double.IsFinite(measurement.StandardError) || measurement.StandardError < 0.0
                || !double.IsFinite(measurement.ErrorBound) || measurement.ErrorBound < 0.0;
            if (bad) {
                reject = true;
            }
        }
        if (reject) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "超体积状态不兼容 / Incompatible hypervolume state");
        }

        _reference = new List<double>(saved.Reference);
        _minimum = new List<double>(saved.Minimum);
        _maximum = new List<double>(saved.Maximum);
        _observations = saved.Observations;
        LastMeasurement = saved.Measurement;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 校准参考点 / Calibrate the reference point.
    /// </summary>
    /// <param name="points">点集 / The point set.</param>
    /// <param name="candidateCount">候选数 / The candidate count.</param>
    /// <returns>测量结果 / The measurement.</returns>
    private Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> Calibrate(
        IReadOnlyList<List<double>> points, int candidateCount) {
        foreach (List<double> point in points) {
            if (_minimum.Count == 0) {
                _minimum = new List<double>(point);
            } else {
                for (int axis = 0; axis < point.Count; axis++) {
                    _minimum[axis] = global::System.Math.Min(_minimum[axis], point[axis]);
                }
            }
            if (_maximum.Count == 0) {
                _maximum = new List<double>(point);
            } else {
                for (int axis = 0; axis < point.Count; axis++) {
                    _maximum[axis] = global::System.Math.Max(_maximum[axis], point[axis]);
                }
            }
        }
        _observations = (int)global::System.Math.Min(
            _observations + (long)candidateCount, (long)CalibrationObservations);
        if (_observations < CalibrationObservations || _minimum.Count == 0) {
            return Complete(new HypervolumeMeasurement(
                0.0, Mode == HypervolumeMode.Exact, Calibrated: false));
        }

        _reference = new List<double>(_minimum.Count);
        for (int axis = 0; axis < _minimum.Count; axis++) {
            double spread = _maximum[axis] - _minimum[axis];
            double margin = global::System.Math.Max(
                spread, global::System.Math.Max(global::System.Math.Abs(_maximum[axis]), 1.0));
            _reference.Add(_maximum[axis] + (margin * 0.1));
        }
        bool overflow = false;
        foreach (double value in _reference) {
            if (!double.IsFinite(value)) {
                overflow = true;
            }
        }
        for (int axis = 0; axis < _reference.Count && axis < _maximum.Count; axis++) {
            if (_reference[axis] <= _maximum[axis]) {
                overflow = true;
            }
        }
        if (overflow) {
            _reference = new List<double>();
            return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                "自动参考点溢出，请配置显式参考点 / Automatic reference overflow; configure an explicit reference");
        }
        // 冻结的这一动作不跨参考定义计信用。
        // The freezing action is not credited across reference definitions.
        return Complete(new HypervolumeMeasurement(0.0, Mode == HypervolumeMode.Exact));
    }

    /// <summary>
    /// 采样估计 / Sample-based estimate.
    /// </summary>
    /// <param name="oldPoints">旧点集 / The old points.</param>
    /// <param name="freshPoints">新点集 / The new points.</param>
    /// <param name="work">几何计算额度 / The geometric work budget.</param>
    /// <returns>测量结果 / The measurement.</returns>
    private Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> Sampled(
        IReadOnlyList<List<double>> oldPoints,
        IReadOnlyList<List<double>> freshPoints,
        GeometryWork work) {
        var points = new List<List<double>>(oldPoints.Count + freshPoints.Count);
        foreach (List<double> point in oldPoints) {
            if (BelowReference(point)) {
                points.Add(point);
            }
        }
        foreach (List<double> point in freshPoints) {
            if (BelowReference(point)) {
                points.Add(point);
            }
        }
        if (points.Count == 0) {
            return Results.Ok(new HypervolumeMeasurement(0.0, false));
        }

        var lower = new List<double>(_reference.Count);
        var widths = new List<double>(_reference.Count);
        for (int axis = 0; axis < _reference.Count; axis++) {
            double smallest = points[0][axis];
            foreach (List<double> point in points) {
                if (point[axis] < smallest) {
                    smallest = point[axis];
                }
            }
            lower.Add(smallest);
            widths.Add(_reference[axis] - smallest);
        }
        double box = 1.0;
        foreach (double width in widths) {
            box *= width;
        }
        if (!double.IsFinite(box)) {
            return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                "采样积分区域溢出 / Sampling integration box overflow");
        }

        var random = new SearchRandom(Seed);
        int hits = 0;
        for (int sampleIndex = 0; sampleIndex < Samples; sampleIndex++) {
            var sample = new List<double>(_reference.Count);
            for (int axis = 0; axis < _reference.Count; axis++) {
                sample.Add(lower[axis] + (random.NextDouble() * widths[axis]));
            }
            bool? wasCovered = Dominated(oldPoints, sample, work);
            if (wasCovered is null) {
                return WorkFailure(work);
            }
            bool isNew = false;
            if (!wasCovered.Value) {
                bool? covered = Dominated(freshPoints, sample, work);
                if (covered is null) {
                    return WorkFailure(work);
                }
                isNew = covered.Value;
            }
            if (isNew) {
                hits++;
            }
        }
        double probability = hits / (double)Samples;
        return Results.Ok(new HypervolumeMeasurement(
            box * probability, false, Samples,
            box * global::System.Math.Sqrt(probability * (1.0 - probability) / Samples),
            box * global::System.Math.Sqrt(global::System.Math.Log(200.0) / (2.0 * Samples)),
            work.Used));
    }

    /// <summary>
    /// 点是否严格优于参考点 / Whether a point strictly beats the reference point.
    /// </summary>
    /// <param name="point">点 / The point.</param>
    /// <returns>是否在参考点之内 / Whether it is inside the reference point.</returns>
    private bool BelowReference(List<double> point) {
        for (int axis = 0; axis < point.Count; axis++) {
            if (!(point[axis] < _reference[axis])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 判断样本是否被任一点覆盖 / Decide whether any point covers the sample.
    /// </summary>
    /// <param name="entries">点集 / The points.</param>
    /// <param name="sample">样本 / The sample.</param>
    /// <param name="work">几何计算额度 / The geometric work budget.</param>
    /// <returns>是否覆盖；额度耗尽时返回 `null` / Whether it is covered, or `null` when the budget ran out.</returns>
    private static bool? Dominated(
        IReadOnlyList<List<double>> entries, List<double> sample, GeometryWork work) {
        foreach (List<double> point in entries) {
            if (!work.Consume()) {
                return null;
            }
            bool covered = true;
            for (int axis = 0; axis < point.Count; axis++) {
                if (!(point[axis] <= sample[axis])) {
                    covered = false;
                    break;
                }
            }
            if (covered) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 构造额度耗尽的失败 / Build a work-exhaustion failure.
    /// </summary>
    /// <param name="work">几何计算额度 / The geometric work budget.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> WorkFailure(
        GeometryWork work) => SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
            $"超体积几何计算额度耗尽 / Hypervolume geometric work exhausted: {work.Limit}");

    /// <summary>
    /// 收尾一次测量 / Finalize a measurement.
    /// </summary>
    /// <param name="value">测量 / The measurement.</param>
    /// <returns>测量结果 / The measurement result.</returns>
    private Result<HypervolumeMeasurement, ErrorCode, Error<ErrorCode>> Complete(
        HypervolumeMeasurement value) {
        if (!double.IsFinite(value.Delta) || !double.IsFinite(value.StandardError)
            || !double.IsFinite(value.ErrorBound)) {
            return SearchCodecSupport.SearchInvalid<HypervolumeMeasurement>(
                "超体积或误差指标溢出 / Hypervolume or uncertainty overflow");
        }
        LastMeasurement = value;
        return Results.Ok(value);
    }

    /// <summary>
    /// 定义串 / The definition string.
    /// </summary>
    /// <returns>定义串 / The definition string.</returns>
    private string Definition() {
        var parts = new List<string>(_referencePoint.Count);
        foreach (Flt64 value in _referencePoint) {
            parts.Add(value.ToDouble().ToString("R"));
        }
        return string.Join("|", string.Join(",", parts), Mode.ToString(),
            MaxWork, Samples, Seed, CalibrationObservations);
    }

    /// <summary>
    /// 把点转为 `double` 列表 / Convert a point into a `double` list.
    /// </summary>
    /// <param name="point">点 / The point.</param>
    /// <returns>`double` 列表 / The `double` list.</returns>
    private static List<double> ToDoubles(IReadOnlyList<Flt64> point) {
        var result = new List<double>(point.Count);
        foreach (Flt64 value in point) {
            result.Add(value.ToDouble());
        }
        return result;
    }

    /// <summary>
    /// 拼接状态里的全部数值 / Concatenate every numeric value in a state.
    /// </summary>
    /// <param name="state">状态 / The state.</param>
    /// <returns>数值序列 / The value sequence.</returns>
    private static IEnumerable<double> AllFiniteInputs(HypervolumeState state) {
        foreach (double value in state.Reference) {
            yield return value;
        }
        foreach (double value in state.Minimum) {
            yield return value;
        }
        foreach (double value in state.Maximum) {
            yield return value;
        }
    }

    /// <summary>
    /// 两个向量是否逐项相等 / Whether two vectors match item by item.
    /// </summary>
    /// <param name="left">左向量 / The left vector.</param>
    /// <param name="right">右向量 / The right vector.</param>
    /// <returns>是否相等 / Whether they are equal.</returns>
    private static bool VectorsEqual(IReadOnlyList<double> left, IReadOnlyList<double> right) {
        if (left.Count != right.Count) {
            return false;
        }
        for (int index = 0; index < left.Count; index++) {
            if (left[index] != right[index]) {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 几何计算额度 / A geometric work budget.
/// </summary>
internal sealed class GeometryWork {
    private long _used;

    /// <summary>
    /// 构造额度 / Construct the budget.
    /// </summary>
    /// <param name="limit">上限 / The limit.</param>
    public GeometryWork(long limit) => Limit = limit;

    /// <summary>上限 / The limit.</summary>
    public long Limit { get; }

    /// <summary>已用量 / The used amount.</summary>
    public long Used => _used;

    /// <summary>
    /// 消耗一次额度 / Consume one unit of the budget.
    /// </summary>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    public bool Consume() {
        if (_used >= Limit) {
            return false;
        }
        _used++;
        return true;
    }
}

/// <summary>
/// 超体积几何 / Hypervolume geometry.
/// </summary>
internal static class HypervolumeGeometry {
    /// <summary>
    /// 精确超体积 / The exact hypervolume.
    /// </summary>
    /// <param name="points">点集 / The points.</param>
    /// <param name="reference">参考点 / The reference point.</param>
    /// <param name="work">几何计算额度 / The geometric work budget.</param>
    /// <returns>体积 / The volume.</returns>
    public static Result<double, ErrorCode, Error<ErrorCode>> ExactVolume(
        IReadOnlyList<List<double>> points, IReadOnlyList<double> reference, GeometryWork work) {
        var eligible = new List<List<double>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (List<double> point in points) {
            if (!work.Consume()) {
                return Exhausted(work);
            }
            if (!seen.Add(Key(point))) {
                continue;
            }
            bool below = true;
            for (int axis = 0; axis < point.Count; axis++) {
                if (!(point[axis] < reference[axis])) {
                    below = false;
                    break;
                }
            }
            if (below) {
                eligible.Add(point);
            }
        }
        if (eligible.Count == 0) {
            return Results.Ok(0.0);
        }

        var front = new List<List<double>>();
        foreach (List<double> point in eligible) {
            bool dominated = false;
            foreach (List<double> other in eligible) {
                if (!work.Consume()) {
                    return Exhausted(work);
                }
                if (Same(other, point)) {
                    continue;
                }
                bool worse = true;
                for (int axis = 0; axis < point.Count; axis++) {
                    if (!(other[axis] <= point[axis])) {
                        worse = false;
                        break;
                    }
                }
                if (worse) {
                    dominated = true;
                    break;
                }
            }
            if (!dominated) {
                front.Add(point);
            }
        }

        int dimension = reference.Count;
        if (front.Count == 1) {
            double volume = 1.0;
            for (int axis = 0; axis < reference.Count; axis++) {
                volume *= reference[axis] - front[0][axis];
            }
            return FiniteVolume(volume);
        }
        if (dimension == 1) {
            double smallest = front[0][0];
            foreach (List<double> point in front) {
                if (point[0] < smallest) {
                    smallest = point[0];
                }
            }
            return FiniteVolume(reference[0] - smallest);
        }
        if (dimension == 2) {
            double height = reference[1];
            double area = 0.0;
            var sorted = new List<List<double>>(front);
            sorted.Sort((left, right) => left[0].CompareTo(right[0]));
            foreach (List<double> point in sorted) {
                if (!work.Consume()) {
                    return Exhausted(work);
                }
                if (point[1] < height) {
                    area += (reference[0] - point[0]) * (height - point[1]);
                    height = point[1];
                }
            }
            return FiniteVolume(area);
        }

        int lastAxis = dimension - 1;
        var levels = new List<double>();
        var levelSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (List<double> point in front) {
            if (levelSeen.Add(point[lastAxis].ToString("R"))) {
                levels.Add(point[lastAxis]);
            }
        }
        levels.Sort();
        var slicedReference = new List<double>(reference.Count - 1);
        for (int axis = 0; axis < reference.Count - 1; axis++) {
            slicedReference.Add(reference[axis]);
        }
        double total = 0.0;
        for (int index = 0; index < levels.Count; index++) {
            if (!work.Consume()) {
                return Exhausted(work);
            }
            double level = levels[index];
            var active = new List<List<double>>();
            foreach (List<double> point in front) {
                if (point[lastAxis] <= level) {
                    var sliced = new List<double>(point.Count - 1);
                    for (int axis = 0; axis < point.Count - 1; axis++) {
                        sliced.Add(point[axis]);
                    }
                    active.Add(sliced);
                }
            }
            Result<double, ErrorCode, Error<ErrorCode>> slicedVolume =
                ExactVolume(active, slicedReference, work);
            if (slicedVolume is Failed<double, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<double>(failed.Error);
            }
            double next = index + 1 < levels.Count ? levels[index + 1] : reference[lastAxis];
            total += ((Ok<double, ErrorCode, Error<ErrorCode>>)slicedVolume).Value * (next - level);
        }
        return FiniteVolume(total);
    }

    /// <summary>
    /// 校验体积有限且非负 / Validate that a volume is finite and non-negative.
    /// </summary>
    /// <param name="value">体积 / The volume.</param>
    /// <returns>体积 / The volume.</returns>
    private static Result<double, ErrorCode, Error<ErrorCode>> FiniteVolume(double value) =>
        double.IsFinite(value) && value >= 0.0
            ? Results.Ok(value)
            : SearchCodecSupport.SearchInvalid<double>("超体积溢出 / Hypervolume overflow");

    /// <summary>
    /// 构造额度耗尽的失败 / Build a work-exhaustion failure.
    /// </summary>
    /// <param name="work">几何计算额度 / The geometric work budget.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<double, ErrorCode, Error<ErrorCode>> Exhausted(GeometryWork work) =>
        SearchCodecSupport.SearchInvalid<double>(
            $"超体积几何计算额度耗尽 / Hypervolume geometric work exhausted: {work.Limit}");

    /// <summary>
    /// 两个点是否逐项相同 / Whether two points are identical item by item.
    /// </summary>
    /// <param name="left">左点 / The left point.</param>
    /// <param name="right">右点 / The right point.</param>
    /// <returns>是否相同 / Whether they are identical.</returns>
    private static bool Same(IReadOnlyList<double> left, IReadOnlyList<double> right) {
        if (left.Count != right.Count) {
            return false;
        }
        for (int index = 0; index < left.Count; index++) {
            if (left[index] != right[index]) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 点的可比较键 / A comparable key for a point.
    /// </summary>
    /// <param name="point">点 / The point.</param>
    /// <returns>键 / The key.</returns>
    private static string Key(IReadOnlyList<double> point) {
        var parts = new List<string>(point.Count);
        foreach (double value in point) {
            parts.Add(value.ToString("R"));
        }
        return string.Join(",", parts);
    }
}
