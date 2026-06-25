#nullable enable

namespace Fuookami.Ospf.Framework.Persistence.Expression;
/// <summary>
/// 标量函数 DSL 辅助方法 / Scalar function DSL helper methods.
/// </summary>
public static class ScalarFunctionDsl {
    /// <summary>小写转换 / Lowercase conversion.</summary>
    public static string Lower(string field) => $"LOWER({field})";

    /// <summary>大写转换 / Uppercase conversion.</summary>
    public static string Upper(string field) => $"UPPER({field})";

    /// <summary>去除空白 / Trim whitespace.</summary>
    public static string Trim(string field) => $"TRIM({field})";

    /// <summary>获取长度 / Get length.</summary>
    public static string Length(string field) => $"LENGTH({field})";

    /// <summary>合并空值 / Coalesce null values.</summary>
    public static string Coalesce(params string[] fields) => $"COALESCE({string.Join(", ", fields)})";
}
