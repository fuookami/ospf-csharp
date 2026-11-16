#nullable enable

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Fuookami.Ospf.Framework.Plugin.Persistence;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Framework.Plugin.Persistence.MySQL;
using Fuookami.Ospf.Framework.Plugin.Persistence.PostgreSQL;
using Fuookami.Ospf.Framework.Plugin.Persistence.Redis;
using Fuookami.Ospf.Framework.Plugin.Persistence.SQLite;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;
/// <summary>
/// 接口验证测试 / Interface verification tests.
///
/// 验证所有仓储接口和类型正确编译并可实例化。
/// Verifies that all repository interfaces and types compile and are instantiable.
/// </summary>
public class InterfaceVerificationTests {
    [Fact]
    public void IRepository_Interface_Should_Exist() {
        Type type = typeof(IRepository<>);
        type.Should().NotBeNull();
        type.IsInterface.Should().BeTrue();
        type.IsGenericType.Should().BeTrue();
    }

    [Fact]
    public void IPersistenceApiController_Interface_Should_Exist() {
        Type type = typeof(IPersistenceApiController);
        type.Should().NotBeNull();
        type.IsInterface.Should().BeTrue();
    }

    [Fact]
    public void MongoRepository_Should_Be_Abstract() {
        Type type = typeof(MongoRepository<>);
        type.Should().NotBeNull();
        type.IsAbstract.Should().BeTrue();
        type.IsGenericType.Should().BeTrue();
    }

    [Fact]
    public void SqliteRepository_Should_Be_Abstract() {
        Type type = typeof(SqliteRepository<>);
        type.Should().NotBeNull();
        type.IsAbstract.Should().BeTrue();
        type.IsGenericType.Should().BeTrue();
    }

    [Fact]
    public void MySqlRepository_Should_Be_Abstract() {
        Type type = typeof(MySqlRepository<>);
        type.Should().NotBeNull();
        type.IsAbstract.Should().BeTrue();
        type.IsGenericType.Should().BeTrue();
    }

    [Fact]
    public void RedisClient_Should_Implement_IDisposable() {
        Type type = typeof(RedisClient);
        type.Should().NotBeNull();
        typeof(IDisposable).IsAssignableFrom(type).Should().BeTrue();
    }

    [Fact]
    public void RedisConfig_Record_Should_Be_Creatable() {
        var config = new RedisConfig(
            Urls: new[] { "localhost:6379" },
            Name: "test",
            MasterName: null,
            Database: 0,
            Password: "");

        config.Name.Should().Be("test");
        config.Key.Should().Be(new RedisClientKey("test", 0));
    }

    [Fact]
    public void RedisConfig_Serialization_Roundtrip() {
        var config = new RedisConfig(
            Urls: new[] { "localhost:6379" },
            Name: "test",
            MasterName: "master",
            Database: 1,
            Password: "secret");

        string json = config.ToJson();
        json.Should().NotBeNullOrEmpty();

        var deserialized = RedisConfig.FromJson(json);
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("test");
        deserialized.Database.Should().Be(1);
    }

    [Fact]
    public void IRepository_Should_Have_All_Crud_Methods() {
        Type type = typeof(IRepository<object>);
        MethodInfo[] methods = type.GetMethods();

        methods.Should().Contain(m => m.Name == "FindByIdAsync");
        methods.Should().Contain(m => m.Name == "FindAllAsync");
        methods.Should().Contain(m => m.Name == "InsertAsync");
        methods.Should().Contain(m => m.Name == "InsertManyAsync");
        methods.Should().Contain(m => m.Name == "UpdateAsync");
        methods.Should().Contain(m => m.Name == "DeleteAsync");
    }

    [Fact]
    public void IRepository_FindByIdAsync_Should_Return_Task() {
        MethodInfo? method = typeof(IRepository<object>).GetMethod("FindByIdAsync");
        method.Should().NotBeNull();
        method!.ReturnType.IsGenericType.Should().BeTrue();
        method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(Task<>));
    }

    [Fact]
    public void IPersistenceApiController_Should_Have_MongoClient_Property() {
        PropertyInfo? prop = typeof(IPersistenceApiController).GetProperty("MongoClient");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(MongoDB.Driver.IMongoDatabase));
    }

    [Fact]
    public void RedisClient_Key_Should_Support_Equality() {
        var key1 = new RedisClientKey("test", 0);
        var key2 = new RedisClientKey("test", 0);
        var key3 = new RedisClientKey("other", 0);

        key1.Should().Be(key2);
        key1.Should().NotBe(key3);
        (key1 == key2).Should().BeTrue();
        (key1 == key3).Should().BeFalse();
    }

    /// <summary>**PostgreSQL 仓储也必须是抽象基类**/ The PostgreSQL repository must be an abstract base too.</summary>
    [Fact]
    public void PostgreSqlRepository_Should_Be_Abstract() {
        Type type = typeof(PostgreSqlRepository<>);
        type.IsAbstract.Should().BeTrue("it is a base class to be specialised, not an implementation");
        typeof(IRepository<>).MakeGenericType(typeof(object)).IsAssignableFrom(typeof(PostgreSqlRepository<>))
            .Should().BeFalse("the closed generic is abstract; only closed subclasses implement the contract");
    }


    /// <summary>**PostgreSQL 提供程序真的接上了**/ The PostgreSQL provider is really wired.</summary>
    /// <remarks>
    /// 这一条**不需要活的数据库**：`UseNpgsql` 在配置阶段就确定提供程序名，
    /// 所以它能证明"依赖进了程序集、配置写对了"，而不能证明"能连上"——**后者是外部未验收项**。
    ///
    /// This **needs no live database**: `UseNpgsql` fixes the provider name during configuration, so it proves "the dependency is in the assembly and the
    /// configuration is right", not "it can connect" — **the latter is an externally unverified item**.
    /// </remarks>
    [Fact]
    public void PostgreSqlProvider_ShouldBeConfiguredWithoutALiveDatabase() {
        DbContextOptions<DbContext> options = new DbContextOptionsBuilder<DbContext>()
            .UseNpgsql("Host=localhost;Database=ospf;Username=ospf;Password=ospf")
            .Options;

        using var context = new DbContext(options);
        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

}
