// 纯参数化 SQL 编译器 / Pure parameterized SQL compiler.
// 与`SqlRelationalQueryCompiler` 对应。不连接数据库、不依赖任何 ORM：只把关系查询计划编译成
// 参数化 SQL。所有标量字面量都进参数列表，SQL 字符串里不含业务值。
// it compiles a relational query plan into parameterized SQL. Every scalar literal becomes a parameter, so the SQL
// text carries no business values.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 纯参数化 SQL 编译器 / Pure parameterized SQL compiler.
/// </summary>
/// <remarks>
/// **一处与实现差异**：Kotlin 的泛型模式匹配直接解构 <c>Comparison&lt;*&gt;</c> 等节点；C# 的泛型
/// 实参在运行时才确定，单一模式覆盖不了全部实例化，因此统一按属性反射取值。取值方式与
/// <see cref="RelationalQueryJoinValidator"/>／<see cref="RelationalQueryExpressionFreezer"/> 一致，三者共用同一
/// 套约定。
///
/// **A difference from the code**: Kotlin destructures <c>Comparison&lt;*&gt;</c> and friends through generic
/// pattern matching; a C# generic argument is only known at run time so one pattern cannot cover every
/// instantiation, and properties are read reflectively instead. The convention is shared with
/// <see cref="RelationalQueryJoinValidator"/> and <see cref="RelationalQueryExpressionFreezer"/>.
/// </remarks>
public sealed class SqlRelationalQueryCompiler : IRelationalQueryCompiler<CompiledRelationalQuery> {
    private readonly RelationalQuerySourceRegistry _registry;

    /// <summary>
    /// 构造编译器 / Construct a compiler.
    /// </summary>
    /// <param name="dialect">SQL 方言 / SQL dialect.</param>
    /// <param name="sourceRegistry">来源白名单；为空表示不做白名单校验 / Source allowlist; null means no allowlist.</param>
    public SqlRelationalQueryCompiler(
        RelationalQueryDialect dialect,
        RelationalQuerySourceRegistry? sourceRegistry = null) {
        Dialect = dialect;
        _registry = sourceRegistry ?? RelationalQuerySourceRegistry.Empty;
    }

    /// <summary>
    /// 由来源列表构造 / Construct from a sequence of sources.
    /// </summary>
    /// <param name="dialect">SQL 方言 / SQL dialect.</param>
    /// <param name="sources">来源映射 / Source mappings.</param>
    public SqlRelationalQueryCompiler(
        RelationalQueryDialect dialect,
        IEnumerable<RelationalQuerySource> sources)
        : this(dialect, new RelationalQuerySourceRegistry(sources)) {
    }

    /// <summary>
    /// 由名称映射构造 / Construct from a name-to-source mapping.
    /// </summary>
    /// <param name="dialect">SQL 方言 / SQL dialect.</param>
    /// <param name="sources">名称到来源映射 / Name-to-source mappings.</param>
    public SqlRelationalQueryCompiler(
        RelationalQueryDialect dialect,
        IReadOnlyDictionary<string, RelationalQuerySource> sources)
        : this(dialect, new RelationalQuerySourceRegistry(sources)) {
    }

    /// <inheritdoc/>
    public RelationalQueryDialect Dialect { get; }

