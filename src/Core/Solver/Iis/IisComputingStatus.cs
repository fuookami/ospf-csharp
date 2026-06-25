#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Iis
{
    /// <summary>
    /// IIS 计算进度状态 / IIS computing progress status.
    /// </summary>
    /// <param name="Iteration">当前迭代 / Current iteration</param>
    /// <param name="ElapsedTime">已用时间 / Elapsed time</param>
    /// <param name="Found">是否已找到 IIS / Whether IIS found</param>
    public sealed record IisComputingStatus(
        int Iteration,
        TimeSpan ElapsedTime,
        bool Found);

    /// <summary>
    /// IIS 计算状态回调委托 / IIS computing status callback delegate.
    /// </summary>
    /// <param name="iteration">当前迭代 / Current iteration</param>
    /// <param name="elapsedTime">已用时间 / Elapsed time</param>
    /// <param name="status">IIS 计算状态 / IIS computing status</param>
    /// <returns>操作结果 / Operation result</returns>
    public delegate Try IisComputingStatusCallBack(int iteration, TimeSpan elapsedTime, IisComputingStatus status);
}
