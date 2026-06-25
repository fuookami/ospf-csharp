#nullable enable

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>
/// 无意义返回值的标记类型 / Marker type for return values with no meaningful content.
/// Replaces Kotlin's built-in Unit.
/// </summary>
public readonly struct Unit : System.IEquatable<Unit> {
    /// <summary>默认实例 / Default instance.</summary>
    public static readonly Unit Default = default;

    /// <inheritdoc/>
    public bool Equals(Unit other) => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <inheritdoc/>
    public override string ToString() => "()";

    /// <summary>相等运算符 / Equality operator.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>不等运算符 / Inequality operator.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
