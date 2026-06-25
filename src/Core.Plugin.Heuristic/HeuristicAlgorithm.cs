#nullable enable

using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Heuristic;
/// <summary>
/// 启发式算法统一基类 / Uniform base for all heuristic algorithms.
/// 每个算法通过 InvokeAsync 执行迭代搜索。
/// Each algorithm runs its iterative search via InvokeAsync.
/// </summary>
/// <typeparam name="Obj">目标类型 / Objective type</typeparam>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public abstract class HeuristicAlgorithm<Obj, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 执行启发式搜索 / Execute the heuristic search.
    /// </summary>
    /// <param name="model">回调模型接口 / callback model interface</param>
    /// <param name="runningCallBack">每轮迭代回调（返回 Failed 提前终止）/ per-iteration callback (Failed aborts)</param>
    /// <param name="cancellationToken">取消令牌 / cancellation token</param>
    /// <returns>最优个体列表 / best individuals</returns>
    public abstract Task<Result<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IAbstractCallBackModelInterface<Obj, ObjValue, V> model,
        Func<Iteration, IIndividual<ObjValue, V>, IReadOnlyList<IIndividual<ObjValue, V>>, Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null,
        CancellationToken cancellationToken = default);
}
