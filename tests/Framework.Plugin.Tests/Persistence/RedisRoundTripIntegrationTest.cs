#nullable enable
#if REDIS_INTEGRATION

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.Redis;
// **本文件的命名空间最后一段也叫 `Redis`，会把静态类 `Redis` 遮住**——用别名绕开。
// **This file's namespace ends in `Redis`, which shadows the static class `Redis`; an alias avoids the collision.**
using RedisRegistry = Fuookami.Ospf.Framework.Plugin.Persistence.Redis.Redis;
using Xunit;

// **命名空间里不能再出现 `Redis` 这一段：** **它会遮住静态类 `Redis`，`RedisInterfaceTests` 会编译失败。**
// **No `Redis` namespace segment: it shadows the static class `Redis` and breaks `RedisInterfaceTests`.**
namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **Redis 真实往返（连本机实例，不是影子检查）** / A real Redis round-trip against a live instance.
/// </summary>
/// <remarks>
/// **这一条此前不存在，而原因是错误分类。** **矩阵与验收表把 Redis 记成"环境所限"，而一次复审实测：
/// `127.0.0.1:6379` 端口开着、`PING` 返回 `+PONG`、且不需要密码。** **所以"没有环境"不成立，
/// 缺的只是<b>一条真的去连它的测试**。**
///
/// **这里补的就是这一条：** **用生产入口 `RedisRegistry.Init(config)` 起客户端（不是直接 new，因为构造函数是 `internal`），
/// 然后对五种取值形态各做一次真往返，并把写入的键删掉。**
///
/// **它验证的正是之前没人验过的那件事：** **`RedisClient` 的 203 行代码在真实服务器上能不能用。**
///
/// **This did not exist before, and the reason was a misclassification.** **The matrix and the acceptance table recorded Redis as
/// "limited by the environment", while a measurement of `127.0.0.1:6379` showed it open, `PING` answering `+PONG`, and no password required.**
/// **So the environment was there; what was missing was a test that actually connects.**adds that test: it starts the client
/// through the production entry point `RedisRegistry.Init(config)` (the constructor is `internal`), round-trips all five value shapes, and
/// deletes the keys it wrote.**
/// </remarks>
public class RedisRoundTripIntegrationTest
{
    private const string Endpoint = "127.0.0.1:6379";

    private static RedisConfig Config(string name) => new(
        Urls: new[] { Endpoint },
        Name: name,
        MasterName: null,
        Database: 15,
        Password: string.Empty);

    /// <summary>**生产入口能起来，且失败原因是空的** / The production entry point starts, and reports no failure.</summary>
    [Fact]
    public void TheProductionEntryPoint_ShouldStartAClient() {
        RedisClient? client = RedisRegistry.Init(Config("ospf-b1298-entry"));

        client.Should().NotBeNull(
            "a live Redis answers on 127.0.0.1:6379; if this fails, RedisRegistry.LastInitError says why");
        client!.Dispose();
        RedisRegistry.LastInitError.Should().BeNull("a successful initialization leaves no diagnostic behind");
    }

    /// <summary>**五个键删干净** / The five keys are removed afterwards.</summary>
    [Fact]
    public void TheFiveValueShapes_ShouldRoundTrip() {
        RedisClient? client = RedisRegistry.Init(Config("ospf-b1298-roundtrip"));
        client.Should().NotBeNull();
        string prefix = "ospf-b1298:" + Guid.NewGuid().ToString("N");
        string[] keys = [prefix + ":string", prefix + ":list", prefix + ":set", prefix + ":map", prefix + ":object"];

        try {
            // ① 字符串
            client!.Set(keys[0], "值-with-unicode");
            client.Get(keys[0]).Should().Be("值-with-unicode", "a string round-trips byte for byte");

            // **三个集合型必须用显式类型的局部变量：** **`HashSet<string>`／`Dictionary<,>` 也匹配泛型 `Set<T>`，
            // 于是会被写成 JSON 字符串，接着 `GetList`／`GetSet`／`GetMap` 就报 `WRONGTYPE`。**
            //
            // **The three collection shapes need explicitly typed locals: a `HashSet<string>` or `Dictionary<,>` also matches the
            // generic `Set<T>`, so it would be stored as a JSON string and `GetList`/`GetSet`/`GetMap` would then raise
            // `WRONGTYPE`.**
            IReadOnlyList<string> list = new[] { "a", "b", "b" };
            IReadOnlySet<string> set = new HashSet<string> { "x", "y", "y" };
            IReadOnlyDictionary<string, string> map = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };

            // ② 列表（有序，允许重复）
            client.Set(keys[1], list);
            client.GetList(keys[1]).Should().Equal(new[] { "a", "b", "b" }, "a list keeps order and duplicates");

            // ③ 集合（去重，无序）
            client.Set(keys[2], set);
            client.GetSet(keys[2]).Should().BeEquivalentTo(new[] { "x", "y" }, "a set drops duplicates");

            // ④ 哈希表
            client.Set(keys[3], map);
            client.GetMap(keys[3]).Should().BeEquivalentTo(
                new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
                "a map round-trips key by key");

            // ⑤ JSON 序列化对象
            client.Set(keys[4], new Payload("n", 42));
            client.GetObject<Payload>(keys[4]).Should().Be(
                new Payload("n", 42),
                "an object round-trips through JSON; records compare by value");
        }
        finally {
            foreach (string key in keys) {
                client!.Database.KeyDelete(key);
            }
        }

