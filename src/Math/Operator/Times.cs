#nullable enable

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 乘法运算符接口 / Multiplication Operator Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface ITimes<in TRhs, out TRet> {
    /// <summary>
    /// 乘法运算 / Multiplication operation
    /// </summary>
    /// <param name="rhs">乘数 / Multiplier</param>
    /// <returns>积 / Product</returns>
    TRet Times(TRhs rhs);
}

/// <summary>
/// 乘法赋值接口 / Multiplication Assignment Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
public interface ITimesAssign<in TRhs> {
    /// <summary>
    /// 乘法赋值 / Multiplication assignment
    /// </summary>
    /// <param name="rhs">乘数 / Multiplier</param>
    void TimesAssign(TRhs rhs);
}

/// <summary>
/// 叉积运算符接口 / Cross Product Operator Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface ICross<in TRhs, out TRet> {
    /// <summary>
    /// 叉积运算 / Cross product operation
    /// </summary>
    /// <param name="rhs">右向量 / Right vector</param>
    /// <returns>叉积结果 / Cross product result</returns>
    TRet Cross(TRhs rhs);
}
