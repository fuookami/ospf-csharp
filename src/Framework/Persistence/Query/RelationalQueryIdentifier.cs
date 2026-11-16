// 关系查询标识符与来源解析 / Relational query identifiers and source resolution.
// 与`quoteIdentifier`／`quoteIdentifierOrFallback`／`definition`／`column` 对应。这部分是 SQL 注入
// 的实际防线：所有物理表名与列名都必须经过这里，且允许字符集之外的输入一律结构化拒绝。
// The pinned Kotlin: /`quoteIdentifierOrFallback`/`definition`/`column`. This is
// the actual injection defence: every physical table and column name goes through here, and anything outside the
// permitted shape is rejected structurally.

using System;
using System.Collections.Generic;
using System.Text;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 关系查询标识符与来源解析器 / Relational query identifier and source resolver.
/// </summary>
internal static class RelationalQueryIdentifier {
    /// <summary>
    /// 引用标识符 / Quote an identifier.
    /// </summary>
    /// <remarks>
    /// 规则：裁剪空白；空串或含 NUL 拒绝；按 <c>.</c> 分段，**任何空分段都拒绝**（`a..b`、`.a`、`a.`
    /// 都是畸形标识符而不是"可以忽略的错误"）；每段用方言引号包裹并**把引号本身加倍**——不加倍的话，标识符里
    /// 的一个引号就能闭合引用串、把后续内容变成 SQL。
    ///
    /// Rules match the code: trim; reject empty or NUL-containing input; split on <c>.</c> and reject **any empty
    /// segment** (`a..b`, `.a`, `a.` are malformed identifiers, not "harmless mistakes"); wrap each segment in the
    /// dialect quote and **double the quote character inside** — without doubling, a single quote inside an
    /// identifier closes the quoted string and turns the remainder into SQL.
    /// </remarks>
    /// <param name="identifier">待引用标识符 / Identifier to quote.</param>
    /// <param name="dialect">SQL 方言 / SQL dialect.</param>
    /// <param name="field">计划字段路径，用于错误定位 / Plan field path for error reporting.</param>
    /// <returns>引用后的标识符或结构化错误 / The quoted identifier or a structured failure.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> Quote(
        string identifier,
        RelationalQueryDialect dialect,
        string field) {
        ArgumentNullException.ThrowIfNull(identifier);

        string value = identifier.Trim();
        if (value.Length == 0 || value.Contains('\0', StringComparison.Ordinal)) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.InvalidIdentifier,
                field,
                "标识符不能为空或包含 NUL / Identifier must not be blank or contain NUL");
        }

        string[] parts = value.Split('.');
        foreach (string part in parts) {
            if (string.IsNullOrWhiteSpace(part)) {
                return Failure<string>(
                    RelationalQueryCompilationErrorCategory.InvalidIdentifier,
                    field,
                    "标识符包含空分段 / Identifier contains an empty segment");
            }
        }

        char quote = QuoteCharacter(dialect);
        var text = new StringBuilder();
        for (int index = 0; index < parts.Length; index++) {
            if (index > 0) {
                text.Append('.');
            }
            AppendQuoted(text, parts[index], quote);
        }
        return Results.Ok<string>(text.ToString());
    }

    /// <summary>
    /// 引用标识符，失败时退化为不带校验的引用 / Quote an identifier, falling back to unchecked quoting.
    /// </summary>
    /// <remarks>
    /// 仅用于**已经过 <see cref="Quote"/> 校验**的限定符（别名）。直接用于外部输入会让失败校验形同虚设。
    /// Only for qualifiers (aliases) that **already passed <see cref="Quote"/>**; using it on external input would
    /// make the validation pointless.
    /// </remarks>
    /// <param name="identifier">标识符 / Identifier.</param>
    /// <param name="dialect">SQL 方言 / SQL dialect.</param>
    /// <returns>引用后的标识符 / The quoted identifier.</returns>
    public static string QuoteOrFallback(string identifier, RelationalQueryDialect dialect) {
        ArgumentNullException.ThrowIfNull(identifier);
        var text = new StringBuilder();
        AppendQuoted(text, identifier, QuoteCharacter(dialect));
        return text.ToString();
    }

    /// <summary>
    /// 解析来源定义 / Resolve a source definition.
    /// </summary>
    /// <remarks>
    /// 注册表为空时把逻辑名当作物理表名（既有行为）；注册表非空时**只允许白名单内的来源**，未知来源返回 null。
    /// 这个分支方向不能反：反了就等于空注册表变成"全部拒绝"、非空注册表变成"全部放行"。
    ///
    /// With an empty registry the logical name doubles as the physical table name (existing behaviour); a
    /// non-empty registry **only allows allowlisted sources** and returns null for anything else. The branch
    /// direction must not be reversed, or an empty registry would reject everything and a populated one would
    /// accept everything.
    /// </remarks>
    /// <param name="registry">来源注册表 / Source registry.</param>
    /// <param name="name">逻辑来源名 / Logical source name.</param>
    /// <returns>来源定义；不允许时为 null / The definition, or null when not allowed.</returns>
    public static RelationalQuerySource? Definition(
        RelationalQuerySourceRegistry registry,
        string name) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(name);

        RelationalQuerySource? found = registry.Find(name);
        if (found is not null) {
            return found;
        }
        return registry.Sources.Count == 0
            ? new RelationalQuerySource(name, name)
            : null;
    }

    /// <summary>
    /// 解析列 / Resolve a column.
    /// </summary>
    /// <remarks>
    /// 来源的字段映射为空时把逻辑路径直接当物理列名；非空时**必须命中白名单**，未知列返回结构化错误，绝不回落到
    /// 逻辑名——回落会让白名单失去意义。
    ///
    /// An empty column map uses the logical path as the physical name; a populated one **must hit the allowlist**,
    /// and an unknown column is a structured failure — never a fallback to the logical name, which would defeat the
    /// allowlist.
    /// </remarks>
    /// <param name="source">来源定义 / Source definition.</param>
    /// <param name="path">逻辑字段路径 / Logical field path.</param>
    /// <param name="field">计划字段路径 / Plan field path.</param>
    /// <returns>物理列名或结构化错误 / The physical column name or a structured failure.</returns>
    public static Result<string, ErrorCode, Error<ErrorCode>> ResolveColumn(
        RelationalQuerySource source,
        string path,
        string field) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);

        string logical = path.Trim();
        if (source.ColumnMap.Count == 0) {
            return Results.Ok<string>(logical);
        }
        if (source.ColumnMap.TryGetValue(logical, out string? physical)) {
            return Results.Ok<string>(physical);
        }
        return Failure<string>(
            RelationalQueryCompilationErrorCategory.UnknownColumn,
            field,
            $"未知查询字段：{source.Name}.{logical} / "
            + $"Unknown query column: {source.Name}.{logical}");
    }

    private static char QuoteCharacter(RelationalQueryDialect dialect) =>
        dialect == RelationalQueryDialect.MySql ? '`' : '"';

    private static void AppendQuoted(StringBuilder text, string value, char quote) {
        text.Append(quote);
        foreach (char character in value) {
            // 引号加倍：这是引用串里唯一能表达"字面引号"的方式。
            // Doubling is the only way to express a literal quote inside a quoted identifier.
            if (character == quote) {
                text.Append(quote);
            }
            text.Append(character);
        }
        text.Append(quote);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        RelationalQueryCompilationErrorCategory category,
        string field,
        string reason) =>
        Results.Failed<T>(new ExErr<ErrorCode, RelationalQueryCompilationError>(
            ErrorCode.IllegalArgument,
            reason,
            new RelationalQueryCompilationError(category, field, reason)));
}
