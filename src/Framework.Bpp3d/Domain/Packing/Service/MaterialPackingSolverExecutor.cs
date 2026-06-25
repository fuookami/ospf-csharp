#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
/// <summary>
/// 求解执行器接口 / Solver executor interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface ISolverExecutor<V> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 执行求解 / Execute solve.
    /// </summary>
    Task<Result<IReadOnlyList<MaterialPackingPlan<V>>, ErrorCode, Error<ErrorCode>>> ExecuteAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 物料装箱求解执行器 / Material packing solver executor.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class MaterialPackingSolverExecutor<V> : ISolverExecutor<V>
    where V : struct, IFloatingNumber<V> {
    public Task<Result<IReadOnlyList<MaterialPackingPlan<V>>, ErrorCode, Error<ErrorCode>>> ExecuteAsync(
        CancellationToken cancellationToken = default) {
        return Task.FromResult(
            Results.Ok<IReadOnlyList<MaterialPackingPlan<V>>>(new List<MaterialPackingPlan<V>>()));
    }
}

/// <summary>
/// 穷举物料装箱求解执行器 / Exhaustive material packing solver executor.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class ExhaustiveMaterialPackingSolverExecutor<V> : ISolverExecutor<V>
    where V : struct, IFloatingNumber<V> {
    public Task<Result<IReadOnlyList<MaterialPackingPlan<V>>, ErrorCode, Error<ErrorCode>>> ExecuteAsync(
        CancellationToken cancellationToken = default) {
        return Task.FromResult(
            Results.Ok<IReadOnlyList<MaterialPackingPlan<V>>>(new List<MaterialPackingPlan<V>>()));
    }
}
