#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service;
/// <summary>
/// 任务束解分析器 / Bunch solution analyzer
/// </summary>
public static class BunchSolutionAnalyzer {
    /// <summary>
    /// 从任务束编译 solver 解中提取任务束解 / Extract bunch solution from a bunch-compilation solver solution
    /// </summary>
    /// <typeparam name="B">任务束类型 / Task bunch type</typeparam>
    /// <typeparam name="T">任务类型 / Task type</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
    /// <param name="iteration">当前迭代号 / Current iteration number</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="bunches">按迭代分组的任务束 / Task bunches grouped by iteration</param>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="results">求解结果数组 / Solver results array</param>
    /// <returns>任务束解 / Bunch solution</returns>
    public static Result<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> Analyze<B, T, E, A>(
        ulong iteration,
        IReadOnlyList<T> tasks,
        IReadOnlyList<IReadOnlyList<B>> bunches,
        BunchCompilation<B, T, E, A> compilation,
        IReadOnlyList<Flt64> results)
        where B : AbstractTaskBunch<T, E, A>
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var assignedBunches = new List<B>();
        var canceledTasks = new List<T>();

        // Extract assigned bunches from results
        // (simplified: in real implementation, would iterate through solver tokens)
        for (int i = 0; i <= (int)iteration && i < bunches.Count; i++) {
            for (int j = 0; j < bunches[i].Count; j++) {
                int resultIndex = i * bunches[i].Count + j;
                if (resultIndex < results.Count && results[resultIndex].ToDouble() >= 0.5) {
                    assignedBunches.Add(bunches[i][j]);
                }
            }
        }

        return Results.Ok(new BunchSolution<B, T, E, A>(assignedBunches, canceledTasks));
    }
}
