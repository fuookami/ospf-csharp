// 关系查询编译器契约与来源注册表测试 / Tests for the compiler contract and source registry.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询编译器契约与来源注册表 / Relational query compiler contract and source registry.
/// </summary>
public sealed class RelationalQueryCompilerContractTest {
    /// <summary>
    /// 注册表必须在构造时深复制：调用方之后改自己的字典不得影响注册表，否则"构造后不可变、可并发复用"是假的。
    /// The registry must copy deeply at construction: mutating the caller's dictionary afterwards must not affect
    /// it, otherwise "immutable after construction, safe to share" is fiction.
    /// </summary>
    [Fact]
    public void Registry_ShouldCopyMappingsAtConstruction() {
        var columns = new Dictionary<string, string> { ["id"] = "job_id" };
        var registry = new RelationalQuerySourceRegistry(new[] {
            new RelationalQuerySource("job", "jobs", columns, new[] { "id" }),
        });

        columns["start"] = "job_start";

        RelationalQuerySource? source = registry.Find("job");
        source.Should().NotBeNull();
        source!.ColumnMap.Should().HaveCount(1,
            "构造后修改原字典不得影响注册表 / mutating the original dictionary must not affect the registry");
    }

    /// <summary>
    /// 名称映射构造时以**条目名**为准：否则查找键与来源名会静默分叉，按名查找就会失败。
    /// The name-keyed constructor prefers the **entry key**: otherwise the lookup key and the source name would
    /// silently diverge and lookups would fail.
    /// </summary>
    [Fact]
    public void Registry_ShouldPreferEntryName() {
        var registry = new RelationalQuerySourceRegistry(
            new Dictionary<string, RelationalQuerySource> {
                ["machine"] = new("stale-name", "machines"),
            });

        registry.Find("machine").Should().NotBeNull();
        registry.Find("stale-name").Should().BeNull(
            "条目名才是查找键 / the entry key is the lookup key");
    }

    /// <summary>查找不存在的来源必须返回 null 而不是抛异常 / Looking up an absent source returns null, never throws.</summary>
    [Fact]
    public void Find_ShouldReturnNullWhenAbsent() {
        RelationalQuerySourceRegistry.Empty.Find("ghost").Should().BeNull();
        RelationalQuerySourceRegistry.Empty.Sources.Should().BeEmpty();
    }

    /// <summary>
    /// 未显式给默认投影时回落到字段映射的键——这是行为，改了会让无投影查询的类型静默变化。
    /// Without an explicit default projection it falls back to the column-map keys, matching the code;
    /// changing that would silently alter the shape of a projection-less query.
    /// </summary>
    [Fact]
    public void Source_ShouldFallBackToColumnKeys() {
        var source = new RelationalQuerySource(
            "job",
            "jobs",
            new Dictionary<string, string> { ["id"] = "job_id", ["start"] = "job_start" });

        source.Defaults.Should().Equal("id", "start");
        source.ColumnMap.Should().HaveCount(2);
    }

    /// <summary>显式默认投影优先于回落的键 / An explicit default projection wins over the fallback keys.</summary>
    [Fact]
    public void Source_ShouldPreferExplicitDefaults() {
        var source = new RelationalQuerySource(
            "job",
            "jobs",
            new Dictionary<string, string> { ["id"] = "job_id" },
            new[] { "id" });

        source.Defaults.Should().Equal("id");
    }

    /// <summary>
    /// 由查询来源构造时只取逻辑名，物理表名由适配器给出——把逻辑名当表名是 SQL 注入式的常见错误来源。
    /// The query-source constructor takes only the logical name; the physical table comes from the adapter, and
    /// conflating the two is a common source of injection-shaped mistakes.
    /// </summary>
    [Fact]
    public void FromQuerySource_ShouldTakeLogicalNameOnly() {
        var source = RelationalQuerySource.FromQuerySource(
            new QuerySource("job", "j"),
            table: "jobs");

        source.Name.Should().Be("job");
        source.Table.Should().Be("jobs");
    }