    /// <inheritdoc/>
    public Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> Compile(
        RelationalQueryPlan plan) {
        ArgumentNullException.ThrowIfNull(plan);

        Result<Success, ErrorCode, Error<ErrorCode>> validation = plan.Validate();
        if (!validation.IsOk) {
            RelationalQueryValidationError? detail = validation switch {
                Failed<Success, ErrorCode, Error<ErrorCode>> failed =>
                    failed.Error is ExErr<ErrorCode, RelationalQueryValidationError> valued
                        ? valued.TypedValue
                        : null,
                _ => null,
            };
            return Failure<CompiledRelationalQuery>(
                RelationalQueryCompilationErrorCategory.InvalidPlan,
                detail?.Field ?? "plan",
                detail?.Message ?? "查询计划校验失败 / Query plan validation failed");
        }

        var state = new SqlState();
        var bound = new Dictionary<string, BoundSource>(StringComparer.Ordinal);
        var joinParameters = new List<object?>();
        var existsParameters = new List<object?>();
        var joinSql = new List<string>();
        var existsSql = new List<string>();

        RelationalQuerySource? rootDefinition =
            RelationalQueryIdentifier.Definition(_registry, plan.Root.Name);
        if (rootDefinition is null) {
            return Failure<CompiledRelationalQuery>(
                RelationalQueryCompilationErrorCategory.UnknownSource,
                "root",
                $"未知根查询来源：{plan.Root.Name} / Unknown root query source: {plan.Root.Name}");
        }
        Result<BoundSource, ErrorCode, Error<ErrorCode>> rootBound =
            Bind(rootDefinition, plan.Root, "root");
        if (!rootBound.IsOk) {
            return Propagate<CompiledRelationalQuery, BoundSource>(rootBound);
        }
        AddBound(bound, rootBound.Value);

        for (int index = 0; index < plan.Joins.Count; index++) {
            JoinSpec join = plan.Joins[index];
            string sourceField = $"joins[{index}].source";
            RelationalQuerySource? definition =
                RelationalQueryIdentifier.Definition(_registry, join.Source.Name);
            if (definition is null) {
                return Failure<CompiledRelationalQuery>(
                    RelationalQueryCompilationErrorCategory.UnknownSource,
                    sourceField,
                    $"未知 Join 来源：{join.Source.Name} / Unknown join query source: {join.Source.Name}");
            }
            Result<BoundSource, ErrorCode, Error<ErrorCode>> target =
                Bind(definition, join.Source, sourceField);
            if (!target.IsOk) {
                return Propagate<CompiledRelationalQuery, BoundSource>(target);
            }

            var conditionBound =
                new Dictionary<string, BoundSource>(bound, StringComparer.Ordinal);
            AddBound(conditionBound, target.Value);
            var conditionState = new SqlState();
            Result<string, ErrorCode, Error<ErrorCode>> condition = CompileBoolean(
                join.Condition, conditionBound, conditionState, $"joins[{index}].condition");
            if (!condition.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(condition);
            }

            Result<string, ErrorCode, Error<ErrorCode>> table = RelationalQueryIdentifier.Quote(
                definition.Table, Dialect, $"{sourceField}.table");
            if (!table.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(table);
            }
            string sourceSql =
                $"{table.Value} AS {RelationalQueryIdentifier.QuoteOrFallback(target.Value.Qualifier, Dialect)}";
            switch (join.Type) {
                case JoinType.Inner:
                    joinSql.Add($"INNER JOIN {sourceSql} ON {condition.Value}");
                    joinParameters.AddRange(conditionState.Parameters);
                    AddBound(bound, target.Value);
                    break;
                case JoinType.Left:
                    joinSql.Add($"LEFT JOIN {sourceSql} ON {condition.Value}");
                    joinParameters.AddRange(conditionState.Parameters);
                    AddBound(bound, target.Value);
                    break;
                default:
                    // Exists 是半连接：既不进入 FROM，也不绑定（因此后续条件无法引用它）。
                    // Exists is a semi-join: it enters neither FROM nor the bindings, so later conditions cannot
                    // reference it.
                    existsSql.Add($"EXISTS (SELECT 1 FROM {sourceSql} WHERE {condition.Value})");
                    existsParameters.AddRange(conditionState.Parameters);
                    break;
            }
        }

        var predicateState = new SqlState();
        string? predicate = null;
        if (plan.Predicate is { } planPredicate) {
            Result<string, ErrorCode, Error<ErrorCode>> compiled = CompileBoolean(
                planPredicate, bound, predicateState, "predicate");
            if (!compiled.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(compiled);
            }
            predicate = compiled.Value;
        }

        IReadOnlyList<ProjectionSpec> projectionRefs;
        if (plan.Projections.Count == 0) {
            if (rootBound.Value.Definition.Defaults.Count == 0) {
                return Failure<CompiledRelationalQuery>(
                    RelationalQueryCompilationErrorCategory.InvalidPlan,
                    "projections",
                    "必须提供显式投影或注册默认字段 / "
                    + "An explicit projection or registered default column is required");
            }
            projectionRefs = rootBound.Value.Definition.Defaults
                .Select(column => new ProjectionSpec(new ColumnRef(plan.Root.Name, column)))
                .ToArray();
        }
        else {
            projectionRefs = plan.Projections;
        }

        var projectionSql = new List<string>();
        for (int index = 0; index < projectionRefs.Count; index++) {
            ProjectionSpec projection = projectionRefs[index];
            Result<string, ErrorCode, Error<ErrorCode>> column = CompileColumn(
                projection.Column, bound, $"projections[{index}]");
            if (!column.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(column);
            }
            string? alias = null;
            if (projection.Alias is { } aliasText) {
                Result<string, ErrorCode, Error<ErrorCode>> quoted =
                    RelationalQueryIdentifier.Quote(aliasText, Dialect, $"projections[{index}].alias");
                if (!quoted.IsOk) {
                    return Propagate<CompiledRelationalQuery, string>(quoted);
                }
                alias = quoted.Value;
            }
            projectionSql.Add(alias is null ? column.Value : $"{column.Value} AS {alias}");
        }

        Result<string, ErrorCode, Error<ErrorCode>> rootTable = RelationalQueryIdentifier.Quote(
            rootBound.Value.Definition.Table, Dialect, "root.table");
        if (!rootTable.IsOk) {
            return Propagate<CompiledRelationalQuery, string>(rootTable);
        }

        var groupSql = new List<string>();
        for (int index = 0; index < plan.GroupBy.Count; index++) {
            Result<string, ErrorCode, Error<ErrorCode>> column =
                CompileColumn(plan.GroupBy[index], bound, $"groupBy[{index}]");
            if (!column.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(column);
            }
            groupSql.Add(column.Value);
        }

        var orderSql = new List<string>();
        for (int index = 0; index < plan.OrderBy.Count; index++) {
            OrderSpec order = plan.OrderBy[index];
            Result<string, ErrorCode, Error<ErrorCode>> column =
                CompileColumn(order.Column, bound, $"orderBy[{index}]");
            if (!column.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(column);
            }
            orderSql.Add(OrderSql(column.Value, order));
        }

        string pagination = "";
        if (plan.Page is { } page) {
            Result<string, ErrorCode, Error<ErrorCode>> paged = PaginationSql(page);
            if (!paged.IsOk) {
                return Propagate<CompiledRelationalQuery, string>(paged);
            }
            pagination = paged.Value;
        }

        var sql = new StringBuilder();
        sql.Append("SELECT ");
        if (plan.Distinct) {
            sql.Append("DISTINCT ");
        }
        sql.Append(string.Join(", ", projectionSql));
        sql.Append(" FROM ").Append(rootTable.Value).Append(" AS ")
            .Append(RelationalQueryIdentifier.QuoteOrFallback(rootBound.Value.Qualifier, Dialect));
        if (joinSql.Count > 0) {
            sql.Append(' ').Append(string.Join(" ", joinSql));
        }
        var whereParts = new List<string>();
        if (predicate is not null) {
            whereParts.Add(predicate);
        }
        whereParts.AddRange(existsSql);
        if (whereParts.Count > 0) {
            sql.Append(" WHERE ").Append(string.Join(" AND ", whereParts.Select(part => $"({part})")));
        }
        if (groupSql.Count > 0) {
            sql.Append(" GROUP BY ").Append(string.Join(", ", groupSql));
        }
        if (orderSql.Count > 0) {
            sql.Append(" ORDER BY ").Append(string.Join(", ", orderSql));
        }
        sql.Append(pagination);

        var parameters = new List<object?>();
        parameters.AddRange(joinParameters);
        parameters.AddRange(predicateState.Parameters);
        parameters.AddRange(existsParameters);
        return Results.Ok(new CompiledRelationalQuery(
            Sql: sql.ToString(),
            Parameters: new ReadOnlyCollection<object?>(parameters),
            CanonicalHash: plan.Hash()));
    }

