#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Error;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 领域值转换工具 / Domain value conversion utilities
/// </summary>
public static class DomainValueConversion {
    /// <summary>
    /// 转换 solver 值到领域数值类型 / Convert solver value to domain numeric type
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="sample">领域数值样本 / Domain value sample.</param>
    /// <param name="value">solver 边界值 / Solver boundary value.</param>
    /// <returns>与 sample 同类型的领域数值 / Domain value with the same numeric type as sample.</returns>
    public static Result<V, ErrorCode, Error<ErrorCode>> ConvertSolverValue<V>(V sample, Flt64 value)
        where V : struct {
        if (sample is Flt64) {
            return new Ok<V, ErrorCode, Error<ErrorCode>>(value is V v ? v : default);
        }
        if (sample is FltX) {
            var fltXVal = value.ToFltX();
            return new Ok<V, ErrorCode, Error<ErrorCode>>(fltXVal is V v ? v : default);
        }
        return new Failed<V, ErrorCode, Error<ErrorCode>>(
            new Csp1dCapabilityError($"RealNumber type: {sample.GetType().Name}"));
    }

    /// <summary>
    /// 将领域数值转换为 Flt64 / Convert domain value to Flt64
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="value">领域数值 / Domain value.</param>
    /// <returns>Flt64 值 / Flt64 value.</returns>
    public static Flt64 ToFlt64<V>(this V value) where V : struct {
        if (value is Flt64 f) {
            return f;
        }

        if (value is FltX fx) {
            return fx.ToFlt64();
        }

        if (value is Fuookami.Ospf.Math.Algebra.Number.Int64 i64) {
            return i64.ToFlt64();
        }

        return new Flt64(System.Convert.ToDouble(value));
    }
}
