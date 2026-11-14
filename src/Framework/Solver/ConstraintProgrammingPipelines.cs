#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// CP 注册管线契约 / CP registration pipeline contract.
/// </summary>
/// <remarks>
/// **与 `IPipeline&lt;M&gt;` 分开，不共用**：`IPipeline` 的注册走 `RegisterConstraintGroup`，那条路要求模型是
/// `IMetaModel&lt;Flt64&gt;`；CP 模型不是，硬套会让"管线不生效"表现为"注册被静默跳过"——
/// **而调用方看到的是一个空约束集，不是一个错误**。
///
/// **Kept separate from `IPipeline&lt;M&gt;` rather than shared**: `IPipeline` registers through `RegisterConstraintGroup`, which requires the model to be
/// an `IMetaModel&lt;Flt64&gt;`; a CP model is not one, so forcing it would make "the pipeline does not take effect" show up as "the registration was
/// silently skipped" — **and the caller sees an empty constraint set rather than an error**.
/// </remarks>
public interface IConstraintProgrammingPipeline {
    /// <summary>稳定管线名称 / Stable pipeline name.</summary>
    string Name { get; }

    /// <summary>注册变量与约束 / Register variables and constraints.</summary>
    /// <param name="model">接收注册内容的 CP 模型 / CP model receiving the registration.</param>
    /// <returns>注册结果 / The registration result.</returns>
    Try Register(ConstraintProgrammingModel model);
}

/// <summary>
/// CP 管线列表的执行辅助 / Helpers for running a list of CP pipelines.
/// </summary>
public static class ConstraintProgrammingPipelineExtensions {
    /// <summary>
    /// 依次执行 CP 管线 / Run the CP pipelines in order.
    /// </summary>
    /// <remarks>
    /// **遇到第一个失败就停下**：继续执行会让后续管线在缺失的前置注册之上继续建模，
    /// 而**失败点从第一个真实错误漂移到某个无关的"变量不存在"**。
    ///
    /// **Stops at the first failure**: continuing would let later pipelines keep modelling on top of a missing prerequisite registration,
    /// **moving the failure point from the first real error to some unrelated "variable does not exist"**.
    /// </remarks>
    /// <param name="pipelines">待执行管线 / Pipelines to run.</param>
    /// <param name="model">接收全部注册内容的 CP 模型 / CP model receiving every registration.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public static Try RegisterConstraintProgramming(
        this IReadOnlyList<IConstraintProgrammingPipeline> pipelines,
        ConstraintProgrammingModel model) {
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(model);
        foreach (IConstraintProgrammingPipeline pipeline in pipelines) {
            Try registered = pipeline.Register(model);
            if (registered.IsFailed) {
                return registered;
            }
        }
        return TryFactory.Create();
    }
}

/// <summary>
/// Benders 子问题管线契约 / Benders subproblem pipeline contract.
/// </summary>
public interface IBendersSubproblemPipeline {
    /// <summary>使用绑定上下文注册 CP 子问题 / Register a CP subproblem with the binding context.</summary>
    /// <param name="model">CP 子问题模型 / The CP subproblem model.</param>
    /// <param name="binding">主问题到 CP 的绑定上下文 / Master-to-CP binding context.</param>
    /// <returns>注册结果 / The registration result.</returns>
    Try Register(ConstraintProgrammingModel model, IBendersVariableBinding binding);

    /// <summary>把 CP 解提取回领域状态 / Extract a CP solution into domain state.</summary>
    /// <remarks>
    /// **默认什么都不做**：多数子问题管线只负责注册约束，解提取是可选的下游步骤。
    /// 默认报"未实现"会让"这个管线不需要提取"和"这个管线忘了实现提取"变成同一个错误。
    ///
    /// **Does nothing by default**: most subproblem pipelines only register constraints, and extraction is an optional downstream step. Defaulting to
    /// "not implemented" would make "this pipeline needs no extraction" and "this pipeline forgot to implement extraction" the same error.
    /// </remarks>
    /// <param name="solution">待提取的 CP 解 / The CP solution to extract.</param>
    /// <returns>提取结果 / The extraction result.</returns>
    Try ExtractSolution(ConstraintProgrammingSolution solution) => TryFactory.Create();
}