    /// <summary>
    /// 分页子句 / Pagination clause.
    /// </summary>
    /// <param name="page">分页规格 / Page specification.</param>
    /// <returns>分页 SQL 或结构化错误 / The pagination SQL or a structured failure.</returns>
    private Result<string, ErrorCode, Error<ErrorCode>> PaginationSql(PageSpec page) {
        if (page.Limit is not { } limit) {
            return Dialect switch {
                // MySQL 没有独立的 OFFSET 语法：仅偏移分页必须借一个有限 LIMIT，因此这里只能拒绝而不是猜一个大数。
                // MySQL has no standalone OFFSET: offset-only pagination needs a finite LIMIT, so this is a
                // rejection rather than guessing a huge number.
                RelationalQueryDialect.MySql => Failure<string>(
                    RelationalQueryCompilationErrorCategory.UnsupportedDialect,
                    "page.limit",
                    "MySQL 的仅偏移分页必须提供有限 LIMIT / "
                    + "MySQL requires a finite LIMIT for offset-only pagination"),
                RelationalQueryDialect.PostgreSql => Results.Ok($" OFFSET {page.Offset}"),
                RelationalQueryDialect.Sqlite => Results.Ok($" LIMIT -1 OFFSET {page.Offset}"),
                _ => Results.Ok($" OFFSET {page.Offset} ROWS"),
            };
        }
        return Dialect switch {
            RelationalQueryDialect.MySql =>
                Results.Ok($" LIMIT {page.Offset}, {limit}"),
            RelationalQueryDialect.PostgreSql or RelationalQueryDialect.Sqlite =>
                Results.Ok($" LIMIT {limit} OFFSET {page.Offset}"),
            _ => Results.Ok(
                $" OFFSET {page.Offset} ROWS FETCH NEXT {limit} ROWS ONLY"),
        };
    }

