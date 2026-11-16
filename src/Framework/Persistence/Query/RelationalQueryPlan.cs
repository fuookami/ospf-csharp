// 关系查询计划容器 / Relational query plan container.
// 与固定 Kotlin 基线 `RelationalQueryPlan.kt` 对应。这里只落地**规范化与结构校验**；表达式深度冻结、
// 表达式循环检测、Join 图关联校验、canonical／hash 分别留到后续批次（见各成员注释）。
// Mirrors the pinned Kotlin baseline's `RelationalQueryPlan.kt`. lands **normalization and
// structural validation** only; deep expression freezing, expression-cycle detection, join-graph correlation
// checking and canonical/hash are deliberately left to later batches (see the member comments).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 关系查询校验错误 / Relational query validation error.
/// </summary>
/// <param name="Field">出错字段的路径 / Path of the offending field.</param>
/// <param name="Message">错误描述 / Error description.</param>
public sealed record RelationalQueryValidationError(string Field, string Message);

/// <summary>
/// 关系查询计划容器 / Relational query plan container.
/// </summary>
/// <remarks>
/// **已实现**：构造时的空白裁剪与只读化、表达式深度冻结（含自定义 payload 复制）、结构校验（名称／别名、
/// Join 数量与深度、投影数量、分页、空列引用）。
///
/// **这里刻意未实现**（基线的同名能力，按依赖顺序排在后续批次）：
/// <list type="bullet">
/// <item>树级表达式循环检测：C# 表达式节点是不可变记录、只能自底向上构造，结构上无法成环，因此没有对应
/// 实现——移植过来会是不可达代码。详见 <see cref="RelationalQueryExpressionFreezer"/>。</item>
/// <item>不可复制 payload 的结构化拒绝：当前对未知 payload 类型原样保留而不是报错。</item>
/// <item>Join 图关联校验（关联条件必须把被 Join 的数据源与已绑定的数据源关联起来）。</item>
/// <item><c>canonical</c> / <c>canonicalHash</c> / <c>hash</c>。</item>
/// </list>
///
/// **Implemented**: whitespace trimming, read-only wrapping and deep expression freezing (custom payloads
/// copied) at construction, plus structural validation (names/aliases, join count and depth, projection count,
/// paging, empty column references).
///
/// **Deliberately not implemented** (deferred by dependency order): tree-level expression-cycle detection (a C#
/// expression tree cannot cycle structurally — see <see cref="RelationalQueryExpressionFreezer"/>), structured
/// rejection of uncopyable payloads (unknown payloads are currently kept as-is), join-graph correlation
/// validation, and <c>canonical</c>/<c>canonicalHash</c>/<c>hash</c>.
/// </remarks>
public sealed class RelationalQueryPlan {
    /// <summary>构造计划 / Construct a plan.</summary>
    /// <param name="root">根数据源 / Root source.</param>
    /// <param name="joins">按顺序应用的 Join 规格 / Join specifications applied in order.</param>
    /// <param name="predicate">根查询谓词 / Root query predicate.</param>
    /// <param name="projections">返回投影 / Returned projections.</param>
    /// <param name="distinct">是否对投影去重 / Whether to distinct the projection.</param>
    /// <param name="groupBy">分组列 / Grouping columns.</param>
    /// <param name="orderBy">排序规格 / Ordering specifications.</param>
    /// <param name="page">分页规格 / Optional pagination specification.</param>
    /// <param name="rootKey">根粒度计数使用的根键 / Root key used for root-granularity counts.</param>
    public RelationalQueryPlan(
        QuerySource root,
        IReadOnlyList<JoinSpec>? joins = null,
        BooleanExpression? predicate = null,
        IReadOnlyList<ProjectionSpec>? projections = null,
        bool distinct = false,
        IReadOnlyList<ColumnRef>? groupBy = null,
        IReadOnlyList<OrderSpec>? orderBy = null,
        PageSpec? page = null,
        IReadOnlyList<ColumnRef>? rootKey = null) {
        ArgumentNullException.ThrowIfNull(root);

        Root = Normalize(root);
        Joins = FreezeList(joins ?? Array.Empty<JoinSpec>(), Normalize);
        Predicate = predicate is null
            ? null
            : RelationalQueryExpressionFreezer.Freeze(predicate);
        Projections = FreezeList(projections ?? Array.Empty<ProjectionSpec>(), Normalize);
        Distinct = distinct;
        GroupBy = FreezeList(groupBy ?? Array.Empty<ColumnRef>(), Normalize);
        OrderBy = FreezeList(orderBy ?? Array.Empty<OrderSpec>(), Normalize);
        Page = page;
        RootKey = FreezeList(rootKey ?? Array.Empty<ColumnRef>(), Normalize);
    }

