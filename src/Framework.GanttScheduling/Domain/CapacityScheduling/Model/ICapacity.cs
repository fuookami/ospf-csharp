#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 产能编译抽象接口 / Capacity compilation abstract interface
/// </summary>
/// <remarks>
/// 提供统一的产能计算接口，用于约束和目标函数。
/// Provides unified capacity calculation interface for constraints and objectives.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public interface ICapacity<A> where A : IProductionAction {
    /// <summary>执行器列表 / Executor list</summary>
    IReadOnlyList<Executor> Executors { get; }

    /// <summary>
    /// 解析解 / Extract solution from model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>产能调度解 / Capacity scheduling solution</returns>
    Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model);
}