    private string OrderSql(string column, OrderSpec order) {
        string direction = order.Direction == SortDirection.Ascending ? "ASC" : "DESC";
        if (order.Nulls == NullsOrder.Unspecified) {
            return $"{column} {direction}";
        }
        if (Dialect is RelationalQueryDialect.MySql or RelationalQueryDialect.Sqlite) {
            // MySQL 与 SQLite 都不支持 NULLS FIRST/LAST，只能用 CASE 排序键模拟。
            // Neither MySQL nor SQLite supports NULLS FIRST/LAST, so a CASE sort key emulates it.
            string nullRank = order.Nulls == NullsOrder.First ? "0" : "1";
            string nonNullRank = order.Nulls == NullsOrder.First ? "1" : "0";
            return $"CASE WHEN {column} IS NULL THEN {nullRank} ELSE {nonNullRank} END ASC, "
                + $"{column} {direction}";
        }
        return $"{column} {direction} NULLS {(order.Nulls == NullsOrder.First ? "FIRST" : "LAST")}";
    }

    private string BooleanSql(Trivalent value) {
        bool? truth = value switch {
            Fuookami.Ospf.Math.Trivalent.True => true,
            Fuookami.Ospf.Math.Trivalent.False => false,
            _ => null,
        };
        if (Dialect == RelationalQueryDialect.Oracle) {
            return truth switch {
                true => "1 = 1",
                false => "1 = 0",
                _ => "NULL = NULL",
            };
        }
        return truth switch {
            true => "TRUE",
            false => "FALSE",
            _ => "NULL",
        };
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileBoolean(
        BooleanExpression expression,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) => expression switch {
        BooleanConstant constant => Results.Ok(BooleanSql(constant.Value)),
        AndExpression and => CompileLogical(and.Operands, "AND", bound, state, field),
        OrExpression or => CompileLogical(or.Operands, "OR", bound, state, field),
        NotExpression not => Wrap(CompileBoolean(not.Operand, bound, state, $"{field}.operand"),
            inner => $"NOT ({inner})"),
        NullCheck check => CompileNullCheck(check, bound, state, field),
        BooleanCustom => Failure<string>(
            RelationalQueryCompilationErrorCategory.UnsupportedExpression,
            field,
            "不支持自定义布尔表达式 / Custom boolean expressions are not supported"),
        Fuookami.Ospf.Math.Symbol.Expression.Comparison<bool> comparison =>
            CompileComparison(comparison.Left, comparison.Right, comparison.Operator, bound, state, field),
        _ => CompileGenericBoolean(expression, bound, state, field),
    };

    private Result<string, ErrorCode, Error<ErrorCode>> CompileGenericBoolean(
        BooleanExpression expression,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        Type type = expression.GetType();
        if (!type.IsGenericType) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.UnsupportedExpression,
                field,
                $"不支持的表达式节点：{type.Name} / Unsupported expression node: {type.Name}");
        }
        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(Fuookami.Ospf.Math.Symbol.Expression.Comparison<>)) {
            return CompileComparison(
                (object)Property(expression, "Left")!,
                (object)Property(expression, "Right")!,
                (ComparisonOperator)Property(expression, "Operator")!,
                bound, state, field);
        }
        if (definition == typeof(InExpression<>)) {
            return CompileIn(expression, bound, state, field);
        }
        if (definition == typeof(PatternMatch<>)) {
            return CompilePattern(expression, bound, state, field);
        }
        return Failure<string>(
            RelationalQueryCompilationErrorCategory.UnsupportedExpression,
            field,
            $"不支持的表达式节点：{type.Name} / Unsupported expression node: {type.Name}");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileComparison(
        object leftScalar,
        object rightScalar,
        ComparisonOperator @operator,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        bool isEquality = @operator is ComparisonOperator.Eq or ComparisonOperator.Ne;
        // `= NULL` 永远为假、`<> NULL` 永远为真，必须改写成 IS NULL / IS NOT NULL，否则过滤条件会静默失效。
        // `= NULL` is never true and `<> NULL` never false, so these must become IS NULL / IS NOT NULL or the
        // filter silently stops filtering.
        string nullPredicate = @operator == ComparisonOperator.Eq ? "IS" : "IS NOT";
        if (isEquality && IsNullConstant(rightScalar)) {
            return Wrap(
                CompileScalar(leftScalar, bound, state, $"{field}.left"),
                inner => $"{inner} {nullPredicate} NULL");
        }
        if (isEquality && IsNullConstant(leftScalar)) {
            return Wrap(
                CompileScalar(rightScalar, bound, state, $"{field}.right"),
                inner => $"{inner} {nullPredicate} NULL");
        }
        Result<string, ErrorCode, Error<ErrorCode>> left =
            CompileScalar(leftScalar, bound, state, $"{field}.left");
        if (!left.IsOk) {
            return left;
        }
        Result<string, ErrorCode, Error<ErrorCode>> right =
            CompileScalar(rightScalar, bound, state, $"{field}.right");
        if (!right.IsOk) {
            return right;
        }
        string symbol = @operator switch {
            ComparisonOperator.Eq => "=",
            ComparisonOperator.Ne => "<>",
            ComparisonOperator.Lt => "<",
            ComparisonOperator.Le => "<=",
            ComparisonOperator.Gt => ">",
            _ => ">=",
        };
        return Results.Ok($"{left.Value} {symbol} {right.Value}");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileIn(
        BooleanExpression expression,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        var candidates = (IEnumerable)Property(expression, "Candidates")!;
        bool negated = (bool)Property(expression, "Negated")!;
        var compiled = new List<string>();
        foreach (object candidate in candidates) {
            Result<string, ErrorCode, Error<ErrorCode>> result = CompileScalar(
                candidate, bound, state, $"{field}.candidates[{compiled.Count}]");
            if (!result.IsOk) {
                return result;
            }
            compiled.Add(result.Value);
        }
        if (compiled.Count == 0) {
            // 空 IN 集合在 SQL 里是语法错误；`x IN ` 也不等价于任何真值，必须退化成常量。
            // An empty IN set is a syntax error in SQL and matches nothing, so it degrades to a constant.
            return Results.Ok(BooleanSql(Trivalent.Invoke(negated)));
        }
        Result<string, ErrorCode, Error<ErrorCode>> value = CompileScalar(
            (object)Property(expression, "Value")!, bound, state, $"{field}.value");
        if (!value.IsOk) {
            return value;
        }
        return Results.Ok(
            $"{value.Value} {(negated ? "NOT IN" : "IN")} ({string.Join(", ", compiled)})");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileNullCheck(
        NullCheck check,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) =>
        Wrap(
            CompileReference(check.Path.Value, bound, state, field),
            inner => $"{inner} {NullCheckSuffix(check)}");

    private static string NullCheckSuffix(NullCheck check) =>
        check.Type == NullCheckType.IsNull ? "IS NULL" : "IS NOT NULL";

    private Result<string, ErrorCode, Error<ErrorCode>> CompilePattern(
        BooleanExpression expression,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        object valueScalar = Property(expression, "Value")!;
        object patternScalar = Property(expression, "Pattern")!;
        var mode = (PatternMatchMode)Property(expression, "Mode")!;
        bool negated = (bool)Property(expression, "Negated")!;

        Result<string, ErrorCode, Error<ErrorCode>> value =
            CompileScalar(valueScalar, bound, state, $"{field}.value");
        if (!value.IsOk) {
            return value;
        }

        if (mode == PatternMatchMode.Regex) {
            Result<string, ErrorCode, Error<ErrorCode>> regex =
                CompileScalar(patternScalar, bound, state, $"{field}.pattern");
            if (!regex.IsOk) {
                return regex;
            }
            return Dialect switch {
                RelationalQueryDialect.Oracle or RelationalQueryDialect.H2 => Results.Ok(
                    $"{(negated ? "NOT " : "")}REGEXP_LIKE({value.Value}, {regex.Value})"),
                RelationalQueryDialect.MySql =>
                    Results.Ok($"{value.Value} {(negated ? "NOT " : "")}REGEXP {regex.Value}"),
                RelationalQueryDialect.PostgreSql =>
                    Results.Ok($"{value.Value} {(negated ? "!~" : "~")} {regex.Value}"),
                _ => Failure<string>(
                    RelationalQueryCompilationErrorCategory.UnsupportedExpression,
                    field,
                    "SQLite 正则需要应用注册函数 / "
                    + "SQLite regex requires an application-registered function"),
            };
        }

        // 模式串是常量时把通配符并进**参数值**，而不是拼进 SQL——拼进 SQL 会让 % 变成可构造的注入面。
        // When the pattern is a constant the wildcards join the **parameter value** rather than the SQL text;
        // concatenating into SQL would turn % into a constructible injection surface.
        string patternSql;
        if (Property(patternScalar, "Value") is string patternText &&
            mode is not PatternMatchMode.Exact and not PatternMatchMode.Like) {
            state.Parameters.Add(mode switch {
                PatternMatchMode.Prefix => $"{patternText}%",
                PatternMatchMode.Suffix => $"%{patternText}",
                PatternMatchMode.Contains => $"%{patternText}%",
                _ => patternText,
            });
            patternSql = "?";
        }
        else {
            Result<string, ErrorCode, Error<ErrorCode>> compiled =
                CompileScalar(patternScalar, bound, state, $"{field}.pattern");
            if (!compiled.IsOk) {
                return compiled;
            }
            patternSql = compiled.Value;
        }

        string @operator = mode == PatternMatchMode.Exact
            ? (negated ? "<>" : "=")
            : (negated ? "NOT LIKE" : "LIKE");
        return Results.Ok($"{value.Value} {@operator} {patternSql}");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileLogical(
        IReadOnlyList<BooleanExpression> operands,
        string @operator,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        var values = new List<string>();
        for (int index = 0; index < operands.Count; index++) {
            Result<string, ErrorCode, Error<ErrorCode>> result = CompileBoolean(
                operands[index], bound, state, $"{field}.operands[{index}]");
            if (!result.IsOk) {
                return result;
            }
            values.Add(result.Value);
        }
        if (values.Count == 0) {
            // 空 AND 恒真、空 OR 恒假：拼出 `` 会直接语法错误。
            // An empty AND is true and an empty OR false; emitting `` would be a syntax error.
            return Results.Ok(BooleanSql(Trivalent.Invoke(@operator == "AND")));
        }
        return Results.Ok(string.Join($" {@operator} ", values.Select(value => $"({value})")));
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileScalar(
        object expression,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        ArgumentNullException.ThrowIfNull(expression);
        Type type = expression.GetType();
        if (!type.IsGenericType) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.UnsupportedExpression,
                field,
                $"不支持的标量节点：{type.Name} / Unsupported scalar node: {type.Name}");
        }
        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(ScalarConstant<>)) {
            // 所有字面量都走参数绑定：SQL 文本里不含业务值。
            // Every literal is a bound parameter, so the SQL text carries no business values.
            state.Parameters.Add(Property(expression, "Value"));
            return Results.Ok("?");
        }
        if (definition == typeof(ScalarReference<>)) {
            object? path = Property(expression, "Path");
            return CompileReference(
                path is PropertyPath propertyPath ? propertyPath.Value : "",
                bound, state, field);
        }
        if (definition == typeof(ScalarSymbolReference<>)) {
            object? symbol = Property(expression, "Symbol");
            return CompileReference(
                Property(symbol, "Name")?.ToString() ?? "",
                bound, state, field);
        }
        if (definition == typeof(ScalarCustom<>)) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.UnsupportedExpression,
                field,
                "不支持自定义标量表达式 / Custom scalar expressions are not supported");
        }
        if (definition == typeof(ScalarUnary<>)) {
            return Wrap(
                CompileScalar(Property(expression, "Operand")!, bound, state, $"{field}.operand"),
                inner => (UnaryOperator)Property(expression, "Operator")! switch {
                    UnaryOperator.Negate => $"(-{inner})",
                    UnaryOperator.Positive => $"(+{inner})",
                    _ => $"ABS({inner})",
                });
        }
        if (definition == typeof(ScalarBinary<>)) {
            Result<string, ErrorCode, Error<ErrorCode>> left = CompileScalar(
                Property(expression, "Left")!, bound, state, $"{field}.left");
            if (!left.IsOk) {
                return left;
            }
            Result<string, ErrorCode, Error<ErrorCode>> right = CompileScalar(
                Property(expression, "Right")!, bound, state, $"{field}.right");
            if (!right.IsOk) {
                return right;
            }
            var @operator = (BinaryOperator)Property(expression, "Operator")!;
            return Results.Ok(@operator switch {
                BinaryOperator.Add => $"({left.Value} + {right.Value})",
                BinaryOperator.Subtract => $"({left.Value} - {right.Value})",
                BinaryOperator.Multiply => $"({left.Value} * {right.Value})",
                BinaryOperator.Divide => $"({left.Value} / {right.Value})",
                // Oracle 没有 % 运算符，只有 MOD 函数。
                // Oracle has no % operator, only the MOD function.
                BinaryOperator.Modulo => Dialect == RelationalQueryDialect.Oracle
                    ? $"MOD({left.Value}, {right.Value})"
                    : $"({left.Value} % {right.Value})",
                _ => $"POWER({left.Value}, {right.Value})",
            });
        }
        if (definition == typeof(ScalarFunction<>)) {
            return CompileFunction(expression, bound, state, field);
        }
        return Failure<string>(
            RelationalQueryCompilationErrorCategory.UnsupportedExpression,
            field,
            $"不支持的标量节点：{type.Name} / Unsupported scalar node: {type.Name}");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileFunction(
        object expression,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        string name = (Property(expression, "Name")?.ToString() ?? "").ToLowerInvariant();
        if (name is not ("abs" or "lower" or "upper" or "trim" or "length" or "coalesce")) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.UnsupportedExpression,
                field,
                $"不支持的标量函数：{name} / Unsupported scalar function: {name}");
        }
        var arguments = new List<string>();
        foreach (object argument in (IEnumerable)Property(expression, "Arguments")!) {
            Result<string, ErrorCode, Error<ErrorCode>> result = CompileScalar(
                argument, bound, state, $"{field}.arguments[{arguments.Count}]");
            if (!result.IsOk) {
                return result;
            }
            arguments.Add(result.Value);
        }
        bool validArity = name == "coalesce"
            ? arguments.Count > 0
            : arguments.Count == 1;
        if (!validArity) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.UnsupportedExpression,
                field,
                $"函数 {name} 的参数数量非法 / Invalid argument count for function {name}");
        }
        return Results.Ok($"{name.ToUpperInvariant()}({string.Join(", ", arguments)})");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileReference(
        string path,
        IReadOnlyDictionary<string, BoundSource> bound,
        SqlState state,
        string field) {
        string normalized = path.Trim();
        int separator = normalized.IndexOf('.', StringComparison.Ordinal);
        BoundSource source;
        string columnPath;
        if (separator > 0 && bound.TryGetValue(normalized[..separator], out BoundSource? qualified)) {
            source = qualified;
            columnPath = normalized[(separator + 1)..];
        }
        else {
            var candidates = bound.Values.Distinct().ToList();
            if (candidates.Count != 1) {
                // 未限定列在多个来源上必须报歧义，而不是随便挑一个——挑错会静默返回别的数据源的值。
                // An unqualified column across several sources must be reported as ambiguous rather than picking
                // one, because a wrong pick silently returns another source's value.
                return Failure<string>(
                    RelationalQueryCompilationErrorCategory.UnknownColumn,
                    field,
                    $"未限定字段有歧义：{normalized} / Unqualified column is ambiguous: {normalized}");
            }
            source = candidates[0];
            columnPath = normalized;
        }
        return Column(source, columnPath, field);
    }

    private Result<string, ErrorCode, Error<ErrorCode>> Column(
        BoundSource source,
        string path,
        string field) {
        Result<string, ErrorCode, Error<ErrorCode>> physical =
            RelationalQueryIdentifier.ResolveColumn(source.Definition, path, field);
        if (!physical.IsOk) {
            return physical;
        }
        Result<string, ErrorCode, Error<ErrorCode>> quoted =
            RelationalQueryIdentifier.Quote(physical.Value, Dialect, field);
        if (!quoted.IsOk) {
            return quoted;
        }
        return Results.Ok(
            $"{RelationalQueryIdentifier.QuoteOrFallback(source.Qualifier, Dialect)}.{quoted.Value}");
    }

    private Result<string, ErrorCode, Error<ErrorCode>> CompileColumn(
        ColumnRef reference,
        IReadOnlyDictionary<string, BoundSource> bound,
        string field) {
        ArgumentNullException.ThrowIfNull(reference);
        if (!bound.TryGetValue(reference.Source, out BoundSource? source)) {
            return Failure<string>(
                RelationalQueryCompilationErrorCategory.UnknownSource,
                field,
                $"未知字段来源：{reference.Source} / Unknown column source: {reference.Source}");
        }
        return Column(source, reference.Path, field);
    }

    private Result<BoundSource, ErrorCode, Error<ErrorCode>> Bind(
        RelationalQuerySource definition,
        QuerySource source,
        string field) {
        Result<string, ErrorCode, Error<ErrorCode>> table =
            RelationalQueryIdentifier.Quote(definition.Table, Dialect, $"{field}.table");
        if (!table.IsOk) {
            return Propagate<BoundSource, string>(table);
        }
        string qualifier = source.Alias ?? source.Name;
        Result<string, ErrorCode, Error<ErrorCode>> quotedQualifier =
            RelationalQueryIdentifier.Quote(qualifier, Dialect, $"{field}.alias");
        if (!quotedQualifier.IsOk) {
            return Propagate<BoundSource, string>(quotedQualifier);
        }
        return Results.Ok(new BoundSource(definition, source, qualifier));
    }

    private static void AddBound(
        IDictionary<string, BoundSource> bound,
        BoundSource source) {
        bound[source.Source.Name] = source;
        if (source.Source.Alias is { } alias) {
            bound[alias] = source;
        }
    }

    private static bool IsNullConstant(object scalar) =>
        scalar.GetType().IsGenericType &&
        scalar.GetType().GetGenericTypeDefinition() == typeof(ScalarConstant<>) &&
        Property(scalar, "Value") is null;

    private static object? Property(object? instance, string name) =>
        instance?.GetType().GetProperty(name)?.GetValue(instance);

    private static Result<string, ErrorCode, Error<ErrorCode>> Wrap(
        Result<string, ErrorCode, Error<ErrorCode>> result,
        Func<string, string> build) =>
        result.IsOk ? Results.Ok(build(result.Value)) : result;

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        RelationalQueryCompilationErrorCategory category,
        string field,
        string reason) =>
        Results.Failed<T>(new ExErr<ErrorCode, RelationalQueryCompilationError>(
            ErrorCode.IllegalArgument,
            reason,
            new RelationalQueryCompilationError(category, field, reason)));

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, TIn>(
        Result<TIn, ErrorCode, Error<ErrorCode>> inner) =>
        inner switch {
            Failed<TIn, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<TIn, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                RelationalQueryCompilationErrorCategory.InvalidPlan,
                "plan",
                "编译失败 / Compilation failed"),
        };

    /// <summary>绑定的来源 / A bound source.</summary>
    /// <param name="Definition">来源定义 / Source definition.</param>
    /// <param name="Source">查询来源 / Query source.</param>
    /// <param name="Qualifier">SQL 限定符 / SQL qualifier.</param>
    private sealed record BoundSource(
        RelationalQuerySource Definition,
        QuerySource Source,
        string Qualifier);

    /// <summary>编译期状态 / Compilation state.</summary>
    private sealed class SqlState {
        /// <summary>已绑定的参数 / Bound parameters.</summary>
        public List<object?> Parameters { get; } = new();
    }
}
