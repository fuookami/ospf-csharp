// 参数化 SQL 编译器测试 / Tests for the parameterized SQL compiler.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 参数化 SQL 编译器 / Parameterized SQL compiler.
/// </summary>
public sealed class SqlRelationalQueryCompilerTest {
    /// <summary>
    /// 最小计划必须编译成可用的 SELECT——只测错误会让"永远失败"也算实现。
    /// A minimal plan must compile into a usable SELECT; a failure-only suite would let "always fail" count.
    /// </summary>
    [Fact]
    public void Compile_ShouldProduceSelectForMinimalPlan() {
        CompiledRelationalQuery compiled = Compile(Plan());

        compiled.Sql.Should().Be(
            "SELECT \"j\".\"start\" FROM \"jobs\" AS \"j\"");
        compiled.Parameters.Should().BeEmpty();
        compiled.CanonicalHash.Should().Be(Plan().Hash());
    }

    /// <summary>
    /// **字面量必须进参数列表，SQL 文本里不得出现业务值**——这是参数化的全部意义。
    /// **Literals must become parameters and business values must not appear in the SQL text** — that is the
    /// whole point of parameterization.
    /// </summary>
    [Fact]
    public void Compile_ShouldBindLiteralsAsParameters() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            predicate: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<int>(
                Operator: ComparisonOperator.Gt,
                Left: new ScalarReference<int>(new PropertyPath("j.id")),
                Right: new ScalarConstant<int>(42)),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

        CompiledRelationalQuery compiled = Compile(plan);

