// 关系查询编译器契约与来源注册表 / Relational query compiler contract and source registry.
// 的契约部分对应。SQL 编译器本体（`SqlRelationalQueryCompiler`）
// 随后单独补齐。
// The SQL compiler body
// (`SqlRelationalQueryCompiler`) is implemented in its own batch.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 关系数据库方言 / Relational database dialect.
/// </summary>
public enum RelationalQueryDialect {
    /// <summary>MySQL 方言 / MySQL dialect.</summary>
    MySql,

    /// <summary>PostgreSQL 方言 / PostgreSQL dialect.</summary>
    PostgreSql,

    /// <summary>SQLite 方言 / SQLite dialect.</summary>
    Sqlite,

    /// <summary>Oracle 方言 / Oracle dialect.</summary>
    Oracle,

    /// <summary>H2 方言 / H2 dialect.</summary>
    H2,
}

/// <summary>
/// 关系查询计划编译器 / Relational query plan compiler.
/// </summary>
/// <remarks>
/// 编译器实现负责校验计划、解析适配器白名单并生成后端查询对象。所有失败都通过结果类型返回；具体数据库驱动、
/// 连接与行映射不属于 framework 契约。
///
/// Implementations validate plans, resolve adapter allowlists and produce backend query objects. All failures are
/// returned through the result type; database drivers, connections and row mapping are outside the framework
/// contract.
/// </remarks>
/// <typeparam name="T">后端编译结果类型 / Backend-specific compiled query type.</typeparam>
public interface IRelationalQueryCompiler<T> {
    /// <summary>编译器使用的数据库方言 / Database dialect used by the compiler.</summary>
    RelationalQueryDialect Dialect { get; }

    /// <summary>
    /// 编译关系查询计划 / Compile a relational query plan.
    /// </summary>
    /// <param name="plan">待编译的关系查询计划 / Relational query plan to compile.</param>
    /// <returns>编译结果或结构化错误 / Compiled result or structured failure.</returns>
    Result<T, ErrorCode, Error<ErrorCode>> Compile(RelationalQueryPlan plan);
}

/// <summary>
/// 关系查询来源映射 / Relational query source mapping.
/// </summary>
/// <remarks>
/// <see cref="Name"/> 是计划中的逻辑来源名，<see cref="Table"/> 与 <see cref="Columns"/> 是适配器维护的物理白名单。
/// <see cref="Name"/> is the logical source name in a plan; <see cref="Table"/> and <see cref="Columns"/> are the
/// adapter-owned physical allowlists.
/// </remarks>
/// <param name="Name">逻辑来源名 / Logical source name.</param>
/// <param name="Table">物理表名 / Physical table name.</param>
/// <param name="Columns">逻辑字段路径到物理列名的映射 / Logical field path to physical column mapping.</param>
/// <param name="DefaultColumns">无显式投影时使用的字段；为空时取 <paramref name="Columns"/> 的键。</param>
public sealed record RelationalQuerySource(
    string Name,
    string Table,
    IReadOnlyDictionary<string, string>? Columns = null,
    IReadOnlyList<string>? DefaultColumns = null) {
    /// <summary>字段映射（非空）/ Field mapping (never null).</summary>
    public IReadOnlyDictionary<string, string> ColumnMap =>
        Columns ?? EmptyColumnMap.Value;

    /// <summary>
    /// 默认投影字段 / Default projection columns.
    /// </summary>
    /// <remarks>
    /// 未显式给出时取字段映射的键——**顺序即字典的枚举顺序**，因此上层应传入确定性有序字典，否则默认投影会
    /// 随实现细节变化。
    /// When not supplied explicitly this falls back to the column-map keys — the order being the dictionary's
    /// enumeration order, so callers should supply a deterministic ordered dictionary or the default projection
    /// will drift with implementation details.
    /// </remarks>
    public IReadOnlyList<string> Defaults =>
        DefaultColumns ?? new List<string>(ColumnMap.Keys);

    /// <summary>
    /// 通过查询来源创建映射 / Create a mapping from a query source.
    /// </summary>
    /// <param name="source">查询来源 / Query source.</param>
    /// <param name="table">物理表名 / Physical table name.</param>
    /// <param name="columns">字段映射 / Field mapping.</param>
    /// <param name="defaultColumns">默认投影字段 / Default projection columns.</param>
    /// <returns>来源映射 / Source mapping.</returns>
    public static RelationalQuerySource FromQuerySource(
        QuerySource source,
        string table,
        IReadOnlyDictionary<string, string>? columns = null,
        IReadOnlyList<string>? defaultColumns = null) {
        ArgumentNullException.ThrowIfNull(source);
        return new RelationalQuerySource(source.Name, table, columns, defaultColumns);
    }

    private static readonly Lazy<IReadOnlyDictionary<string, string>> EmptyColumnMap =
        new(static () => new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)));
}

/// <summary>
/// 关系查询来源注册表 / Relational query source registry.
/// </summary>
/// <remarks>
/// 注册表构造后不可变，因此同一个编译器可以安全地并发使用。构造时**深复制**每个来源的字段映射与默认投影，
/// 调用方在构造后修改自己的字典不会影响注册表。
///
/// The registry is immutable after construction, so one compiler can be used concurrently. Field mappings and
/// default projections are **copied** at construction, so a caller mutating its own dictionaries afterwards cannot
/// affect the registry.
/// </remarks>
public sealed class RelationalQuerySourceRegistry {
    private readonly List<RelationalQuerySource> _sources;

