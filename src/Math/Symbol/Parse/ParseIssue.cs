#nullable enable

namespace Fuookami.Ospf.Math.Symbol.Parse
{
    /// <summary>解析问题类型 / Parse issue type (forward-declared; phase 13 owns full module).</summary>
    public enum ParseIssueType
    {
        /// <summary>语法错误 / Syntax error</summary>
        Syntax,
        /// <summary>语义错误 / Semantic error</summary>
        Semantic,
    }

    /// <summary>
    /// 解析问题 / Parse issue.
    /// Kotlin symbol/parse/ParseIssue.kt — ported in full by phase 13.
    /// </summary>
    public sealed record ParseIssue(
        ParseIssueType Type,
        string Message,
        string? Input = null,
        int Position = 0);
}
