// 路线编译模型 / Route compilation model.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;

/// <summary>
/// 路线编译模型 / Route compilation model.
/// </summary>
/// <remarks>
/// 管理路线变量、客户覆盖符号、车队使用量符号和路线成本符号。每次迭代新增路线时创建一组非负整数变量，并同步更新所有中间符号。
/// Manages route variables, customer coverage symbols, fleet usage symbols and route cost symbols. Each iteration of new routes
/// creates a set of non-negative integer variables and updates every intermediate symbol in step.
/// **中间符号必须与变量同批增量更新，而不是每轮重建**。重建会让已注册进模型的旧符号失去与约束/目标的关联，模型规模随迭代次数
/// 线性膨胀；增量更新则让每一列的成本、覆盖与用量在同一处被记账，三者永远同步。
/// **The intermediate symbols are updated incrementally in the same batch as the variables, never rebuilt per iteration.** Rebuilding
/// would detach previously registered symbols from their constraints and objective and grow the model linearly with iterations;
/// incremental updates book each column's cost, coverage and usage in one place so the three can never drift apart.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteCompilation<V> where V : struct, IRealNumber<V> {
    private readonly Dictionary<CustomerId, int> _customerIndex;
    private readonly Dictionary<VehicleTypeId, int> _vehicleTypeIndex;
    private readonly List<UIntVariable1> _x = new();
    private readonly List<IReadOnlyList<Route<V>>> _routesIteration = new();
    private readonly Dictionary<string, (int Group, int RouteIndex)> _routeVariableIndex
        = new(StringComparer.Ordinal);
    private bool _registered;

    /// <summary>
    /// 构造路线编译模型 / Construct the route compilation model.
    /// </summary>
    /// <param name="instance">VRPTW 实例 / The VRPTW instance.</param>
    /// <param name="valueAdapter">数值边界 / The numeric boundary.</param>
    public RouteCompilation(VrptwInstance<V> instance, INetworkSchedulingSolverValueAdapter<V> valueAdapter) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(valueAdapter);
        Instance = instance;
        ValueAdapter = valueAdapter;
        _customerIndex = instance.Customers
            .Select((customer, index) => (customer.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        _vehicleTypeIndex = instance.VehicleTypes
            .Select((type, index) => (type.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
    }

    /// <summary>VRPTW 实例 / The VRPTW instance.</summary>
    public VrptwInstance<V> Instance { get; }

    /// <summary>数值边界 / The numeric boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> ValueAdapter { get; }

    /// <summary>列池 / The column pool.</summary>
    public RouteColumnPool<V> ColumnPool { get; } = new();

    /// <summary>人工覆盖 / The artificial coverage.</summary>
    public ArtificialCoverage<V> ArtificialCoverage { get; private set; } = null!;

    /// <summary>每次迭代的路线变量 / Route variables per iteration.</summary>
    public IReadOnlyList<UIntVariable1> X => _x.ToList();

    /// <summary>每次迭代添加的路线 / Routes added per iteration.</summary>
    public IReadOnlyList<IReadOnlyList<Route<V>>> RoutesIteration => _routesIteration.ToList();

    /// <summary>所有路线 / All routes.</summary>
    public IReadOnlyList<Route<V>> Routes => _routesIteration.SelectMany(batch => batch).ToList();

    /// <summary>路线成本中间符号 / The route-cost intermediate symbol.</summary>
    public LinearExpressionSymbol? RouteCost { get; private set; }

    /// <summary>客户覆盖中间符号 / The customer-coverage intermediate symbols.</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1>? CustomerCoverage { get; private set; }

    /// <summary>车队使用量中间符号 / The fleet-usage intermediate symbols.</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1>? FleetUsage { get; private set; }

    /// <summary>路线到变量索引的双向映射 / Route-to-variable-index mapping.</summary>
    public IReadOnlyDictionary<string, (int Group, int RouteIndex)> RouteVariableIndex =>
        _routeVariableIndex.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// 获取路线对应的变量多项式，供额外主问题贡献增量装配使用 / Get a route's variable polynomial for incremental assembly by
    /// extra master contributions.
    /// </summary>
    /// <param name="route">路线 / Route.</param>
    /// <returns>列变量多项式或缺少变量时失败 / The column variable polynomial, or a failure when absent.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ColumnVariable(Route<V> route) {
        ArgumentNullException.ThrowIfNull(route);
        if (!_routeVariableIndex.TryGetValue(route.Signature, out (int Group, int RouteIndex) index)) {
            return NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                $"获取路线变量失败：路线 {route.Signature} 缺少变量索引 / Failed to get route variable: route "
                + $"{route.Signature} has no variable index");
        }
        if (index.Group >= _x.Count) {
            return NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                $"获取路线变量失败：路线 {route.Signature} 的变量组不存在 / Failed to get route variable: variable group for route "
                + $"{route.Signature} does not exist");
        }
        return Results.Ok(Variable(_x[index.Group][index.RouteIndex]));
    }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_registered) {
            return Results.Ok(Results.SuccessInstance);
        }

        ArtificialCoverage = new ArtificialCoverage<V>(Instance.Customers);
        Try coverage = ArtificialCoverage.Register(model);
        if (coverage is Failed<Success, ErrorCode, Error<ErrorCode>> coverageFailure) {
            return Results.Failed<Success>(coverageFailure.Error);
        }

        var routeCost = new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(), name: "route_cost");
        Try addedRouteCost = model.Add(routeCost);
        if (addedRouteCost is Failed<Success, ErrorCode, Error<ErrorCode>> routeCostFailure) {
            return Results.Failed<Success>(routeCostFailure.Error);
        }

        // 每个覆盖符号持有**自己的**可变多项式，初始含一个 `a[i]` 单项，而不是共享人工覆盖符号的实例。
        // 共享实例会造成别名：给覆盖符号加一条路线单项，人工覆盖符号也同步"多出一条"，之后从一处移除单项会把另一处一起改坏。
        // Each coverage symbol owns **its own** mutable polynomial, seeded with a single `a[i]` term, rather than sharing the
        // artificial-coverage symbol's instance. Sharing aliases the two: adding a route term to coverage also "adds" one to the
        // artificial symbol, and later removing a term from one corrupts the other.
        URealVariable1 artificialVariables = ArtificialCoverage.Variables!;
        var customerCoverage = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "customer_coverage",
            Shape1.Invoke(Instance.Customers.Count),
            (index, _) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, artificialVariables[index]) }, Flt64.Zero),
                name: $"customer_coverage_{Instance.Customers[index].Id.Value}"));
        Try addedCoverage = model.Add((IEnumerable<IIntermediateSymbol>)customerCoverage);
        if (addedCoverage is Failed<Success, ErrorCode, Error<ErrorCode>> addedCoverageFailure) {
            return Results.Failed<Success>(addedCoverageFailure.Error);
        }

        var fleetUsage = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "fleet_usage",
            Shape1.Invoke(Instance.VehicleTypes.Count),
            (index, _) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(),
                name: $"fleet_usage_{Instance.VehicleTypes[index].Id.Value}"));
        Try addedUsage = model.Add((IEnumerable<IIntermediateSymbol>)fleetUsage);
        if (addedUsage is Failed<Success, ErrorCode, Error<ErrorCode>> addedUsageFailure) {
            return Results.Failed<Success>(addedUsageFailure.Error);
        }

        RouteCost = routeCost;
        CustomerCoverage = customerCoverage;
        FleetUsage = fleetUsage;
        _registered = true;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 添加路线列 / Add route columns.
    /// </summary>
    /// <remarks>
    /// **成本、覆盖与车队用量三者在同一批里同步累加**。任何一处漏更新都会让模型内部不自洽：漏成本会让该列在目标里免费，漏覆盖会让
    /// 该列不参与客户满足，漏车队用量会让车辆数凭空少算——三者都不会让求解器报错。
    /// **Cost, coverage and fleet usage accumulate in the same batch.** Missing any one makes the model internally inconsistent:
    /// a missing cost makes the column free in the objective, a missing coverage removes it from customer satisfaction, and a missing
    /// fleet usage under-counts vehicles — none of the three makes the solver complain.
    /// </remarks>
    /// <param name="iteration">迭代次数 / Iteration count.</param>
    /// <param name="newRoutes">新路线列表 / The new routes.</param>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <returns>去重后实际添加的路线 / The routes actually added after deduplication.</returns>
    public Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<Route<V>> newRoutes,
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(newRoutes);
        ArgumentNullException.ThrowIfNull(model);
        if (RouteCost is null || CustomerCoverage is null || FleetUsage is null) {
            return NetworkSchedulingErrors.Failure<IReadOnlyList<Route<V>>>(
                "添加路线列失败：路线编译模型尚未注册 / Failed to add route columns: route compilation model is not registered");
        }

        IReadOnlyList<Route<V>> addedRoutes = ColumnPool.AddColumns(newRoutes);
        if (addedRoutes.Count == 0) {
            return Results.Ok(addedRoutes);
        }

        var variables = new UIntVariable1($"x_{iteration}", Shape1.Invoke(addedRoutes.Count));
        int groupIndex = _x.Count;
        for (int index = 0; index < addedRoutes.Count; index++) {
            Route<V> route = addedRoutes[index];
            variables[index].Name = $"x_{iteration}_{route.VehicleTypeId.Value}_{index}";
            _routeVariableIndex[route.Signature] = (groupIndex, index);
        }
        Try added = model.Add((IEnumerable<IVariableItem>)variables);
        if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addedFailure) {
            return Results.Failed<IReadOnlyList<Route<V>>>(addedFailure.Error);
        }
        _x.Add(variables);
        _routesIteration.Add(addedRoutes);

        // Phase I 的列成本：按求解器单位归一化后写入路线成本符号。
        // Phase-I column cost: normalised into solver units before entering the route-cost symbol.
        RouteCost.Flush();
        MutableLinearPolynomial<Flt64> routeCost = RouteCost.AsMutable();
        for (int index = 0; index < addedRoutes.Count; index++) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> solverCost =
                ValueAdapter.Normalize(addedRoutes[index].Cost, Instance.Units.CostUnit);
            if (solverCost is Failed<Flt64, ErrorCode, Error<ErrorCode>> costFailure) {
                return Results.Failed<IReadOnlyList<Route<V>>>(costFailure.Error);
            }
            routeCost.AddMonomial(new LinearMonomial<Flt64>(solverCost.Value, variables[index]));
        }

        for (int customerIndex = 0; customerIndex < Instance.Customers.Count; customerIndex++) {
            CustomerId customerId = Instance.Customers[customerIndex].Id;
            var covering = new List<int>();
            for (int routeIndex = 0; routeIndex < addedRoutes.Count; routeIndex++) {
                if (addedRoutes[routeIndex].Stops.Any(stop => stop.CustomerId is { } id && id.Equals(customerId))) {
                    covering.Add(routeIndex);
                }
            }
            if (covering.Count == 0) {
                continue;
            }
            LinearExpressionSymbol coverage = CustomerCoverage[customerIndex];
            coverage.Flush();
            MutableLinearPolynomial<Flt64> polynomial = coverage.AsMutable();
            foreach (int routeIndex in covering) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variables[routeIndex]));
            }
        }

        for (int typeIndex = 0; typeIndex < Instance.VehicleTypes.Count; typeIndex++) {
            VehicleTypeId typeId = Instance.VehicleTypes[typeIndex].Id;
            var typed = new List<int>();
            for (int routeIndex = 0; routeIndex < addedRoutes.Count; routeIndex++) {
                if (addedRoutes[routeIndex].VehicleTypeId.Equals(typeId)) {
                    typed.Add(routeIndex);
                }
            }
            if (typed.Count == 0) {
                continue;
            }
            LinearExpressionSymbol usage = FleetUsage[typeIndex];
            usage.Flush();
            MutableLinearPolynomial<Flt64> polynomial = usage.AsMutable();
            foreach (int routeIndex in typed) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variables[routeIndex]));
            }
        }

        return Results.Ok(addedRoutes);
    }

    /// <summary>
    /// 移除路线列 / Remove route columns.
    /// </summary>
    /// <remarks>
    /// **默认不自动删列**：仅在显式启用列上限时才由调用方调用，本实现提供但从不主动调用。
 /// The implementation notes "the first version defaults to no automatic column removal; only called when a column limit is explicitly
    /// enabled"; this implementation follows suit and never calls it on its own.
    /// </remarks>
    /// <param name="routesToRemove">待移除路线 / Routes to remove.</param>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try RemoveColumns(IReadOnlyList<Route<V>> routesToRemove, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(routesToRemove);
        ArgumentNullException.ThrowIfNull(model);
        var bySignature = new Dictionary<string, Route<V>>(StringComparer.Ordinal);
        foreach (Route<V> route in ColumnPool.Routes) {
            bySignature.TryAdd(route.Signature, route);
        }
        var existing = new List<Route<V>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Route<V> route in routesToRemove) {
            if (bySignature.TryGetValue(route.Signature, out Route<V>? known) && seen.Add(known.Signature)) {
                existing.Add(known);
            }
        }
        if (existing.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }

        var removedVariables = new HashSet<IVariableItem>();
        foreach (Route<V> route in existing) {
            if (_routeVariableIndex.TryGetValue(route.Signature, out (int Group, int RouteIndex) index)
                && index.Group < _x.Count) {
                removedVariables.Add(_x[index.Group][index.RouteIndex]);
            }
        }
        if (removedVariables.Count > 0) {
            Try objective = model.RemoveObjectiveTerms(removedVariables);
            if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<Success>(objectiveFailure.Error);
            }
        }

        foreach (Route<V> route in existing) {
            if (!_routeVariableIndex.TryGetValue(route.Signature, out (int Group, int RouteIndex) index)) {
                continue;
            }
            IVariableItem variable = _x[index.Group][index.RouteIndex];
            RemoveVariableTerm(RouteCost!, variable);
            for (int customerIndex = 0; customerIndex < Instance.Customers.Count; customerIndex++) {
                CustomerId customerId = Instance.Customers[customerIndex].Id;
                if (route.Stops.Any(stop => stop.CustomerId is { } id && id.Equals(customerId))) {
                    RemoveVariableTerm(CustomerCoverage![customerIndex], variable);
                }
            }
            if (!_vehicleTypeIndex.TryGetValue(route.VehicleTypeId, out int typeIndex)) {
                return NetworkSchedulingErrors.TryFailure(
                    $"删除路线列失败：车辆类型 {route.VehicleTypeId.Value} 不存在 / Failed to remove route column: vehicle type "
                    + $"{route.VehicleTypeId.Value} does not exist");
            }
            RemoveVariableTerm(FleetUsage![typeIndex], variable);
            model.Remove(variable);
            _routeVariableIndex.Remove(route.Signature);
        }
        ColumnPool.RemoveColumns(existing);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 切换到 Phase II / Switch to Phase II.
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try SwitchToPhaseTwo(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return ArtificialCoverage is null
            ? NetworkSchedulingErrors.TryFailure(
                "切换到 Phase II 失败：路线编译模型尚未注册 / Failed to switch to Phase II: route compilation model is not "
                + "registered")
            : ArtificialCoverage.SwitchToPhaseTwo(model);
    }

    /// <summary>
    /// 为指定新增列注册增量路线成本目标 / Register an incremental route-cost objective for the given columns.
    /// </summary>
    /// <param name="objectiveRoutes">这里新增路线 / The routes added in.</param>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try RegisterRouteCostObjective(
        IReadOnlyList<Route<V>> objectiveRoutes,
        LinearMetaModel<Flt64> model,
        string name) {
        ArgumentNullException.ThrowIfNull(objectiveRoutes);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(name);
        if (objectiveRoutes.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }
        LinearPolynomial<Flt64> total = new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        foreach (Route<V> route in objectiveRoutes) {
            if (!_routeVariableIndex.TryGetValue(route.Signature, out (int Group, int RouteIndex) index)) {
                return NetworkSchedulingErrors.TryFailure(
                    $"注册路线成本目标失败：路线 {route.Signature} 缺少变量索引 / Failed to register route cost objective: route "
                    + $"{route.Signature} has no variable index");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> solverCost =
                ValueAdapter.Normalize(route.Cost, Instance.Units.CostUnit);
            if (solverCost is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "注册路线成本目标失败：路线成本单位不兼容 / Failed to register route cost objective: incompatible route-cost "
                    + "unit");
            }
            total = Add(
                total,
                Scale(solverCost.Value, Variable(_x[index.Group][index.RouteIndex])));
        }
        model.AddObject(ObjectCategory.Minimum, total, name, name);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取路线变量值 / Extract route variable values.
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <param name="integralityTolerance">整数容差；缺省取实例容差 / Integrality tolerance; defaults to the instance tolerance.</param>
    /// <returns>路线及其变量值 / Routes together with their variable values.</returns>
    /// <remarks>
    /// **该参数在本方法体内不被使用**：它只供 `extractSolution` 转发。这里保留
    /// 同样的签名，是为了让调用方在两个提取方法之间切换时不必改参数列表。
 /// **This parameter is unused inside this method
    /// so `extractSolution` can forward it. Keeping the same signature lets callers switch between the two extraction methods without
    /// rewriting their argument lists.
    /// </remarks>
    public Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> ExtractRouteValues(
        LinearMetaModel<Flt64> model,
        Flt64? integralityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        var result = new List<(Route<V> Route, Flt64 Value)>();
        foreach (Route<V> route in ColumnPool.Routes) {
            if (!_routeVariableIndex.TryGetValue(route.Signature, out (int Group, int RouteIndex) index)
                || index.Group >= _x.Count) {
                continue;
            }
            IVariableItem variable = _x[index.Group][index.RouteIndex];
            foreach (Token<Flt64> token in model.Tokens.Tokens) {
                if (!token.BelongsTo(variable)) {
                    continue;
                }
                if (token.Result is { } value) {
                    result.Add((route, value));
                }
                break;
            }
        }
        return Results.Ok<IReadOnlyList<(Route<V> Route, Flt64 Value)>>(result);
    }

    /// <summary>
    /// 提取解中的路线 / Extract the routes in the solution.
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <param name="integralityTolerance">整数容差；缺省取实例容差 / Integrality tolerance; defaults to the instance tolerance.</param>
    /// <returns>选中的路线 / The selected routes.</returns>
    public Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model,
        Flt64? integralityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        Flt64 tolerance = integralityTolerance ?? Instance.Tolerances.Integrality;
        Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> routeValues =
            ExtractRouteValues(model);
        if (routeValues is Failed<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> valueFailure) {
            return Results.Failed<IReadOnlyList<Route<V>>>(valueFailure.Error);
        }
        Flt64 threshold = Flt64.One.Minus(tolerance);
        return Results.Ok<IReadOnlyList<Route<V>>>(
            routeValues.Value
                .Where(entry => entry.Value.CompareTo(threshold) >= 0)
                .Select(entry => entry.Route)
                .ToList());
    }

    /// <summary>从中间符号中移除某个变量的全部单项 / Strip one variable's monomials from an intermediate symbol.</summary>
    /// <param name="symbol">中间符号 / The intermediate symbol.</param>
    /// <param name="variable">待移除变量 / The variable to remove.</param>
    private static void RemoveVariableTerm(LinearExpressionSymbol symbol, IVariableItem variable) {
        symbol.Flush();
        MutableLinearPolynomial<Flt64> polynomial = symbol.AsMutable();
        var retained = polynomial.Monomials
            .Where(term => !ReferenceEquals(term.Symbol, variable))
            .ToList();
        Flt64 constant = polynomial.Constant;
        polynomial.Clear();
        foreach (LinearMonomial<Flt64> term in retained) {
            polynomial.AddMonomial(term);
        }
        polynomial.SetConstant(constant);
    }

    /// <summary>把变量包装成一次多项式 / Wrap a variable as a linear polynomial.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>一次多项式 / The linear polynomial.</returns>
    private static LinearPolynomial<Flt64> Variable(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    /// <summary>按标量缩放表达式 / Scale an expression by a scalar.</summary>
    /// <param name="factor">标量 / The scalar.</param>
    /// <param name="polynomial">表达式 / The expression.</param>
    /// <returns>缩放后的表达式 / The scaled expression.</returns>
    private static LinearPolynomial<Flt64> Scale(Flt64 factor, LinearPolynomial<Flt64> polynomial) =>
        new(
            polynomial.Monomials
                .Select(term => new LinearMonomial<Flt64>(factor.Times(term.Coefficient), term.Symbol))
                .ToList(),
            factor.Times(polynomial.Constant));

    /// <summary>相加两个表达式 / Add two expressions.</summary>
    /// <param name="left">左式 / Left operand.</param>
    /// <param name="right">右式 / Right operand.</param>
    /// <returns>和 / The sum.</returns>
    private static LinearPolynomial<Flt64> Add(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) =>
        new(
            left.Monomials.Concat(right.Monomials).ToList(),
            left.Constant.Plus(right.Constant));
}
