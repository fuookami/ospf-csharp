#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model
{
    /// <summary>
    /// 物料装箱计划 / Material packing plan.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record MaterialPackingPlan<V>(
        string BinTypeCode,
        IReadOnlyList<MaterialPackingNumbers<V>> Numbers)
        where V : struct, IFloatingNumber<V>;
}
