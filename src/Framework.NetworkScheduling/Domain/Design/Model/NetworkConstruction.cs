// 建设变量组件 / Construction variable component.
// 与固定 Kotlin （10137 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;

/// <summary>
/// 设施与通道变量组件，仅负责共享建设决策 / Facility/service variable component owning shared construction only.
/// </summary>
/// <remarks>
/// **同一套建设变量被各场景共用**，因此本组件只拥有"建不建、建到哪个等级"这一层，不拥有任何商品流变量。若把流变量也塞进来，每个场景
/// 都会重复建设变量，模型规模随场景数线性膨胀，而建设决策本该只出现一次。
/// **One set of construction variables is shared by every scenario**, so this component owns only "build or not, at which level"
/// and no commodity-flow variables. Folding flow variables in would duplicate construction variables per scenario, growing the model
/// linearly with the scenario count for a decision that should appear exactly once.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkConstruction<V> where V : struct, IRealNumber<V> {
    private readonly Dictionary<NetworkArcId, IReadOnlyList<BinVar>> _services = new();
    private readonly Dictionary<NetworkNodeId, BinVar> _facilities = new();
    private readonly NetworkDesignProblem<V> _problem;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;
    private readonly string _name;
    private bool _attempted;
    private LinearMetaModel<Flt64>? _model;

    /// <summary>
    /// 构造建设变量组件 / Construct the construction variable component.
    /// </summary>
    /// <param name="problem">建设问题 / Design problem.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public NetworkConstruction(
        NetworkDesignProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        _adapter = adapter;
        _name = name;
    }

    /// <summary>通道等级变量 / Service level variables.</summary>
    public IReadOnlyDictionary<NetworkArcId, IReadOnlyList<BinVar>> Services => _services;

    /// <summary>设施变量 / Facility variables.</summary>
    public IReadOnlyDictionary<NetworkNodeId, BinVar> Facilities => _facilities;

    /// <summary>建设费用表达式 / The construction-cost expression.</summary>
    public LinearPolynomial<Flt64> ConstructionCost { get; private set; } = Zero();

    /// <summary>
    /// 服务启用表达式 / Service activation expression.
    /// </summary>
    /// <remarks>
    /// **未声明的通道默认为"完全可用"（常数 1），不是"不可用"（常数 0）**。通道只有被显式列为候选时才带建设决策；把缺省当 0 会让
    /// 所有没有写进候选表的弧在流模型里被直接封死，而调用方只是没打算对它做建设决策。
    /// **An undeclared service defaults to "fully available" (constant one), not "unavailable" (constant zero).** A service carries a
    /// construction decision only when it is explicitly a candidate; defaulting to zero would shut every arc missing from the candidate
    /// list straight out of the flow model when the caller merely never meant to make a decision about it.
    /// </remarks>
    /// <param name="arc">服务标识 / Service identity.</param>
    /// <returns>启用量表达式 / The activation expression.</returns>
    public LinearPolynomial<Flt64> Availability(NetworkArcId arc) {
        ArgumentNullException.ThrowIfNull(arc);
        if (!_services.TryGetValue(arc, out IReadOnlyList<BinVar>? variables)) {
            return Constant(Flt64.One);
        }
        LinearPolynomial<Flt64> expression = Zero();
        foreach (BinVar variable in variables) {
            expression = Add(expression, Variable(variable));
        }
        return expression;
    }

    /// <summary>
    /// 可选设施启用量；固定设施为一 / Optional facility activation, one for fixed facilities.
    /// </summary>
    /// <param name="node">节点 / Node.</param>
    /// <returns>启用量表达式 / The activation expression.</returns>
    public LinearPolynomial<Flt64> FacilityAvailability(NetworkNodeId node) {
        ArgumentNullException.ThrowIfNull(node);
        return _facilities.TryGetValue(node, out BinVar? variable)
            ? Variable(variable)
            : Constant(Flt64.One);
    }

    /// <summary>
    /// 注册建设变量、互斥等级和预算 / Register construction, exclusive levels and budget.
    /// </summary>
    /// <remarks>
    /// **等级容量必须落在声明包络内**。等级容量是建设后的总容量，包络来自名义流弧的上界；超出包络的等级会让"建了却用不上"的容量进入
    /// 模型，费用照样计入，而容量约束永远不起作用——解看起来更优，其实只是为一个不可能被用满的通道付了钱。
    /// **A level's capacity must fit the declared envelope.** Level capacity is the resulting total; the envelope comes from the
    /// nominal flow arc's upper bound. A level beyond the envelope puts capacity into the model that can never be used, still charges
    /// for it, and leaves the capacity constraint inert — the solution looks better while having paid for a service that cannot fill.
    /// **必选通道用等式、可选通道用不等式**：可选通道的 `activation ≤ 1` 只禁止并存，不强制选择。
    /// **A required service uses an equality, an optional one an inequality**: `activation ≤ 1` only forbids coexistence, it does not
    /// force a choice.
    /// </remarks>
    /// <param name="target">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(_model, target)) {
            return Results.Ok(Results.SuccessInstance);
        }
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "建设变量组件不可复用 / Construction component cannot be reused");
        }
        _attempted = true;

        if (_problem.Services.Select(service => service.Arc).Distinct().Count() != _problem.Services.Count
            || _problem.Facilities.Select(facility => facility.Node).Distinct().Count()
                != _problem.Facilities.Count) {
            return NetworkSchedulingErrors.TryFailure(
                "建设资源重复 / Duplicate design asset");
        }

        LinearPolynomial<Flt64> constructionCost = Zero();
        foreach (NetworkFacility<V> facility in _problem.Facilities) {
            if (_problem.Flow.Graph.Node(facility.Node) is null) {
                return NetworkSchedulingErrors.TryFailure(
                    "设施节点不存在 / Facility node does not exist");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                _adapter.Normalize(facility.Cost, _problem.Flow.CostUnit);
            if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "设施成本单位不兼容 / Incompatible facility cost unit");
            }
            if (coefficient.Value.CompareTo(Flt64.Zero) < 0) {
                return NetworkSchedulingErrors.TryFailure(
                    "设施成本不能为负 / Facility cost must be nonnegative");
            }
            var variable = new BinVar($"{_name}_facility_{facility.Node.Value}");
            if (target.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                return Results.Failed<Success>(addFailure.Error);
            }
            _facilities[facility.Node] = variable;
            constructionCost = Add(constructionCost, Scale(coefficient.Value, Variable(variable)));
            if (facility.Required) {
                Try required = target.AddConstraint(Variable(variable).Eq(Constant(Flt64.One)), null);
                if (required is Failed<Success, ErrorCode, Error<ErrorCode>> requiredFailure) {
                    return Results.Failed<Success>(requiredFailure.Error);
                }
            }
        }

        foreach (NetworkService<V> service in _problem.Services) {
            Domain.Flow.Model.FlowArc<V>? arc = _problem.Flow.Arcs.FirstOrDefault(
                candidate => candidate.NetworkArc.Id.Equals(service.Arc));
            if (arc is null) {
                return NetworkSchedulingErrors.TryFailure(
                    "建设通道不存在 / Design service does not exist");
            }
            if (service.Levels.Count == 0
                || service.Levels.Select(level => level.Id).Distinct().Count() != service.Levels.Count
                || service.Levels.Any(level => string.IsNullOrWhiteSpace(level.Id))) {
                return NetworkSchedulingErrors.TryFailure(
                    "建设等级为空或重复 / Empty or duplicate capacity level");
            }
            var variables = new List<BinVar>();
            LinearPolynomial<Flt64> activation = Zero();
            PhysicalUnit capacityUnit = arc.Capacity.Upper.Unit;
            Result<Flt64, ErrorCode, Error<ErrorCode>> envelope = _adapter.Normalize(
                arc.Capacity.Upper, capacityUnit);
            if (envelope is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "通道容量包络无效 / Invalid service capacity envelope");
            }
            foreach (CapacityLevel<V> level in service.Levels) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> capacity =
                    _adapter.Normalize(level.Capacity, capacityUnit);
                Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                    _adapter.Normalize(level.Cost, _problem.Flow.CostUnit);
                if (capacity is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "建设容量单位不兼容 / Incompatible construction capacity unit");
                }
                if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "建设成本单位不兼容 / Incompatible construction cost unit");
                }
                if (capacity.Value.CompareTo(Flt64.Zero) < 0
                    || coefficient.Value.CompareTo(Flt64.Zero) < 0
                    || capacity.Value.CompareTo(envelope.Value) > 0) {
                    return NetworkSchedulingErrors.TryFailure(
                        "等级容量必须位于声明包络内且费用非负 / Level capacity must fit the declared envelope and cost must be "
                        + "nonnegative");
                }
                var variable = new BinVar($"{_name}_{service.Arc.Value}_{level.Id}");
                if (target.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                    return Results.Failed<Success>(addFailure.Error);
                }
                variables.Add(variable);
                activation = Add(activation, Variable(variable));
                constructionCost = Add(constructionCost, Scale(coefficient.Value, Variable(variable)));
            }
            _services[service.Arc] = variables;
            Try exclusive = service.Required
                ? target.AddConstraint(activation.Eq(Constant(Flt64.One)), null)
                : target.AddConstraint(activation.Le(Constant(Flt64.One)), null);
            if (exclusive is Failed<Success, ErrorCode, Error<ErrorCode>> exclusiveFailure) {
                return Results.Failed<Success>(exclusiveFailure.Error);
            }
        }

        if (_problem.Budget is { } budget) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> limit =
                _adapter.Normalize(budget, _problem.Flow.CostUnit);
            if (limit is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "预算单位不兼容 / Incompatible budget unit");
            }
            if (limit.Value.CompareTo(Flt64.Zero) < 0) {
                return NetworkSchedulingErrors.TryFailure(
                    "预算不能为负 / Budget must be nonnegative");
            }
            Try bounded = target.AddConstraint(constructionCost.Le(Constant(limit.Value)), null);
            if (bounded is Failed<Success, ErrorCode, Error<ErrorCode>> boundedFailure) {
                return Results.Failed<Success>(boundedFailure.Error);
            }
        }

        ConstructionCost = constructionCost;
        _model = target;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取建设选择 / Extract construction choices.
    /// </summary>
    /// <remarks>
    /// **二值变量按 0.5 阈值判定，而不是严格等于 1**。求解器返回的是浮点松弛解，整数变量常带 1e-9 级误差；用等号判定会把一个明确选中
    /// 的等级读成未选，建设费用却仍按该变量计入，于是"费用花了但没建成"这种自相矛盾的结果会流到上层。
    /// **Binary variables are read with a 0.5 threshold, not equality with one.** The solver returns floating-point values and
    /// integer variables carry 1e-9-scale error; an equality test would read a clearly selected level as unselected while its cost is
    /// still charged, so the contradictory "paid for but not built" outcome would reach the caller.
    /// </remarks>
    /// <param name="target">已求解模型 / Solved model.</param>
    /// <returns>建设选择和费用 / The choices and their cost.</returns>
    public Result<NetworkConstructionSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(_model, target)) {
            return NetworkSchedulingErrors.Failure<NetworkConstructionSolution<V>>(
                "建设变量未注册 / Construction variables not registered");
        }
        var open = new HashSet<NetworkNodeId>();
        var selected = new Dictionary<NetworkArcId, string>();
        Flt64 total = Flt64.Zero;
        Flt64 half = new(0.5);

        foreach (NetworkFacility<V> facility in _problem.Facilities) {
            if (_facilities.TryGetValue(facility.Node, out BinVar? variable) is false
                || target.Tokens.Find(variable)?.Result is not { } value) {
                return NetworkSchedulingErrors.Failure<NetworkConstructionSolution<V>>(
                    "缺失设施结果 / Missing facility result");
            }
            if (value.CompareTo(half) > 0) {
                open.Add(facility.Node);
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                _adapter.Normalize(facility.Cost, _problem.Flow.CostUnit);
            if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.Failure<NetworkConstructionSolution<V>>(
                    "设施费用无效 / Invalid facility cost");
            }
            total = total.Plus(value.Times(coefficient.Value));
        }

        foreach (NetworkService<V> service in _problem.Services) {
            if (!_services.TryGetValue(service.Arc, out IReadOnlyList<BinVar>? variables)) {
                continue;
            }
            for (int index = 0; index < variables.Count; index++) {
                if (target.Tokens.Find(variables[index])?.Result is not { } value) {
                    return NetworkSchedulingErrors.Failure<NetworkConstructionSolution<V>>(
                        "缺失通道结果 / Missing service result");
                }
                if (value.CompareTo(half) > 0) {
                    selected[service.Arc] = service.Levels[index].Id;
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                    _adapter.Normalize(service.Levels[index].Cost, _problem.Flow.CostUnit);
                if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.Failure<NetworkConstructionSolution<V>>(
                        "通道费用无效 / Invalid service cost");
                }
                total = total.Plus(value.Times(coefficient.Value));
            }
        }

        Result<V, ErrorCode, Error<ErrorCode>> converted = _adapter.FromSolverValue(total);
        if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<NetworkConstructionSolution<V>>(
                "建设费用转换失败 / Construction cost conversion failed");
        }
        return Results.Ok(new NetworkConstructionSolution<V>(
            open, selected, new Quantity<V>(converted.Value, _problem.Flow.CostUnit)));
    }

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    /// <param name="value">常数值 / The constant.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);

    /// <summary>构造零多项式 / Build the zero polynomial.</summary>
    /// <returns>零多项式 / The zero polynomial.</returns>
    private static LinearPolynomial<Flt64> Zero() => Constant(Flt64.Zero);

    /// <summary>把一个变量包装成一次多项式 / Wrap a variable as a linear polynomial.</summary>
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
