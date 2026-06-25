#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 阈值松弛工具 / Threshold slack utility.
/// 提供阈值松弛多项式的创建和访问功能。
/// Provides creation and access for threshold slack polynomials.
/// </summary>
internal static class ThresholdSlack {
    /// <summary>
    /// 创建阈值松弛 / Create a threshold slack
    /// </summary>
    /// <param name="x">输入值 / Input value</param>
    /// <param name="threshold">阈值 / Threshold value</param>
    /// <returns>阈值松弛实例 / Threshold slack instance</returns>
    public static ThresholdSlackResult Create(Flt64 x, Flt64 threshold) {
        Flt64 slack = x - threshold;
        Flt64 positiveSlack = slack > Flt64.Zero ? slack : Flt64.Zero;
        return new ThresholdSlackResult(positiveSlack, x);
    }
}

/// <summary>
/// 阈值松弛结果 / Threshold slack result
/// </summary>
/// <param name="PositiveSlack">正松弛值 / Positive slack value</param>
/// <param name="CappedValue">封顶值 / Capped value</param>
internal sealed record ThresholdSlackResult(Flt64 PositiveSlack, Flt64 CappedValue);
