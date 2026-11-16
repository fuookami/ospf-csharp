// 实验纵向平衡：主甲板纵向力矩与实际/推荐之差 / Experimental longitudinal balance: the main-deck longitudinal torque gap between actual and estimated.

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
/// 实验纵向平衡 / Experimental longitudinal balance.
/// </summary>
/// <remarks>
/// 逐条对齐：把**主甲板**各舱位的纵向力矩加起来，
/// 实际与推荐各一个，再取两者之差的双侧松弛。
///
/// 与冗余的差别是**只加主甲板**：下货舱的力矩不进纵向平衡（它由别的约束管），把全部舱位加起来会让平衡量随下舱装载一起漂。
///
/// sum the longitudinal torque over the **main deck only**, once for actual and once for estimated, then take the two-sided slack of the difference. The
/// difference from redundancy is "main deck only": the lower holds' torque is handled by other rows, and adding every position makes the balance drift with lower-hold loading.
/// </remarks>
public sealed class ExperimentalLongitudinalBalance
{
    private readonly IReadOnlyList<Stowage.Model.StowagePosition> _positions;
    private readonly Stowage.Model.Load _load;

    /// <summary>
    /// 构造纵向平衡模型 / Construct the longitudinal balance model.
    /// </summary>
    /// <param name="positions">舱位 / The stowage positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public ExperimentalLongitudinalBalance(
        IReadOnlyList<Stowage.Model.StowagePosition> positions,
        Stowage.Model.Load load) {
        _positions = positions;
        _load = load;
    }

    /// <summary>主甲板实际纵向力矩符号 / The main-deck actual longitudinal torque symbol</summary>
    public LinearExpressionSymbol? MainActualLongitudinalTorqueSymbol { get; private set; }

    /// <summary>推荐纵向力矩符号 / The estimated longitudinal torque symbol</summary>
    public LinearExpressionSymbol? PredicateLongitudinalTorqueSymbol { get; private set; }

    /// <summary>纵向力矩松弛符号 / The longitudinal torque slack symbol</summary>
    public ILinearIntermediateSymbol<Flt64>? LongitudinalTorqueSlackSymbol { get; private set; }

    /// <summary>
    /// 由纵向力矩构造主甲板合计多项式 / Build the main-deck total polynomial from the longitudinal torques.
    /// </summary>
    /// <param name="estimate">是否用推荐力矩 / Whether to use the estimated torque.</param>
    /// <returns>合计多项式 / The total polynomial.</returns>
    public LinearPolynomial<Flt64> TorquePolynomial(bool estimate) {
        var polynomial = new MutableLinearPolynomial<Flt64>();
        for (int j = 0; j < _positions.Count; ++j) {
            if (!_positions[j].AircraftPosition.Location.Main) {
                continue;
            }
            LinearExpressionSymbol? torque = estimate
                ? _load.EstimateLongitudinalTorqueSymbol(j)
                : _load.ActualLongitudinalTorqueSymbol(j);
            if (torque is null) {
                continue;
            }
            foreach (LinearMonomial<Flt64> monomial in torque.Polynomial.Monomials) {
                polynomial.AddMonomial(monomial);
            }
            polynomial.AddConstant(torque.Polynomial.Constant);
        }
        return new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant);
    }

    /// <summary>
    /// 注册纵向平衡符号与松弛 / Register the balance symbols and the slack.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        LinearPolynomial<Flt64> actualPolynomial = TorquePolynomial(estimate: false);
        var actual = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(actualPolynomial.Monomials, actualPolynomial.Constant),
            name: "main_actual_longitudinal_torque");
        Try addedActual = model.Add(actual);
        if (!addedActual.IsOk) {
            return addedActual;
        }
        MainActualLongitudinalTorqueSymbol = actual;

        LinearPolynomial<Flt64> predicatePolynomial = TorquePolynomial(estimate: true);
        var predicate = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(predicatePolynomial.Monomials, predicatePolynomial.Constant),
            name: "predicate_longitudinal_torque");
        Try addedPredicate = model.Add(predicate);
        if (!addedPredicate.IsOk) {
            return addedPredicate;
        }
        PredicateLongitudinalTorqueSymbol = predicate;

        // x 是实际、y 是推荐：松弛量就是"实际比推荐多/少多少力矩"，目标里罚它把两者拉齐。
        // `x` is actual and `y` is estimated: the slack is how much torque the actual exceeds (or falls short of) the estimate, and penalising it pulls them together.
        var slack = new SlackFunction<Flt64>(
            x: new LinearPolynomial<Flt64>(actual.Polynomial.Monomials, actual.Polynomial.Constant),
            y: new LinearPolynomial<Flt64>(predicate.Polynomial.Monomials, predicate.Polynomial.Constant),
            converter: IIntoValue<Flt64>.Identity,
            withNegative: true,
            withPositive: true,
            name: "longitudinal_torque_slack");
        var adapted = new LinearFunctionSymbolAdapter<Flt64>(slack);
        Try addedSlack = model.Add(adapted);
        if (!addedSlack.IsOk) {
            return addedSlack;
        }
        LongitudinalTorqueSlackSymbol = adapted;

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
