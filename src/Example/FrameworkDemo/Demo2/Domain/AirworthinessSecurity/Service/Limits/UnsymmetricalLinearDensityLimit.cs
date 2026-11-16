// 不对称线性密度限制 / The unsymmetrical linear density bound.

using System;
using System.Collections.Generic;
using System.Globalization;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 不对称线性密度限制 / The unsymmetrical linear density bound.
/// </summary>
/// <remarks>
/// 逐条对齐：每条限制线 × 每个限制行加一行
/// `系数_左 × 左线密度 + 系数_右 × 右线密度 ≤ 最大和`，名字 `{name}_{区域名}_{力臂}_{行序号}`。
///
/// 四处容易做错：
///
/// 1. **系数可空**。`LeftCoefficient` 为 `null` 时左项整个不进多项式——不是乘 0，而是**不出现**。下游按符号取系数时 `null` 与 0 的行为不一样。
/// 2. **一条线要两行**（左减右 ≤ lhs、右减左 ≤ rhs）。只加一行时只限制单侧。
/// 3. **名字带行序号**。同一线上两行同名时"第 3 条约束不可行"不知道是哪一侧。
/// 4. **整线两侧都不可用才跳过**。只有一侧不可用时仍加——`none { it.status.available }`。
///
/// one row per limit line per limit row, `coef_left × left density + coef_right × right density ≤ max sum`, named
/// `{name}_{zone}_{arm}_{index}`. Four easy mistakes: (1) **the coefficients are nullable** — a `null` left coefficient keeps the left term **out** of the polynomial rather than
/// multiplying by zero, and downstream reading a coefficient by symbol treats `null` and 0 differently; (2) **two rows per line** (`left − right ≤ lhs`, `right − left ≤ rhs`), and
/// one alone bounds a single side; (3) **the name carries the row index**, or "constraint 3 is infeasible" does not say which side; (4) **the line is skipped only when both sides
/// are unavailable** — the code is `none { it.status.available }`, so one unavailable side still gets its row.
/// </remarks>
internal sealed class UnsymmetricalLinearDensityLimit
{
    private readonly MaxUnsymmetricalLinearDensity _maxUnsymmetricalLinearDensity;
    private readonly LinearDensity _linearDensity;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly string _name;

    /// <summary>
    /// 构造不对称线性密度限制 / Construct the unsymmetrical linear density bound.
    /// </summary>
    /// <param name="maxUnsymmetricalLinearDensity">不对称限制 / The unsymmetrical limits.</param>
    /// <param name="linearDensity">线性密度模型 / The linear density model.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public UnsymmetricalLinearDensityLimit(
        MaxUnsymmetricalLinearDensity maxUnsymmetricalLinearDensity,
        LinearDensity linearDensity,
        IReadOnlyList<StowagePosition> positions,
        string name = "unsymmetrical_linear_density_limit") {
        _maxUnsymmetricalLinearDensity = maxUnsymmetricalLinearDensity;
        _linearDensity = linearDensity;
        _positions = positions;
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
        foreach (UnsymmetricalLinearDensityLimitZone zone in _maxUnsymmetricalLinearDensity.LimitZones) {
            foreach (UnsymmetricalLinearDensityLimitLine line in zone.Lines) {
                if (!_positions[line.LeftPositionIndex].Status.Available
                    && !_positions[line.RightPositionIndex].Status.Available) {
                    continue;
                }
                if (_linearDensity.LinearDensitySymbol(line.LeftPositionIndex) is not { } left) {
                    return Missing(zone.Name, line.LeftPositionIndex);
                }
                if (_linearDensity.LinearDensitySymbol(line.RightPositionIndex) is not { } right) {
                    return Missing(zone.Name, line.RightPositionIndex);
                }

                for (int l = 0; l < zone.Limits.Count; ++l) {
                    Model.UnsymmetricalLinearDensityLimit limit = zone.Limits[l];
                    var polynomial = new MutableLinearPolynomial<Flt64>();
                    if (limit.LeftCoefficient is { } leftCoefficient) {
                        Scale(polynomial, left.Polynomial, leftCoefficient);
                    }
                    if (limit.RightCoefficient is { } rightCoefficient) {
                        Scale(polynomial, right.Polynomial, rightCoefficient);
                    }

                    string name = _name + "_" + zone.Name + "_"
                        + line.Arm.ToString(CultureInfo.InvariantCulture) + "_" + l;
                    var row = new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
                        new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(limit.MaxSum)),
                        Comparison.LE,
                        name);
                    Try result = model.AddConstraint(row, group: null, name: name);
                    if (!result.IsOk) {
                        return result;
                    }
                    added.Add(name);
                }
            }
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try Missing(string zoneName, int positionIndex) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "不对称线性密度区域 " + zoneName + " 的舱位缺少线密度符号 / a position in zone " + zoneName
            + " has no linear density symbol: " + _positions[positionIndex].Name));

    private static void Scale(
        MutableLinearPolynomial<Flt64> target,
        LinearPolynomial<Flt64> source,
        double coefficient) {
        var factor = new Flt64(coefficient);
        foreach (LinearMonomial<Flt64> monomial in source.Monomials) {
            target.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient.Times(factor), monomial.Symbol));
        }
        target.AddConstant(source.Constant.Times(factor));
    }
}
