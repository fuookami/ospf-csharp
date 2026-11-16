// 表面密度上限约束 / The upper bound on surface density.

using System;
using System.Collections.Generic;
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
/// 表面密度上限 / The upper bound on surface density.
/// </summary>
/// <remarks>
/// 逐条对齐：对**每个限区** × **每个落在该限区且可用的舱位**加一行
/// `surface_density_{舱位} ≤ 限区的最大表面密度`，名为 `{name}_{限区名}_{舱位名}`。
///
/// 三处细节：
///
/// 1. **不可用舱位跳过**。基线判 `position.status.unavailable`。给一个已经不可用的舱位加密度上限毫无意义，而且它的符号根本不该有价值。
/// 2. **区域逐个来**，不是"取最严的限区"。两个区域重叠时，重叠处的舱位必须**同时**满足两条上限——取最严可以少一行约束，但那要求先算出
///    哪个更严，而"更严"是随舱位面积变化的。基线不这么做。
/// 3. **名字三段拼接**。`surface_density_limit_Z1_P1` 这种名字让人能从约束反查到区域与舱位；只用 `name` 的话所有行同名，
///    求解器报"第 3 条约束不可行"时无法定位。
///
/// for **each zone** × **each available position intersecting that zone**, add `surface_density_{position} ≤ zone maximum`, named
/// `{name}_{zone}_{position}`. Three details: (1) **unavailable positions are skipped** — capping the density of a position that cannot take cargo is meaningless and its symbol should
/// carry no value; (2) **zones are handled one by one**, not by picking the tightest — where two zones overlap the positions must satisfy **both** caps, and "which is tighter" varies
/// with each position's area; (3) **the name is three segments**, so a solver's "constraint 3 is infeasible" can be traced back to a zone and a position.
/// </remarks>
internal sealed class SurfaceDensityLimit
{
    private readonly SurfaceDensity _surfaceDensity;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly string _name;

    /// <summary>
    /// 构造表面密度上限 / Construct the upper bound on surface density.
    /// </summary>
    /// <param name="surfaceDensity">表面密度模型 / The surface density model.</param>
    /// <param name="positions">舱位（含可用性）/ The positions, carrying availability.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public SurfaceDensityLimit(
        SurfaceDensity surfaceDensity,
        IReadOnlyList<StowagePosition> positions,
        string name = "surface_density_limit") {
        _surfaceDensity = surfaceDensity;
        _positions = positions;
        _name = name;
    }

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        var added = new List<string>();
        foreach (SurfaceDensityLimitZone zone in _surfaceDensity.LimitZones) {
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                if (!position.Status.Available) {
                    continue;
                }
                if (!position.AircraftPosition.Coordinate.WithIntersectionWith(zone.FrontArm, zone.BackArm)) {
                    continue;
                }
                if (_surfaceDensity.SurfaceDensitySymbol(j) is not { } density) {
                    // 落在限区内却没有符号，说明 `SurfaceDensity.Register` 没跑或跑失败。静默跳过会让整条密度约束凭空消失。
                    // Inside a zone but without a symbol means `SurfaceDensity.Register` never ran or failed. Skipping silently makes the whole density constraint vanish.
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "限区内舱位缺少表面密度符号 / a position inside a limit zone has no surface density symbol: " + position.Name));
                }

                var row = new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(density.Polynomial.Monomials, density.Polynomial.Constant),
                    new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(zone.MaxSurfaceDensity)),
                    Comparison.LE,
                    _name + "_" + zone.Name + "_" + position.Name);
                Try result = model.AddConstraint(row, group: null, name: _name + "_" + zone.Name + "_" + position.Name);
                if (!result.IsOk) {
                    return result;
                }
                added.Add(_name + "_" + zone.Name + "_" + position.Name);
            }
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();
}
