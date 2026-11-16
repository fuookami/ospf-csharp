#nullable enable

using System;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.Redis;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **Redis 的失败原因必须能读出来（默认构建也覆盖）** /
/// Redis must expose why it failed to start (covered by the default build too).
/// </summary>
/// <remarks>
/// **真实往返的判据在 `#if REDIS_INTEGRATION` 里，只有本机有 Redis 时才跑。** **而"诊断面存在"这件事
/// <b>不该只在有服务器时才被检查**——它是一条 API 承诺。**
///
/// **所以这一条用反射钉两件事：** **① `Redis.LastInitError` 这个可读属性在；② 它是公开可读的。**
/// **删掉它，默认构建就红，而不是等到某次集成跑才发现。**
///
/// **The real round-trip lives behind `#if REDIS_INTEGRATION` and only runs where Redis exists.** **Whether the diagnostic surface
/// exists, however, is an API promise and <b>must not be checked only when a server happens to be around.** **So this pins two things
/// by reflection: the readable `Redis.LastInitError` property exists, and it is publicly readable.** **Deleting it reddens the default
/// build instead of surfacing in some later integration run.**
/// </remarks>
public class RedisInitDiagnosticTests
{
    /// <summary>**`LastInitError` 必须存在且公开可读** / `LastInitError` exists and is publicly readable.</summary>
    [Fact]
    public void TheInitDiagnostic_ShouldExist() {
        PropertyInfo? property = typeof(Redis).GetProperty(
            "LastInitError",
            BindingFlags.Public | BindingFlags.Static);

        property.Should().NotBeNull(
            "\"cannot connect\" and \"not configured\" used to look identical; the diagnostic is what tells them apart");
        property!.PropertyType.Should().Be<string?>(
            "the diagnostic is a nullable string: null means the last initialization succeeded");
        property.GetMethod!.IsPublic.Should().BeTrue(
            "a diagnostic that cannot be read from outside the assembly is not a diagnostic");
    }

    /// <summary>
    /// **`Init` 的签名没有变成抛异常** / `Init` still reports failure by returning null.
    /// </summary>
    /// <remarks>
    /// **这是一条刻意的约束：** **调用方（比如插件装配）已经把 `null` 当作"没配置"，把签名改成抛异常会是一次行为变更。**
    /// **判据把选择固定下来，避免下一个人顺手改掉。**
    ///
    /// **A deliberate constraint: callers already treat `null` as "not configured", so throwing would be a behaviour change.** **The
    /// criterion fixes that choice so nobody changes it casually.**
    /// </remarks>
    [Fact]
    public void TheInitSignature_ShouldStillReturnNullable() {
        MethodInfo? init = typeof(Redis).GetMethod(
            "Init",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(RedisConfig) },
            modifiers: null);

        init.Should().NotBeNull("`Redis.Init(RedisConfig)` is the production entry point");
        init!.ReturnType.Should().Be<RedisClient?>(
            "failure is reported by returning null plus `LastInitError`, not by throwing");
    }
}