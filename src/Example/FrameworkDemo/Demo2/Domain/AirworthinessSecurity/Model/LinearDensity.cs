// 线性密度：单位长度重量，以及按力臂切出的限制线 / Linear density: weight per unit length, plus the limit lines cut at arms.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 线性密度限制区域 / A zone with a linear density limit.
/// </summary>
/// <param name="Name">区域名称 / The zone name.</param>
/// <param name="Locations">此区域包含的甲板位置 / The deck locations this zone covers.</param>
/// <param name="FrontArm">区域前力臂 / The zone's front arm.</param>
/// <param name="BackArm">区域后力臂 / The zone's back arm.</param>
/// <param name="MaxLinearDensity">最大允许线性密度 / The maximum allowed linear density.</param>
public sealed record LinearDensityLimitZone(
    string Name,
    IReadOnlySet<DeckLocation> Locations,
    double FrontArm,
    double BackArm,
    double MaxLinearDensity);

/// <summary>
/// 线性密度限制线 / A linear density limit line.
/// </summary>
/// <param name="Zone">所属限制区域 / The parent limit zone.</param>
/// <param name="Arm">此线的力臂 / The arm of this line.</param>
/// <param name="PositionIndices">此线覆盖的舱位下标 / The position indices this line covers.</param>
public sealed record LinearDensityLimitLine(
    LinearDensityLimitZone Zone,
    double Arm,
    IReadOnlyList<int> PositionIndices);

/// <summary>
/// 线性密度模型 / The linear density model.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/airworthiness_security/model/LinearDensity.kt`。两件事：
///
/// 1. **符号**：每个舱位一个 `linear_density_{舱位}`，值 = `estimateLoadWeight[j] / 舱位长度`。系数用**长度**而不是面积——
///    线性密度问的是"沿机身方向单位长度上压了多少重量"，与宽度无关。
/// 2. **限制线**：沿机身按力臂切面，每个切面覆盖"压在这一刀上"的舱位集合。舱位往往跨好几个切面，一条线就是一次横截面检查。
///
/// 与 `SurfaceDensity` 对照读最清楚：表面密度**只给落在限区内的舱位**建符号（没规格就没必要算），线性密度**给所有舱位**建符号
/// （限制线是后面按区域切的，符号本身不依赖区域）。两处口径不同是基线的选择，照抄。
///
/// Mirrors the baseline. Two parts: (1) **symbols** — one `linear_density_{position}` per position equal to `estimateLoadWeight[j] / position length`
/// (length, not area: linear density is weight per unit length along the fuselage, independent of width); (2) **limit lines** — cross-sections cut along the fuselage at arms,
/// each covering the set of positions that sit on that cut. A position often spans several cuts, and each line is one cross-section check.
///
/// Reading it against `SurfaceDensity` makes the difference clear: surface density builds symbols **only for positions inside a zone** (no specification, nothing to compute),
/// linear density builds them for **every** position (the limit lines are cut per zone afterwards; the symbols do not depend on zones). Both are the baseline's choices and are
/// copied as-is.
/// </remarks>
public sealed class LinearDensity
{
    private readonly IReadOnlyList<Position> _positions;
    private readonly Load _load;

    private readonly Dictionary<int, LinearExpressionSymbol> _symbols = new();

