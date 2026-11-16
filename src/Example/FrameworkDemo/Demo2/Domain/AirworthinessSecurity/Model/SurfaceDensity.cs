// 表面密度：每个舱位的单位面积重量 / Surface density: weight per unit area at each position.

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
/// 表面密度限制区域 / A zone with a surface density limit.
/// </summary>
/// <param name="Name">区域名称 / The zone name.</param>
/// <param name="FrontArm">区域前力臂 / The zone's front arm.</param>
/// <param name="BackArm">区域后力臂 / The zone's back arm.</param>
/// <param name="MaxSurfaceDensity">最大允许表面密度 / The maximum allowed surface density.</param>
public sealed record SurfaceDensityLimitZone(
    string Name,
    double FrontArm,
    double BackArm,
    double MaxSurfaceDensity);

/// <summary>
/// 表面密度模型 / The surface density model.
/// </summary>
/// <remarks>
/// 逐条对齐：每个落在限区内的舱位产一个
/// `surface_density_{舱位}` 符号，值 = `estimateLoadWeight[j] / 舱位面积`。
///
/// 三处容易做错：
///
/// 1. **面积来源**。用 `PositionShape.Area`（申报面积优先于长×宽），不是现算长×宽。货站可用面积与"长×宽"经常差很多，
///    而表面密度是"按面积算重量"——面积错一点，重量就错一片。
/// 2. **只注册落在限区内的舱位**。`model.add` 前有 `if (limitsZones.any { ... withIntersectionWith ... })`。少了这个判断，
///    不在任何限区里的舱位也会得到一个"表面密度"符号——那个数字没有任何规格可对照，白白进了模型。
/// 3. **"有交集"不是"完整落在区间内"**。用 `Between` 代替 `WithIntersectionWith` 会漏掉所有压在区域边界上的舱位，而那恰恰是最该查的一批。
///
/// every position inside a limit zone gets a `surface_density_{position}` symbol equal to `estimateLoadWeight[j] / position area`. Three easy mistakes:
/// (1) **where the area comes from** — `PositionShape.Area` with the declared area preferred over length × width, since usable deck area often differs a lot and surface density
/// is weight per area; (2) **only positions inside a zone are registered** — without that check, positions outside every zone get a symbol with no specification to compare
/// against, cluttering the model for nothing; (3) **"intersects" is not "lies entirely within"** — using `Between` drops every position straddling a zone boundary, which is
/// exactly the set that most needs checking.
/// </remarks>
public sealed class SurfaceDensity
{
    private readonly IReadOnlyList<Position> _positions;
    private readonly Load _load;

    private readonly Dictionary<int, LinearExpressionSymbol> _symbols = new();

    /// <summary>
    /// 构造表面密度模型 / Construct the surface density model.
    /// </summary>
    /// <param name="limitZones">限制区域 / The limit zones.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public SurfaceDensity(
        IReadOnlyList<SurfaceDensityLimitZone> limitZones,
        IReadOnlyList<Position> positions,
        Load load) {
        ArgumentNullException.ThrowIfNull(limitZones);
        ArgumentNullException.ThrowIfNull(positions);
        LimitZones = limitZones;
        _positions = positions;
        _load = load;
    }

    /// <summary>限制区域 / The limit zones</summary>
    public IReadOnlyList<SurfaceDensityLimitZone> LimitZones { get; }

    /// <summary>
    /// 取某舱位的表面密度符号 / The surface density symbol of a position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>表面密度符号；不在任何限区内时为 `null` / The symbol, or `null` when outside every zone.</returns>
    public LinearExpressionSymbol? SurfaceDensitySymbol(int positionIndex) =>
        _symbols.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 某舱位是否落在任一限区内 / Whether a position falls inside any limit zone.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>是否在限区内 / Whether it is inside.</returns>
    public bool InAnyZone(int positionIndex) {
        PositionCoordinate coordinate = _positions[positionIndex].Coordinate;
        foreach (SurfaceDensityLimitZone zone in LimitZones) {
            if (coordinate.WithIntersectionWith(zone.FrontArm, zone.BackArm)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 注册表面密度符号 / Register the surface density symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _symbols.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            if (!InAnyZone(j)) {
                continue;
            }
            if (_load.EstimateLoadWeightSymbol(j) is not { } loadWeight) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "推荐装载重量符号未注册 / the recommended load weight symbol is not registered"));
            }
            double area = _positions[j].Shape.Area;
            if (area <= 0.0) {
                // 面积为零会让系数变成 Infinity 并污染整个模型。舱位面积为零是**数据错误**，当场报出来。
                // A zero area makes the coefficient Infinity and poisons the model. A zero-area position is a **data error** and is reported here.
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "舱位面积必须为正 / the position area must be positive: " + _positions[j].SpaceName));
            }
            var coefficient = new Flt64(1.0 / area);
            var polynomial = new MutableLinearPolynomial<Flt64>();
            foreach (LinearMonomial<Flt64> monomial in loadWeight.Polynomial.Monomials) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(
                    monomial.Coefficient.Times(coefficient),
                    monomial.Symbol));
            }
            // ponytail: 本仓所有算例里 `estimateLoadWeight` 的常数项都是 0，所以这一行**当前不可观测**——
            // ponytail: 把它去掉，变异也检不出来。保留是因为"按系数缩放整个多项式"才是对的；常数项若哪天真非零，
            // ponytail: 漏掉它的后果是表面密度少算一截，而没有任何判据会报警。等出现非零常数的算例时补一条判据。
            // ponytail: Every example configuration in this repo gives `estimateLoadWeight` a zero constant (measured), so this line is **currently unobservable** — removing
            // ponytail: it survives mutation testing. It stays because scaling the whole polynomial is the correct thing; if the constant ever became non-zero, dropping it would
            // ponytail: understate the surface density with no criterion to warn. Add a criterion once a configuration has a non-zero constant.
            polynomial.AddConstant(new Flt64(loadWeight.Polynomial.Constant.ToDouble()).Times(coefficient));
            var symbol = new LinearExpressionSymbol(
                polynomial,
                name: "surface_density_" + _positions[j].SpaceName);
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _symbols[j] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
