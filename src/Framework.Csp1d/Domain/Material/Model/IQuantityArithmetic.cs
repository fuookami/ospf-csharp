#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Error;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// Quantity&lt;V&gt; 算术策略 / Arithmetic strategy for Quantity&lt;V&gt;
///
/// 提供领域数值下的 Quantity 加减运算，避免在领域模型中直接依赖 Flt64/FltX。
/// Provides Quantity addition/subtraction under domain numerics, avoiding direct Flt64/FltX dependency in domain models.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface IQuantityArithmetic<V> where V : struct {
    /// <summary>
    /// 物理量加法 / Quantity addition
    /// </summary>
    /// <param name="a">左操作数 / Left operand.</param>
    /// <param name="b">右操作数 / Right operand.</param>
    /// <returns>相加结果 / Addition result.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Add(Quantity<V> a, Quantity<V> b);

    /// <summary>
    /// 物理量减法 / Quantity subtraction
    /// </summary>
    /// <param name="a">左操作数 / Left operand.</param>
    /// <param name="b">右操作数 / Right operand.</param>
    /// <returns>相减结果 / Subtraction result.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Subtract(Quantity<V> a, Quantity<V> b);

    /// <summary>
    /// 创建指定单位的零值物理量 / Create zero quantity for the given unit
    /// </summary>
    /// <param name="unit">物理单位 / Physical unit.</param>
    /// <returns>零值物理量 / Zero quantity.</returns>
    Quantity<V> Zero(PhysicalUnit unit);
}

// ===== Default implementations =====

internal sealed class DefaultQuantityArithmeticFlt64 : IQuantityArithmetic<Flt64> {
    public static readonly DefaultQuantityArithmeticFlt64 Instance = new();
    private DefaultQuantityArithmeticFlt64() { }

    public Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> Add(Quantity<Flt64> a, Quantity<Flt64> b) => a.Add(b);
    public Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> Subtract(Quantity<Flt64> a, Quantity<Flt64> b) => a.Subtract(b);
    public Quantity<Flt64> Zero(PhysicalUnit unit) => new(Flt64.Zero, unit);
}

internal sealed class DefaultQuantityArithmeticFltX : IQuantityArithmetic<FltX> {
    public static readonly DefaultQuantityArithmeticFltX Instance = new();
    private DefaultQuantityArithmeticFltX() { }

    public Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> Add(Quantity<FltX> a, Quantity<FltX> b) => a.Add(b);
    public Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> Subtract(Quantity<FltX> a, Quantity<FltX> b) => a.Subtract(b);
    public Quantity<FltX> Zero(PhysicalUnit unit) => new(FltX.Zero, unit);
}

/// <summary>
/// 默认物理量算术解析器 / Default quantity arithmetic resolver
///
/// 通过领域数值样本解析对应的 IQuantityArithmetic 实现，调用方只依赖泛型接口。
/// Resolves an IQuantityArithmetic implementation from a domain value sample so callers depend only on the generic interface.
/// </summary>
public static class DefaultQuantityArithmetic {
    /// <summary>
    /// 根据领域数值样本解析算术策略 / Resolve arithmetic strategy from a domain value sample
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="sample">领域数值样本 / Domain value sample.</param>
    /// <returns>泛型物理量算术策略 / Quantity arithmetic strategy.</returns>
    public static Result<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>> ResolveFor<V>(V sample)
        where V : struct {
        if (sample is Flt64) {
            return new Ok<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>>(
                (IQuantityArithmetic<V>)(object)DefaultQuantityArithmeticFlt64.Instance);
        }
        if (sample is FltX) {
            return new Ok<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>>(
                (IQuantityArithmetic<V>)(object)DefaultQuantityArithmeticFltX.Instance);
        }
        return new Failed<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>>(
            new Csp1dTypeError($"Unsupported RealNumber type: {sample.GetType().Name}"));
    }
}
