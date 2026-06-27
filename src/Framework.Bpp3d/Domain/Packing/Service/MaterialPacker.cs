#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;

/// <summary>
/// 物料装箱器 / Material packer.
/// 委托求解执行器进行物料装箱规划 / Delegates to solver executor for material packing planning.
/// </summary>
public sealed class MaterialPacker {
    private readonly ISolverExecutor<FltX> _executor;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="executor">装箱求解执行器 / Packing solver executor</param>
    public MaterialPacker(ISolverExecutor<FltX>? executor = null) {
        _executor = executor is not null ? executor : new ExhaustiveMaterialPackingSolverExecutor<FltX>();
    }

    /// <summary>
    /// 执行装箱规划 / Execute packing plan.
    /// </summary>
    /// <returns>装箱计划列表 / Packing plan list</returns>
    public async Task<Result<IReadOnlyList<MaterialPackingPlan<FltX>>, ErrorCode, Error<ErrorCode>>> PlanAsync() {
        return await _executor.ExecuteAsync();
    }
}
