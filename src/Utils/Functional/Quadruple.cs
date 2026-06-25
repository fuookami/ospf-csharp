#nullable enable

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>四元组 / Quadruple (mirrors ospf-kotlin Quadruple).</summary>
public sealed record Quadruple<A, B, C, D>(A First, B Second, C Third, D Fourth);
