#nullable enable

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 取余运算符接口 / Remainder Operator Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IRem<in TRhs, out TRet> {
    /// <summary>
    /// 取余运算 / Remainder operation
    /// </summary>
    /// <param name="rhs">除数 / Divisor</param>
    /// <returns>余数 / Remainder</returns>
    TRet Rem(TRhs rhs);
}

/// <summary>
/// 取余赋值接口 / Remainder Assignment Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
public interface IRemAssign<in TRhs> {
    /// <summary>
    /// 取余赋值 / Remainder assignment
    /// </summary>
    /// <param name="rhs">除数 / Divisor</param>
    void RemAssign(TRhs rhs);
}

/// <summary>
/// 取余扩展方法 / Remainder extension methods
/// </summary>
public static class RemExtensions {
    /// <summary>
    /// 取模运算（等价于 Rem）/ Modulo operation (equivalent to Rem)
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    /// <param name="self">自身 / Self</param>
    /// <param name="rhs">除数 / Divisor</param>
    /// <returns>余数 / Remainder</returns>
    public static TRet Mod<TRhs, TRet>(this IRem<TRhs, TRet> self, TRhs rhs) => self.Rem(rhs);
}