    /// <summary>根数据源 / Root source.</summary>
    public QuerySource Root { get; }

    /// <summary>按顺序应用的 Join 规格 / Join specifications applied in order.</summary>
    public IReadOnlyList<JoinSpec> Joins { get; }

    /// <summary>根查询谓词 / Root query predicate.</summary>
    /// <remarks>已冻结：构造时深度复制 / Frozen at construction.</remarks>
    public BooleanExpression? Predicate { get; }

    /// <summary>返回投影 / Returned projections.</summary>
    public IReadOnlyList<ProjectionSpec> Projections { get; }

    /// <summary>是否对投影去重 / Whether to distinct the projection.</summary>
    public bool Distinct { get; }

    /// <summary>分组列 / Grouping columns.</summary>
    public IReadOnlyList<ColumnRef> GroupBy { get; }

    /// <summary>排序规格 / Ordering specifications.</summary>
    public IReadOnlyList<OrderSpec> OrderBy { get; }

    /// <summary>分页规格 / Optional pagination specification.</summary>
    public PageSpec? Page { get; }

    /// <summary>根粒度计数使用的根键 / Root key used for root-granularity counts.</summary>
    public IReadOnlyList<ColumnRef> RootKey { get; }

    /// <summary>
    /// 校验计划结构 / Validate plan structure.
    /// </summary>
    /// <param name="limits">计划限制 / Plan limits.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate(
        RelationalQueryLimits? limits = null) {
        RelationalQueryLimits effective = limits ?? new RelationalQueryLimits();

        if (string.IsNullOrWhiteSpace(Root.Name)) {
            return Failure("根数据源名称不能为空 / Root source name must not be blank", "root");
        }
        if (Root.Alias is not null && Root.Alias.Length == 0) {
            return Failure("根数据源别名不能为空 / Root source alias must not be blank", "root.alias");
        }
        if (string.Equals(Root.Alias, Root.Name, StringComparison.Ordinal)) {
            return Failure(
                "根数据源别名不能与名称相同 / Root source alias must differ from its name",
                "root.alias");
        }
        if (Joins.Count > effective.MaxJoins) {
            return Failure("Join 数量超出限制 / Join count exceeds configured limit", "joins");
        }
        if (Joins.Count + 1 > effective.MaxDepth) {
            return Failure("Join 深度超出限制 / Join depth exceeds configured limit", "joins");
        }
        if (Projections.Count > effective.MaxProjections) {
            return Failure(
                "投影数量超出限制 / Projection count exceeds configured limit",
                "projections");
        }
        if (Predicate is { } predicate) {
            string? unsupported =
                RelationalQueryExpressionFreezer.FindUnsupportedPayload(predicate, "predicate");
            if (unsupported is not null) {
                return Failure(
                    "自定义表达式 payload 无法安全复制，请使用不可变标量或框架支持的容器 / "
                    + "Custom expression payload cannot be safely copied; use an immutable scalar or a known "
                    + "container",
                    unsupported);
            }
        }
        for (int index = 0; index < Joins.Count; index++) {
            string? unsupported = RelationalQueryExpressionFreezer.FindUnsupportedPayload(
                Joins[index].Condition, $"joins[{index}].condition");
            if (unsupported is not null) {
                return Failure(
                    "自定义表达式 payload 无法安全复制，请使用不可变标量或框架支持的容器 / "
                    + "Custom expression payload cannot be safely copied; use an immutable scalar or a known "
                    + "container",
                    unsupported);
            }
        }
        if (Page is { } page && (page.Limit is <= 0 || page.Offset < 0)) {
            return Failure(
                "分页上限（如指定）必须为正数且偏移量不能为负数 / "
                + "Page limit, when specified, must be positive and offset must not be negative",
                "page");
        }
        if (HasEmptyColumn(RootKey)) {
            return Failure(
                "根键包含空列引用 / Root key contains an empty column reference",
                "rootKey");
        }
        foreach (ProjectionSpec projection in Projections) {
            if (IsEmpty(projection.Column)) {
                return Failure(
                    "投影包含空列引用 / Projection contains an empty column reference",
                    "projections");
            }
        }
        if (HasEmptyColumn(GroupBy)) {
            return Failure(
                "分组包含空列引用 / Group by contains an empty column reference",
                "groupBy");
        }
        foreach (OrderSpec order in OrderBy) {
            if (IsEmpty(order.Column)) {
                return Failure(
                    "排序包含空列引用 / Order by contains an empty column reference",
                    "orderBy");
            }
        }
        return ValidateJoinGraph();
    }

    /// <summary>
    /// 校验 Join 图 / Validate the join graph.
    /// </summary>
    /// <remarks>
    /// 数据源命名必须无歧义（名称不得与任何别名相同、不得重复），关联条件必须引用已绑定的数据源，并且**确实
    /// 存在**一条目标侧与已绑定侧之间的列对列关联——否则该 Join 退化成笛卡尔积，行数会静默膨胀。
    /// 相关语义与基线一致：`AND` 任一支路满足即可，`OR` 要求每个分支都保留关联，`NOT` 递归检查；
    /// 关联条件里出现布尔常量直接拒绝（`WHERE true` 式的 Join 就是笛卡尔积）。
    ///
    /// Source naming must be unambiguous (no name equal to any alias, no duplicates), conditions must reference
    /// already bound sources, and a column-to-column correlation between the joined side and a bound side must
    /// **actually exist** — otherwise the join degrades into a cross product and the row count silently inflates.
    /// The semantics match the baseline: `AND` needs any branch, `OR` needs every branch, `NOT` recurses, and a
    /// boolean constant in the condition is rejected outright (a `WHERE true` join is a cross product).
    /// </remarks>
    /// <returns>校验结果 / Validation result.</returns>
    private Result<Success, ErrorCode, Error<ErrorCode>> ValidateJoinGraph() {
        var names = new HashSet<string>(StringComparer.Ordinal) { Root.Name };
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var boundSources = new HashSet<string>(StringComparer.Ordinal) { Root.Name };
        if (Root.Alias is { } rootAlias) {
            aliases.Add(rootAlias);
            boundSources.Add(rootAlias);
        }

        for (int index = 0; index < Joins.Count; index++) {
            JoinSpec join = Joins[index];
            string source = $"joins[{index}].source";
            if (string.IsNullOrWhiteSpace(join.Source.Name)) {
                return Failure("Join 数据源名称不能为空 / Join source name must not be blank", source);
            }
            if (aliases.Contains(join.Source.Name)) {
                return Failure(
                    $"Join 数据源名称与别名冲突：{join.Source.Name} / "
                    + $"Join source name conflicts with an alias: {join.Source.Name}",
                    source);
            }
            if (!names.Add(join.Source.Name)) {
                return Failure(
                    $"Join 数据源重复：{join.Source.Name} / Join source is duplicated: {join.Source.Name}",
                    source);
            }
            var targetSources = new HashSet<string>(StringComparer.Ordinal) { join.Source.Name };
            if (join.Source.Alias is { } alias) {
                if (alias.Length == 0) {
                    return Failure(
                        "Join 数据源别名不能为空 / Join source alias must not be blank",
                        $"{source}.alias");
                }
                if (string.Equals(alias, join.Source.Name, StringComparison.Ordinal) ||
                    names.Contains(alias) || aliases.Contains(alias)) {
                    return Failure(
                        $"Join 数据源别名有歧义：{alias} / Join source alias is ambiguous: {alias}",
                        $"{source}.alias");
                }
                if (!aliases.Add(alias)) {
                    return Failure(
                        $"Join 数据源别名重复：{alias} / Join source alias is duplicated: {alias}",
                        $"{source}.alias");
                }
                targetSources.Add(alias);
            }

            string condition = $"joins[{index}].condition";
            if (join.Condition.CollectReferences().Count == 0) {
                return Failure(
                    "Join 关联条件必须包含限定的列关联 / "
                    + "Join condition must contain a qualified column relationship",
                    condition);
            }
            if (RelationalQueryJoinValidator.ContainsBooleanConstant(join.Condition)) {
                return Failure(
                    "Join 关联条件不能包含布尔常量 / "
                    + "Join condition must not contain a boolean constant",
                    condition);
            }
            foreach (Fuookami.Ospf.Math.Symbol.Expression.PropertyPath reference
                in join.Condition.CollectReferences()) {
                string? qualifier =
                    RelationalQueryJoinValidator.SourceQualifier(reference.Value);
                if (qualifier is not null &&
                    !boundSources.Contains(qualifier) && !targetSources.Contains(qualifier)) {
                    return Failure(
                        $"Join 关联条件引用了未知或尚未绑定的数据源：{qualifier} / "
                        + $"Join condition references an unknown or not-yet-bound source: {qualifier}",
                        condition);
                }
            }
            if (!RelationalQueryJoinValidator.ContainsExactColumnCorrelation(
                join.Condition, targetSources, boundSources)) {
                return Failure(
                    "Join 关联条件必须把被 Join 的数据源与已绑定的数据源关联起来 / "
                    + "Join condition must relate the joined source to an already bound source",
                    condition);
            }
            if (join.Type == JoinType.Exists && !join.Cardinality.IsToMany()) {
                return Failure(
                    "Exists Join 必须声明多行基数 / "
                    + "Exists join requires a to-many cardinality declaration",
                    $"joins[{index}].cardinality");
            }
            if (join.Type != JoinType.Exists) {
                boundSources.UnionWith(targetSources);
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 返回稳定的计划表示 / Return a stable representation of this plan.
    /// </summary>
    /// <returns>规范化计划字符串 / Canonical plan string.</returns>
    public string Canonical() => RelationalQueryPlanCanonical.Render(this);

    /// <summary>
    /// 获取规范化计划的 SHA-256 / Get the SHA-256 of the canonical plan.
    /// </summary>
    /// <returns>十六进制 SHA-256 摘要 / Hexadecimal SHA-256 digest.</returns>
    public string CanonicalHash() =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Canonical())));

    /// <summary>
    /// 获取规范化计划哈希（审计 API 的简短别名）/ Get the normalized plan hash (short audit alias).
    /// </summary>
    /// <returns>十六进制 SHA-256 摘要 / Hexadecimal SHA-256 digest.</returns>
    public string Hash() => CanonicalHash();

    private static bool HasEmptyColumn(IReadOnlyList<ColumnRef> columns) {
        foreach (ColumnRef column in columns) {
            if (IsEmpty(column)) {
                return true;
            }
        }
        return false;
    }

    private static bool IsEmpty(ColumnRef column) =>
        string.IsNullOrWhiteSpace(column.Source) || string.IsNullOrWhiteSpace(column.Path);

    private static Result<Success, ErrorCode, Error<ErrorCode>> Failure(
        string message,
        string field) =>
        Results.Failed<Success>(new ExErr<ErrorCode, RelationalQueryValidationError>(
            ErrorCode.IllegalArgument,
            message,
            new RelationalQueryValidationError(field, message)));

    private static IReadOnlyList<T> FreezeList<T>(IReadOnlyList<T> values, Func<T, T> map) {
        var mapped = new T[values.Count];
        for (int index = 0; index < values.Count; index++) {
            mapped[index] = map(values[index]);
        }
        return new ReadOnlyCollection<T>(mapped);
    }

    private static QuerySource Normalize(QuerySource source) =>
        new(source.Name.Trim(), source.Alias?.Trim());

    private static ColumnRef Normalize(ColumnRef column) =>
        new(column.Source.Trim(), column.Path.Trim());

    private static ProjectionSpec Normalize(ProjectionSpec projection) =>
        new(Normalize(projection.Column), projection.Alias?.Trim());

    private static OrderSpec Normalize(OrderSpec order) =>
        new(Normalize(order.Column), order.Direction, order.Nulls);

    private static JoinSpec Normalize(JoinSpec join) =>
        new(
            join.Type,
            Normalize(join.Source),
            RelationalQueryExpressionFreezer.Freeze(join.Condition),
            join.Cardinality);
}

