#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// Solver 数值转换适配器 / Solver value conversion adapter for bridging domain V and solver Flt64.
/// </summary>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
public interface ISchedulingSolverValueAdapter<V> where V : struct, IRealNumber<V> {
    /// <summary>将 V 转换为 Flt64 / Convert V to Flt64.</summary>
    Flt64 ToFlt64(V value);

    /// <summary>将 Flt64 转换为 V / Convert Flt64 to V.</summary>
    V FromFlt64(Flt64 value);

    /// <summary>将 solver 解值舍入为最接近的合法值 / Round a solver solution value to the nearest legal value.</summary>
    V RoundSolution(Flt64 value);

    /// <summary>将 solver Flt64 下取整为 ulong / Floor a solver Flt64 to ulong for integer solution determination.</summary>
    ulong FloorToUInt64(Flt64 value);

    /// <summary>对 Flt64 值下取整 / Floor a Flt64 value.</summary>
    Flt64 FloorValue(Flt64 value);
}

/// <summary>
/// Flt64 恒等 adapter 实现 / Flt64 identity adapter implementation.
/// </summary>
public sealed class Flt64SolverValueAdapter : ISchedulingSolverValueAdapter<Flt64> {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly Flt64SolverValueAdapter Instance = new();

    /// <inheritdoc/>
    public Flt64 ToFlt64(Flt64 value) => value;

    /// <inheritdoc/>
    public Flt64 FromFlt64(Flt64 value) => value;

    /// <inheritdoc/>
    public Flt64 RoundSolution(Flt64 value) => new(System.Math.Round(value.ToDouble()));

    /// <inheritdoc/>
    public ulong FloorToUInt64(Flt64 value) => (ulong)System.Math.Round(value.ToDouble());

    /// <inheritdoc/>
    public Flt64 FloorValue(Flt64 value) => new(System.Math.Floor(value.ToDouble()));
}

/// <summary>
/// solver 默认 adapter 的共享出口 / Shared default adapter for solver internals.
/// </summary>
public static class SchedulingSolverValueAdapter {
    /// <summary>Flt64 默认 adapter / Default Flt64 adapter.</summary>
    public static ISchedulingSolverValueAdapter<Flt64> Flt64 => Flt64SolverValueAdapter.Instance;
}
