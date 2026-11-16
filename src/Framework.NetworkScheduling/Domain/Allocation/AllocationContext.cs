// 运输分配问题、方案与上下文 / Transport-allocation problem, solution and context.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Allocation;

/// <summary>
/// 不可变运输分配业务输入 / The immutable transport-allocation business input.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationProblem<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造运输分配问题 / Construct a transport-allocation problem.
    /// </summary>
    /// <param name="locations">地点 / Locations.</param>
    /// <param name="services">可选或固定运输服务 / Optional or fixed transport services.</param>
    /// <param name="requests">运输请求 / Shipment requests.</param>
    /// <param name="zero">数值零 / The numeric zero.</param>
    /// <param name="quotas">共享配额 / Shared quotas.</param>
    /// <param name="hubs">可选枢纽 / Optional hubs.</param>
    /// <param name="costUnit">运行和建设费用的统一单位 / The common unit for operating and construction costs.</param>
    /// <param name="serviceUsage">异量纲服务容量的单位请求消耗覆盖 / Per-unit request usage overrides for dimensionally different
    /// service capacities.</param>
    /// <param name="budget">可选建设预算 / The optional construction budget.</param>
    public AllocationProblem(
        IReadOnlyList<AllocationLocation<V>> locations,
        IReadOnlyList<AllocationService<V>> services,
        IReadOnlyList<AllocationRequest<V>> requests,
        V zero,
        IReadOnlyList<AllocationQuota<V>>? quotas = null,
        IReadOnlyList<AllocationHub<V>>? hubs = null,
        PhysicalUnit? costUnit = null,
        IReadOnlyDictionary<AllocationFlowKey, Quantity<V>>? serviceUsage = null,
        Quantity<V>? budget = null) {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(requests);
        Locations = locations.ToList();
        Services = services.ToList();
        Requests = requests.ToList();
        Zero = zero;
        Quotas = quotas?.ToList() ?? new List<AllocationQuota<V>>();
        Hubs = hubs?.ToList() ?? new List<AllocationHub<V>>();
        CostUnit = costUnit ?? NoneUnit.Instance;
        ServiceUsage = serviceUsage is null
            ? new Dictionary<AllocationFlowKey, Quantity<V>>()
            : serviceUsage.ToDictionary(pair => pair.Key, pair => pair.Value);
        Budget = budget;
    }

    /// <summary>地点 / Locations.</summary>
    public IReadOnlyList<AllocationLocation<V>> Locations { get; }

    /// <summary>可选或固定运输服务 / Optional or fixed transport services.</summary>
    public IReadOnlyList<AllocationService<V>> Services { get; }

    /// <summary>运输请求 / Shipment requests.</summary>
    public IReadOnlyList<AllocationRequest<V>> Requests { get; }

    /// <summary>数值零 / The numeric zero.</summary>
    public V Zero { get; }

    /// <summary>共享配额 / Shared quotas.</summary>
    public IReadOnlyList<AllocationQuota<V>> Quotas { get; }

    /// <summary>可选枢纽 / Optional hubs.</summary>
    public IReadOnlyList<AllocationHub<V>> Hubs { get; }

    /// <summary>运行和建设费用的统一单位 / The common unit for operating and construction costs.</summary>
    public PhysicalUnit CostUnit { get; }

    /// <summary>异量纲服务容量的单位请求消耗覆盖 / Per-unit request usage overrides.</summary>
    public IReadOnlyDictionary<AllocationFlowKey, Quantity<V>> ServiceUsage { get; }

    /// <summary>可选建设预算 / The optional construction budget.</summary>
    public Quantity<V>? Budget { get; }
}

