#nullable enable

using Fuookami.Ospf.Framework.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 影子价格键类型转换支持 / Shadow price key cast support.
/// 将约束参数安全收敛为目标影子价格 key 类型。
/// Safely narrows constraint args to the target shadow-price key type.
/// </summary>
internal static class ShadowPriceKeyCastSupport {
    /// <summary>
    /// 安全转换约束参数为影子价格键 / Safely cast constraint args to shadow price key
    /// </summary>
    /// <typeparam name="K">影子价格键类型 / Shadow price key type</typeparam>
    /// <param name="args">约束参数 / Constraint args</param>
    /// <returns>影子价格键，若类型不匹配则为null / Shadow price key, or null if type mismatch</returns>
    public static K? ShadowPriceKeyOf<K>(object? args) where K : ShadowPriceKey => args as K;
}
