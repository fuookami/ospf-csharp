#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>带条件的值 / Value with condition (mirrors ospf-kotlin Condition).</summary>
public sealed record Condition<T>(T Value, Predicate<T> Predicate) {
    private readonly Lazy<bool> _result = new(() => Predicate(Value));

    /// <summary>谓词的惰性结果 / Lazily evaluated predicate result.</summary>
    public bool Result => _result.Value;

    /// <summary>谓词为真时返回 null / Return null when the predicate is true.</summary>
    public T? AsNull() => Result ? default : Value;
}

/// <summary>Condition 辅助方法 / Condition helper methods.</summary>
public static class ConditionExtensions {
    /// <summary>为值附加惰性条件 / Attach a lazy condition to a value.</summary>
    public static Condition<T> IfTrue<T>(this T value, Predicate<T> predicate) =>
        new(value, predicate);
}