/// <summary>
/// 运输分配方案及服务/枢纽建设决策 / Transport-allocation flows and service/hub construction decisions.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Shipments">各请求在各服务上的运输量 / Quantity shipped for each request-service pair.</param>
/// <param name="Flow">底层流费用、资源和供需结果 / The underlying flow cost, resource use and balance results.</param>
/// <param name="OpenHubs">已开设枢纽 / Open hubs.</param>
/// <param name="ServiceLevels">已选择的服务建设等级 / Selected service construction levels.</param>
/// <param name="ConstructionCost">建设费用 / Construction cost.</param>
/// <param name="TotalCost">运行与建设总费用 / Total operating and construction cost.</param>
public sealed record AllocationSolution<V>(
    IReadOnlyDictionary<AllocationFlowKey, Quantity<V>> Shipments,
    FlowSolution<V> Flow,
    IReadOnlySet<NetworkNodeId> OpenHubs,
    IReadOnlyDictionary<NetworkArcId, string> ServiceLevels,
    Quantity<V> ConstructionCost,
    Quantity<V> TotalCost) where V : struct, IRealNumber<V>;

/// <summary>
/// 运输分配到 Flow 与 Network Design 的业务构造和注册边界 / The business construction and registration boundary over Flow and
/// Network Design.
/// </summary>
/// <remarks>
/// **这一层不重复领域约束**。容量、配额、供需平衡都由 <see cref="NetworkDesignContext{V}"/> 与流问题管理；这里只做业务输入到领域
/// 输入的转换，以及结果的单位归一。重复一遍会让两套口径迟早对不上。
/// **This layer does not duplicate domain constraints.** Capacity, quotas and supply-demand balance all belong to
/// <see cref="NetworkDesignContext{V}"/> and the flow problem; this layer only converts business input to domain input and normalises
/// the result's units. Duplicating them would eventually leave two inconsistent sets of rules.
/// **建设费用必须能换算到运行费用单位**。两者量纲不同时不能相加；静默按原值相加会让总费用变成一个没有量纲意义的数字。
/// **The construction cost must convert into the operating-cost unit.** Costs of different dimensions cannot be added; adding the
/// raw values would make the total a number with no meaningful dimension.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class AllocationContext<V> where V : struct, IRealNumber<V> {
    private AllocationContext(AllocationProblem<V> problem, NetworkDesignContext<V> design, string name) {
        Problem = problem;
        Design = design;
        Name = name;
    }

    /// <summary>业务输入快照 / The business-input snapshot.</summary>
    public AllocationProblem<V> Problem { get; }

    /// <summary>下层结构决策上下文 / The underlying structural-decision context.</summary>
    public NetworkDesignContext<V> Design { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>
    /// 校验业务输入并构建独立的底层模型上下文 / Validate business input and build an independent underlying context.
    /// </summary>
    /// <param name="problem">业务运输分配问题 / The business transport-allocation problem.</param>
    /// <param name="adapter">数值转换边界 / The numeric conversion boundary.</param>
    /// <param name="name">模型命名空间 / The model namespace.</param>
    /// <returns>上下文或构造错误 / The context, or a construction error.</returns>
    public static Result<AllocationContext<V>, ErrorCode, Error<ErrorCode>> Create(
        AllocationProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "network_allocation") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        if (string.IsNullOrWhiteSpace(name)) {
            return Failure<AllocationContext<V>>(
                "分配模型名称不能为空 / Allocation model name cannot be blank");
        }
        Result<NetworkDesignProblem<V>, ErrorCode, Error<ErrorCode>> design =
            ToNetworkDesignProblem(problem, adapter);
        if (design is Failed<NetworkDesignProblem<V>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<AllocationContext<V>>(failure.Error);
        }
        return Results.Ok(new AllocationContext<V>(
            problem,
            new NetworkDesignContext<V>(
                ((Ok<NetworkDesignProblem<V>, ErrorCode, Error<ErrorCode>>)design).Value,
                adapter,
                name),
            name));
    }

    /// <summary>
    /// 注册运输分配、建设决策及可选费用目标 / Register allocation, design decisions and the optional cost objective.
    /// </summary>
    /// <param name="target">元模型 / The meta model.</param>
    /// <param name="minimizeCost">是否最小化运行加建设费用 / Whether to minimise operating plus construction cost.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> target, bool minimizeCost = true) {
        ArgumentNullException.ThrowIfNull(target);
        return Design.Register(target, minimizeCost);
    }

    /// <summary>
    /// 提取运输分配和建设决策 / Extract the allocation and construction decisions.
    /// </summary>
    /// <param name="target">已注册并求解的元模型 / The registered and solved meta model.</param>
    /// <returns>业务方案或提取错误 / The business solution, or an extraction error.</returns>
    public Result<AllocationSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(target);
        Result<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>> design = Design.ExtractSolution(target);
        if (design is Failed<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<AllocationSolution<V>>(failure.Error);
        }
        NetworkDesignSolution<V> inner = ((Ok<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>>)design).Value;
        var shipments = new Dictionary<AllocationFlowKey, Quantity<V>>();
        foreach ((FlowKey key, Quantity<V> amount) in inner.Flow.Flows) {
            shipments[new AllocationFlowKey(key.Commodity, key.Arc)] = amount;
        }
        if (!inner.ConstructionCost.Unit.SameDimension(inner.Flow.Cost.Unit)) {
            return Failure<AllocationSolution<V>>(
                "建设费用无法转换到运行费用单位 / Construction cost cannot be converted to the operating-cost unit");
        }
        return Results.Ok(new AllocationSolution<V>(
            shipments,
            inner.Flow,
            inner.OpenFacilities,
            inner.ServiceLevels,
            inner.ConstructionCost,
            new Quantity<V>(
                inner.Flow.Cost.Value.Plus(inner.ConstructionCost.Value), inner.Flow.Cost.Unit)));
    }

    /// <summary>
    /// 业务输入到领域输入的转换 / Convert business input to domain input.
    /// </summary>
    /// <param name="problem">业务问题 / The business problem.</param>
    /// <param name="adapter">数值边界 / The numeric boundary.</param>
    /// <returns>结构决策问题或校验错误 / The design problem, or a validation error.</returns>
    private static Result<NetworkDesignProblem<V>, ErrorCode, Error<ErrorCode>> ToNetworkDesignProblem(
        AllocationProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter) {
        var locationIds = problem.Locations.Select(location => location.Id).ToList();
        var serviceIds = problem.Services.Select(service => service.Id).ToList();
        var requestIds = problem.Requests.Select(request => request.Id).ToList();
        var quotaIds = problem.Quotas.Select(quota => quota.Id).ToList();
        var hubIds = problem.Hubs.Select(hub => hub.Location).ToList();
        var controlledQuotaIds = problem.Hubs.SelectMany(hub => hub.QuotaIds).ToList();
        if (locationIds.Any(id => string.IsNullOrWhiteSpace(id.Value))
            || locationIds.Distinct().Count() != locationIds.Count
            || serviceIds.Any(id => string.IsNullOrWhiteSpace(id.Value))
            || serviceIds.Distinct().Count() != serviceIds.Count
            || requestIds.Any(string.IsNullOrWhiteSpace)
            || requestIds.Distinct(StringComparer.Ordinal).Count() != requestIds.Count
            || quotaIds.Any(id => string.IsNullOrWhiteSpace(id) || id.StartsWith("arc:", StringComparison.Ordinal))
            || quotaIds.Distinct(StringComparer.Ordinal).Count() != quotaIds.Count
            || hubIds.Distinct().Count() != hubIds.Count
            || controlledQuotaIds.Distinct(StringComparer.Ordinal).Count() != controlledQuotaIds.Count) {
            return Failure<NetworkDesignProblem<V>>(
                "分配输入包含空标识或重复资源 / Allocation input contains a blank identity or duplicate resource");
        }
        Result<Flt64, ErrorCode, Error<ErrorCode>> zero = adapter.ToSolverValue(problem.Zero);
        if (zero is Failed<Flt64, ErrorCode, Error<ErrorCode>> zeroFailure) {
            return Results.Failed<NetworkDesignProblem<V>>(zeroFailure.Error);
        }
        if (!((Ok<Flt64, ErrorCode, Error<ErrorCode>>)zero).Value.Equals(Flt64.Zero)) {
            return Failure<NetworkDesignProblem<V>>(
                "分配问题的零值必须可转换为 solver 零 / Allocation zero must convert to solver zero");
        }
        var locations = problem.Locations.ToDictionary(location => location.Id);
        var services = problem.Services.ToDictionary(service => service.Id);
        var requests = problem.Requests.ToDictionary(request => request.Id, StringComparer.Ordinal);
        var quotas = problem.Quotas.ToDictionary(quota => quota.Id, StringComparer.Ordinal);
        if (problem.Services.Any(service =>
                !locations.ContainsKey(service.From) || !locations.ContainsKey(service.To))) {
            return Failure<NetworkDesignProblem<V>>("服务引用了不存在的地点 / A service references an unknown location");
        }
        foreach (AllocationService<V> service in problem.Services) {
            int? fromLayer = locations[service.From].Layer;
            int? toLayer = locations[service.To].Layer;
            if (fromLayer is { } source && toLayer is { } target && source > target) {
                return Failure<NetworkDesignProblem<V>>(
                    $"服务方向违反地点层级顺序：{service.Id.Value} / Service direction violates location layer order: "
                    + service.Id.Value);
            }
        }
        if (problem.Requests.Any(request =>
                !locations.ContainsKey(request.Origin) || !locations.ContainsKey(request.Destination)
                || request.Origin.Equals(request.Destination)
                || (request.AllowedServices is { } allowed && allowed.Any(id => !services.ContainsKey(id)))
                || request.Costs.Keys.Any(id => !services.ContainsKey(id)))) {
            return Failure<NetworkDesignProblem<V>>(
                "请求端点、兼容服务或费用覆盖引用无效 / Invalid request endpoint, compatible service, or cost override");
        }
        foreach (AllocationRequest<V> request in problem.Requests) {
            if (!request.Quantity.Unit.SameDimension(request.Unit)) {
                return Failure<NetworkDesignProblem<V>>(
                    $"请求数量单位不兼容：{request.Id} / Incompatible request quantity unit: {request.Id}");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> amount = adapter.ToSolverValue(request.Quantity.Value);
            if (amount is Failed<Flt64, ErrorCode, Error<ErrorCode>> amountFailure) {
                return Results.Failed<NetworkDesignProblem<V>>(amountFailure.Error);
            }
            double value = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)amount).Value.ToDouble();
            if (!double.IsFinite(value) || !(value > 0.0)) {
                return Failure<NetworkDesignProblem<V>>(
                    $"请求数量必须为有限正数：{request.Id} / Request amount must be finite and positive: {request.Id}");
            }
            if (request.IntegerStep is { } step) {
                if (!step.Unit.SameDimension(request.Unit)) {
                    return Failure<NetworkDesignProblem<V>>(
                        $"整数流步长单位不兼容：{request.Id} / Incompatible integer-flow step unit: {request.Id}");
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> stepValue = adapter.ToSolverValue(step.Value);
                if (stepValue is Failed<Flt64, ErrorCode, Error<ErrorCode>> stepFailure) {
                    return Results.Failed<NetworkDesignProblem<V>>(stepFailure.Error);
                }
                double stepAmount = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)stepValue).Value.ToDouble();
                if (!double.IsFinite(stepAmount) || !(stepAmount > 0.0)) {
                    return Failure<NetworkDesignProblem<V>>(
                        $"整数流步长必须为有限正数：{request.Id} / Integer-flow step must be finite and positive: "
                        + request.Id);
                }
            }
        }
        if (problem.ServiceUsage.Keys.Any(key => UsageIsInvalid(key, requests, services))) {
            return Failure<NetworkDesignProblem<V>>(
                "服务容量消耗引用了不存在的请求或服务 / Service-capacity usage references an unknown request or service");
        }
        if (problem.Quotas.Any(quota => quota.Usage.Keys.Any(key => UsageIsInvalid(key, requests, services)))) {
            return Failure<NetworkDesignProblem<V>>(
                "共享配额引用了不存在的请求或服务 / A shared quota references an unknown request or service");
        }
        if (problem.Hubs.Any(hub => !locations.ContainsKey(hub.Location)
                || hub.QuotaIds.Any(id => !quotas.ContainsKey(id)))) {
            return Failure<NetworkDesignProblem<V>>(
                "枢纽引用了不存在的地点或共享配额 / A hub references an unknown location or shared quota");
        }

        var nodes = problem.Locations
            .Select(location => new NetworkNode<V>(location.Id, location.Attributes))
            .ToList();
        var arcs = new List<NetworkArc<V>>();
        var flowArcs = new List<FlowArc<V>>();
        foreach (AllocationService<V> service in problem.Services) {
            Result<NetworkArc<V>, ErrorCode, Error<ErrorCode>> created = NetworkArc<V>.Invoke(
                service.From, service.To, service.Cost, service.Attributes, service.Id);
            if (created is Failed<NetworkArc<V>, ErrorCode, Error<ErrorCode>> arcFailure) {
                return Results.Failed<NetworkDesignProblem<V>>(arcFailure.Error);
            }
            NetworkArc<V> arc = ((Ok<NetworkArc<V>, ErrorCode, Error<ErrorCode>>)created).Value;
            arcs.Add(arc);
            Result<FlowArc<V>, ErrorCode, Error<ErrorCode>> flowArc =
                FlowArc<V>.Invoke(arc, service.Capacity);
            if (flowArc is Failed<FlowArc<V>, ErrorCode, Error<ErrorCode>> flowArcFailure) {
                return Results.Failed<NetworkDesignProblem<V>>(flowArcFailure.Error);
            }
            flowArcs.Add(((Ok<FlowArc<V>, ErrorCode, Error<ErrorCode>>)flowArc).Value);
        }
        Result<NetworkGraph<NetworkNode<V>, NetworkArc<V>>, ErrorCode, Error<ErrorCode>> graph =
            NetworkGraph<NetworkNode<V>, NetworkArc<V>>.Invoke<V>(nodes, arcs);
        if (graph is Failed<NetworkGraph<NetworkNode<V>, NetworkArc<V>>, ErrorCode, Error<ErrorCode>> graphFailure) {
            return Results.Failed<NetworkDesignProblem<V>>(graphFailure.Error);
        }
        NetworkGraph<NetworkNode<V>, NetworkArc<V>> builtGraph =
            ((Ok<NetworkGraph<NetworkNode<V>, NetworkArc<V>>, ErrorCode, Error<ErrorCode>>)graph).Value;
        var commodities = new List<CommodityFlow<V>>();
        Result<V, ErrorCode, Error<ErrorCode>> negativeOne = adapter.FromSolverValue(new Flt64(-1.0));
        if (negativeOne is Failed<V, ErrorCode, Error<ErrorCode>> negativeFailure) {
            return Results.Failed<NetworkDesignProblem<V>>(negativeFailure.Error);
        }
        V negated = ((Ok<V, ErrorCode, Error<ErrorCode>>)negativeOne).Value;
        foreach (AllocationRequest<V> request in problem.Requests) {
            Result<NodeBalance<V>, ErrorCode, Error<ErrorCode>> supplyBalance =
                NodeBalance<V>.Invoke(request.Quantity);
            if (supplyBalance is Failed<NodeBalance<V>, ErrorCode, Error<ErrorCode>> supplyFailure) {
                return Results.Failed<NetworkDesignProblem<V>>(supplyFailure.Error);
            }
            Result<NodeBalance<V>, ErrorCode, Error<ErrorCode>> demandBalance =
                NodeBalance<V>.Invoke(new Quantity<V>(request.Quantity.Value.Times(negated), request.Unit));
            if (demandBalance is Failed<NodeBalance<V>, ErrorCode, Error<ErrorCode>> demandFailure) {
                return Results.Failed<NetworkDesignProblem<V>>(demandFailure.Error);
            }
            Result<SupplyDemand<V>, ErrorCode, Error<ErrorCode>> supply = SupplyDemand<V>.Invoke(
                request.Origin, ((Ok<NodeBalance<V>, ErrorCode, Error<ErrorCode>>)supplyBalance).Value);
            if (supply is Failed<SupplyDemand<V>, ErrorCode, Error<ErrorCode>> supplyFailure2) {
                return Results.Failed<NetworkDesignProblem<V>>(supplyFailure2.Error);
            }
            Result<SupplyDemand<V>, ErrorCode, Error<ErrorCode>> demand = SupplyDemand<V>.Invoke(
                request.Destination, ((Ok<NodeBalance<V>, ErrorCode, Error<ErrorCode>>)demandBalance).Value);
            if (demand is Failed<SupplyDemand<V>, ErrorCode, Error<ErrorCode>> demandFailure2) {
                return Results.Failed<NetworkDesignProblem<V>>(demandFailure2.Error);
            }
            Result<FlowCommodity<V>, ErrorCode, Error<ErrorCode>> commodity = FlowCommodity<V>.Invoke(
                request.Id,
                new[] {
                    ((Ok<SupplyDemand<V>, ErrorCode, Error<ErrorCode>>)supply).Value,
                    ((Ok<SupplyDemand<V>, ErrorCode, Error<ErrorCode>>)demand).Value,
                });
            if (commodity is Failed<FlowCommodity<V>, ErrorCode, Error<ErrorCode>> commodityFailure) {
                return Results.Failed<NetworkDesignProblem<V>>(commodityFailure.Error);
            }
            commodities.Add(new CommodityFlow<V>(
                ((Ok<FlowCommodity<V>, ErrorCode, Error<ErrorCode>>)commodity).Value,
                request.Unit,
                request.IntegerStep,
                request.Costs,
                request.AllowedServices));
        }
        var resources = problem.Quotas
            .Select(quota => new FlowCapacity<V>(
                quota.Id,
                quota.Bounds,
                quota.Usage.ToDictionary(
                    pair => new FlowKey(pair.Key.Request, pair.Key.Service),
                    pair => pair.Value)))
            .ToList();
        var capacityUsage = problem.ServiceUsage.ToDictionary(
            pair => new FlowKey(pair.Key.Request, pair.Key.Service),
            pair => pair.Value);
        var flowProblem = new FlowProblem<V>(
            builtGraph, flowArcs, commodities, problem.Zero, problem.CostUnit, capacityUsage, resources);
        var designServices = problem.Services
            .Where(service => service.CapacityLevels.Count > 0)
            .Select(service => new NetworkService<V>(
                service.Id,
                service.CapacityLevels
                    .Select(level => new CapacityLevel<V>(level.Id, level.Capacity, level.Cost))
                    .ToList(),
                service.Required))
            .ToList();
        var facilities = problem.Hubs
            .Select(hub => new NetworkFacility<V>(
                hub.Location, hub.OpeningCost, hub.Required, hub.QuotaIds))
            .ToList();
        return Results.Ok(new NetworkDesignProblem<V>(
            flowProblem, designServices, facilities, problem.Budget));
    }

    /// <summary>
    /// 判断某个消耗键是否引用了无效的请求或服务 / Whether a usage key references an invalid request or service.
    /// </summary>
    /// <param name="key">消耗键 / The usage key.</param>
    /// <param name="requests">请求索引 / The request index.</param>
    /// <param name="services">服务索引 / The service index.</param>
    /// <returns>是否无效 / Whether it is invalid.</returns>
    private static bool UsageIsInvalid(
        AllocationFlowKey key,
        IReadOnlyDictionary<string, AllocationRequest<V>> requests,
        IReadOnlyDictionary<NetworkArcId, AllocationService<V>> services) {
        if (!requests.TryGetValue(key.Request, out AllocationRequest<V>? request)
            || !services.ContainsKey(key.Service)) {
            return true;
        }
        return request.AllowedServices is { } allowed && !allowed.Contains(key.Service);
    }

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
