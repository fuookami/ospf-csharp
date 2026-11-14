// 沿机身的累积载荷重量限制 / Cumulative load weight limits along the fuselage.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 累积方向 / The direction of accumulation.
/// </summary>
public enum CumulativeLoadDirection
{
    /// <summary>向前 / Forward</summary>
    FWD,

    /// <summary>向后 / Aft</summary>
    AFT,
}

/// <summary>
/// 累积载荷限制曲线上的点 / A point on the cumulative load limit curve.
/// </summary>
/// <param name="ToArm">力臂位置 / The arm position.</param>
/// <param name="MaxSum">此力臂处的最大累积和（千克）/ The maximum cumulative sum at this arm, in kilogram.</param>
public sealed record CumulativeLoadPoint(double ToArm, double MaxSum);

/// <summary>
/// 累积载荷检查点 / A checkpoint for cumulative load verification.
/// </summary>
/// <param name="Zone">父限制区域 / The parent limit zone.</param>
/// <param name="ToArm">累积载荷的力臂上限 / The arm up to which load is accumulated.</param>
/// <param name="MaxSum">最大允许累积和 / The maximum allowed cumulative sum.</param>
/// <param name="Parts">贡献此检查点的部分 / The parts contributing to this checkpoint.</param>
public sealed record CumulativeCheckPoint(
    CumulativeLoadLimitZone Zone,
    double ToArm,
    double MaxSum,
    IReadOnlyList<ZoneLoadLimitPart> Parts);

/// <summary>
/// 某个方向上的累积载荷限制区域 / A zone defining cumulative load limits in one direction.
/// </summary>
/// <remarks>
/// 与基线 `demo2/domain/airworthiness_security/model/MaxCumulativeLoadWeight.kt` 对齐。三处容易做错：
///
/// 1. **`AFT` 方向把点表倒序**。倒序之后检查点按"从远到近"排列，累积区间才与前进方向一致；不倒序会让后向累积的每个检查点覆盖错误的舱位集合。
/// 2. **力臂相同的点先去重再排序**。基线用 `distinctBy { toArm }`，保留**第一次出现**的那个；先排序后去重会保留另一个 `maxSum`。
/// 3. **重叠比例按舱位的实际长度算**，不是按"是否落在区间内"二值判断。跨度为零的舱位（{code}length &lt;= 0{/code}）直接排除，避免除零。
///
/// Aligned with the baseline. Three easy mistakes: (1) **`AFT` reverses the point table** so checkpoints run far-to-near and each accumulation interval matches the direction;
/// without it, every aft checkpoint covers the wrong set of positions; (2) **points with the same arm are deduplicated before sorting** — the baseline's `distinctBy { toArm }`
/// keeps the **first** occurrence, while sorting first would keep the other `maxSum`; (3) **the overlap ratio uses the position's real length**, not an in/out test, and positions of
/// zero span ({code}length &lt;= 0{/code}) are dropped to avoid dividing by zero.
/// </remarks>
public sealed class CumulativeLoadLimitZone
{
    /// <summary>
    /// 构造限制区域 / Construct the limit zone.
    /// </summary>
    /// <param name="direction">累积方向 / The direction.</param>
    /// <param name="fromArm">累积的起始力臂 / The starting arm.</param>
    /// <param name="points">限制曲线点 / The limit curve points.</param>
    /// <param name="positions">舱位 / The positions.</param>
    public CumulativeLoadLimitZone(
        CumulativeLoadDirection direction,
        double fromArm,
        IReadOnlyList<CumulativeLoadPoint> points,
        IReadOnlyList<Position> positions) {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(positions);

        Direction = direction;
        FromArm = fromArm;
        Name = "cumulative_" + direction.ToString().ToLowerInvariant() + "_" + fromArm.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var ordered = points
            .GroupBy(point => point.ToArm)
            .Select(group => group.First())
            .OrderBy(point => point.ToArm)
            .ToList();
        if (direction == CumulativeLoadDirection.AFT) {
            ordered.Reverse();
        }
        Points = ordered;

        var checkPoints = new List<CumulativeCheckPoint>(ordered.Count);
        foreach (CumulativeLoadPoint point in ordered) {
            checkPoints.Add(new CumulativeCheckPoint(
                this,
                point.ToArm,
                point.MaxSum,
                ContributingParts(fromArm, point.ToArm, positions)));
        }
        CheckPoints = checkPoints;
    }

