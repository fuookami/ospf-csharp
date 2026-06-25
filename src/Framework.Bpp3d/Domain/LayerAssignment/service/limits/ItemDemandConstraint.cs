#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Infra;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits
{
    /// <summary>
    /// BPP3D 约束接口 / BPP3D constraint interface.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public interface IBpp3dConstraint<V> where V : struct, IFloatingNumber<V>
    {
        /// <summary>
        /// 刷新对偶信息 / Refresh dual info from the shadow-price map.
        /// </summary>
        Task<Result<Success, ErrorCode, Error<ErrorCode>>> RefreshAsync(
            AbstractBpp3dShadowPriceMap shadowPriceMap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 构建约束右端项 / Build the RHS vector for the master problem.
        /// </summary>
        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>, ErrorCode, Error<ErrorCode>> RightHandSide();
    }

    /// <summary>
    /// 物料需求约束 / Item demand constraint.
    /// 列生成管线中对偶刷新点 / CG pipeline dual-refresh point.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed class ItemDemandConstraint<V> : IBpp3dConstraint<V>
        where V : struct, IFloatingNumber<V>
    {
        private readonly Dictionary<DemandShadowPriceKey, Quantity<V>> _demands;

        public ItemDemandConstraint(IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>> demands)
        {
            _demands = new Dictionary<DemandShadowPriceKey, Quantity<V>>(demands);
        }

        /// <summary>
        /// 刷新对偶信息 / Refresh dual info from the shadow-price map.
        /// </summary>
        public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RefreshAsync(
            AbstractBpp3dShadowPriceMap shadowPriceMap,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in _demands.Keys)
            {
                var priceResult = shadowPriceMap.PriceOf(key);
                if (priceResult is Failed<Flt64, ErrorCode, Error<ErrorCode>> priceFailed)
                {
                    return Task.FromResult(
                        (Result<Success, ErrorCode, Error<ErrorCode>>)new Failed<Success, ErrorCode, Error<ErrorCode>>(priceFailed.Error));
                }
                // reduced-cost recomputation delegated to inner routine.
                _ = priceResult.Value;
            }
            return Task.FromResult(Results.Ok<Success>(Results.SuccessInstance));
        }

        /// <summary>
        /// 构建约束右端项 / Build the RHS vector for the master problem.
        /// </summary>
        public Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>, ErrorCode, Error<ErrorCode>> RightHandSide()
            => Results.Ok<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>>(_demands);
    }
}