        // **删掉之后必须读不到——否则上面的"往返成功"可能只是一直读到旧值。**
        // **注意只有字符串/对象键能调 `Get`：** **对列表调 `StringGet` 会抛 `WRONGTYPE`（`Get` 与 `GetList` 是两种类型）。**
        //
        // **After deletion the keys must be gone.** **Only the string and object keys may be read with `Get`; calling `Get` on a list
        // raises `WRONGTYPE`, because `Get` and `GetList` expect different value types.**
        client!.Get(keys[0]).Should().BeNull($"{keys[0]} was deleted and must not read back");
        client.Get(keys[4]).Should().BeNull($"{keys[4]} was deleted and must not read back");
        foreach (string key in keys) {
            client.Database.KeyExists(key).Should().BeFalse($"{key} must really be gone");
        }
    }

    /// <summary>**过期时间真的生效** / The expiry really takes effect.</summary>
    /// <remarks>
    /// **`Set(..., ex)` 这条路径此前没有任何证据。** **用 1 秒的 TTL 等它过期，比断言 `TTL` 命令更接近调用方看到的事实。**
    ///
    /// **Nothing ever proved the `ex` path.** **Waiting out a one-second TTL is closer to what a caller observes than asserting the
    /// `TTL` command.**
    /// </remarks>
    [Fact]
    public void TheExpiry_ShouldTakeEffect() {
        RedisClient? client = RedisRegistry.Init(Config("ospf-b1298-ttl"));
        client.Should().NotBeNull();
        string key = "ospf-b1298:ttl:" + Guid.NewGuid().ToString("N");

        try {
            client!.Set(key, "short-lived", TimeSpan.FromSeconds(1));
            client.Get(key).Should().Be("short-lived", "the value is readable before it expires");
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            client.Get(key).Should().BeNull("the value is gone after the TTL elapses");
        }
        finally {
            client!.Database.KeyDelete(key);
        }
    }

    /// <summary>**连不上时给出原因，而不是静默返回 null** / An unreachable endpoint reports why.</summary>
    /// <remarks>
    /// **注意不能复用同一个配置名：** **`Redis.Init` 会先查已注册的客户端，同名同库会直接返回旧的**——
    /// **所以要用一个没人用过的名字，否则这条判据会假绿。**
    ///
    /// **The config name must be unused: `Redis.Init` returns an already-registered client for the same name and database, so
    /// reusing one would make this criterion green for the wrong reason.**
    /// </remarks>
    [Fact]
    public void AnUnreachableEndpoint_ShouldReportWhy() {
        RedisConfig bad = new(
            Urls: new[] { "127.0.0.1:6399" },
            Name: "ospf-b1298-unreachable",
            MasterName: null,
            Database: 15,
            Password: string.Empty);

        RedisClient? client = RedisRegistry.Init(bad);

        client.Should().BeNull("nothing listens on port 6399");
        RedisRegistry.LastInitError.Should().NotBeNull("the reason must be readable, not swallowed");
        RedisRegistry.LastInitError.Should().Contain(
            "6399",
            "the diagnostic names the endpoint that failed, so a reader can tell a wrong port from a wrong password");
    }

    /// <summary>往返用的载荷 / The payload used by the round-trip.</summary>
    public sealed record Payload(string Name, int Count);
}

#endif