// 冗余模型：主甲板备用容量与其松弛 / The redundancy model: main-deck spare capacity and its slack.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Model;

/// <summary>
/// 冗余（主甲板备用容量）/ Redundancy (main-deck spare capacity).
/// </summary>
/// <remarks>
/// 逐条对齐：
///
/// - `redundancy` = 主甲板总容量 − 主甲板**实际**装载重量；
/// - `predicate_redundancy` = 主甲板总容量 − 主甲板**推荐**装载重量；
/// - `redundancy_slack` = `redundancy − minRedundancy` 的双侧松弛（`minRedundancy` 是常数零）。
///
/// 三者**不能合并**：`redundancy` 与 `predicateRedundancy` 一个是已装载、一个是规划中的装载，混用会让"备用容量"
/// 在规划阶段就等于实际值，于是目标函数对决策变量不再敏感。`redundancySlack` 才是目标里那一项。
///
///: `redundancy` is the main-deck capacity minus the main-deck **actual** load, `predicate_redundancy` is the same
/// minus the **estimated** load, and `redundancy_slack` is the two-sided slack of `redundancy` against `minRedundancy` (a constant zero). The three **must not be merged**:
/// actual versus planned loading is the whole point, and merging them makes spare capacity equal to the actual value during planning, so the objective stops being sensitive to
/// the decisions. `redundancySlack` is the term the objective actually uses.
/// </remarks>
public sealed class Redundancy
{
    private readonly IReadOnlyList<Stowage.Model.Item> _items;
    private readonly IReadOnlyList<Stowage.Model.StowagePosition> _positions;
    private readonly Stowage.Model.Load _load;

    /// <summary>
    /// 构造冗余模型 / Construct the redundancy model.
    /// </summary>
    /// <param name="items">货物 / The cargo items.</param>
    /// <param name="positions">舱位 / The stowage positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public Redundancy(
        IReadOnlyList<Stowage.Model.Item> items,
        IReadOnlyList<Stowage.Model.StowagePosition> positions,
        Stowage.Model.Load load) {
        _items = items;
        _positions = positions;
        _load = load;
    }

    /// <summary>主甲板总容量 / Total main-deck capacity</summary>
    /// <remarks>
    /// `position.mlw.mlw.value` 直接相加——**不换算单位**，因为 `mlw` 的单位与 `weightUnit` 一致。
    /// C# 的 `Item.Weight` 与 `MaxLoadWeightLimit.Mlw` 都是无单位 `double`（按千克），所以这里同样直接相加。
    ///
    /// The code sums `position.mlw.mlw.value` directly, **without unit conversion**, because `mlw` is already in `weightUnit`. C#'s `Item.Weight` and
    /// `MaxLoadWeightLimit.Mlw` are both bare doubles (kilograms), so the same direct sum applies.
    /// </remarks>
    public Flt64 MainDeckCapacity => _positions
        .Where(position => position.AircraftPosition.Location.Main)
        .Aggregate(Flt64.Zero, (total, position) => total.Plus(new Flt64(position.Mlw.Mlw)));

    /// <summary>最小冗余边界 / The minimum redundancy bound</summary>
    public Flt64 MinRedundancy => Flt64.Zero;

    /// <summary>最大冗余边界 / The maximum redundancy bound</summary>
    public Flt64 MaxRedundancy => MainDeckCapacity;

    /// <summary>实际冗余符号 / The actual redundancy symbol</summary>
    public LinearExpressionSymbol? RedundancySymbol { get; private set; }

    /// <summary>推荐冗余符号 / The estimated redundancy symbol</summary>
    public LinearExpressionSymbol? PredicateRedundancySymbol { get; private set; }

    /// <summary>冗余松弛符号 / The redundancy slack symbol</summary>
    public ILinearIntermediateSymbol<Flt64>? RedundancySlackSymbol { get; private set; }

    /// <summary>冗余松弛函数（判据要看它的右端）/ The redundancy slack function, whose right-hand side the criteria inspect</summary>
    public SlackFunction<Flt64>? RedundancySlackFunction { get; private set; }

    /// <summary>
    /// 由装载重量构造冗余多项式 / Build the redundancy polynomial from the load weights.
    /// </summary>
    /// <param name="estimate">是否用推荐装载重量 / Whether to use the estimated load weight.</param>
    /// <returns>冗余多项式 / The redundancy polynomial.</returns>
    public LinearPolynomial<Flt64> RedundancyPolynomial(bool estimate) {
        var polynomial = new MutableLinearPolynomial<Flt64>(constant: MainDeckCapacity);
        for (int j = 0; j < _positions.Count; ++j) {
            if (!_positions[j].AircraftPosition.Location.Main) {
                continue;
            }
            LinearExpressionSymbol? weight = estimate
                ? _load.EstimateLoadWeightSymbol(j)
                : _load.ActualLoadWeightSymbol(j);
            if (weight is null) {
                continue;
            }
            foreach (LinearMonomial<Flt64> monomial in weight.Polynomial.Monomials) {
                polynomial.AddMonomial(
                    new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
            }
        }
        return new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant);
    }

    /// <summary>
    /// 注册冗余符号与松弛 / Register the redundancy symbols and the slack.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        LinearPolynomial<Flt64> actualPolynomial = RedundancyPolynomial(estimate: false);
        var actual = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(actualPolynomial.Monomials, actualPolynomial.Constant),
            name: "redundancy");
        Try addedActual = model.Add(actual);
        if (!addedActual.IsOk) {
            return addedActual;
        }
        RedundancySymbol = actual;

        LinearPolynomial<Flt64> predicatePolynomial = RedundancyPolynomial(estimate: true);
        var predicate = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(predicatePolynomial.Monomials, predicatePolynomial.Constant),
            name: "predicate_redundancy");
        Try addedPredicate = model.Add(predicate);
        if (!addedPredicate.IsOk) {
            return addedPredicate;
        }
        PredicateRedundancySymbol = predicate;

        // 松弛的右侧是常数零，`withNegative`/`withPositive` 都给 true——基线就是这样：允许 redundancy 低于下界，
        // 由目标函数去罚它，而不是用硬约束把模型判死。
        // The slack's right side is the constant zero and both flags are true, exactly as in the code: redundancy may dip below the bound and the objective penalises it,
        // rather than a hard row declaring the model infeasible.
        var slack = new SlackFunction<Flt64>(
            x: new LinearPolynomial<Flt64>(actual.Polynomial.Monomials, actual.Polynomial.Constant),
            y: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), MinRedundancy),
            converter: IIntoValue<Flt64>.Identity,
            withNegative: true,
            withPositive: true,
            name: "redundancy_slack");
        var adapted = new LinearFunctionSymbolAdapter<Flt64>(slack);
        Try addedSlack = model.Add(adapted);
        if (!addedSlack.IsOk) {
            return addedSlack;
        }
        RedundancySlackSymbol = adapted;
        RedundancySlackFunction = slack;

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
