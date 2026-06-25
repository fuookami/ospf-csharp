#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fuookami.Ospf.Core.Model.Intermediate;
/// <summary>转储辅助工具（内部）/ Dump helper utilities (internal)</summary>
internal static class DumpHelpers {
    /// <summary>格式化矩阵条目 / Format matrix entry</summary>
    /// <param name="row">行索引 / Row index</param>
    /// <param name="col">列索引 / Column index</param>
    /// <param name="value">值 / Value</param>
    /// <returns>格式化字符串 / Formatted string</returns>
    public static string FormatEntry(int row, int col, double value) =>
        $"[{row},{col}]={value:G6}";

    /// <summary>格式化二次矩阵条目 / Format quadratic matrix entry</summary>
    /// <param name="row">行索引 / Row index</param>
    /// <param name="col1">第一个列索引 / First column index</param>
    /// <param name="col2">第二个列索引 / Second column index</param>
    /// <param name="value">值 / Value</param>
    /// <returns>格式化字符串 / Formatted string</returns>
    public static string FormatQuadraticEntry(int row, int col1, int col2, double value) =>
        $"[{row},{col1},{col2}]={value:G6}";

    /// <summary>格式化约束关系 / Format constraint relation</summary>
    /// <param name="sign">约束关系 / Constraint relation</param>
    /// <returns>符号字符串 / Symbol string</returns>
    public static string FormatSign(Basic.ConstraintRelation sign) => sign.ToString();

    /// <summary>写入头部注释 / Write header comment</summary>
    /// <param name="writer">写入器 / Writer</param>
    /// <param name="modelName">模型名称 / Model name</param>
    public static void WriteHeader(StreamWriter writer, string modelName) {
        writer.WriteLine($"// Model: {modelName}");
        writer.WriteLine($"// Generated: {DateTime.UtcNow:O}");
        writer.WriteLine();
    }

    /// <summary>构建缩进字符串 / Build indent string</summary>
    /// <param name="depth">缩进深度 / Indent depth</param>
    /// <returns>缩进字符串 / Indent string</returns>
    public static string Indent(int depth) => new(' ', depth * 2);
}
