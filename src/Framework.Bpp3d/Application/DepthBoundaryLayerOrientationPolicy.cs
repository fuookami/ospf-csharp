#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Application
{
    /// <summary>深度边界层方向策略 / Depth boundary layer orientation policy.</summary>
    public sealed record DepthBoundaryLayerOrientationPolicy(
        IReadOnlySet<Axis3>? FirstLayerAllowedCylinderAxes = null,
        IReadOnlySet<Axis3>? LastLayerAllowedCylinderAxes = null,
        IReadOnlySet<Orientation>? FirstLayerAllowedCuboidOrientations = null,
        IReadOnlySet<Orientation>? LastLayerAllowedCuboidOrientations = null)
    {
        public bool Enabled =>
            FirstLayerAllowedCylinderAxes is not null || LastLayerAllowedCylinderAxes is not null ||
            FirstLayerAllowedCuboidOrientations is not null || LastLayerAllowedCuboidOrientations is not null;

        internal Result<Success, ErrorCode, Error<ErrorCode>> EnsureSatisfied(IReadOnlyList<Bin<BinLayer, FltX>> bins)
        {
            if (!Enabled) return Results.Ok<Success>(Results.SuccessInstance);
            for (var i = 0; i < bins.Count; i++)
            {
                var ordered = bins[i].Units.OrderBy(u => u.Z.Value.ToFlt64().ToDouble()).ToList();
                if (ordered.Count == 0) continue;
                var first = CheckBoundary(i, true, ordered.First());
                if (first.IsFailed) return first;
                var last = CheckBoundary(i, false, ordered.Last());
                if (last.IsFailed) return last;
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> CheckBoundary(int binIndex, bool isFirst, QuantityPlacement3<BinLayer, FltX> placement)
        {
            var allowedAxes = isFirst ? FirstLayerAllowedCylinderAxes : LastLayerAllowedCylinderAxes;
            var allowedOrientations = isFirst ? FirstLayerAllowedCuboidOrientations : LastLayerAllowedCuboidOrientations;
            if (allowedAxes is null && allowedOrientations is null) return Results.Ok<Success>(Results.SuccessInstance);
            var side = isFirst ? "first" : "last";

            foreach (var unit in placement.Unit.Units)
            {
                if (allowedOrientations is not null && !allowedOrientations.Contains(unit.Orientation))
                {
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument,
                            $"Depth boundary layer orientation policy violation: bin={binIndex}, boundary={side}, orientation={unit.Orientation}, allowed=[{string.Join(", ", allowedOrientations)}]"));
                }
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }
}