    /// <summary>
    /// 编译结果是后端无关的值：SQL 与参数分离，<c>PlanHash</c> 只是 <c>CanonicalHash</c> 的别名。
    /// A compiled query is a backend-agnostic value: SQL and parameters are separate, and <c>PlanHash</c> merely
    /// aliases <c>CanonicalHash</c>.
    /// </summary>
    [Fact]
    public void CompiledQuery_ShouldAliasPlanHash() {
        var compiled = new CompiledRelationalQuery(
            Sql: "SELECT 1",
            Parameters: new object?[] { 1 },
            CanonicalHash: "abc");

        compiled.PlanHash.Should().Be("abc");
        compiled.Parameters.Should().HaveCount(1);
    }

    /// <summary>
    /// 方言枚举必须覆盖基线全部取值——少一个会让某个后端无法声明自己，只能静默落到别的方言。
    /// The dialect enum must cover every baseline value; a missing one would leave a backend unable to declare
    /// itself and silently falling back to another dialect.
    /// </summary>
    [Fact]
    public void Dialect_ShouldCoverBaselineValues() {
        System.Enum.GetValues<RelationalQueryDialect>()
            .Should().HaveCount(5);
        System.Enum.IsDefined(RelationalQueryDialect.PostgreSql).Should().BeTrue();
        System.Enum.IsDefined(RelationalQueryDialect.H2).Should().BeTrue();
    }

    /// <summary>
    /// 编译错误类别必须覆盖基线全部取值，且每一类都能承载字段与原因——否则下游只能靠字符串匹配分类。
    /// Error categories must cover every baseline value and each must carry field and reason; otherwise downstream
    /// can only classify by string matching.
    /// </summary>
    [Fact]
    public void CompilationErrorCategory_ShouldCoverBaselineValues() {
        System.Enum.GetValues<RelationalQueryCompilationErrorCategory>().Should().HaveCount(7);

        var error = new RelationalQueryCompilationError(
            RelationalQueryCompilationErrorCategory.UnknownColumn,
            "projections",
            "no such column");
        error.Category.Should().Be(RelationalQueryCompilationErrorCategory.UnknownColumn);
        error.Field.Should().Be("projections");
        error.Reason.Should().Be("no such column");
    }

    /// <summary>
    /// 空注册表是可用的单例，而不是 null——编译器需要它来表达"不做白名单校验"。
    /// The empty registry is a usable singleton rather than null, because the compiler needs it to express "no
    /// allowlist validation".
    /// </summary>
    [Fact]
    public void Empty_ShouldBeUsableSingleton() {
        RelationalQuerySourceRegistry.Empty.Should().NotBeNull();
        RelationalQuerySourceRegistry.Empty.Sources.Should().BeEmpty();
        RelationalQuerySourceRegistry.Empty.Find("any").Should().BeNull();
    }

    /// <summary>注册表来源列表必须只读，改不动 / The registry's source list must be read-only.</summary>
    [Fact]
    public void Sources_ShouldBeReadOnly() {
        var registry = new RelationalQuerySourceRegistry(new[] {
            new RelationalQuerySource("job", "jobs"),
        });

        registry.Sources.Should().BeAssignableTo<System.Collections.ObjectModel.ReadOnlyCollection<RelationalQuerySource>>();
    }

    /// <summary>编译器契约必须能被实现且按值返回结果 / The compiler contract must be implementable and return a value.</summary>
    [Fact]
    public void Compiler_ShouldBeImplementable() {
        IRelationalQueryCompiler<string> compiler = new EchoCompiler();

        compiler.Dialect.Should().Be(RelationalQueryDialect.Sqlite);
        compiler.Compile(new QueryPlan(root: new QuerySource("job"))).Value.Should().Be("job");
    }

    private sealed class EchoCompiler : IRelationalQueryCompiler<string> {
        public RelationalQueryDialect Dialect => RelationalQueryDialect.Sqlite;

        public Fuookami.Ospf.Utils.Functional.Result<string, Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Compile(
            QueryPlan plan) => Fuookami.Ospf.Utils.Functional.Results.Ok(plan.Root.Name);
    }
}
