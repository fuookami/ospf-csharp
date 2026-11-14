// 力矩：按飞行阶段的纵向力矩、横向力矩、CLIM 与指数 / Torque: per-phase longitudinal torque, lateral torque, CLIM and index.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;

/// <summary>
/// 力矩模型 / The torque model.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/mac/model/Torque.kt`。四族符号：
///
/// | 族 | 构成 | 按阶段 |
/// | --- | --- | --- |
/// | `longitudinal_torque_{阶段}` | 舱位纵向力矩 + 燃油指数（仅起飞/着陆）+ 干操作重力 × 平衡力臂 + 救生筏重量 × 力臂 | 是 |
/// | `lateral_torque` | 舱位横向力矩 | 否 |
/// | `clim` | 舱位 CLIM（**仅宽体机注册**） | 否 |
/// | `index_{阶段}` | 舱位指数 + 燃油指数（仅起飞/着陆）+ 干操作指数 + 救生筏指数 | 是 |
///
/// 四处容易做错：
///
/// 1. **燃油只进起飞/着陆**。零油重阶段没有燃油——这正是"零油"的定义，与 `TotalWeight` 同一口径。
/// 2. **干操作重量以"重力 × 力臂"进力矩**，而以"指数"进指数。两处用的是不同的量，不是同一个数换个名字。
/// 3. **CLIM 只在宽体机上注册**。窄体机注册一个 CLIM 符号会让约束生成器以为有 CLIM 限制可加。
/// 4. **救生筏的力矩要用 `Formula.Arm` 从指数反推力臂**，不是直接把指数当力臂用。指数是"力矩除以系数"，量纲与力臂不同。
///
/// Mirrors the baseline. Four families, tabulated above. Four easy mistakes: (1) **fuel belongs only to take-off and landing** — the zero-fuel phase has none, which is the
/// definition, the same convention as `TotalWeight`; (2) **dry operating weight enters torque as gravity × arm** and enters the index as an index — different quantities, not one
/// number renamed; (3) **CLIM is registered on wide-body aircraft only** — a CLIM symbol on a narrow-body tells the constraint generator there is a CLIM limit to add;
/// (4) **the liferaft's torque needs `Formula.Arm`** to recover an arm from its index, since an index is "torque over a coefficient" and has different dimensions.
/// </remarks>
public sealed class Torque
{
    private readonly Fuselage _fuselage;
    private readonly IReadOnlyDictionary<FlightPhase, FuelConstant> _fuel;
    private readonly Formula _formula;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly bool _wideBody;

    private readonly Dictionary<FlightPhase, LinearExpressionSymbol> _longitudinal = new();
    private readonly Dictionary<FlightPhase, LinearExpressionSymbol> _index = new();

    /// <summary>
    /// 构造力矩模型 / Construct the torque model.
    /// </summary>
    /// <param name="fuselage">机身 / The fuselage.</param>
    /// <param name="fuel">按飞行阶段的燃油常量 / The fuel constants by flight phase.</param>
    /// <param name="formula">公式 / The formula.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="wideBody">是否宽体机 / Whether the aircraft is wide-body.</param>
    public Torque(
        Fuselage fuselage,
        IReadOnlyDictionary<FlightPhase, FuelConstant> fuel,
        Formula formula,
        IReadOnlyList<StowagePosition> positions,
        Load load,
        bool wideBody) {
        _fuselage = fuselage;
        _fuel = fuel;
        _formula = formula;
        _positions = positions;
        _load = load;
        _wideBody = wideBody;
    }

