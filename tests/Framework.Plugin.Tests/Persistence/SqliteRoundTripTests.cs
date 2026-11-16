// SQLite 的真实往返 / A real round trip through SQLite.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **真实往返：写进去、关掉连接、再读出来** / A real round trip: write, close, read back.
/// </summary>
/// <remarks>
/// 已有的 `SqliteRepositoryTests` 每条判据都在**同一个连接**里写和读。那证明的是"对象没被改坏"，
/// **不证明"存下去了"**——一个只往内存字典里塞的实现也能全绿。
///
/// 这里因此**关掉连接再开一个新的**：只有真的落到了库里，第二次连接才看得见。
/// 这正是矩阵里"数据库持久化往返"那一行说"没有真实往返测试"时缺的东西。
///
/// The existing `SqliteRepositoryTests` write and read **inside one connection**. That proves the object was not mangled, **not that it was stored** — an
/// implementation that only fills a dictionary in memory passes all of them. This closes the connection and opens a new one, so only what really reached the file
/// is visible. That is exactly what the "no real round-trip test" note on the matrix row was pointing at.
/// </remarks>
public class SqliteRoundTripTests {
    /// <summary>**关掉连接后仍然读得到** / Still readable after the connection is closed.</summary>
    [Fact]
    public async Task WhatWasWritten_ShouldSurviveAClosedConnection() {
        // **文件库，不是 `:memory:`**：`:memory:` 库随连接消失，用它测"落盘"等于没测。
        // **A file database, not `:memory:`**: an in-memory one vanishes with its connection, so testing "it persisted" with it tests nothing.
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ospf-b950-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            await using (TestDbContext first = Open(file)) {
                TestSqliteRepository repository = new(first);
                Result<Success, ErrorCode, Error<ErrorCode>> inserted = await repository.InsertAsync(new TestEntity { Id = 7, Name = "written" });
                inserted.IsFailed.Should().BeFalse("the insert has to succeed before anything can be read back: " + inserted);
            }

            // **第一个上下文已经释放**，库文件是唯一的信息载体。
            // **The first context is disposed**; the file is the only carrier of the information.
            await using (TestDbContext second = Open(file)) {
                TestSqliteRepository repository = new(second);
                Result<TestEntity?, ErrorCode, Error<ErrorCode>> found = await repository.FindByIdAsync(7);
                found.IsFailed.Should().BeFalse("reading the row back must not fail: " + found);
                found.Value.Should().NotBeNull("the row has to be in the file, not in a dictionary that died with the context");
                found.Value!.Name.Should().Be("written");
            }
        } finally {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(file)) {
                System.IO.File.Delete(file);
            }
        }
    }

    /// <summary>**批量写入也真的落盘** / A batch insert really persists too.</summary>
    /// <remarks>
    /// 单条写和批量写走的是两条代码路径（`InsertAsync` / `InsertManyAsync`）。只测一条会放过另一条**忘了 `SaveChanges`**——
    /// 而那正是批量实现最容易漏的一步。
    ///
    /// A single insert and a batch insert take two code paths. Testing one lets the other **forget `SaveChanges`**, which is the step batch implementations miss most.
    /// </remarks>
    [Fact]
    public async Task ABatchInsert_ShouldAlsoPersist() {
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ospf-b950-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            await using (TestDbContext first = Open(file)) {
                TestSqliteRepository repository = new(first);
                Result<Success, ErrorCode, Error<ErrorCode>> inserted = await repository.InsertManyAsync([
                    new TestEntity { Id = 1, Name = "a" },
                    new TestEntity { Id = 2, Name = "b" },
                ]);
                inserted.IsFailed.Should().BeFalse("the batch insert has to succeed before its rows can be read back: " + inserted);
            }

            await using (TestDbContext second = Open(file)) {
                TestSqliteRepository repository = new(second);
                Result<IReadOnlyList<TestEntity>, ErrorCode, Error<ErrorCode>> all = await repository.FindAllAsync();
                all.IsFailed.Should().BeFalse("reading all rows back must not fail: " + all);
                all.Value.Select(entity => entity.Name).Should().BeEquivalentTo(new[] { "a", "b" },
                    "a batch path that forgets to save leaves the file empty and the result set clean");
            }
        } finally {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(file)) {
                System.IO.File.Delete(file);
            }
        }
    }

    private static TestDbContext Open(string file) {
        DbContextOptions<TestDbContext> options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=" + file)
            .Options;
        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