    /// <summary>累积方向 / The direction</summary>
    public CumulativeLoadDirection Direction { get; }

    /// <summary>限制区域名称 / The zone name</summary>
    public string Name { get; }

    /// <summary>累积的起始力臂 / The starting arm</summary>
    public double FromArm { get; }

    /// <summary>限制曲线点（已去重、排序、按方向可能倒序）/ The limit curve points, deduplicated, sorted, possibly reversed</summary>
    public IReadOnlyList<CumulativeLoadPoint> Points { get; }

    /// <summary>此区域内的检查点 / The checkpoints within this zone</summary>
    public IReadOnlyList<CumulativeCheckPoint> CheckPoints { get; }

    /// <summary>
    /// 落在 `[fromArm, toArm]` 内的舱位及其重叠比例 / The positions within the arm interval and their overlap ratios.
    /// </summary>
    /// <param name="fromArm">起始力臂 / The starting arm.</param>
    /// <param name="toArm">到力臂 / The ending arm.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>贡献部分 / The contributing parts.</returns>
    private static IReadOnlyList<ZoneLoadLimitPart> ContributingParts(
        double fromArm,
        double toArm,
        IReadOnlyList<Position> positions) {
        double intervalStart = System.Math.Min(fromArm, toArm);
        double intervalEnd = System.Math.Max(fromArm, toArm);
        if (intervalStart == intervalEnd) {
            return Array.Empty<ZoneLoadLimitPart>();
        }

        var parts = new List<ZoneLoadLimitPart>();
        for (int j = 0; j < positions.Count; ++j) {
            double frontArm = positions[j].Coordinate.FrontArm;
            double backArm = positions[j].Coordinate.BackArm;
            double length = backArm - frontArm;
            if (length <= 0.0) {
                // 跨度为零或为负的舱位没有"重叠比例"可言，基线直接排除。
                //
                // **记录一处不可观测**：这条守卫在 `length == 0` 时其实不改变结果——`overlap` 是
                // `min(back, end) − max(front, start)`，而 `front == back` 时它必然 `<= 0`，下一行的 `overlap > 0` 已经把它挡掉。
                // 实测把守卫改成 `if (false)` 判据全绿。它真正起作用的是 `length < 0`（`FrontArm > BackArm` 的畸形舱位），
                // 那种输入目前没有任何调用方产生，所以只留注释，不伪造判据。
                //
                // A position of zero or negative span has no "overlap ratio", so the baseline drops it. **One unobservable recorded**: at `length == 0` the guard does not change
                // the outcome — `overlap` is `min(back, end) − max(front, start)`, necessarily `<= 0` when `front == back`, so the next line's `overlap > 0` already rejects it;
                // replacing the guard with `if (false)` leaves every criterion green (measured). It only matters for `length < 0` (a malformed position with `FrontArm > BackArm`),
                // which no caller produces today — hence a comment rather than a fabricated criterion.
                continue;
            }
            double overlap = System.Math.Min(backArm, intervalEnd) - System.Math.Max(frontArm, intervalStart);
            if (overlap > 0.0) {
                parts.Add(new ZoneLoadLimitPart(j, overlap / length));
            }
        }
        return parts;
    }
}

/// <summary>
/// 沿机身向前或向后的累积载荷重量限制 / Cumulative load weight limits along the fuselage, forward or aft.
/// </summary>
public sealed class MaxCumulativeLoadWeight
{
    /// <summary>限制区域 / The limit zones</summary>
    public IReadOnlyList<CumulativeLoadLimitZone> LimitZones { get; init; } = Array.Empty<CumulativeLoadLimitZone>();

    /// <summary>全部检查点（各区域展平）/ All checkpoints, flattened across zones</summary>
    public IReadOnlyList<CumulativeCheckPoint> CheckPoints =>
        LimitZones.SelectMany(zone => zone.CheckPoints).ToList();
}