        compiled.Sql.Should().NotContain("42");
        compiled.Parameters.Should().ContainSingle().Which.Should().Be(42);
        compiled.Sql.Should().Contain("?");
    }

    /// <summary>
    /// `= NULL` 永远为假，必须改写为 `IS NULL`，否则过滤条件会静默失效。
    /// `= NULL` is never true and must become `IS NULL`, or the filter silently stops filtering.
    /// </summary>
    [Fact]
    public void Compile_ShouldRewriteNullEquality() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            // 用引用类型承载 null：ScalarConstant<int> 的 T 是值类型，无法表示 null。
            // A reference type carries the null: ScalarConstant<int> has a value-type T and cannot hold null.
            predicate: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<string>(
                Operator: ComparisonOperator.Eq,
                Left: new ScalarReference<string>(new PropertyPath("j.name")),
                Right: new ScalarConstant<string>(null!)),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

        CompiledRelationalQuery compiled = Compile(plan);

        compiled.Sql.Should().Contain("IS NULL");
        compiled.Sql.Should().NotContain("= ?");
        compiled.Parameters.Should().BeEmpty("NULL 不是参数 / NULL is not a parameter");
    }

    /// <summary>
    /// 空 <c>IN</c> 集合在 SQL 里是语法错误，必须退化成常量而不是拼出 `IN `。
    /// An empty <c>IN</c> set is a SQL syntax error and must degrade to a constant rather than `IN `.
    /// </summary>
    [Fact]
    public void Compile_ShouldDegradeEmptyInSet() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            predicate: new InExpression<int>(
                Value: new ScalarReference<int>(new PropertyPath("j.id")),
                Candidates: System.Array.Empty<ScalarExpression<int>>()),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

        CompiledRelationalQuery compiled = Compile(plan);

        compiled.Sql.Should().Contain("FALSE");
        compiled.Sql.Should().NotContain("IN ()");
    }

    /// <summary>
    /// 前缀模式串的通配符必须并进**参数值**而不是 SQL 文本，否则 % 会成为可构造的注入面。
    /// Wildcards of a prefix pattern must join the **parameter value**, not the SQL text, or % becomes a
    /// constructible injection surface.
    /// </summary>
    [Fact]
    public void Compile_ShouldBindWildcardIntoParameter() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            predicate: new PatternMatch<string>(
                Value: new ScalarReference<string>(new PropertyPath("j.name")),
                Pattern: new ScalarConstant<string>("ab"),
                Mode: PatternMatchMode.Prefix),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

        CompiledRelationalQuery compiled = Compile(plan);

        compiled.Sql.Should().Contain("LIKE ?");
        compiled.Sql.Should().NotContain("ab");
        compiled.Parameters.Should().Contain("ab%");
    }

    /// <summary>MySQL 的 NULLS 排序必须用 CASE 模拟，因为它不支持 NULLS FIRST/LAST。</summary>
    [Fact]
    public void Compile_ShouldEmulateNullOrderingOnMySql() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            orderBy: new[] {
                new OrderSpec(new ColumnRef("job", "start"), SortDirection.Ascending, NullsOrder.Last),
            },
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

        CompiledRelationalQuery mySql = Compile(plan, RelationalQueryDialect.MySql);
        mySql.Sql.Should().Contain("CASE WHEN", "MySQL 不支持 NULLS LAST / MySQL lacks NULLS LAST");
        mySql.Sql.Should().Contain("ELSE 0 END ASC");

        CompiledRelationalQuery postgres = Compile(plan, RelationalQueryDialect.PostgreSql);
        postgres.Sql.Should().Contain("NULLS LAST");
    }

    /// <summary>
    /// MySQL 没有独立 OFFSET 语法，仅偏移分页必须拒绝而不是猜一个大 LIMIT。
    /// MySQL has no standalone OFFSET, so offset-only pagination must be rejected rather than guessing a huge
    /// LIMIT.
    /// </summary>
    [Fact]
    public void Compile_ShouldRejectOffsetOnlyPaginationOnMySql() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) },
            page: new PageSpec(Limit: null, Offset: 10));

        Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> result =
            Compiler(RelationalQueryDialect.MySql).Compile(plan);

        result.IsOk.Should().BeFalse();
        Error(result).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.UnsupportedDialect);

        Compile(plan, RelationalQueryDialect.Sqlite).Sql.Should().Contain("LIMIT -1 OFFSET 10");
    }

    /// <summary>
    /// 未限定列在多个来源上有歧义，必须报错而不是随便挑一个——挑错会静默返回别的来源的值。
    /// An unqualified column across several sources is ambiguous and must be rejected rather than picked at
    /// random, because a wrong pick silently returns another source's value.
    /// </summary>
    [Fact]
    public void Compile_ShouldRejectAmbiguousUnqualifiedColumn() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            joins: new[] {
                new JoinSpec(
                    Type: JoinType.Inner,
                    Source: new QuerySource("machine", "m"),
                    Condition: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<int>(
                        Operator: ComparisonOperator.Eq,
                        Left: new ScalarReference<int>(new PropertyPath("m.jobId")),
                        Right: new ScalarReference<int>(new PropertyPath("job.id"))),
                    Cardinality: JoinCardinality.ManyToOne),
            },
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "id")) },
            orderBy: new[] {
                new OrderSpec(new ColumnRef("job", "id")),
            });

        // 显式限定时正常编译（先确认夹具本身合法）。
        // Explicit qualification compiles (confirms the fixture itself is legal first).
        Compile(plan).Sql.Should().Contain("INNER JOIN");

        QueryPlan ambiguous = new(
            root: new QuerySource("job", "j"),
            joins: plan.Joins,
            predicate: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<int>(
                Operator: ComparisonOperator.Eq,
                Left: new ScalarReference<int>(new PropertyPath("id")),
                Right: new ScalarConstant<int>(1)),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "id")) });

        Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> result =
            Compiler(RelationalQueryDialect.Sqlite).Compile(ambiguous);

        result.IsOk.Should().BeFalse();
        Error(result).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.UnknownColumn);
        Error(result).Reason.Should().Contain("歧义");
    }

    /// <summary>未知来源必须被白名单拒绝 / An unknown source must be rejected by the allowlist.</summary>
    [Fact]
    public void Compile_ShouldRejectUnknownSourceWithRegistry() {
        var compiler = new SqlRelationalQueryCompiler(
            RelationalQueryDialect.Sqlite,
            new[] { new RelationalQuerySource("other", "others") });

        Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> result =
            compiler.Compile(Plan());

        result.IsOk.Should().BeFalse();
        Error(result).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.UnknownSource);
    }

    /// <summary>空投影且无默认字段时必须报 InvalidPlan / An empty projection with no defaults is InvalidPlan.</summary>
    [Fact]
    public void Compile_ShouldRejectEmptyProjectionWithoutDefaults() {
        QueryPlan plan = new(root: new QuerySource("job", "j"));

        Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> result =
            Compiler(RelationalQueryDialect.Sqlite).Compile(plan);

        result.IsOk.Should().BeFalse();
        Error(result).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.InvalidPlan);
    }

    /// <summary>注册了默认字段时，空投影回落到默认字段 / With registered defaults an empty projection uses them.</summary>
    [Fact]
    public void Compile_ShouldUseRegisteredDefaults() {
        var compiler = new SqlRelationalQueryCompiler(
            RelationalQueryDialect.Sqlite,
            new[] {
                new RelationalQuerySource(
                    "job",
                    "jobs",
                    new Dictionary<string, string> { ["start"] = "job_start" },
                    new[] { "start" }),
            });

        CompiledRelationalQuery compiled = compiler.Compile(new QueryPlan(root: new QuerySource("job"))).Value;

        compiled.Sql.Should().Contain("\"job_start\"");
    }

    /// <summary>
    /// <c>Exists</c> 是半连接：既不进 FROM，也不绑定，因此后续条件无法引用它。
    /// <c>Exists</c> is a semi-join: it enters neither FROM nor the bindings, so later conditions cannot use it.
    /// </summary>
    [Fact]
    public void Compile_ShouldEmitExistsAsSubquery() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            joins: new[] {
                new JoinSpec(
                    Type: JoinType.Exists,
                    Source: new QuerySource("machine", "m"),
                    Condition: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<int>(
                        Operator: ComparisonOperator.Eq,
                        Left: new ScalarReference<int>(new PropertyPath("m.jobId")),
                        Right: new ScalarReference<int>(new PropertyPath("job.id"))),
                    Cardinality: JoinCardinality.ManyToMany),
            },
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "id")) });

        CompiledRelationalQuery compiled = Compile(plan);

        compiled.Sql.Should().Contain("WHERE (EXISTS (SELECT 1 FROM");
        compiled.Sql.Should().NotContain("INNER JOIN");
    }

    /// <summary>不支持的标量函数必须结构化拒绝，而不是拼进 SQL / An unsupported function is rejected, not emitted.</summary>
    [Fact]
    public void Compile_ShouldRejectUnsupportedFunction() {
        QueryPlan plan = new(
            root: new QuerySource("job", "j"),
            predicate: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<int>(
                Operator: ComparisonOperator.Eq,
                Left: new ScalarFunction<int>(
                    Name: "totally_unknown",
                    Arguments: new ScalarExpression<int>[] {
                        new ScalarReference<int>(new PropertyPath("j.id")),
                    }),
                Right: new ScalarConstant<int>(1)),
            projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

        Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> result =
            Compiler(RelationalQueryDialect.Sqlite).Compile(plan);

        result.IsOk.Should().BeFalse();
        Error(result).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.UnsupportedExpression);
    }

    /// <summary>校验不通过的计划必须在编译前被拦下 / A plan failing validation is rejected before compiling.</summary>
    [Fact]
    public void Compile_ShouldRejectInvalidPlan() {
        QueryPlan plan = new(root: new QuerySource("  "));

        Result<CompiledRelationalQuery, ErrorCode, Error<ErrorCode>> result =
            Compiler(RelationalQueryDialect.Sqlite).Compile(plan);

        result.IsOk.Should().BeFalse();
        Error(result).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.InvalidPlan);
    }

    private static CompiledRelationalQuery Compile(
        QueryPlan plan,
        RelationalQueryDialect dialect = RelationalQueryDialect.PostgreSql) =>
        Compiler(dialect).Compile(plan).Value;

    private static SqlRelationalQueryCompiler Compiler(RelationalQueryDialect dialect) =>
        new(
            dialect,
            new[] { new RelationalQuerySource("job", "jobs"), new RelationalQuerySource("machine", "machines") });

    private static QueryPlan Plan() => new(
        root: new QuerySource("job", "j"),
        projections: new[] { new ProjectionSpec(new ColumnRef("job", "start")) });

    private static RelationalQueryCompilationError Error<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeFalse();
        return ((ExErr<ErrorCode, RelationalQueryCompilationError>)
            ((Failed<T, ErrorCode, Error<ErrorCode>>)result).Error).TypedValue;
    }
}
