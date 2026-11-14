// 关系查询标识符与来源解析测试 / Tests for identifiers and source resolution.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询标识符与来源解析 / Relational query identifiers and source resolution.
/// </summary>
public sealed class RelationalQueryIdentifierTest {
    /// <summary>
    /// MySQL 用反引号、其余方言用双引号。搞错方言引号会让 SQL 在目标库上直接语法错误。
    /// MySQL uses backticks and every other dialect uses double quotes; the wrong quote is a syntax error on the
    /// target database.
    /// </summary>
    [Theory]
    [InlineData(RelationalQueryDialect.MySql, "`jobs`")]
    [InlineData(RelationalQueryDialect.PostgreSql, "\"jobs\"")]
    [InlineData(RelationalQueryDialect.Sqlite, "\"jobs\"")]
    [InlineData(RelationalQueryDialect.Oracle, "\"jobs\"")]
    [InlineData(RelationalQueryDialect.H2, "\"jobs\"")]
    public void Quote_ShouldUseDialectQuote(RelationalQueryDialect dialect, string expected) =>
        Quote("jobs", dialect).Should().Be(expected);

    /// <summary>
    /// **前缀注入的防线**：标识符里的引号必须加倍。不加倍时 <c>jobs" ; DROP TABLE x ; --</c> 会闭合引用串，
    /// 后续内容变成可执行的 SQL。
    /// **The injection defence**: a quote inside an identifier must be doubled. Without doubling,
    /// <c>jobs" ; DROP TABLE x ; --</c> closes the quoted string and the remainder becomes executable SQL.
    /// </summary>
    [Fact]
    public void Quote_ShouldDoubleEmbeddedQuote() {
        Quote("jobs\" ; DROP TABLE x ; --", RelationalQueryDialect.PostgreSql)
            .Should().Be("\"jobs\"\" ; DROP TABLE x ; --\"");
        Quote("jobs` ; DROP TABLE x ; --", RelationalQueryDialect.MySql)
            .Should().Be("`jobs`` ; DROP TABLE x ; --`");
    }

    /// <summary>
    /// 多段标识符逐段引用：只引用整串会把 <c>schema.table</c> 变成一个不存在的表名。
    /// A multi-part identifier is quoted segment by segment; quoting the whole string would turn
    /// <c>schema.table</c> into one non-existent table name.
    /// </summary>
    [Fact]
    public void Quote_ShouldQuoteEachSegment() =>
        Quote("public.jobs", RelationalQueryDialect.PostgreSql)
            .Should().Be("\"public\".\"jobs\"");

    /// <summary>空、全空白、含 NUL 的标识符都必须拒绝 / Blank, whitespace-only and NUL-containing identifiers are rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b")]
    public void Quote_ShouldRejectBlankOrNul(string identifier) {
        Category(identifier).Should().Be(
            RelationalQueryCompilationErrorCategory.InvalidIdentifier);
    }

    /// <summary>
    /// 空分段是畸形标识符，不是"可以忽略的错误"——忽略它会让 <c>a..b</c> 变成 <c>a.b</c>，指向另一张表。
    /// An empty segment is a malformed identifier, not a harmless mistake: ignoring it turns <c>a..b</c> into
    /// <c>a.b</c>, a different table.
    /// </summary>
    [Theory]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    public void Quote_ShouldRejectEmptySegment(string identifier) {
        Category(identifier).Should().Be(
            RelationalQueryCompilationErrorCategory.InvalidIdentifier);
    }

    /// <summary>裁剪空白后合法 / Trimming whitespace still yields a valid identifier.</summary>
    [Fact]
    public void Quote_ShouldTrimWhitespace() =>
        Quote("  jobs  ", RelationalQueryDialect.Sqlite).Should().Be("\"jobs\"");

    /// <summary>
    /// 空注册表时逻辑名**就是**物理表名（基线行为）；这个方向反了会让默认配置变成"全部拒绝"。
    /// With an empty registry the logical name **is** the physical table; reversing this would make the default
    /// configuration reject everything.
    /// </summary>
    [Fact]
    public void Definition_ShouldPassThroughWhenRegistryEmpty() {
        RelationalQuerySource? definition = RelationalQueryIdentifier.Definition(
            RelationalQuerySourceRegistry.Empty,
            "job");

        definition.Should().NotBeNull();
        definition!.Table.Should().Be("job");
    }

    /// <summary>
    /// 非空注册表时未登记来源必须返回 null——否则白名单形同虚设。
    /// With a populated registry an unregistered source must yield null, or the allowlist is pointless.
    /// </summary>
    [Fact]
    public void Definition_ShouldRejectUnknownSourceWithRegistry() {
        var registry = new RelationalQuerySourceRegistry(new[] {
            new RelationalQuerySource("job", "jobs"),
        });

        RelationalQueryIdentifier.Definition(registry, "job").Should().NotBeNull();
        RelationalQueryIdentifier.Definition(registry, "ghost").Should().BeNull();
    }

    /// <summary>
    /// 字段映射为空时逻辑路径直接作物理列名；非空时必须命中白名单，**不得**回落到逻辑名。
    /// An empty column map uses the logical path as the physical name; a populated one must hit the allowlist and
    /// must **not** fall back to the logical name.
    /// </summary>
    [Fact]
    public void ResolveColumn_ShouldHonourAllowlist() {
        var untyped = new RelationalQuerySource("job", "jobs");
        RelationalQueryIdentifier.ResolveColumn(untyped, "start", "projections")
            .Value.Should().Be("start");

        var typed = new RelationalQuerySource(
            "job",
            "jobs",
            new Dictionary<string, string> { ["start"] = "job_start" });
        RelationalQueryIdentifier.ResolveColumn(typed, "start", "projections")
            .Value.Should().Be("job_start");

        Result<string, ErrorCode, Error<ErrorCode>> unknown =
            RelationalQueryIdentifier.ResolveColumn(typed, "end", "projections");
        unknown.IsOk.Should().BeFalse();
        ErrorOf(unknown).Category.Should().Be(
            RelationalQueryCompilationErrorCategory.UnknownColumn);
        ErrorOf(unknown).Field.Should().Be("projections");
    }

    /// <summary>
    /// 回落引用只做引号加倍、不做分段：它只用于已校验过的别名，别名里的点是名字的一部分而非分段。
    /// The fallback quotes and doubles but does not split: it is only for already-validated aliases, where a dot is
    /// part of the name rather than a separator.
    /// </summary>
    [Fact]
    public void QuoteOrFallback_ShouldNotSplitSegments() =>
        RelationalQueryIdentifier.QuoteOrFallback("a.b", RelationalQueryDialect.PostgreSql)
            .Should().Be("\"a.b\"");

    private static string Quote(string identifier, RelationalQueryDialect dialect) =>
        RelationalQueryIdentifier.Quote(identifier, dialect, "table").Value;

    private static RelationalQueryCompilationErrorCategory Category(string identifier) =>
        ErrorOf(RelationalQueryIdentifier.Quote(identifier, RelationalQueryDialect.Sqlite, "table"))
            .Category;

    private static RelationalQueryCompilationError ErrorOf<T>(
        Result<T, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeFalse();
        var error = (ExErr<ErrorCode, RelationalQueryCompilationError>)
            ((Failed<T, ErrorCode, Error<ErrorCode>>)result).Error;
        return error.TypedValue;
    }
}
