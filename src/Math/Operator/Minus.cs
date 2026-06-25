#nullable enable

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 减法运算符接口 / Subtraction Operator Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IMinus<in TRhs, out TRet> {
    /// <summary>
    /// 减法运算 / Subtraction operation
    /// </summary>
    /// <param name="rhs">减数 / Subtrahend</param>
    /// <returns>差 / Difference</returns>
    TRet Minus(TRhs rhs);
}

/// <summary>
/// 减法赋值接口 / Subtraction Assignment Interface
/// </summary>
/// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
public interface IMinusAssign<in TRhs> {
    /// <summary>
    /// 减法赋值 / Subtraction assignment
    /// </summary>
    /// <param name="rhs">减数 / Subtrahend</param>
    void MinusAssign(TRhs rhs);
}

/// <summary>
/// 自减运算符接口 / Decrement Operator Interface
/// </summary>
/// <typeparam name="TSelf">自减后返回的类型 / Type returned after decrement</typeparam>
public interface IDec<TSelf> {
    /// <summary>
    /// 自减运算 / Decrement operation
    /// </summary>
    /// <returns>自减后的新实例 / New instance after decrement</returns>
    TSelf Decrement();
}
