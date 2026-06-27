#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;

/// <summary>
/// 列生成主模型目标系数的参数。
/// Parameters for column generation master model objective coefficients.
/// </summary>
/// <remarks>
/// 默认值移植自 fsra-proof 基础设施参数。
/// Default values ported from fsra-proof infrastructure parameters.
/// </remarks>
/// <param name="FleetBalanceSlack">非基地机场车队平衡松弛惩罚 / Fleet balance slack penalty for non-base airports.</param>
/// <param name="FleetBalanceBaseSlack">基地机场车队平衡松弛惩罚 / Fleet balance slack penalty for base airports.</param>
/// <param name="ExecutorLeisureCoeff">执行器空闲最小化系数 / Executor leisure minimization coefficient.</param>
/// <param name="TaskCancelCoeff">任务取消最小化系数 / Task cancel minimization coefficient.</param>
public sealed record Parameter(
    Flt64? FleetBalanceSlack = null,
    Flt64? FleetBalanceBaseSlack = null,
    Flt64? ExecutorLeisureCoeff = null,
    Flt64? TaskCancelCoeff = null
) {
    /// <summary>非基地机场车队平衡松弛惩罚 / Fleet balance slack penalty for non-base airports.</summary>
    public Flt64 ResolvedFleetBalanceSlack => FleetBalanceSlack ?? new Flt64(60.0);

    /// <summary>基地机场车队平衡松弛惩罚 / Fleet balance slack penalty for base airports.</summary>
    public Flt64 ResolvedFleetBalanceBaseSlack => FleetBalanceBaseSlack ?? new Flt64(600.0);

    /// <summary>执行器空闲最小化系数 / Executor leisure minimization coefficient.</summary>
    public Flt64 ResolvedExecutorLeisureCoeff => ExecutorLeisureCoeff ?? Flt64.Zero;

    /// <summary>任务取消最小化系数 / Task cancel minimization coefficient.</summary>
    public Flt64 ResolvedTaskCancelCoeff => TaskCancelCoeff ?? new Flt64(9999.0);
}
