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
    /// 箱容量约束 / Bin capacity constraint.
    /// </summary>
    public sealed class BinCapacityConstraint<V> : IBpp3dConstraint<V>
        where V : struct, IFloatingNumber<V>
    {
        public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RefreshAsync(
            AbstractBpp3dShadowPriceMap shadowPriceMap,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Results.Ok<Success>(Results.SuccessInstance));
        }

        public Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>, ErrorCode, Error<ErrorCode>> RightHandSide()
            => Results.Ok<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>>(new Dictionary<DemandShadowPriceKey, Quantity<V>>());
    }
}
