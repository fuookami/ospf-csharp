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
/// <remarks>
/// <para>
/// **两套并存的面，必须说清谁对应基线（B1271）：**
/// **基线的 `ospf-kotlin-core-plugin-heuristic` 里没有任何类——`GA.kt` 只有一个 `fun islandGenetic(...)`，
/// `HS.kt` 只有一个 `fun harmonySearch(...)`。**
/// </para>
/// <para>
/// **所以：本基类与它的 6 个子类不是移植来的，是 C# 侧等价的算法封装面；
/// 而真正的基线对应物在 `ComposedSolvers`（35 个入口，与基线工厂函数一一对应）。**
/// </para>
/// <para>
/// **两者都保留是有意的：组合函数给"按算子拼"的用法，类给"带策略对象的算法实例"的用法。
/// 而每个子类都必须被某个测试真正构造过——那条不变量写在 `AlgorithmClassesShouldBeTestedTests` 里，
/// 因为它是区分"算法"与"桩"的、不需要读实现的判据。**
/// </para>
/// <para>
/// **Two surfaces coexist, and which one mirrors the baseline must be stated: the baseline plugin has no classes —
/// `GA.kt` holds only `fun islandGenetic(...)`, `HS.kt` only `fun harmonySearch(...)`.**
/// **So this base class and its six subclasses were not ported; they are C#'s equivalent algorithm surface, while the real
/// baseline counterpart is `ComposedSolvers` (35 entry points).**
/// **Keeping both is deliberate, and every subclass must be constructed by some test — that invariant lives in
/// `AlgorithmClassesShouldBeTestedTests`, because it distinguishes an algorithm from a stub without reading the
/// implementation.**
/// </para>
/// </remarks>
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
