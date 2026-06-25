#nullable enable

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 除法运算符接口 / Division Operator Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface IDiv<in TRhs, out TRet>
    {
        /// <summary>
        /// 除法运算 / Division operation
        /// </summary>
        /// <param name="rhs">除数 / Divisor</param>
        /// <returns>商 / Quotient</returns>
        TRet Div(TRhs rhs);
    }

    /// <summary>
    /// 除法赋值接口 / Division Assignment Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    public interface IDivAssign<in TRhs>
    {
        /// <summary>
        /// 除法赋值 / Division assignment
        /// </summary>
        /// <param name="rhs">除数 / Divisor</param>
        void DivAssign(TRhs rhs);
    }

    /// <summary>
    /// 整数除法接口 / Integer Division Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface IIntDiv<in TRhs, out TRet>
    {
        /// <summary>
        /// 整数除法 / Integer division
        /// </summary>
        /// <param name="rhs">除数 / Divisor</param>
        /// <returns>整数商 / Integer quotient</returns>
        TRet IntDiv(TRhs rhs);
    }

    /// <summary>
    /// 整数除法赋值接口 / Integer Division Assignment Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    public interface IIntDivAssign<in TRhs>
    {
        /// <summary>
        /// 整数除法赋值 / Integer division assignment
        /// </summary>
        /// <param name="rhs">除数 / Divisor</param>
        void IntDivAssign(TRhs rhs);
    }
}