    /// <summary>
    /// 构造线性密度模型 / Construct the linear density model.
    /// </summary>
    /// <param name="limitZones">限制区域 / The limit zones.</param>
    /// <param name="limitLines">限制线 / The limit lines.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public LinearDensity(
        IReadOnlyList<LinearDensityLimitZone> limitZones,
        IReadOnlyList<LinearDensityLimitLine> limitLines,
        IReadOnlyList<Position> positions,
        Load load) {
        ArgumentNullException.ThrowIfNull(limitZones);
        ArgumentNullException.ThrowIfNull(limitLines);
        ArgumentNullException.ThrowIfNull(positions);
        LimitZones = limitZones;
        LimitLines = limitLines;
        _positions = positions;
        _load = load;
    }

    /// <summary>限制区域 / The limit zones</summary>
    public IReadOnlyList<LinearDensityLimitZone> LimitZones { get; }

    /// <summary>限制线 / The limit lines</summary>
    public IReadOnlyList<LinearDensityLimitLine> LimitLines { get; }

    /// <summary>
    /// 按区域与舱位切出限制线 / Cut the limit lines from the zones and positions.
    /// </summary>
    /// <param name="limitZones">限制区域 / The limit zones.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>限制线 / The limit lines.</returns>
    /// <remarks>
    /// 四步，照抄基线：
    ///
    /// 1. 区域覆盖的舱位 = 甲板位置在区域内 **且** 坐标与区域的力臂区间有交集；
    /// 2. 候选力臂 = 区域两端 + 每个上述舱位的前后臂，去重（**保留首次出现顺序**）；
    /// 3. 只留落在区域力臂区间内的候选——区域外的切面没有意义；
    /// 4. 每个力臂取"压在这一刀上"的舱位（`On`），**空的整条线丢掉**。
    ///
    /// 第 4 步丢掉空线不是省事：一条没有舱位的横截面没有任何重量可检查，留着只会让约束生成器为它加一行恒等于 0 ≤ 上限的废约束。
    ///
    /// Four steps as in the baseline: (1) the zone's positions are those whose deck location is in the zone **and** whose coordinate intersects the zone's arm range;
    /// (2) candidate arms are the zone's two ends plus each such position's front and back arms, de-duplicated (**first occurrence order kept**); (3) only candidates inside the
    /// zone's arm range survive — a cut outside the zone means nothing; (4) each arm takes the positions lying **on** it, and **whole empty lines are dropped**. Dropping empty
    /// lines is not laziness: a cross-section with no position has no weight to check, and keeping it only makes the constraint generator emit a vacuous `0 ≤ limit` row.
    /// </remarks>
    public static IReadOnlyList<LinearDensityLimitLine> BuildLimitLines(
        IReadOnlyList<LinearDensityLimitZone> limitZones,
        IReadOnlyList<Position> positions) {
        ArgumentNullException.ThrowIfNull(limitZones);
        ArgumentNullException.ThrowIfNull(positions);
        var lines = new List<LinearDensityLimitLine>();
        foreach (LinearDensityLimitZone zone in limitZones) {
            var zonePositions = new List<int>();
            for (int j = 0; j < positions.Count; ++j) {
                if (!zone.Locations.Contains(positions[j].Location.Location)) {
                    continue;
                }
                if (!positions[j].Coordinate.WithIntersectionWith(zone.FrontArm, zone.BackArm)) {
                    continue;
                }
                zonePositions.Add(j);
            }

            var candidates = new List<double> { zone.FrontArm, zone.BackArm };
            foreach (int j in zonePositions) {
                if (!candidates.Contains(positions[j].Coordinate.FrontArm)) {
                    candidates.Add(positions[j].Coordinate.FrontArm);
                }
                if (!candidates.Contains(positions[j].Coordinate.BackArm)) {
                    candidates.Add(positions[j].Coordinate.BackArm);
                }
            }
            double lower = System.Math.Min(zone.FrontArm, zone.BackArm);
            double upper = System.Math.Max(zone.FrontArm, zone.BackArm);

            foreach (double arm in candidates) {
                if (arm < lower || arm > upper) {
                    continue;
                }
                var onArm = new List<int>();
                foreach (int j in zonePositions) {
                    if (positions[j].Coordinate.On(arm)) {
                        onArm.Add(j);
                    }
                }
                if (onArm.Count == 0) {
                    continue;
                }
                lines.Add(new LinearDensityLimitLine(zone, arm, onArm));
            }
        }
        return lines;
    }

    /// <summary>
    /// 取某舱位的线性密度符号 / The linear density symbol of a position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>线性密度符号 / The linear density symbol.</returns>
    public LinearExpressionSymbol? LinearDensitySymbol(int positionIndex) =>
        _symbols.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 注册线性密度符号 / Register the linear density symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _symbols.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            if (_load.EstimateLoadWeightSymbol(j) is not { } loadWeight) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "推荐装载重量符号未注册 / the recommended load weight symbol is not registered"));
            }
            double length = _positions[j].Shape.Length;
            if (length <= 0.0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "舱位长度必须为正 / the position length must be positive: " + _positions[j].SpaceName));
            }
            var coefficient = new Flt64(1.0 / length);
            var polynomial = new MutableLinearPolynomial<Flt64>();
            foreach (LinearMonomial<Flt64> monomial in loadWeight.Polynomial.Monomials) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(
                    monomial.Coefficient.Times(coefficient),
                    monomial.Symbol));
            }
            polynomial.AddConstant(new Flt64(loadWeight.Polynomial.Constant.ToDouble()).Times(coefficient));
            var symbol = new LinearExpressionSymbol(
                polynomial,
                name: "linear_density_" + _positions[j].SpaceName);
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _symbols[j] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
