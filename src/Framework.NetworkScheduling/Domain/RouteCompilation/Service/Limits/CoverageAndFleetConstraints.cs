// 客户覆盖与车队数量约束管线 / Customer-coverage and fleet-size constraint pipelines.
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;

/// <summary>
/// 客户覆盖约束管线 / Customer coverage constraint pipeline.
/// </summary>
/// <remarks>
/// 对每个客户 i 添加约束 `customerCoverage[i] = 1`，其中 `customerCoverage[i] = a[i] + sum(x[r] for r visiting i)`，`a[i]` 是人工
/// 变量（Phase I）；`AddColumns` 之后自然包含路线变量。
/// For each customer i it adds `customerCoverage[i] = 1`, where `customerCoverage[i] = a[i] + sum(x[r] for r visiting i)` and
/// `a[i]` is the artificial variable (Phase I); route variables join naturally after `AddColumns`.
/// **等式而不是不等式**：覆盖"至多一次"与"恰好一次"在列生成里是两件事——松弛成 `≥ 1` 会让同一客户被多列重复覆盖却不被惩罚，
/// 主问题因此给出偏低的成本，pricer 也就找不到真正需要补的列。
/// **An equality rather than an inequality**: "at most once" and "exactly once" are different problems in column generation — relaxing
/// to `≥ 1` lets several columns cover the same customer without penalty, so the master reports a diluted cost and the pricer never
/// sees which column is genuinely missing.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class CustomerCoverageConstraint<V>
    : ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>
    where V : struct, IRealNumber<V> {
    private readonly IReadOnlyList<Customer<V>> _customers;
    private readonly RouteCompilation<V> _compilation;

    /// <summary>
    /// 构造客户覆盖约束管线 / Construct the customer-coverage constraint pipeline.
    /// </summary>
    /// <param name="customers">客户列表 / The customers.</param>
    /// <param name="compilation">路线编译模型 / The route compilation model.</param>
    /// <param name="name">管线及约束名称前缀 / Pipeline and constraint-name prefix.</param>
    public CustomerCoverageConstraint(
        IReadOnlyList<Customer<V>> customers,
        RouteCompilation<V> compilation,
        string name = "customer_coverage") {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(name);
        _customers = customers;
        _compilation = compilation;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册每个客户的覆盖等式 / Register every customer's coverage equality.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_compilation.CustomerCoverage is not { } coverage) {
            return NetworkSchedulingErrors.TryFailure(
                "客户覆盖管线失败：路线编译模型尚未注册 / Customer-coverage pipeline failed: route compilation model is not "
                + "registered");
        }
        for (int index = 0; index < _customers.Count; index++) {
            CustomerId customerId = _customers[index].Id;
            Try result = model.AddConstraint(
                coverage[index].Polynomial.Eq(Constant(Flt64.One)),
                this,
                name: $"{Name}_{customerId.Value}",
                args: new CustomerCoverageShadowPriceKey(customerId));
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 取该次定价所访问客户的覆盖对偶之和 / Sum the coverage duals of the customers visited by this pricing call.
    /// </summary>
    /// <returns>提取器 / The extractor.</returns>
    public ShadowPriceExtractor<VrpShadowPriceArguments, VrpShadowPriceMap>? Extractor() =>
        (map, args) => args.VisitedCustomerIds.Aggregate(
            Flt64.Zero,
            (acc, customerId) =>
                acc.Plus(
                    map.Map.TryGetValue(new CustomerCoverageShadowPriceKey(customerId), out ShadowPrice? price)
                        ? price.Price
                        : Flt64.Zero));

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    /// <param name="value">常数值 / The constant.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}

/// <summary>
/// 车队数量约束管线 / Fleet size constraint pipeline.
/// </summary>
/// <remarks>
/// 对每个车辆类型 k 添加约束 `fleetUsage[k] ≤ amount[k]`。
/// For each vehicle type k it adds `fleetUsage[k] ≤ amount[k]`.
/// **solver 返回的是 framework 约定的有符号 LP 对偶值**（当前最小化实现与测试快照为非正），在 reduced cost 公式
/// `reducedCost = cost − customerDual − fleetDual` 中直接使用，不再做归一化。自行"修正符号"会让车队对偶朝反方向抵消客户对偶，
/// pricer 于是系统性地偏好或回避某些车型。
/// **The solver returns framework-signed LP dual values** (non-positive for the current minimisation implementation and test
/// snapshots); these feed the reduced-cost formula `reducedCost = cost − customerDual − fleetDual` directly, with no normalisation.
/// "Fixing" the sign by hand would make the fleet dual cancel the customer dual the wrong way, so the pricer would systematically
/// favour or avoid certain vehicle types.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FleetSizeConstraint<V>
    : ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>
    where V : struct, IRealNumber<V> {
    private readonly IReadOnlyList<VehicleType<V>> _vehicleTypes;
    private readonly RouteCompilation<V> _compilation;

    /// <summary>
    /// 构造车队数量约束管线 / Construct the fleet-size constraint pipeline.
    /// </summary>
    /// <param name="vehicleTypes">车辆类型列表 / The vehicle types.</param>
    /// <param name="compilation">路线编译模型 / The route compilation model.</param>
    /// <param name="name">管线及约束名称前缀 / Pipeline and constraint-name prefix.</param>
    public FleetSizeConstraint(
        IReadOnlyList<VehicleType<V>> vehicleTypes,
        RouteCompilation<V> compilation,
        string name = "fleet_size") {
        ArgumentNullException.ThrowIfNull(vehicleTypes);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(name);
        _vehicleTypes = vehicleTypes;
        _compilation = compilation;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册每个车型的车队上界 / Register every vehicle type's fleet upper bound.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_compilation.FleetUsage is not { } usage) {
            return NetworkSchedulingErrors.TryFailure(
                "车队数量管线失败：路线编译模型尚未注册 / Fleet-size pipeline failed: route compilation model is not registered");
        }
        for (int index = 0; index < _vehicleTypes.Count; index++) {
            VehicleTypeId vehicleTypeId = _vehicleTypes[index].Id;
            var limit = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(_vehicleTypes[index].Amount));
            Try result = model.AddConstraint(
                usage[index].Polynomial.Le(limit),
                this,
                name: $"{Name}_{vehicleTypeId.Value}",
                args: new FleetSizeShadowPriceKey(vehicleTypeId));
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 取本次定价车型的车队对偶 / Read the fleet dual for the vehicle type being priced.
    /// </summary>
    /// <returns>提取器 / The extractor.</returns>
    public ShadowPriceExtractor<VrpShadowPriceArguments, VrpShadowPriceMap>? Extractor() =>
        (map, args) => map.Map.TryGetValue(new FleetSizeShadowPriceKey(args.VehicleTypeId), out ShadowPrice? price)
            ? price.Price
            : Flt64.Zero;
}
