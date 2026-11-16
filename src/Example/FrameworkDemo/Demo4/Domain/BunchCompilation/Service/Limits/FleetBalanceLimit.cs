#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service.Limits;

/// <summary>
/// 机队平衡的影子价格键 / The fleet-balance shadow price key.
/// </summary>
/// <remarks>
/// 键是**检查点**（机场 × 机型），不是一个数字下标：影子价格的意义是"再多放一架这个机型到这个机场，成本能降多少"，
/// 没有机场与机型就无从解释。
///
/// The key is a **checkpoint** (airport x minor type), not a numeric index: a shadow price means "how much cheaper if one more aircraft of this type could sit at this airport", and
/// without the airport and type it cannot be interpreted.
/// </remarks>
public sealed class FleetBalanceShadowPriceKey(Airport airport, AircraftMinorType minorType) : ShadowPriceKey(typeof(FleetBalanceShadowPriceKey)) {
    /// <summary>机场 / The airport.</summary>
    public Airport Airport { get; } = airport;

    /// <summary>机型 / The minor type.</summary>
    public AircraftMinorType MinorType { get; } = minorType;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FleetBalanceShadowPriceKey other
        && Equals(Airport, other.Airport)
        && Equals(MinorType, other.MinorType);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Airport, MinorType);

    /// <inheritdoc/>
    public override string ToString() => "Fleet Balance (" + Airport + ", " + MinorType + ")";
}

/// <summary>
/// 机队平衡的约束与目标 / The fleet balance's constraints and objective.
/// </summary>
/// <remarks>
/// ：每个检查点加一条 `slack ≥ 机数`，再把 `Σ 系数 × slack` 放进最小化目标。
///
/// **两半都不能少**：只加约束不加目标，松弛可以取任意大的值，模型照样可行但成本毫无意义；只加目标不加约束，
/// 松弛会一路压到零——**看起来"平衡得完美"，其实一架飞机都没数进去**。这两半正好是一对反向的错误。
///
/// The code's adds `slack ≥ aircraft count` per checkpoint and puts `Σ coefficient × slack` into the minimisation objective.
///
/// **Neither half may be dropped**: with the constraint but no objective the slack may take any large value, the model stays feasible and the cost is meaningless; with the objective but
/// no constraint the slack is driven to zero — **looking perfectly balanced while no aircraft was counted at all**. The two halves are exactly opposite failure modes.
/// </remarks>
public sealed class FleetBalanceLimit
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly FleetBalance _fleetBalance;

    private readonly Func<Airport, AircraftMinorType, Flt64> _coefficient;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="fleetBalance">机队平衡 / The fleet balance.</param>
    /// <param name="coefficient">每个检查点的系数，缺省为 1 / The per-checkpoint coefficient, 1 when omitted.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public FleetBalanceLimit(
        FleetBalance fleetBalance,
        Func<Airport, AircraftMinorType, Flt64>? coefficient = null,
        string name = "fleet_balance_limit") {
        ArgumentNullException.ThrowIfNull(fleetBalance);
        _fleetBalance = fleetBalance;
        _coefficient = coefficient ?? ((_, _) => Flt64.One);
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "机队平衡限制只能施加到线性元模型 / the fleet balance limit can only be applied to a linear meta model"));
        }

        IReadOnlyList<KeyValuePair<FleetBalance.CheckPoint, FleetBalance.Limit>> limits = _fleetBalance.Limits;
        if (limits.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        for (int index = 0; index < limits.Count; ++index) {
            if (!_fleetBalance.Slacks.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? slack)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "机队平衡还没注册 / the fleet balance has not been registered"));
            }
            LinearPolynomial<Flt64> polynomial = slack.Polynomial;
            string rowName = _name + "_" + index;
            Try added = linear.AddConstraint(
                new LinearInequality<Flt64>(
                    polynomial,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(limits[index].Value.Amount)),
                    Comparison.GE,
                    rowName),
                group: null,
                name: rowName,
                args: new FleetBalanceShadowPriceKey(limits[index].Key.Airport, limits[index].Key.MinorType));
            if (!added.IsOk) {
                return added;
            }
        }

        // 目标：`Σ 系数 × slack`。系数在**多项式外面**乘，与基线 `coefficient(...) * slack[l]` 一致。
        // The objective: `Σ coefficient × slack`, multiplied **outside** the polynomial, matching its `coefficient(...) * slack[l]`.
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int index = 0; index < limits.Count; ++index) {
            if (!_fleetBalance.Slacks.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? slack)) {
                continue;
            }
            Flt64 coefficient = _coefficient(limits[index].Key.Airport, limits[index].Key.MinorType);
            LinearPolynomial<Flt64> polynomial = slack.Polynomial;
            foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient * coefficient, monomial.Symbol));
            }
            constant += polynomial.Constant * coefficient;
        }

        if (monomials.Count == 0 && constant.ToDouble() == 0.0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, constant),
            "fleet balance",
            displayName: null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确；抛异常会让整条流水线停下来。
    /// `ponytail:` 天花板——影子价格参数里目前没有检查点信息，等接口需要携带时再补真实查表。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct; throwing would halt the whole
    /// pipeline. **`ponytail:` ceiling** — the shadow-price arguments carry no checkpoint yet; add the real lookup when the interface needs it.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>? Extractor()
        => (map, args) => Flt64.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// 按**行上带的键类型**从对偶解里认回本管线的行。提取器暂时读不到（参数里没有检查点），但映射本身是公开接口——
    /// 手上有检查点的调用方可以直接查。
    ///
    /// Recognises this pipeline's rows in the dual solution **by the key type on the row**. The extractor cannot read them yet (the arguments carry no checkpoint), but the map is a
    /// public interface — a caller holding a checkpoint can look the price up directly.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is FleetBalanceShadowPriceKey key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
