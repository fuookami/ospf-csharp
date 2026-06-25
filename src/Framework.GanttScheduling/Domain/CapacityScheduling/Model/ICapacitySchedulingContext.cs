#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 产能调度上下文接口 / Capacity scheduling context interface
/// </summary>
/// <remarks>
/// 提供产能调度的统一抽象接口。
/// Provides unified abstract interface for capacity scheduling.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public interface ICapacitySchedulingContext<A> where A : IProductionAction {
    /// <summary>生产动作列表 / List of production actions</summary>
    IReadOnlyList<A> Actions { get; }

    /// <summary>时隙列表 / List of time slots</summary>
    IReadOnlyList<TimeRange> Slots { get; }

    /// <summary>时间窗口 / Time window</summary>
    TimeWindow<Flt64> TimeWindow { get; }

    /// <summary>产能编译对象 / Capacity compilation object</summary>
    ICapacity<A> Compilation { get; }

    /// <summary>
    /// 注册到模型 / Register to model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    Try Register(object model);

    /// <summary>
    /// 从模型解析解 / Extract solution from model
    /// </summary>
    /// <param name="model">抽象线性元模型 / Abstract linear meta model</param>
    /// <returns>产能调度解 / Capacity scheduling solution</returns>
    Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model);
}