/// <summary>
/// 关系查询数据源 / Relational query source.
/// </summary>
/// <param name="Name">适配器注册的数据源名称 / Adapter-registered source name.</param>
/// <param name="Alias">查询计划中使用的可选别名 / Optional alias used by the query plan.</param>
public sealed record QuerySource(string Name, string? Alias = null);

/// <summary>
/// 关系查询列引用 / Relational query column reference.
/// </summary>
/// <param name="Source">数据源名称或别名 / Source name or alias.</param>
/// <param name="Path">适配器注册的字段路径 / Adapter-registered field path.</param>
public sealed record ColumnRef(string Source, string Path);

/// <summary>
/// Join 类型 / Join type.
/// </summary>
public enum JoinType {
    /// <summary>内连接 / Inner join.</summary>
    Inner,

    /// <summary>左外连接 / Left outer join.</summary>
    Left,

    /// <summary>相关存在性半连接 / Correlated existence semi-join.</summary>
    Exists,
}

/// <summary>
/// Join 基数 / Join cardinality.
/// </summary>
public enum JoinCardinality {
    /// <summary>一对一 / One-to-one.</summary>
    OneToOne,

    /// <summary>多对一 / Many-to-one.</summary>
    ManyToOne,

    /// <summary>一对多 / One-to-many.</summary>
    OneToMany,

