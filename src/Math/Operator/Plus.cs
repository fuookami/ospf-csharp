#nullable enable

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 加法运算符接口 / Addition Operator Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface IPlus<in TRhs, out TRet>
    {
        /// <summary>
        /// 加法运算 / Addition operation
        /// </summary>
        /// <param name="rhs">加数 / Addend</param>
        /// <returns>和 / Sum</returns>
        TRet Plus(TRhs rhs);
    }

    /// <summary>
    /// 加法运算符特征接口 / Addition Operator Trait Interface
    /// </summary>
    /// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface IPlusTrait<TSelf, in TRhs, out TRet>
    {
        /// <summary>
        /// 加法运算 / Addition operation
        /// </summary>
        /// <param name="self">接收者 / Receiver</param>
        /// <param name="rhs">加数 / Addend</param>
        /// <returns>和 / Sum</returns>
        TRet Plus(TSelf self, TRhs rhs);
    }

    /// <summary>
    /// 加法赋值接口 / Addition Assignment Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    public interface IPlusAssign<in TRhs>
    {
        /// <summary>
        /// 加法赋值 / Addition assignment
        /// </summary>
        /// <param name="rhs">加数 / Addend</param>
        void PlusAssign(TRhs rhs);
    }

    /// <summary>
    /// 自增运算符接口 / Increment Operator Interface
    /// </summary>
    /// <typeparam name="TSelf">自增后返回的类型 / Type returned after increment</typeparam>
    public interface IInc<TSelf>
    {
        /// <summary>
        /// 自增运算 / Increment operation
        /// </summary>
        /// <returns>自增后的新实例 / New instance after increment</returns>
        TSelf Increment();
    }
}
