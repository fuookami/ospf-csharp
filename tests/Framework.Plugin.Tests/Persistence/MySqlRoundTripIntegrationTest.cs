#nullable enable
#if MYSQL_INTEGRATION

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.MySQL;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// MySQL 真实往返（连本机实例，不是影子检查）/ A real MySQL round-trip against a live instance.
/// </summary>
/// <remarks>
/// 这件事此前被记成"环境所限"，而本机 `127.0.0.1:3306` 一直开着。补的就是一条真的去连它的测试：
/// 用 `MySqlRepository` 的生产入口做建表、插入、按主键查、更新、删除，每一步都断言真实结果。
///
/// This used to be recorded as "limited by the environment" while `127.0.0.1:3306` was up the whole time. What was missing was a test that actually
/// connects: through the production `MySqlRepository` entry point it creates the table, inserts, finds by key, updates and deletes, asserting the real
/// result at every step.
///
/// 连接串从环境变量 `OSPF_TEST_MYSQL_CONN` 读，口令不进仓库，也不进日志。
/// The connection string comes from `OSPF_TEST_MYSQL_CONN`, so no password lands in the repository or the logs.
/// </remarks>
public class MySqlRoundTripIntegrationTest
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("OSPF_TEST_MYSQL_CONN")
        ?? throw new InvalidOperationException("OSPF_TEST_MYSQL_CONN is not set; see the run instruction in logs/kotlin-sync-plan.md");

    /// <summary>入口能连上：真实执行一次 `SELECT 1` / The entry point connects: it really runs `SELECT 1`.</summary>
    [Fact]
    public async Task TheEntryPoint_ShouldConnect() {
        await using var context = Context(ConnectionString);

        // **用一句真的 SQL 证明连上了，而不是读一个布尔值：** `CanConnectAsync` 在这里给 false，
        // 而同一串连接串的往返测试是通过的——**一个说不清"为什么"的 false 不能当证据。**
        // **Prove it with a real statement instead of a boolean: `CanConnectAsync` answers false here while round-trips over the same connection
        // string succeed, and a false with no explanation is not evidence.**
        int answer = await context.Database.ExecuteSqlRawAsync("SELECT 1");
        answer.Should().Be(-1, "a statement returning rows reports -1 affected rows, and the call throwing is what would fail the test");
    }

    /// <summary>建表、插入、按主键查、更新、删除，五步都断言真实结果 / Create, insert, find by key, update, delete — each asserted.</summary>
    [Fact]
    public async Task TheRepository_ShouldRoundTripARow() {
        await using var context = Context(ConnectionString);

        // **不删库，只清表。** **第一版用 `EnsureDeletedAsync` 收尾，结果把库删掉了：** **同一次运行的"入口能连上"
        // 那条测试随后就报 `Unknown database 'ospf_test'`——**一条测试把另一条测试的地基拆了。**
        // **表名叫 `Entries` 而不是 `Rows`：`ROWS` 是 MySQL 8 的保留字，第一版就是被它绊住的。**
        // **The table is `Entries`, not `Rows`: `ROWS` is reserved in MySQL 8 and that is what tripped the first version.**
        // **Do not drop the database, only clear the table. The first version ended with `EnsureDeletedAsync`, which dropped the database: the
        // "entry point connects" criterion then failed with `Unknown database 'ospf_test'` — one test had removed another test's foundation.**
        // **建表用一句显式 SQL，不借 EF 的 `EnsureCreated`：** **它在库已存在时什么都不做，而 `HasTablesAsync` 问的是"有没有任何表"，
        // 不是"有没有这张表"——两者都让这一段在第一版里空转。**
        // **The table is created with an explicit statement rather than EF's `EnsureCreated`: that one does nothing when the database already exists,
        // and `HasTablesAsync` asks whether any table exists rather than whether this one does — both left the first version doing nothing.**
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS Entries (Id INT AUTO_INCREMENT PRIMARY KEY, Name LONGTEXT NOT NULL)");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM Entries");

        var repository = new RowRepository(context);
        var row = new RoundTripRow { Name = "first" };

        (await repository.InsertAsync(row)).IsOk.Should().BeTrue("the insert goes through the repository");
        row.Id.Should().BeGreaterThan(0, "the database assigned a key");

        (await repository.FindAllAsync()).Value.Should().HaveCount(1, "one row was inserted and the table was empty before");

        var found = await repository.FindByIdAsync(row.Id);
        found.IsOk.Should().BeTrue("the row just inserted must be findable");
        found.Value.Should().NotBeNull();
        found.Value!.Name.Should().Be("first", "the column round-trips its text as stored");

        row.Name = "second";
        (await repository.UpdateAsync(row)).Value.Should().Be(
            1L,
            "exactly one row matched the key, so the update reports one affected row");

        var updated = await repository.FindByIdAsync(row.Id);
        updated.Value!.Name.Should().Be("second", "the update really landed, not just returned ok");

        (await repository.DeleteAsync(row.Id)).IsOk.Should().BeTrue(
            "the repository deletes by key, so the key is what it gets");
        (await repository.FindAllAsync()).Value.Should().BeEmpty("the only row was deleted");
    }

    /// <summary>不可达端点报出原因，而不是抛异常或挂住 / An unreachable endpoint reports why instead of throwing or hanging.</summary>
    /// <remarks>
    /// 端口 1 上没有服务。仓库层把失败包成 `Failed`，本次断言的是**它真的给了原因**——一条"连不上"但没有原因的
    /// 错误，在排障时与"没有这条测试"等价。
    ///
    /// Nothing listens on port 1. The repository wraps failures in `Failed`, and what is asserted here is that it really gives a reason — a
    /// "cannot connect" with no reason is worth as much as no test at all when you are debugging.
    /// </remarks>
    [Fact]
    public async Task AnUnreachableEndpoint_ShouldReportWhy() {
        const string unreachable = "Server=127.0.0.1;Port=1;Database=ospf_test;Uid=nobody;Pwd=nobody;Connection Timeout=3";
        await using var context = Context(unreachable);
        var repository = new RowRepository(context);

        var result = await repository.FindAllAsync();
        result.IsOk.Should().BeFalse("port 1 has no server");
        string reason = ((Failed<System.Collections.Generic.IReadOnlyList<RoundTripRow>, ErrorCode, Error<ErrorCode>>)result).Message;
        reason.Should().Contain("MySQL", "the message has to name the backend, or a multi-backend failure is unattributable");
        reason.Length.Should().BeGreaterThan(20, "the reason has to carry the driver's own words, not just a code");
    }

    /// <summary>显式版本，不用 `AutoDetect`：后者在建上下文时就连接，会把"不可达"测成"构造失败的异常"。</summary>
    private static RoundTripContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<RoundTripContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0)))
            .Options);

    /// <summary>往返用的实体 / The entity used for the round-trip.</summary>
    private sealed class RoundTripRow {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RoundTripContext(DbContextOptions options) : DbContext(options) {
        public DbSet<RoundTripRow> Entries => Set<RoundTripRow>();
    }

    private sealed class RowRepository(DbContext context) : MySqlRepository<RoundTripRow>(context) {
    }
}
#endif