// 分区到合格设施的显式归属组件 / Explicit partition assignments to eligible facilities.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Service.Limits;
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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Model;

/// <summary>
/// 分区到合格设施的显式归属组件 / Explicit partition assignments to eligible facilities.
/// </summary>
/// <remarks>
/// **归属变量与设施启用量联动，而不是独立存在**。没有这条联动，模型可以在设施未开设的区域里分配归属，分区覆盖看起来"满足"而设施根本
/// 不存在。联动式 `归属 ≤ 设施启用` 让未开设的设施把对应归属钉死在零。
/// **An assignment variable is linked to facility activation rather than standing alone.** Without that link the model can assign
/// ownership in an area whose facility was never opened — coverage looks "met" while the facility does not exist. The linking row
/// `assignment ≤ activation` pins every assignment of an unopened facility to zero.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FacilityCoverageComponent<V> where V : struct, IRealNumber<V> {
    private readonly NetworkStructureProblem<V> _problem;
    private readonly NetworkDesignContext<V> _designContext;
    private readonly string _name;
    private readonly Dictionary<(string Partition, NetworkNodeId Facility), BinVar> _assignments = new();
    private bool _attempted;
    private LinearMetaModel<Flt64>? _registeredModel;

    /// <summary>
    /// 构造设施覆盖组件 / Construct the facility coverage component.
    /// </summary>
    /// <param name="problem">结构问题 / Structure problem.</param>
    /// <param name="designContext">建设上下文 / Construction context.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public FacilityCoverageComponent(
        NetworkStructureProblem<V> problem,
        NetworkDesignContext<V> designContext,
        string name) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(designContext);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        _designContext = designContext;
        _name = name;
    }

    /// <summary>
    /// 注册唯一覆盖、设施启用联动和设施分区配额 / Register unique coverage, facility activation and quotas.
    /// </summary>
    /// <param name="model">目标元模型 / Target meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "设施覆盖组件不可复用 / Facility coverage component cannot be reused");
        }
        _attempted = true;

        foreach (FacilityPartitionRequest partition in _problem.Partitions) {
            LinearPolynomial<Flt64> expression = Zero();
            foreach (NetworkNodeId facility in partition.EligibleFacilities) {
                var variable = new BinVar($"{_name}_partition_{partition.Id}_facility_{facility.Value}");
                if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                    return Results.Failed<Success>(addFailure.Error);
                }
                _assignments[(partition.Id, facility)] = variable;
                expression = Add(expression, Variable(variable));
                Try linking = model.AddConstraint(
                    Variable(variable).Le(_designContext.Aggregation.Construction.FacilityAvailability(facility)),
                    null,
                    name: $"{_name}_partition_{partition.Id}_open_{facility.Value}");
                if (linking is Failed<Success, ErrorCode, Error<ErrorCode>> linkingFailure) {
                    return Results.Failed<Success>(linkingFailure.Error);
                }
            }
            Try coverage = new FacilityPartitionAssignmentConstraint(
                expression,
                partition.Required,
                $"{_name}_partition_{partition.Id}_coverage").Invoke(model);
            if (coverage is Failed<Success, ErrorCode, Error<ErrorCode>> coverageFailure) {
                return Results.Failed<Success>(coverageFailure.Error);
            }
        }

        foreach (FacilityAssignmentQuota quota in _problem.Quotas) {
            LinearPolynomial<Flt64> expression = Zero();
            foreach (((string Partition, NetworkNodeId Facility) key, BinVar variable) in _assignments) {
                if (key.Facility.Equals(quota.Facility)) {
                    expression = Add(expression, Variable(variable));
                }
            }
            Try quotaResult = new FacilityPartitionQuotaConstraint(
                expression,
                quota.Minimum,
                quota.Maximum,
                $"{_name}_facility_{quota.Facility.Value}_quota").Invoke(model);
            if (quotaResult is Failed<Success, ErrorCode, Error<ErrorCode>> quotaFailure) {
                return Results.Failed<Success>(quotaFailure.Error);
            }
        }

        _registeredModel = model;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 读取分区-设施二元归属 / Read a binary partition-facility assignment.
    /// </summary>
    /// <param name="partition">分区标识 / Partition identity.</param>
    /// <param name="facility">设施节点 / Facility node.</param>
    /// <returns>归属表达式或不合格组合错误 / The assignment expression, or an ineligible-pair error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Assignment(
        string partition,
        NetworkNodeId facility) {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(facility);
        if (_registeredModel is null) {
            return NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "设施覆盖尚未注册 / Facility coverage is not registered");
        }
        return _assignments.TryGetValue((partition, facility), out BinVar? variable)
            ? Results.Ok(Variable(variable))
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "设施不具备分区资格 / Facility is not eligible for the partition");
    }

    /// <summary>
    /// 提取已归属分区并校验唯一所有者 / Extract assigned partitions and verify unique ownership.
    /// </summary>
    /// <remarks>
    /// **重复归属与必需分区漏配都直接失败**。前者在业务上是"一块区域有两个管理者"，后者是"没人管"；两者都不会让求解器报错，却会让
    /// 上层拿到一份自相矛盾的覆盖方案。
    /// **Both duplicate ownership and an unowned required partition fail outright.** The first means "two managers for one area", the
    /// second "nobody"; neither makes the solver complain, but both hand the caller a self-contradictory coverage plan.
    /// </remarks>
    /// <param name="model">已求解元模型 / Solved meta model.</param>
    /// <returns>分区所有者或缺失／冲突错误 / Partition owners, or a missing/conflicting-result error.</returns>
    public Result<IReadOnlyDictionary<string, NetworkNodeId>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(_registeredModel, model)) {
            return NetworkSchedulingErrors.Failure<IReadOnlyDictionary<string, NetworkNodeId>>(
                "设施覆盖模型未注册 / Facility-coverage model is not registered");
        }
        var owners = new Dictionary<string, NetworkNodeId>(StringComparer.Ordinal);
        Flt64 half = new(0.5);
        foreach (FacilityPartitionRequest partition in _problem.Partitions) {
            foreach (NetworkNodeId facility in partition.EligibleFacilities) {
                if (!_assignments.TryGetValue((partition.Id, facility), out BinVar? variable)) {
                    return NetworkSchedulingErrors.Failure<IReadOnlyDictionary<string, NetworkNodeId>>(
                        $"分区归属变量缺失：{partition.Id} / Missing partition-assignment variable: {partition.Id}");
                }
                Token<Flt64>? token = model.Tokens.Find(variable);
                if (token?.Result is not { } value) {
                    return NetworkSchedulingErrors.Failure<IReadOnlyDictionary<string, NetworkNodeId>>(
                        "缺失分区归属结果 / Missing partition-assignment result");
                }
                if (value.CompareTo(half) > 0) {
                    if (!owners.TryAdd(partition.Id, facility)) {
                        return NetworkSchedulingErrors.Failure<IReadOnlyDictionary<string, NetworkNodeId>>(
                            "分区被分配给多个设施 / Partition is assigned to multiple facilities");
                    }
                }
            }
            if (partition.Required && !owners.ContainsKey(partition.Id)) {
                return NetworkSchedulingErrors.Failure<IReadOnlyDictionary<string, NetworkNodeId>>(
                    "必需分区没有设施归属 / Required partition has no facility owner");
            }
        }
        return Results.Ok<IReadOnlyDictionary<string, NetworkNodeId>>(owners);
    }

    /// <summary>构造零多项式 / Build the zero polynomial.</summary>
    /// <returns>零多项式 / The zero polynomial.</returns>
    private static LinearPolynomial<Flt64> Zero() =>
        new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

    /// <summary>把变量包装成一次多项式 / Wrap a variable as a linear polynomial.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>一次多项式 / The linear polynomial.</returns>
    private static LinearPolynomial<Flt64> Variable(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    /// <summary>相加两个表达式 / Add two expressions.</summary>
    /// <param name="left">左式 / Left operand.</param>
    /// <param name="right">右式 / Right operand.</param>
    /// <returns>和 / The sum.</returns>
    private static LinearPolynomial<Flt64> Add(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) =>
        new(
            left.Monomials.Concat(right.Monomials).ToList(),
            left.Constant.Plus(right.Constant));
}
