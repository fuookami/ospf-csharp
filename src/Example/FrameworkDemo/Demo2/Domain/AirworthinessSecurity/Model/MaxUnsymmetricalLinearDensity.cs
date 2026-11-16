// 飞机左右两侧不对称线性密度的限制 / Limits on unsymmetrical linear density between the two sides.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 不对称线性密度限制曲线上的点 / A point defining the unsymmetrical limit curve.
/// </summary>
/// <param name="Lhs">左侧值（左侧减右侧的上界）/ The left-hand side value, an upper bound on left minus right.</param>
/// <param name="Rhs">右侧值（右侧减左侧的上界）/ The right-hand side value, an upper bound on right minus left.</param>
public sealed record UnsymmetricalLinearDensityLimitPoint(double Lhs, double Rhs);

/// <summary>
/// 不对称线性密度限制 / One linear row of an unsymmetrical limit.
/// </summary>
/// <param name="LeftCoefficient">左侧系数，可为空 / The left coefficient, nullable.</param>
/// <param name="RightCoefficient">右侧系数，可为空 / The right coefficient, nullable.</param>
/// <param name="MaxSum">最大允许和 / The maximum allowed sum.</param>
public sealed record UnsymmetricalLinearDensityLimit(
    double? LeftCoefficient,
    double? RightCoefficient,
    double MaxSum);

/// <summary>
/// 不对称线性密度限制线 / A limit line within an unsymmetrical zone.
/// </summary>
/// <param name="Zone">父限制区域 / The parent zone.</param>
/// <param name="Arm">此线的力臂位置 / The arm of this line.</param>
/// <param name="LeftPositionIndex">左侧舱位索引 / The left position index.</param>
/// <param name="RightPositionIndex">右侧舱位索引 / The right position index.</param>
public sealed record UnsymmetricalLinearDensityLimitLine(
    UnsymmetricalLinearDensityLimitZone Zone,
    double Arm,
    int LeftPositionIndex,
    int RightPositionIndex);

/// <summary>
/// 定义不对称线性密度限制的区域 / A zone defining unsymmetrical linear density limits.
/// </summary>
/// <remarks>
/// 与对齐。三处容易做错：
///
/// 1. **每条曲线点生成两行**：{code}+1·左 −1·右 &lt;= lhs{/code} 与 {code}−1·左 +1·右 &lt;= rhs{/code}。这是"左比右多不超过 lhs、右比左多不超过 rhs"的两半，
///    只生成一行时只限制单侧不对称。
/// 2. **限制线的候选力臂 = 区域两端 + 区域舱位的前后力臂**，去重、排序、按**归一化**的区间过滤（区域可能 {code}backArm &lt; frontArm{/code}）。
/// 3. **一条限制线要求该力臂上恰好一左一右**。多于或少于一个时跳过——两侧数量不等就无法定义"不对称"。
///
/// Three easy mistakes: (1) **each curve point yields two rows**, `+1·left −1·right ≤ lhs` and `−1·left +1·right ≤ rhs` — the two halves of "left exceeds
/// right by at most lhs" and vice versa, and one row alone bounds only one direction; (2) **candidate arms are the zone ends plus the zone positions' front and back arms**,
/// deduplicated, sorted, and filtered by the **normalised** interval (the zone may have {code}backArm &lt; frontArm{/code}); (3) **a limit line needs exactly one left and one right position at
/// that arm** — more or fewer is skipped, since "asymmetry" is undefined when the sides are unbalanced.
/// </remarks>
public sealed class UnsymmetricalLinearDensityLimitZone
{
    /// <summary>
    /// 构造限制区域 / Construct the limit zone.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="frontArm">区域的前力臂 / The front arm.</param>
    /// <param name="backArm">区域的后力臂 / The back arm.</param>
    /// <param name="points">曲线点 / The curve points.</param>
    /// <param name="positions">全部舱位 / All positions.</param>
    public UnsymmetricalLinearDensityLimitZone(
        string name,
        double frontArm,
        double backArm,
        IReadOnlyList<UnsymmetricalLinearDensityLimitPoint> points,
        IReadOnlyList<Position> positions) {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(positions);

        Name = name;
        FrontArm = frontArm;
        BackArm = backArm;

        var limits = new List<UnsymmetricalLinearDensityLimit>();
        foreach (UnsymmetricalLinearDensityLimitPoint point in points) {
            limits.Add(new UnsymmetricalLinearDensityLimit(1.0, -1.0, point.Lhs));
            limits.Add(new UnsymmetricalLinearDensityLimit(-1.0, 1.0, point.Rhs));
        }
        Limits = limits;

        var zoneIndices = new List<int>();
        for (int j = 0; j < positions.Count; ++j) {
            if (positions[j].Coordinate.WithIntersectionWith(frontArm, backArm)) {
                zoneIndices.Add(j);
            }
        }

        double lower = System.Math.Min(frontArm, backArm);
        double upper = System.Math.Max(frontArm, backArm);
        var arms = new List<double> { frontArm, backArm };
        foreach (int j in zoneIndices) {
            arms.Add(positions[j].Coordinate.FrontArm);
            arms.Add(positions[j].Coordinate.BackArm);
        }
        var ordered = arms
            .GroupBy(arm => arm)
            .Select(group => group.First())
            .OrderBy(arm => arm)
            .Where(arm => lower <= arm && arm <= upper)
            .ToList();

        var lines = new List<UnsymmetricalLinearDensityLimitLine>();
        foreach (double arm in ordered) {
            int left = -1;
            int right = -1;
            int leftCount = 0;
            int rightCount = 0;
            foreach (int j in zoneIndices) {
                PositionCoordinate coordinate = positions[j].Coordinate;
                if (!coordinate.On(arm)) {
                    continue;
                }
                if (coordinate.OnLeft) {
                    ++leftCount;
                    left = j;
                } else if (coordinate.OnRight) {
                    ++rightCount;
                    right = j;
                }
            }
            if (leftCount == 1 && rightCount == 1) {
                lines.Add(new UnsymmetricalLinearDensityLimitLine(this, arm, left, right));
            }
        }
        Lines = lines;
    }

    /// <summary>限制区域名称 / The zone name</summary>
    public string Name { get; }

    /// <summary>区域的前力臂 / The front arm</summary>
    public double FrontArm { get; }

    /// <summary>区域的后力臂 / The back arm</summary>
    public double BackArm { get; }

    /// <summary>限制行（每个曲线点两行）/ The limit rows, two per curve point</summary>
    public IReadOnlyList<UnsymmetricalLinearDensityLimit> Limits { get; }

    /// <summary>限制线 / The limit lines</summary>
    public IReadOnlyList<UnsymmetricalLinearDensityLimitLine> Lines { get; }
}

/// <summary>
/// 飞机左右两侧不对称线性密度的限制 / Limits on unsymmetrical linear density between the two sides.
/// </summary>
public sealed class MaxUnsymmetricalLinearDensity
{
    /// <summary>限制区域 / The limit zones</summary>
    public IReadOnlyList<UnsymmetricalLinearDensityLimitZone> LimitZones { get; init; } =
        Array.Empty<UnsymmetricalLinearDensityLimitZone>();
}
