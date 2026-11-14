#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.Redis;
using System;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// `RedisConfig` 的 JSON 往返 / The JSON round trip of `RedisConfig`.
/// </summary>
/// <remarks>
/// **验收表 5.3 标着 ◐，而 `RedisInterfaceTests` 的十条判据全部是"成员在不在"**：
/// `Should_Have_ToJson_And_FromJson` 只断言两个方法**存在**，**没有一个字说它们对不对**。
///
/// **这条判据不需要 Redis 服务**：`ToJson` / `FromJson` 是纯序列化。
/// **而它管的是真事**：配置往返错了，客户端就会连到**另一个数据库**或**另一个实例**，
/// 而"连上了、能读写"会让这件事看起来完全正常。
///
/// **Acceptance row 5.3 is ◐, and all ten criteria in `RedisInterfaceTests` are "does the member exist"**:
/// `Should_Have_ToJson_And_FromJson` asserts only that the two methods **exist**, **saying nothing about whether they are right**.
///
/// **No Redis server is needed here**: `ToJson` / `FromJson` are pure serialization.
/// **And they govern something real**: a config that round-trips wrong sends the client to **another database** or **another instance**,
/// while "it connected and it reads and writes" makes that look perfectly fine.
/// </remarks>
public class RedisConfigRoundTripTests
{
    /// <summary>**往返之后每个字段都还在** / Every field survives the round trip.</summary>
    /// <remarks>
    /// **`Database` 和 `MasterName` 是最容易悄悄丢的两个**：一个默认 `0` 是合法值，
    /// **另一个是 `string?`，丢了之后哨兵部署会退化为主从直连，而连接仍然成功。**
    ///
    /// **`Database` and `MasterName` are the two that vanish most quietly**: `0` is a legal default,
    /// **and `MasterName` is a `string?` whose loss turns a sentinel deployment into a direct connection — while the connection still succeeds.**
    /// </remarks>
    [Fact]
    public void TheRoundTrip_ShouldPreserveEveryField()
    {
        var original = new RedisConfig(
            Urls: ["redis-a:6379", "redis-b:6380"],
            Name: "orders",
            MasterName: "mymaster",
            Database: 7,
            Password: "s3cret");

        // **不留中间变量，两条风格规则就没得吵**：IDE0007 要 `var`、IDE0008 对可空泛型要显式，
        // **而这里两行都是"变量声明"，硬凑只能让一条红**——**内联成一行，两条都不触发。**
        //
        // **No intermediate variable means the two style rules have nothing to argue about**: IDE0007 wants `var`, IDE0008 wants
        // **explicitness for nullable generics, and both lines are declarations, so any compromise turns one red — inlining triggers neither.**
        var restored = RedisConfig.FromJson(original.ToJson());

        restored.Should().NotBeNull("the JSON came from ToJson on the same type");
        // **`Equal` 的可变参数就是"期望的元素"，没有 reason 位置**——
        // **第一次我把 reason 写成第三个参数，于是判据要求列表里含那句英文，红。**
        //
        // **`Equal`'s variadic parameters ARE the expected elements; there is no reason slot** —
        // **the first version passed the reason as a third element, so the criterion demanded that sentence inside the list, and went red.**
        restored!.Urls.Should().Equal("redis-a:6379", "redis-b:6380");
        restored.Name.Should().Be("orders");
        restored.MasterName.Should().Be("mymaster",
            "losing it silently degrades a sentinel deployment to a direct connection");
        restored.Database.Should().Be(7,
            "a wrong database index reads and writes a different keyspace without any error");
        restored.Password.Should().Be("s3cret");

        // **派生键也要跟着走**：它是 `Name` 与 `Database` 的函数，两个字段对了它就该对。
        // **The derived key has to follow**: it is a function of `Name` and `Database`, so getting those right gets this right.
        restored.Key.Should().Be(original.Key);
    }

    /// <summary>**空串与 null 返回 null，不抛** / An empty or null string returns null instead of throwing.</summary>
    /// <remarks>
    /// **配置文件缺这一节是常态**，而**一个"配置没写"变成"启动时异常"的路径，
    /// 会让人以为是配置格式错了**——实际只是没写。
    ///
    /// **A missing section is normal in a config file**, and **turning "not configured" into "throws at startup"
    /// makes people look for a format error** when nothing was written at all.
    /// </remarks>
    [Fact]
    public void TheEmptyInput_ShouldReturnNullNotThrow()
    {
        RedisConfig.FromJson(string.Empty).Should().BeNull("an empty string is 'not configured'");
        RedisConfig.FromJson(null!).Should().BeNull("null is 'not configured' too");
    }
}
