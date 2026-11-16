// 一维 CSV 数据集读取 / One-dimensional CSV dataset loading.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Application;

/// <summary>
/// 严格 CSV 读取边界 / Strict CSV boundary.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **长度与重量单位由调用方显式声明**，不从表头猜；**表头是封闭的**，多一列少一列都拒。
/// **数字解析器由调用方注入**，这样全程不经过 `double`，领域精度不会在中途掉一格。
///
/// Risk points worth naming up front (each has a criterion):
///
/// | 点 | 若放过 | 后果 |
/// | --- | --- | --- |
/// | 表头必须**完全匹配**（数量与集合都相等） | 少一列也能过 | 后面的 `GetValue` 拿到空串，静默变成 0 |
/// | 引号状态机 | 把 `"a,b"` 拆成两个字段 | 带逗号的名字被读坏 |
/// | `""` 转义 | 内嵌引号截断字段 | 同上 |
/// | 行宽校验 | 短行对齐到前一列 | 字段错位而**不报错** |
/// | 空行跳过 | 末尾换行当成一行空数据 | 宽度校验失败，好数据被拒 |
/// | 库存为空串 ⇒ `null` | 空串当 0 | 未声明库存的箱型被读成"库存 0" |
///
/// **Units are caller-declared, never guessed from the header**, the **header schema is closed**, and the **numeric parser is injected** so the whole path avoids
/// `double` and cannot lose domain precision midway. The risk points and their failure modes are in the table above.
/// </remarks>
public sealed class Bpp1dCsvDatasetLoader<V>
    where V : struct, IFloatingNumber<V> {
    private readonly global::System.Func<string, Result<V, ErrorCode, Error<ErrorCode>>> _parse;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="parse">数字解析器 / Numeric parser.</param>
    public Bpp1dCsvDatasetLoader(global::System.Func<string, Result<V, ErrorCode, Error<ErrorCode>>> parse) {
        _parse = parse;
    }

    /// <summary>
    /// 读取物品与箱型 / Read items and bin types.
    /// </summary>
    /// <param name="itemsText">物品 CSV / Item CSV.</param>
    /// <param name="binsText">箱型 CSV / Bin CSV.</param>
    /// <param name="lengthUnit">长度单位 / Length unit.</param>
    /// <param name="weightUnit">重量单位 / Weight unit.</param>
    /// <param name="spacing">间距 / Spacing.</param>
    /// <param name="objective">目标 / Objective.</param>
    /// <returns>验证后的输入 / Validated input.</returns>
    public Result<PackingProblem<V>, ErrorCode, Error<ErrorCode>> Load(
        string itemsText,
        string binsText,
        PhysicalUnit lengthUnit,
        PhysicalUnit weightUnit,
        Quantity<V> spacing,
        PackingObjective objective = PackingObjective.BinCount) {
        Result<IReadOnlyList<IReadOnlyDictionary<string, string>>, ErrorCode, Error<ErrorCode>> itemRows =
            Rows(itemsText, new[] { "id", "length", "weight", "amount" });
        if (itemRows is not Ok<IReadOnlyList<IReadOnlyDictionary<string, string>>, ErrorCode, Error<ErrorCode>> itemOk) {
            return Propagate<PackingProblem<V>, IReadOnlyList<IReadOnlyDictionary<string, string>>>(itemRows);
        }
        Result<IReadOnlyList<IReadOnlyDictionary<string, string>>, ErrorCode, Error<ErrorCode>> binRows =
            Rows(binsText, new[] { "id", "length", "max_weight", "cost", "inventory" });
        if (binRows is not Ok<IReadOnlyList<IReadOnlyDictionary<string, string>>, ErrorCode, Error<ErrorCode>> binOk) {
            return Propagate<PackingProblem<V>, IReadOnlyList<IReadOnlyDictionary<string, string>>>(binRows);
        }

        var items = new List<Item<V>>();
        foreach (IReadOnlyDictionary<string, string> row in itemOk.Value) {
            Result<V, ErrorCode, Error<ErrorCode>> length = _parse(row["length"]);
            if (length is not Ok<V, ErrorCode, Error<ErrorCode>> lengthOk) {
                return Propagate<PackingProblem<V>, V>(length);
            }
            Result<V, ErrorCode, Error<ErrorCode>> weight = _parse(row["weight"]);
            if (weight is not Ok<V, ErrorCode, Error<ErrorCode>> weightOk) {
                return Propagate<PackingProblem<V>, V>(weight);
            }
            if (!ulong.TryParse(row["amount"], out ulong amount)) {
                return Invalid<PackingProblem<V>>("invalid amount");
            }
            items.Add(new Item<V>(
                Id: row["id"],
                Length: new Quantity<V>(lengthOk.Value, lengthUnit),
                Weight: new Quantity<V>(weightOk.Value, weightUnit),
                Amount: amount));
        }

        var bins = new List<BinType<V>>();
        foreach (IReadOnlyDictionary<string, string> row in binOk.Value) {
            Result<V, ErrorCode, Error<ErrorCode>> length = _parse(row["length"]);
            if (length is not Ok<V, ErrorCode, Error<ErrorCode>> lengthOk) {
                return Propagate<PackingProblem<V>, V>(length);
            }
            Result<V, ErrorCode, Error<ErrorCode>> weight = _parse(row["max_weight"]);
            if (weight is not Ok<V, ErrorCode, Error<ErrorCode>> weightOk) {
                return Propagate<PackingProblem<V>, V>(weight);
            }
            Result<V, ErrorCode, Error<ErrorCode>> cost = _parse(row["cost"]);
            if (cost is not Ok<V, ErrorCode, Error<ErrorCode>> costOk) {
                return Propagate<PackingProblem<V>, V>(cost);
            }
            // **库存空串表示"未声明"，不是 0。** 两者在模型上的后果完全不同：
            // 未声明库存 = 箱型不限量；库存 0 = 这个箱型一件也不能用。
            //
            // **An empty inventory cell means "not declared", not zero.** The two mean opposite things in the model: undeclared inventory is unlimited, while zero
            // means the bin type cannot be used at all.
            string stock = row["inventory"];
            ulong? inventory = null;
            if (stock.Length > 0) {
                if (!ulong.TryParse(stock, out ulong parsed)) {
                    return Invalid<PackingProblem<V>>("invalid inventory");
                }
                inventory = parsed;
            }
            bins.Add(new BinType<V>(
                Id: row["id"],
                Length: new Quantity<V>(lengthOk.Value, lengthUnit),
                MaxWeight: new Quantity<V>(weightOk.Value, weightUnit),
                Cost: costOk.Value,
                Inventory: inventory));
        }

        var problem = new PackingProblem<V>(
            Items: items,
            BinTypes: bins,
            Objective: objective,
            Spacing: spacing);
        Try validation = problem.Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PackingProblem<V>, Success>(validation);
        }
        return Results.Ok(problem);
    }

    /// <summary>
    /// 切分行列 / Split text into rows and fields.
    /// </summary>
    /// <param name="text">CSV 文本 / CSV text.</param>
    /// <param name="schema">表头 / The header schema.</param>
    /// <returns>行到字段的映射 / Rows mapped to fields.</returns>
    private Result<IReadOnlyList<IReadOnlyDictionary<string, string>>, ErrorCode, Error<ErrorCode>> Rows(
        string text, IReadOnlyList<string> schema) {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var value = new global::System.Text.StringBuilder();
        bool quoted = false;
        bool closed = false;
        int index = 0;
        string Trimmed() => value.ToString().Trim();

        while (index < text.Length) {
            char ch = text[index++];
            if (quoted && ch == '"') {
                if (index < text.Length && text[index] == '"') {
                    value.Append('"');
                    ++index;
                }
                else {
                    quoted = false;
                    closed = true;
                }
            }
            else if (quoted) {
                value.Append(ch);
            }
            else if (ch == '"' && value.Length == 0 && !closed) {
                quoted = true;
            }
            else if (ch == ',') {
                fields.Add(Trimmed());
                value.Clear();
                closed = false;
            }
            else if (ch == '\n' || ch == '\r') {
                if (ch == '\r' && index < text.Length && text[index] == '\n') {
                    ++index;
                }
                fields.Add(Trimmed());
                value.Clear();
                closed = false;
                if (AnyNonEmpty(fields)) {
                    records.Add(new List<string>(fields));
                }
                fields.Clear();
            }
            else if (ch == '"' || (closed && char.IsWhiteSpace(ch) == false)) {
                return Invalid<IReadOnlyList<IReadOnlyDictionary<string, string>>>("malformed quote");
            }
            else {
                value.Append(ch);
            }
        }

        if (quoted) {
            return Invalid<IReadOnlyList<IReadOnlyDictionary<string, string>>>("unclosed quote");
        }
        fields.Add(Trimmed());
        if (AnyNonEmpty(fields)) {
            records.Add(new List<string>(fields));
        }

        if (records.Count == 0) {
            return Invalid<IReadOnlyList<IReadOnlyDictionary<string, string>>>("missing header");
        }
        var header = new List<string>();
        foreach (string name in records[0]) {
            // **用码点比较，不用字符串转义字面量。** 第一版写的是 `StartsWith(<转义 BOM>)`，实测每个表头字段都被砍掉第一个字符
            // （`id` 变 `d`），而报错说的是"表头不匹配"——**错误信息把我引向了错误的列**。
            //
            // **Compare code points instead of an escaped string literal.** With the first version every header field lost its first character (`id` became `d`) while
            // the error still said "schema mismatch".
            header.Add(name.Length > 0 && name[0] == (char)0xFEFF ? name[1..] : name);
        }
        var expected = new HashSet<string>(schema);
        var actual = new HashSet<string>(header);
        if (header.Count != schema.Count || expected.SetEquals(actual) == false) {
            return Invalid<IReadOnlyList<IReadOnlyDictionary<string, string>>>(
                $"schema must be [{string.Join(',', schema)}]");
        }
        for (int rowIndex = 1; rowIndex < records.Count; ++rowIndex) {
            if (records[rowIndex].Count != header.Count) {
                return Invalid<IReadOnlyList<IReadOnlyDictionary<string, string>>>("row width");
            }
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (int rowIndex = 1; rowIndex < records.Count; ++rowIndex) {
            var map = new Dictionary<string, string>();
            for (int column = 0; column < header.Count; ++column) {
                map[header[column]] = records[rowIndex][column];
            }
            rows.Add(map);
        }
        return Results.Ok<IReadOnlyList<IReadOnlyDictionary<string, string>>>(rows);
    }

    /// <summary>是否有任一非空字段 / Whether any field is non-empty.</summary>
    /// <param name="fields">字段 / The fields.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private static bool AnyNonEmpty(IReadOnlyList<string> fields) {
        foreach (string field in fields) {
            if (field.Length > 0) {
                return true;
            }
        }
        return false;
    }

    /// <summary>构造非法输入失败 / Build an invalid-input failure.</summary>
    /// <typeparam name="T">目标成功类型 / Target success type.</typeparam>
    /// <param name="reason">原因 / The reason.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Invalid<T>(string reason)
        where T : notnull =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument, $"CSV 输入无效 / Invalid CSV input: {reason}"));

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
