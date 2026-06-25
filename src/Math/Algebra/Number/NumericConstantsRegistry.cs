#nullable enable

using System;
using System.Collections.Concurrent;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Algebra.Number
{
    /// <summary>
    /// 数值常量注册表（无反射，字典查找）/ Numeric constants registry (no reflection, dictionary lookup)
    /// <para>
    /// 替代 Kotlin 的 resolveRealNumberConstantsSafe / resolveCompanionProviderSafe。
    /// 每个数值类型在模块初始化时通过静态构造函数注册其常量实例。
    /// </para>
    /// <para>
    /// Replaces Kotlin's resolveRealNumberConstantsSafe / resolveCompanionProviderSafe.
    /// Each number type registers its constants instance at module load time via static constructor.
    /// </para>
    /// </summary>
    public static class NumericConstantsRegistry
    {
        private static readonly ConcurrentDictionary<Type, object> _store = new();

        /// <summary>
        /// 注册数值常量 / Register numeric constants
        /// </summary>
        public static void Register<T>(INumericConstants<T> constants)
            where T : struct, IRealNumber<T>
            => _store[typeof(T)] = constants;

        /// <summary>
        /// 检查是否已注册 / Check if registered
        /// </summary>
        public static bool IsRegistered<T>()
            where T : struct, IRealNumber<T>
            => _store.ContainsKey(typeof(T));

        /// <summary>
        /// 获取指定类型的常量（主访问器）/ Get constants for specified type (primary accessor)
        /// <para>
        /// 替代 ExpressionRange.Invoke / resolveRealNumberConstantsSafe。
        /// 无反射：生成器预注册；查找为字典命中。
        /// </para>
        /// <para>
        /// Replaces ExpressionRange.Invoke / resolveRealNumberConstantsSafe.
        /// No reflection: generator pre-registers; lookup is a dictionary hit.
        /// </para>
        /// </summary>
        public static INumericConstants<T> For<T>()
            where T : struct, IRealNumber<T>
        {
            NumericConstantsRegistrar.EnsureInitialized();
            if (_store.TryGetValue(typeof(T), out var boxed) && boxed is INumericConstants<T> c)
            {
                return c;
            }
            throw new InvalidOperationException(
                $"No INumericConstants<{typeof(T).Name}> registered. " +
                $"Call NumericConstantsRegistry.Register<{typeof(T).Name}>(...) at startup.");
        }

        /// <summary>
        /// 获取指定类型的常量（可能为 null）/ Get constants for specified type (nullable)
        /// </summary>
        public static INumericConstants<T>? ForOrNull<T>()
            where T : struct, IRealNumber<T>
        {
            NumericConstantsRegistrar.EnsureInitialized();
            return _store.TryGetValue(typeof(T), out var boxed) ? boxed as INumericConstants<T> : null;
        }
    }

    /// <summary>
    /// 伴生常量解析器（反射回退标志）/ Companion constant provider resolver (reflection fallback flag)
    /// <para>
    /// 默认关闭（与 Kotlin 一致）。仅在开启时允许 ForOrNull 扫描注册的程序集。
    /// </para>
    /// <para>
    /// Default OFF (matching Kotlin). Only when ON may ForOrNull scan registered assemblies.
    /// </para>
    /// </summary>
    public static class CompanionConstantProviderResolver
    {
        /// <summary>环境变量名 / Environment variable name</summary>
        public const string ReflectionFallbackEnabledProperty =
            "ospf.kotlin.math.enableCompanionReflectionFallback";

        /// <summary>反射回退是否启用 / Whether reflection fallback is enabled</summary>
        public static bool ReflectionFallbackEnabled =>
            ParseBoolean(Environment.GetEnvironmentVariable(ReflectionFallbackEnabledProperty));

        private static bool ParseBoolean(string? value) =>
            value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    /// <summary>
    /// 模块初始化注册器 / Module initializer registrar
    /// <para>
    /// 在模块加载时注册所有内置数值类型的常量。
    /// 替代 Roslyn 源代码生成器的 [ModuleInitializer] 注册。
    /// </para>
    /// </summary>
    internal static class NumericConstantsRegistrar
    {
        private static bool _initialized;
        private static readonly object _lock = new();

        /// <summary>
        /// 确保所有常量已注册 / Ensure all constants are registered
        /// </summary>
        internal static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                RegisterAll();
                _initialized = true;
            }
        }

        private static void RegisterAll()
        {
            // Floating types
            NumericConstantsRegistry.Register(Flt64Constants.Instance);
            NumericConstantsRegistry.Register(Flt32Constants.Instance);
            NumericConstantsRegistry.Register(FltXConstants.Instance);

            // Signed integers
            NumericConstantsRegistry.Register(Int8Constants.Instance);
            NumericConstantsRegistry.Register(Int16Constants.Instance);
            NumericConstantsRegistry.Register(Int32Constants.Instance);
            NumericConstantsRegistry.Register(Int64Constants.Instance);
            NumericConstantsRegistry.Register(IntXConstants.Instance);

            // Unsigned integers
            NumericConstantsRegistry.Register(UInt8Constants.Instance);
            NumericConstantsRegistry.Register(UInt16Constants.Instance);
            NumericConstantsRegistry.Register(UInt32Constants.Instance);
            NumericConstantsRegistry.Register(UInt64Constants.Instance);
            NumericConstantsRegistry.Register(UIntXConstants.Instance);

            // Rational (temporarily disabled - Rational.cs excluded)
            NumericConstantsRegistry.Register<RtnX>(RtnXConstants.Instance);
            // NumericConstantsRegistry.Register(URtn8Constants.Instance);
        }
    }
}
