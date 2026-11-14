// 路径编译上下文 / Path compilation context.
// 与固定 Kotlin 基线 `path_compilation/PathCompilationContext.kt`（5133 字节）中的上下文类对应。
// Mirrors the context class of the pinned baseline's `path_compilation/PathCompilationContext.kt`.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation;

/// <summary>
/// 每个模型独立的通用路径选择入口 / An independent per-model entry for generic path selection.
/// </summary>
/// <remarks>
/// **solver 最优状态不证明全图或定价域最优**。这个入口只求解给定候选池；把它的结果当成全局最优是最常见的误用，因此
/// <see cref="PathCompilationSolution{V, C}"/> 必须携带 `CandidateIds`。
/// **The solver's optimal status proves nothing about the full network or the pricing domain.** This entry only solves the given
/// candidate pool; treating its result as a global optimum is the most common misuse, which is why
/// <see cref="PathCompilationSolution{V, C}"/> must carry `CandidateIds`.
/// **这一层是薄包装，不新增状态**。所有注册态、修订号与对偶快照都由 <see cref="PathCompilationAggregation{V, C}"/> 持有；上下文只提供
/// 默认命名空间与转发，避免两处状态互相漂移。
/// **This layer is a thin wrapper and adds no state.** All registration state, revision numbers and dual snapshots live in
/// <see cref="PathCompilationAggregation{V, C}"/>; the context only supplies the default namespace and forwards, so the two can
/// never drift apart.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
public sealed class PathCompilationContext<V, C> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造路径编译上下文 / Construct the path compilation context.
    /// </summary>
    /// <param name="problem">有限候选问题 / The finite-candidate problem.</param>
    /// <param name="adapter">数值与单位边界 / The numeric and unit boundary.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    public PathCompilationContext(
        PathCompilationProblem<V, C> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "path_compilation") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(name);
        Problem = problem;
        Adapter = adapter;
        Name = name;
        Aggregation = new PathCompilationAggregation<V, C>(problem, adapter, name);
    }

    /// <summary>有限候选问题 / The finite-candidate problem.</summary>
    public PathCompilationProblem<V, C> Problem { get; }

    /// <summary>数值与单位边界 / The numeric and unit boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> Adapter { get; }

    /// <summary>命名空间 / The namespace.</summary>
    public string Name { get; }

    /// <summary>列生命周期聚合 / The column-lifecycle aggregation.</summary>
    public PathCompilationAggregation<V, C> Aggregation { get; }

    /// <summary>
    /// 注册共享主问题 / Register the shared master.
    /// </summary>
    /// <param name="model">当前模型 / The current model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Aggregation.Register(model);
    }

    /// <summary>
    /// 添加列，同时更新行与目标 / Add columns, updating rows and the objective.
    /// </summary>
    /// <param name="columns">完整候选 / The complete candidates.</param>
    /// <param name="model">当前模型 / The current model.</param>
    /// <returns>实际新增的列 / The columns actually added.</returns>
    public Result<IReadOnlyList<PathColumn<V, C>>, ErrorCode, Error<ErrorCode>> AddColumns(
        IReadOnlyList<PathColumn<V, C>> columns,
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(model);
        return Aggregation.AddColumns(columns, model);
    }

    /// <summary>
    /// 删除列并清除目标与符号项 / Remove columns and clear their objective and symbol terms.
    /// </summary>
    /// <param name="ids">稳定列标识 / The stable column identities.</param>
    /// <param name="model">当前模型 / The current model.</param>
    /// <returns>删除结果 / The removal result.</returns>
    public Try RemoveColumns(IReadOnlySet<string> ids, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(model);
        return Aggregation.RemoveColumns(ids, model);
    }

    /// <summary>
    /// 从已求解模型提取整条路径 / Extract whole paths from a solved model.
    /// </summary>
    /// <param name="model">当前模型 / The current model.</param>
    /// <returns>有限候选集结果 / The finite-pool result.</returns>
    public Result<PathCompilationSolution<V, C>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Aggregation.ExtractSolution(model);
    }
}
