#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
/// <summary>
/// CSP1D 聚合根接口 / CSP1D aggregation root interface.
///
/// 定义聚合根的基本契约：注册变量和中间值到元模型。
/// 各 domain context 的 Aggregation 实现此接口，管理自己的变量集合。
///
/// Define the basic contract for aggregation roots: register variables and intermediate
/// values to the meta model. Each domain context's Aggregation implements this interface,
/// managing its own variable set.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dAggregation<V>
    where V : struct {
    /// <summary>
    /// 注册到元模型 / Register to meta model.
    ///
    /// 将聚合根管理的变量和中间值注册到元模型。
    /// Register the variables and intermediate values managed by this aggregation root to the meta model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    Try Register(LinearMetaModel<Flt64> model);

    /// <summary>
    /// 聚合根管理的切割方案列表 / Cutting plans managed by this aggregation.
    /// </summary>
    IReadOnlyList<CuttingPlan<V>> CuttingPlans { get; }
}