    /// <summary>多对多 / Many-to-many.</summary>
    ManyToMany,
}

/// <summary>Join 基数扩展 / Join cardinality extensions.</summary>
public static class JoinCardinalityExtensions {
    /// <summary>
    /// 是否为目标侧多行关系 / Whether the target side may contain multiple rows.
    /// </summary>
    /// <param name="cardinality">基数 / Cardinality.</param>
    /// <returns>目标侧是否可能多行 / Whether the target side may have multiple rows.</returns>
    public static bool IsToMany(this JoinCardinality cardinality) =>
        cardinality is JoinCardinality.OneToMany or JoinCardinality.ManyToMany;
}

/// <summary>
/// Join 规格 / Join specification.
/// </summary>
/// <param name="Type">Join 类型 / Join type.</param>
/// <param name="Source">待加入的数据源 / Source to join.</param>
/// <param name="Condition">Join 关联条件 / Join correlation condition.</param>
/// <param name="Cardinality">声明的数据源基数 / Declared source cardinality.</param>
public sealed record JoinSpec(
    JoinType Type,
    QuerySource Source,
    BooleanExpression Condition,
    JoinCardinality Cardinality);

/// <summary>
/// 投影字段 / Projection field.
/// </summary>
/// <param name="Column">要返回的列 / Column to return.</param>
/// <param name="Alias">返回结果中的可选别名 / Optional alias in the returned result.</param>
public sealed record ProjectionSpec(ColumnRef Column, string? Alias = null);