    /// <summary>
    /// 由来源映射序列构造 / Construct from a sequence of source mappings.
    /// </summary>
    /// <param name="definitions">来源映射 / Source mappings.</param>
    public RelationalQuerySourceRegistry(IEnumerable<RelationalQuerySource> definitions) {
        ArgumentNullException.ThrowIfNull(definitions);
        var copied = new List<RelationalQuerySource>();
        foreach (RelationalQuerySource definition in definitions) {
            ArgumentNullException.ThrowIfNull(definition);
            var columns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in definition.ColumnMap) {
                columns[entry.Key] = entry.Value;
            }
            var defaults = new List<string>(definition.Defaults);
            copied.Add(definition with {
                Columns = new ReadOnlyDictionary<string, string>(columns),
                DefaultColumns = new ReadOnlyCollection<string>(defaults),
            });
        }
        _sources = copied;
    }

    /// <summary>
    /// 由名称到来源映射构造 / Construct from a name-to-source mapping.
    /// </summary>
    /// <remarks>
    /// 条目名与来源自身的 <see cref="RelationalQuerySource.Name"/> 不一致时以**条目名**为准——否则查找键与
    /// 来源名会静默分叉。
    /// When an entry key differs from the source's own <see cref="RelationalQuerySource.Name"/>, the **entry key**
    /// wins; otherwise the lookup key and the source name would silently diverge.
    /// </remarks>
    /// <param name="definitions">名称到来源映射 / Name-to-source mappings.</param>
    public RelationalQuerySourceRegistry(
        IReadOnlyDictionary<string, RelationalQuerySource> definitions)
        : this(Rename(definitions)) {
    }

    /// <summary>来源映射（只读）/ Source mappings (read-only).</summary>
    public IReadOnlyList<RelationalQuerySource> Sources =>
        new ReadOnlyCollection<RelationalQuerySource>(_sources);

    /// <summary>空注册表 / Empty registry.</summary>
    public static RelationalQuerySourceRegistry Empty { get; } =
        new(Array.Empty<RelationalQuerySource>());

    /// <summary>
    /// 通过来源名查找映射 / Find a mapping by source name.
    /// </summary>
    /// <param name="name">来源名 / Source name.</param>
    /// <returns>映射；不存在时为 null / Mapping, or null when absent.</returns>
    public RelationalQuerySource? Find(string name) {
        ArgumentNullException.ThrowIfNull(name);
        foreach (RelationalQuerySource source in _sources) {
            if (string.Equals(source.Name, name, StringComparison.Ordinal)) {
                return source;
            }
        }
        return null;
    }

    private static IEnumerable<RelationalQuerySource> Rename(
        IReadOnlyDictionary<string, RelationalQuerySource> definitions) {
        ArgumentNullException.ThrowIfNull(definitions);
        var renamed = new List<RelationalQuerySource>(definitions.Count);
        foreach (KeyValuePair<string, RelationalQuerySource> entry in definitions) {
            RelationalQuerySource definition = entry.Value;
            renamed.Add(string.Equals(definition.Name, entry.Key, StringComparison.Ordinal)
                ? definition
                : definition with { Name = entry.Key });
        }
        return renamed;
    }
}

/// <summary>
/// 已编译的关系查询 / Compiled relational query.
/// </summary>
/// <remarks>
/// SQL 中的标量值全部由 <see cref="Parameters"/> 绑定，因此 <see cref="Sql"/> 不含业务字面量。
/// Scalar values in the SQL are all bound through <see cref="Parameters"/>, so <see cref="Sql"/> carries no
/// business literals.
/// </remarks>
/// <param name="Sql">参数化 SQL / Parameterized SQL.</param>
/// <param name="Parameters">SQL 参数 / SQL parameters.</param>
/// <param name="CanonicalHash">查询计划规范化哈希 / Canonical query-plan hash.</param>
public sealed record CompiledRelationalQuery(
    string Sql,
    IReadOnlyList<object?> Parameters,
    string CanonicalHash) {
    /// <summary>计划哈希别名 / Plan-hash alias.</summary>
    public string PlanHash => CanonicalHash;
}

/// <summary>
/// 关系查询编译错误类别 / Relational query compilation error category.
/// </summary>
public enum RelationalQueryCompilationErrorCategory {
    /// <summary>来源不存在 / Unknown source.</summary>
    UnknownSource,

    /// <summary>字段不存在或有歧义 / Unknown or ambiguous column.</summary>
    UnknownColumn,

    /// <summary>标识符不安全 / Unsafe identifier.</summary>
    InvalidIdentifier,

    /// <summary>Join 结构非法 / Invalid join.</summary>
    InvalidJoin,

    /// <summary>表达式不受支持 / Unsupported expression.</summary>
    UnsupportedExpression,

    /// <summary>分页或计划结构非法 / Invalid plan structure.</summary>
    InvalidPlan,

    /// <summary>方言不支持计划语义 / Dialect does not support the plan semantics.</summary>
    UnsupportedDialect,
}

/// <summary>
/// 关系查询结构化编译错误 / Structured relational query compilation error.
/// </summary>
/// <param name="Category">错误类别 / Error category.</param>
/// <param name="Field">发生错误的计划字段 / Plan field containing the error.</param>
/// <param name="Reason">错误原因 / Error reason.</param>
public sealed record RelationalQueryCompilationError(
    RelationalQueryCompilationErrorCategory Category,
    string Field,
    string Reason);
