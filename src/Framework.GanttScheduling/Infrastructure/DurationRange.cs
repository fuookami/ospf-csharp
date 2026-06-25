#nullable enable

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
/// <summary>
/// 持续时间范围，表示 [lb, ub] 区间 / Duration range representing a [lb, ub] interval.
/// </summary>
/// <param name="Lb">下界 / Lower bound.</param>
/// <param name="Ub">上界 / Upper bound.</param>
public sealed record DurationRange(TimeSpan Lb, TimeSpan Ub);
