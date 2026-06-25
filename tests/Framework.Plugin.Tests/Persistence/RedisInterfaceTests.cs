#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.Redis;
using System;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;
/// <summary>
/// Redis 接口验证测试 / Redis interface verification tests.
///
/// 验证 Redis 客户端类型正确编译并支持预期操作。
/// Verifies that Redis client types compile and support expected operations.
/// </summary>
public class RedisInterfaceTests {
    [Fact]
    public void RedisClient_Should_Implement_IDisposable() => typeof(IDisposable).IsAssignableFrom(typeof(RedisClient)).Should().BeTrue();

    [Fact]
    public void RedisClient_Should_Have_Set_Methods() {
        MethodInfo[] methods = typeof(RedisClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        System.Array.Exists(methods, m => m.Name == "Set").Should().BeTrue();
    }

    [Fact]
    public void RedisClient_Should_Have_Get_Method() {
        MethodInfo? method = typeof(RedisClient).GetMethod("Get");
        method.Should().NotBeNull();
    }

    [Fact]
    public void RedisClient_Should_Have_GetList_Method() {
        MethodInfo? method = typeof(RedisClient).GetMethod("GetList");
        method.Should().NotBeNull();
    }

    [Fact]
    public void RedisClient_Should_Have_GetSet_Method() {
        MethodInfo? method = typeof(RedisClient).GetMethod("GetSet");
        method.Should().NotBeNull();
    }

    [Fact]
    public void RedisClient_Should_Have_GetMap_Method() {
        MethodInfo? method = typeof(RedisClient).GetMethod("GetMap");
        method.Should().NotBeNull();
    }

    [Fact]
    public void RedisClient_Should_Have_GetObject_Method() {
        MethodInfo[] methods = typeof(RedisClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        System.Array.Exists(methods, m => m.Name == "GetObject").Should().BeTrue();
    }

    [Fact]
    public void Redis_Static_Manager_Should_Have_Init_Method() {
        MethodInfo? method = typeof(Redis).GetMethod("Init");
        method.Should().NotBeNull();
    }

    [Fact]
    public void Redis_Static_Manager_Should_Have_Get_Method() {
        MethodInfo? method = typeof(Redis).GetMethod("Get", new[] { typeof(RedisClientKey) });
        method.Should().NotBeNull();
    }

    [Fact]
    public void Redis_Static_Manager_Should_Have_GetByName_Method() {
        MethodInfo? method = typeof(Redis).GetMethod("GetByName");
        method.Should().NotBeNull();
    }

    [Fact]
    public void RedisConfig_Should_Have_ToJson_And_FromJson() {
        typeof(RedisConfig).GetMethod("ToJson").Should().NotBeNull();
        typeof(RedisConfig).GetMethod("FromJson").Should().NotBeNull();
    }
}
