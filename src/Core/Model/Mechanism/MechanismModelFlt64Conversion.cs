#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 机制模型 Flt64 转换扩展 / Mechanism model Flt64 conversion extensions.
/// <para>将求解器的 Flt64 解向量转换为模型值类型 V。</para>
/// <para>Convert solver Flt64 solution vectors to the model value type V.</para>
/// </summary>
public static class MechanismModelFlt64ConversionExtensions {
    /// <summary>
    /// 将求解器 Flt64 解转换为模型值类型。
    /// Convert solver Flt64 solution to model value type.
    /// </summary>
    /// <typeparam name="V">目标数值类型 / Target numeric type</typeparam>
    /// <param name="converter">Flt64 值转换器 / Flt64 value converter</param>
    /// <param name="solverSolution">求解器 Flt64 解向量 / Solver Flt64 solution vector</param>
    /// <returns>转换后的值列表 / Converted value list</returns>
    public static IReadOnlyList<V> ConvertSolution<V>(
        this IFlt64ValueConverter<V> converter,
        IReadOnlyList<Flt64> solverSolution)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var result = new V[solverSolution.Count];
        for (int i = 0; i < solverSolution.Count; i++) {
            result[i] = converter.IntoValue(solverSolution[i]);
        }
        return result;
    }
}
