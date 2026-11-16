// CSV 数据集形态与表头校验 / CSV dataset shape and header validation.
//
// 逐行构造 `ActualItem` / `SheetType` 的部分见文末说明。
// Row-wise construction of `ActualItem` / `SheetType` is scoped out — see the tail note.

using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Application;

/// <summary>
/// CSV 数据集形态 / CSV dataset shape.
/// </summary>
public enum Bpp2dCsvDatasetKind {
    /// <summary>分组分层形态 / Grouped-layer shape.</summary>
    GroupedLayer,

    /// <summary>物料宽度数量形态 / Material-width-amount shape.</summary>
    MaterialWidthAmount,
}

/// <summary>
/// 校验通过的 CSV 结构 / A validated CSV structure.
/// </summary>
/// <param name="Kind">数据集形态 / The dataset shape.</param>
/// <param name="Columns">列名（顺序与表头一致）/ The column names, in header order.</param>
/// <param name="Rows">数据行数（不含表头）/ The number of data rows, header excluded.</param>
public sealed record Bpp2dCsvSchema(
    Bpp2dCsvDatasetKind Kind,
    IReadOnlyList<string> Columns,
    int Rows);

/// <summary>
/// CSV 表头与结构校验 / CSV header and structure validation.
/// </summary>
/// <remarks>
/// **所有失败都带行号或列名，且必须是第一个出错的位置**。一个 500 行的文件若只报"某行有错"，
/// 使用者要自己二分查找；而批量导入的失败如果报到最后一个错误，等于没报。
///
/// **Every failure carries a row number or a column name, and must be the first offending position.** A 500-row file reporting only "some row is wrong" forces the user to
/// bisect it, and a bulk import that reports the last error effectively reports nothing.
/// </remarks>
public static class Bpp2dCsvSchemaValidator {
    /// <summary>分组分层形态的必填列 / Required columns of the grouped-layer shape.</summary>
    private static readonly string[] GroupedRequired = { "group_index", "layer_index", "item_id", "amount" };

    /// <summary>物料宽度数量形态的必填列 / Required columns of the material-width-amount shape.</summary>
    private static readonly string[] MaterialRequired = { "material", "width", "amount" };

    /// <summary>两种形态都允许的可选列 / Optional columns allowed in both shapes.</summary>
    private static readonly string[] Optional = {
        "width",
        "height_meter",
        "radius_meter",
        "shape_type",
        "allow_rotate",
        "material_name",
        "material_no",
        "material_weight_kg",
        "sheet_width_meter",
        "sheet_height_meter",
        "sheet_type",
    };

    /// <summary>
    /// 校验 CSV 文本 / Validate CSV text.
    /// </summary>
    /// <param name="text">CSV 文本，首行为表头 / The CSV text whose first line is the header.</param>
    /// <param name="declaredKind">声明的形态，null 表示按表头自动识别 / The declared shape, null meaning auto-detect.</param>
    /// <returns>结构或失败 / The schema or a failure.</returns>
    /// <remarks>
    /// **声明形态与实际不符时报错，而不是以声明为准**：两者不一致说明调用方对文件的理解是错的，
    /// 而"以声明为准"会把一个物料表当成分组表去解析，得到的错误会出现在几层之后，与真正的原因毫无关联。
    ///
    /// **A declared shape that disagrees with the detected one is an error, not the winner**: the disagreement means the caller's understanding of the file is wrong, and
    /// honouring the declaration would parse a material table as a grouped table, surfacing an error several layers away from the real cause.
    /// </remarks>
    public static Result<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>> Validate(
        string text,
        Bpp2dCsvDatasetKind? declaredKind = null) {
        if (string.IsNullOrWhiteSpace(text)) {
            return Failed("CSV 为空 / CSV is empty");
        }

        var lines = new List<string>();
        foreach (string raw in text.Split('\n')) {
            string line = raw.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line)) {
                lines.Add(line);
            }
        }
        if (lines.Count == 0) {
            return Failed("CSV 为空 / CSV is empty");
        }

        var header = new List<string>();
        foreach (string cell in lines[0].Split(',')) {
            header.Add(cell.Trim());
        }

        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var duplicates = new List<string>();
        foreach (string column in header) {
            if (!seen.Add(column) && !duplicates.Contains(column)) {
                duplicates.Add(column);
            }
        }
        if (duplicates.Count > 0) {
            return Failed("CSV 存在重复列：" + string.Join(", ", duplicates)
                + " / Duplicate CSV columns: " + string.Join(", ", duplicates));
        }

        Bpp2dCsvDatasetKind detected;
        if (ContainsAll(header, GroupedRequired)) {
            detected = Bpp2dCsvDatasetKind.GroupedLayer;
        } else if (ContainsAll(header, MaterialRequired)) {
            detected = Bpp2dCsvDatasetKind.MaterialWidthAmount;
        } else {
            return Failed("CSV 表头无法识别 / Unrecognised CSV header: neither ["
                + string.Join(", ", GroupedRequired) + "] nor ["
                + string.Join(", ", MaterialRequired) + "] is present");
        }

        if (declaredKind is not null && declaredKind.Value != detected) {
            return Failed("CSV 形态不匹配：声明 " + declaredKind.Value + "，实际 " + detected
                + " / Declared " + declaredKind.Value + " but detected " + detected);
        }

        var allowed = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string column in detected == Bpp2dCsvDatasetKind.GroupedLayer ? GroupedRequired : MaterialRequired) {
            allowed.Add(column);
        }
        foreach (string column in Optional) {
            allowed.Add(column);
        }

        var unknown = new List<string>();
        foreach (string column in header) {
            if (!allowed.Contains(column)) {
                unknown.Add(column);
            }
        }
        if (unknown.Count > 0) {
            return Failed("CSV 存在未知列：" + string.Join(", ", unknown)
                + " / Unknown CSV columns: " + string.Join(", ", unknown));
        }

        for (int index = 1; index < lines.Count; index++) {
            int cellCount = lines[index].Split(',').Length;
            if (cellCount != header.Count) {
                return Failed("第 " + (index + 1) + " 行列数不匹配：期望 " + header.Count
                    + "，实际 " + cellCount + " / Row " + (index + 1) + " has " + cellCount
                    + " cells but the header has " + header.Count);
            }
        }

        return Results.Ok(new Bpp2dCsvSchema(detected, header, lines.Count - 1));
    }

    /// <summary>判断表头是否包含全部指定列 / Whether the header contains every specified column.</summary>
    /// <param name="header">表头 / The header.</param>
    /// <param name="required">必需的列 / The required columns.</param>
    /// <returns>是否全部包含 / Whether all are present.</returns>
    private static bool ContainsAll(IReadOnlyList<string> header, IReadOnlyList<string> required) {
        foreach (string column in required) {
            bool found = false;
            foreach (string present in header) {
                if (string.Equals(present, column, System.StringComparison.Ordinal)) {
                    found = true;
                    break;
                }
            }
            if (!found) {
                return false;
            }
        }
        return true;
    }

    /// <summary>构造失败结果 / Build a failure.</summary>
    /// <param name="message">描述 / The description.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<Bpp2dCsvSchema, ErrorCode, Error<ErrorCode>> Failed(string message) =>
        Results.Failed<Bpp2dCsvSchema>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
