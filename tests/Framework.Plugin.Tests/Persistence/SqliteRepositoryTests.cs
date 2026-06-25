#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.SQLite;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;
/// <summary>
/// 测试实体 / Test entity.
/// </summary>
public class TestEntity {
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

/// <summary>
/// 测试 DbContext / Test database context.
/// </summary>
public class TestDbContext : DbContext {
    public DbSet<TestEntity> TestEntities { get; set; } = null!;

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
}

/// <summary>
/// 测试 SQLite 仓储实现 / Test SQLite repository implementation.
/// </summary>
public class TestSqliteRepository : SqliteRepository<TestEntity> {
    public TestSqliteRepository(TestDbContext context) : base(context) { }
}

/// <summary>
/// SQLite 仓储集成测试 / SQLite repository integration tests.
///
/// 使用 SQLite 内存数据库，CI 可运行。
/// Uses SQLite in-memory database, CI-runnable.
/// </summary>
public class SqliteRepositoryTests : IDisposable {
    private readonly TestDbContext _context;
    private readonly TestSqliteRepository _repository;

    public SqliteRepositoryTests() {
        DbContextOptions<TestDbContext> options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new TestDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new TestSqliteRepository(_context);
    }

    public void Dispose() {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Fact]
    public async Task InsertAsync_Should_Add_Entity() {
        var entity = new TestEntity { Id = 1, Name = "Test", Value = 42 };

        Result<Success, ErrorCode, Error<ErrorCode>> result = await _repository.InsertAsync(entity);

        result.IsOk.Should().BeTrue();
        TestEntity? found = await _context.TestEntities.FindAsync(1);
        found.Should().NotBeNull();
        found!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task FindByIdAsync_Should_Return_Entity() {
        _context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test", Value = 42 });
        await _context.SaveChangesAsync();

        Result<TestEntity?, ErrorCode, Error<ErrorCode>> result = await _repository.FindByIdAsync(1);

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task FindByIdAsync_Should_Return_Null_For_Missing_Entity() {
        Result<TestEntity?, ErrorCode, Error<ErrorCode>> result = await _repository.FindByIdAsync(999);

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task FindAllAsync_Should_Return_All_Entities() {
        _context.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "A", Value = 1 },
            new TestEntity { Id = 2, Name = "B", Value = 2 });
        await _context.SaveChangesAsync();

        Result<IReadOnlyList<TestEntity>, ErrorCode, Error<ErrorCode>> result = await _repository.FindAllAsync();

        result.IsOk.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task InsertManyAsync_Should_Add_Multiple_Entities() {
        var entities = new List<TestEntity>
        {
            new() { Id = 1, Name = "A", Value = 1 },
            new() { Id = 2, Name = "B", Value = 2 },
            new() { Id = 3, Name = "C", Value = 3 },
        };

        Result<Success, ErrorCode, Error<ErrorCode>> result = await _repository.InsertManyAsync(entities);

        result.IsOk.Should().BeTrue();
        _context.TestEntities.Count().Should().Be(3);
    }

    [Fact]
    public async Task UpdateAsync_Should_Modify_Entity() {
        _context.TestEntities.Add(new TestEntity { Id = 1, Name = "Old", Value = 1 });
        await _context.SaveChangesAsync();

        // Detach tracked entity before updating via repository
        _context.ChangeTracker.Clear();

        var entity = new TestEntity { Id = 1, Name = "New", Value = 99 };
        Result<long, ErrorCode, Error<ErrorCode>> result = await _repository.UpdateAsync(entity);

        result.IsOk.Should().BeTrue();
        // Clear again to force re-read from DB
        _context.ChangeTracker.Clear();
        TestEntity? found = await _context.TestEntities.FindAsync(1);
        found!.Name.Should().Be("New");
        found.Value.Should().Be(99);
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_Entity() {
        _context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test", Value = 1 });
        await _context.SaveChangesAsync();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await _repository.DeleteAsync(1);

        result.IsOk.Should().BeTrue();
        _context.TestEntities.Count().Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_Should_Fail_For_Missing_Entity() {
        Result<Success, ErrorCode, Error<ErrorCode>> result = await _repository.DeleteAsync(999);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task InsertAsync_Should_Return_Failed_On_Duplicate_Key() {
        _context.TestEntities.Add(new TestEntity { Id = 1, Name = "First", Value = 1 });
        await _context.SaveChangesAsync();

        var duplicate = new TestEntity { Id = 1, Name = "Duplicate", Value = 2 };
        Result<Success, ErrorCode, Error<ErrorCode>> result = await _repository.InsertAsync(duplicate);

        // SQLite throws on duplicate key; repository should catch and return Failed
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task IRepository_Methods_Should_Return_Result_Type() {
        var entity = new TestEntity { Id = 1, Name = "Test", Value = 1 };
        Result<Success, ErrorCode, Error<ErrorCode>> insertResult = await _repository.InsertAsync(entity);
        insertResult.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        Result<TestEntity?, ErrorCode, Error<ErrorCode>> findResult = await _repository.FindByIdAsync(1);
        findResult.Should().BeOfType<Ok<TestEntity?, ErrorCode, Error<ErrorCode>>>();

        Result<IReadOnlyList<TestEntity>, ErrorCode, Error<ErrorCode>> allResult = await _repository.FindAllAsync();
        allResult.Should().BeOfType<Ok<IReadOnlyList<TestEntity>, ErrorCode, Error<ErrorCode>>>();
    }
}
