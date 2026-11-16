// CG 包络约束：纵向力矩落在包络两侧之间 / The CG envelope constraint: longitudinal torque between the two sides.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// CG 包络约束 / The CG envelope constraint.
/// </summary>
/// <remarks>
/// 逐条对齐：对**每个飞行阶段** × **该阶段的每条包络**各加两行——
///
/// | 行 | 关系 | 名称 |
/// | --- | --- | --- |
/// | 上界 | 纵向力矩 ≤ 最大指数 | `{name}_{包络名}_ub` |
/// | 下界 | 纵向力矩 ≥ 最小指数 | `{name}_{包络名}_lb` |
///
/// 四处容易做错：
///
/// 1. **两行都要有**。只加上界时重心可以无限前移而求解器一声不响——飞机可以飞到机头压得抬不起来，模型照样"最优"。
/// 2. **同一阶段的多条包络是"且"不是"或"**。两张包络同时适用时必须同时满足；把它们合并成一条（取更严的）要求先判定谁更严，
///    而"谁更严"随总重变化。基线逐条加。
/// 3. **力矩取该阶段的那一个**，不是首阶段的、也不是 `ZeroFuel` 的。按阶段取错会让起飞和着陆共用一个力矩边界。
/// 4. **`_ub` 与 `_lb` 后缀**。两行同名时求解器报"第 3 条约束不可行"无法区分是上界还是下界。
///
/// for **each flight phase** × **each envelope of that phase**, two rows — an upper bound (torque ≤ max index, `_ub`) and a lower bound (torque ≥ min index,
/// `_lb`). Four easy mistakes: (1) **both rows are needed** — with only the upper bound the centre of gravity can drift arbitrarily forward with no complaint, the aircraft nose-heavy
/// and the model still "optimal"; (2) **several envelopes for one phase mean AND, not OR** — each must hold, and folding them into one (taking the tighter) requires deciding which
/// is tighter, which varies with total weight; (3) **the torque is that phase's**, not the first phase's nor the zero-fuel one, or take-off and landing share one boundary;
/// (4) **the `_ub` / `_lb` suffixes** — two rows with one name make "constraint 3 is infeasible" ambiguous.
/// </remarks>
internal sealed class EnvelopeLimit
{
    private readonly Torque _torque;
    private readonly IReadOnlyDictionary<FlightPhase, IReadOnlyList<IEnvelope>> _envelopes;
    private readonly string _name;

    /// <summary>
    /// 构造 CG 包络约束 / Construct the CG envelope constraint.
    /// </summary>
    /// <param name="torque">力矩模型 / The torque model.</param>
    /// <param name="envelopes">按飞行阶段的包络线 / The envelopes by flight phase.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public EnvelopeLimit(
        Torque torque,
        IReadOnlyDictionary<FlightPhase, IReadOnlyList<IEnvelope>> envelopes,
        string name = "envelope_limit") {
        _torque = torque;
        _envelopes = envelopes;
        _name = name;
    }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        var added = new List<string>();
        foreach (KeyValuePair<FlightPhase, IReadOnlyList<IEnvelope>> pair in _envelopes) {
            FlightPhase phase = pair.Key;
            if (_torque.LongitudinalTorque(phase) is not { } longitudinal) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "纵向力矩符号未注册 / the longitudinal torque symbol is not registered: "
                    + phase.ToString().ToLowerInvariant()));
            }
            foreach (IEnvelope envelope in pair.Value) {
                if (envelope.MaxIndexSymbol is not { } maximum || envelope.MinIndexSymbol is not { } minimum) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "包络符号未注册 / the envelope symbols are not registered: " + envelope.Name));
                }

                string upperName = _name + "_" + envelope.Name + "_ub";
                var upper = new LinearInequality<Flt64>(
                    CopyOf(longitudinal),
                    CopyOf(maximum),
                    Comparison.LE,
                    upperName);
                Try addedUpper = model.AddConstraint(upper, group: null, name: upperName);
                if (!addedUpper.IsOk) {
                    return addedUpper;
                }
                added.Add(upperName);

                string lowerName = _name + "_" + envelope.Name + "_lb";
                var lower = new LinearInequality<Flt64>(
                    CopyOf(longitudinal),
                    CopyOf(minimum),
                    Comparison.GE,
                    lowerName);
                Try addedLower = model.AddConstraint(lower, group: null, name: lowerName);
                if (!addedLower.IsOk) {
                    return addedLower;
                }
                added.Add(lowerName);
            }
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static LinearPolynomial<Flt64> CopyOf(ILinearIntermediateSymbol<Flt64> symbol) =>
        new(symbol.Polynomial.Monomials, symbol.Polynomial.Constant);
}
