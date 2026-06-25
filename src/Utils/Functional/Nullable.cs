#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>Nullable 辅助方法 / Nullable helper methods.</summary>
    public static class NullableExtensions
    {
        /// <summary>如果为 null 则返回默认值 / Return default if null.</summary>
        public static T IfNull<T>(this T? value, Func<T> defaultValue) where T : notnull =>
            value ?? defaultValue();

        /// <summary>如果为 null 或空则返回默认值 / Return default if null or empty.</summary>
        public static T IfNullOrEmpty<T>(this T? value, Func<T> defaultValue) where T : notnull =>
            value ?? defaultValue();
    }
}
