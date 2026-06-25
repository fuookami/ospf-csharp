#nullable enable

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Core.Solver.Output
{
    /// <summary>
    /// 求解器状态枚举，表示求解过程的各种结果。
    /// Solver status enum, representing various results of the solving process.
    /// </summary>
    public enum SolverStatus
    {
        /// <summary>最优解 / Optimal solution</summary>
        Optimal,
        /// <summary>可行解 / Feasible solution</summary>
        Feasible,
        /// <summary>不可行 / Infeasible</summary>
        Infeasible,
        /// <summary>无界 / Unbounded</summary>
        Unbounded,
        /// <summary>不可行或无界 / Infeasible or unbounded</summary>
        InfeasibleOrUnbounded,
        /// <summary>求解异常 / Solving exception</summary>
        SolvingException,
    }

    /// <summary>
    /// SolverStatus 扩展方法 / SolverStatus extension methods.
    /// </summary>
    public static class SolverStatusExtensions
    {
        /// <summary>是否成功 / Whether succeeded</summary>
        public static bool Succeeded(this SolverStatus status) => status switch
        {
            SolverStatus.Optimal or SolverStatus.Feasible => true,
            _ => false,
        };

        /// <summary>是否失败 / Whether failed</summary>
        public static bool Failed(this SolverStatus status) => !status.Succeeded();

        /// <summary>错误码 / Error code</summary>
        public static ErrorCode? ErrCode(this SolverStatus status) => status switch
        {
            SolverStatus.Infeasible => ErrorCode.ORModelInfeasible,
            SolverStatus.Unbounded => ErrorCode.ORModelUnbounded,
            SolverStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
            SolverStatus.SolvingException => ErrorCode.OREngineSolvingException,
            _ => null,
        };
    }
}
