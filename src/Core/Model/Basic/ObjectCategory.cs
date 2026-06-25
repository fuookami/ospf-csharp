#nullable enable

namespace Fuookami.Ospf.Core.Model.Basic;
/// <summary>
/// 目标函数分类（优化方向）/ Objective function category (optimization direction)
/// </summary>
public enum ObjectCategory {
    /// <summary>最大化目标 / Maximization objective</summary>
    Maximum,
    /// <summary>最小化目标 / Minimization objective</summary>
    Minimum,
}

/// <summary>
/// ObjectCategory 扩展方法 / ObjectCategory extension methods
/// </summary>
public static class ObjectCategoryExtensions {
    /// <summary>反转的目标类型 / Reversed objective category</summary>
    public static ObjectCategory Reverse(this ObjectCategory category) => category switch {
        ObjectCategory.Maximum => ObjectCategory.Minimum,
        ObjectCategory.Minimum => ObjectCategory.Maximum,
        _ => category,
    };

    /// <summary>转换为符号字符串 / Convert to symbol string</summary>
    public static string ToSymbolString(this ObjectCategory category) => category.ToString();
}
