#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>带条件的值 / Value with condition (mirrors ospf-kotlin Condition).</summary>
public sealed record Condition<T>(T Value, Predicate<T> Predicate);
