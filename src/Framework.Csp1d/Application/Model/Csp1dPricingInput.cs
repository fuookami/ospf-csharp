#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// CSP1D 定价输入 / CSP1D pricing input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record Csp1dPricingInput<V>(
    Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.GenerationInput<V> GenerationInput,
    Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model.ShadowPriceMap<V> ShadowPrices,
    UInt64 MaxGeneratedPlans
) where V : struct;
