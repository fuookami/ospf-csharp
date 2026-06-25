#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.MultiArray;
/// <summary>
/// 默认值注册表，用于替代 Kotlin 的 reified T 默认值
/// Default value registry, replaces Kotlin's reified T default values
/// </summary>
public static class DefaultValueRegistry {
    private static readonly Dictionary<Type, Func<object>> Factories = new();

    static DefaultValueRegistry() {
        Register(typeof(int), () => 0);
        Register(typeof(long), () => 0L);
        Register(typeof(double), () => 0.0);
        Register(typeof(float), () => 0.0f);
        Register(typeof(bool), () => false);
        Register(typeof(string), () => "");
    }

    /// <summary>
    /// 注册一个类型的默认值工厂
    /// Register a default value factory for a type
    /// </summary>
    public static void Register(Type type, Func<object> factory) => Factories[type] = factory;

    /// <summary>
    /// 获取指定类型的默认值，如果未注册则返回 null
    /// Get default value for the specified type, returns null if not registered
    /// </summary>
    public static object? GetDefault(Type type) => Factories.TryGetValue(type, out Func<object>? factory) ? factory() : null;

    /// <summary>
    /// 获取指定类型的默认值，如果未注册则抛出异常
    /// Get default value for the specified type, throws if not registered
    /// </summary>
    public static T GetDefault<T>() {
        if (Factories.TryGetValue(typeof(T), out Func<object>? factory)) {
            return (T)factory();
        }
        throw new InvalidOperationException($"No default value registered for type {typeof(T).Name}");
    }

    /// <summary>
    /// 检查是否已注册指定类型
    /// Check if a type is registered
    /// </summary>
    public static bool IsRegistered(Type type) => Factories.ContainsKey(type);

    /// <summary>
    /// 检查是否已注册指定类型
    /// Check if a type is registered
    /// </summary>
    public static bool IsRegistered<T>() => Factories.ContainsKey(typeof(T));
}