    /// <summary>取某阶段的纵向力矩符号 / The longitudinal torque symbol of a phase</summary>
    /// <param name="phase">飞行阶段 / The flight phase.</param>
    /// <returns>纵向力矩符号 / The longitudinal torque symbol.</returns>
    public LinearExpressionSymbol? LongitudinalTorque(FlightPhase phase) =>
        _longitudinal.TryGetValue(phase, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>取某阶段的指数符号 / The index symbol of a phase</summary>
    /// <param name="phase">飞行阶段 / The flight phase.</param>
    /// <returns>指数符号 / The index symbol.</returns>
    public LinearExpressionSymbol? Index(FlightPhase phase) =>
        _index.TryGetValue(phase, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>横向力矩符号 / The lateral torque symbol</summary>
    public LinearExpressionSymbol? LateralTorqueSymbol { get; private set; }

    /// <summary>CLIM 符号（窄体机为 `null`）/ The CLIM symbol, `null` on narrow-body aircraft</summary>
    public LinearExpressionSymbol? ClimSymbol { get; private set; }

    /// <summary>
    /// 干操作重量产生的力矩 / The torque produced by the dry operating weight.
    /// </summary>
    /// <returns>力矩 / The torque.</returns>
    /// <remarks>
    /// 基线写的是 `aircraftModel.gravity(fuselage.dow) * fuselage.balancedArm`：**先取重力，再乘力臂**。顺序不能反——先乘力臂会把"重量×力臂"
    /// 当成重量去求重力（差一个 g），而两者数值相差近十倍，看不出是量纲错，只会得到一个偏大十倍的力矩。
    ///
    /// The baseline writes `aircraftModel.gravity(fuselage.dow) * fuselage.balancedArm`: **gravity first, then the arm**. The order matters — multiplying the arm first feeds
    /// "weight × arm" to the gravity conversion (off by g), and the two differ by nearly an order of magnitude, so it reads as a plausible torque rather than a unit error.
    /// </remarks>
    public double DryOperatingTorque() => TorqueOf(_fuselage.Dow, _fuselage.BalancedArm);

    /// <summary>
    /// 单位重量在该力臂上的力矩 / The torque of unit weight at an arm.
    /// </summary>
    /// <param name="arm">力臂 / The arm.</param>
    /// <returns>每千克的力矩 / The torque per kilogram.</returns>
    public static double TorquePerWeight(double arm) {
        Quantity<Flt64> force = AircraftModel.Gravity(new Quantity<Flt64>(Flt64.One, AircraftModel.WeightUnit));
        Quantity<Flt64> moment = force.Multiply(new Quantity<Flt64>(new Flt64(arm), AircraftModel.LengthUnit));
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> converted = moment.To(AircraftModel.TorqueUnit);
        return converted.IsOk ? converted.Value.Value.ToDouble() : double.NaN;
    }

    /// <summary>
    /// 注册四族符号 / Register the four families.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        _longitudinal.Clear();
        foreach (FlightPhase phase in Enum.GetValues<FlightPhase>()) {
            var polynomial = new MutableLinearPolynomial<Flt64>();
            for (int j = 0; j < _positions.Count; ++j) {
                if (_load.EstimateLongitudinalTorqueSymbol(j) is not { } torque) {
                    return Missing("纵向力矩 / longitudinal torque", j);
                }
                Copy(polynomial, torque.Polynomial);
            }
            if (fuelApplies(phase) && _fuel.TryGetValue(phase, out FuelConstant? phaseFuel)) {
                polynomial.AddConstant(new Flt64(phaseFuel.Index));
            }
            polynomial.AddConstant(new Flt64(DryOperatingTorque()));
            if (_fuselage.Liferaft is { } liferaft) {
                double arm = _formula.Arm(liferaft.Index, liferaft.Weight);
                if (double.IsNaN(arm)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "救生筏重量为零，力臂无定义 / the liferaft weight is zero, so its arm is undefined"));
                }
                polynomial.AddConstant(new Flt64(TorqueOf(liferaft.Weight, arm)));
            }
            var symbol = new LinearExpressionSymbol(
                polynomial,
                name: "longitudinal_torque_" + phase.ToString().ToLowerInvariant());
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _longitudinal[phase] = symbol;
        }

        var lateral = new MutableLinearPolynomial<Flt64>();
        for (int j = 0; j < _positions.Count; ++j) {
            if (_load.LateralTorqueSymbol(j) is not { } torque) {
                return Missing("横向力矩 / lateral torque", j);
            }
            Copy(lateral, torque.Polynomial);
        }
        LateralTorqueSymbol = new LinearExpressionSymbol(lateral, name: "lateral_torque");
        Try addedLateral = model.Add(LateralTorqueSymbol);
        if (!addedLateral.IsOk) {
            return addedLateral;
        }

        if (_wideBody) {
            var clim = new MutableLinearPolynomial<Flt64>();
            for (int j = 0; j < _positions.Count; ++j) {
                if (_load.ClimSymbol(j) is not { } torque) {
                    return Missing("CLIM", j);
                }
                Copy(clim, torque.Polynomial);
            }
            ClimSymbol = new LinearExpressionSymbol(clim, name: "clim");
            Try addedClim = model.Add(ClimSymbol);
            if (!addedClim.IsOk) {
                return addedClim;
            }
        }

        _index.Clear();
        foreach (FlightPhase phase in Enum.GetValues<FlightPhase>()) {
            var polynomial = new MutableLinearPolynomial<Flt64>();
            for (int j = 0; j < _positions.Count; ++j) {
                if (_load.IndexSymbol(j) is not { } index) {
                    return Missing("指数 / index", j);
                }
                Copy(polynomial, index.Polynomial);
            }
            if (fuelApplies(phase) && _fuel.TryGetValue(phase, out FuelConstant? phaseFuel)) {
                polynomial.AddConstant(new Flt64(phaseFuel.Index));
            }
            polynomial.AddConstant(new Flt64(_fuselage.Doi));
            if (_fuselage.Liferaft is { } liferaft) {
                polynomial.AddConstant(new Flt64(liferaft.Index));
            }
            var symbol = new LinearExpressionSymbol(
                polynomial,
                name: "index_" + phase.ToString().ToLowerInvariant());
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _index[phase] = symbol;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static bool fuelApplies(FlightPhase phase) =>
        phase is FlightPhase.TakeOff or FlightPhase.Landing;

    private Try Missing(string family, int positionIndex) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            family + " 符号未注册 / the " + family + " symbol is not registered: " + _positions[positionIndex].Name));

    private static void Copy(MutableLinearPolynomial<Flt64> target, LinearPolynomial<Flt64> source) {
        foreach (LinearMonomial<Flt64> monomial in source.Monomials) {
            target.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }
        target.AddConstant(source.Constant);
    }

    private static double TorqueOf(double weight, double arm) => weight * TorquePerWeight(arm);
}
