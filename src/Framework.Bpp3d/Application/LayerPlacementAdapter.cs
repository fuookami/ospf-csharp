#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Application
{
    /// <summary>层放置适配器 / Layer placement adapter.</summary>
    public static class LayerPlacementAdapter
    {
        public static Result<QuantityPlacement3<BinLayer, FltX>, ErrorCode, Error<ErrorCode>> ToLayerPlacement(
            this BinLayer layer, Quantity<FltX>? z = null)
        {
            return new Ok<QuantityPlacement3<BinLayer, FltX>, ErrorCode, Error<ErrorCode>>(
                layer.ToKnownCoordinateLayerPlacement(z));
        }

        public static QuantityPlacement3<BinLayer, FltX> ToKnownCoordinateLayerPlacement(
            this BinLayer layer, Quantity<FltX>? z = null)
        {
            var position = z is null
                ? BinLayerHelpers.Point3FltX()
                : BinLayerHelpers.Point3FltX(z: z);
            return BinLayerHelpers.BinLayerPlacementOf(layer.Copy(), position);
        }
    }
}
