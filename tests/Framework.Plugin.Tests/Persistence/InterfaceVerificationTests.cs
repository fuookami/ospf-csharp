#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Framework.Plugin.Persistence.MySQL;
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
}