/// <summary>
/// 排序方向 / Sort direction.
/// </summary>
public enum SortDirection {
    /// <summary>升序 / Ascending order.</summary>
    Ascending,

    /// <summary>降序 / Descending order.</summary>
    Descending,
}

/// <summary>
/// NULL 排序位置 / NULL ordering.
/// </summary>
public enum NullsOrder {
    /// <summary>不指定 NULL 位置 / Do not specify NULL placement.</summary>
    Unspecified,

    /// <summary>NULL 排在前面 / NULL values first.</summary>
    First,

    /// <summary>NULL 排在后面 / NULL values last.</summary>
    Last,
}

/// <summary>
/// 排序规格 / Order specification.
/// </summary>
/// <param name="Column">要排序的列 / Column to sort by.</param>
/// <param name="Direction">排序方向 / Sort direction.</param>
/// <param name="Nulls">NULL 值排序位置 / NULL value placement.</param>
public sealed record OrderSpec(
    ColumnRef Column,
    SortDirection Direction = SortDirection.Ascending,
    NullsOrder Nulls = NullsOrder.Unspecified);

/// <summary>
/// 分页规格 / Page specification.
/// </summary>
/// <param name="Limit">单页最大记录数，null 表示不设上限 / Maximum records per page; null means unbounded.</param>
/// <param name="Offset">跳过的记录数 / Number of records to skip.</param>
public sealed record PageSpec(int? Limit = null, int Offset = 0);

/// <summary>
/// 查询计划限制 / Query plan limits.
/// </summary>
/// <param name="MaxJoins">允许的最大 Join 数 / Maximum number of allowed joins.</param>
/// <param name="MaxDepth">允许的最大数据源深度 / Maximum allowed source depth.</param>
/// <param name="MaxProjections">允许的最大投影字段数 / Maximum number of allowed projections.</param>
public sealed record RelationalQueryLimits(
    int MaxJoins = 16,
    int MaxDepth = 16,
    int MaxProjections = 128);
