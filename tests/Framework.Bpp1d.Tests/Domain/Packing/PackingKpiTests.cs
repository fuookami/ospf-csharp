// 一维装箱指标判据 / Criteria for one-dimensional packing indicators.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.Packing;

/// <summary>
/// **一维装箱指标** / One-dimensional packing indicators.
/// </summary>
/// <remarks>
/// 对应基线的 `PackingResult.kpi(unit)`。判据钉住三件事：
///
/// | 点 | 为什么重要 |
/// | --- | --- |
/// | **长度换算到调用方单位** | 混合箱型的箱长单位可能不同（毫米与米），直接相加是无意义的数 |
/// | **换算失败返回 `Failed`** | 静默跳过会让 KPI 偏小，而偏小的 KPI 看起来像"装箱更紧凑" |
/// | **件数按放置数累加** | 按方案数累加会把一个方案算成一件 |
///
/// Mirrors the baseline's `PackingResult.kpi(unit)`; the three points above are what the criteria pin.
/// </remarks>
public class PackingKpiTests {
    /// <summary>**混合单位必须换算到同一口径** / Mixed units must be converted to one reporting unit.</summary>
    /// <remarks>
    /// 两个箱型：一个 1000 毫米、一个 1 米。**它们其实是同一长度**。
    /// 若实现不换算就直接相加，会得到 `1000 + 1 = 1001`（米口径）——荒谬值。
    /// 正确结果在米口径下是 `1 + 1 = 2`。
    ///
    /// Two bin types: one 1000 mm, one 1 m — **the same length**. Adding raw values would give `1000 + 1 = 1001` in metres, which is nonsense; the correct total in
    /// metres is `1 + 1 = 2`.
    /// </remarks>
    [Fact]
    public void Kpi_ShouldConvertMixedUnitsToOneReportingUnit() {
        var result = new PackingResult<Flt64>(
            Plans: new List<PackingPlan<Flt64>> {
                new(new Bin<Flt64>("bin_0", new BinType<Flt64>(
                    "B1", Millimetres(1000.0), Grams(1000.0), new Flt64(2.0))), new List<Placement<Flt64>>()),
                new(new Bin<Flt64>("bin_1", new BinType<Flt64>(
                    "B2", Metres(1.0), Grams(1000.0), new Flt64(3.0))), new List<Placement<Flt64>>()),
            },
            Remnants: new List<Remnant<Flt64>>(),
            Materials: new Dictionary<string, ulong>(),
            Utilization: new Flt64(0.5));

        Result<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>> result2 = result.Kpi(SIBaseUnits.Meter);

        result2.IsOk.Should().BeTrue(result2.ToString());
        PackingKpi<Flt64> kpi = ((Ok<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>>)result2).Value;
        kpi.ConsumedLength.Value.ToDouble().Should().BeApproximately(
            2.0, 1e-9, "1000 mm and 1 m are each one metre, so the total is 2 m; adding raw values would give 1001");
        kpi.ConsumedLength.Unit.Should().Be(SIBaseUnits.Meter, "the result must be reported in the requested unit");
        kpi.BinCount.Should().Be(2UL);
        kpi.Cost.ToDouble().Should().BeApproximately(5.0, 1e-9, "2 + 3");
    }

    /// <summary>**件数按放置数累加，不按方案数** / Item count sums placements, not plans.</summary>
    [Fact]
    public void Kpi_ShouldCountPlacementsNotPlans() {
        var plan = new PackingPlan<Flt64>(
            new Bin<Flt64>("bin_0", new BinType<Flt64>(
                "B1", Metres(10.0), Grams(1000.0), new Flt64(1.0))),
            new List<Placement<Flt64>> {
                new("I1", Metres(0.0), Metres(2.0)),
                new("I1", Metres(2.0), Metres(2.0)),
                new("I2", Metres(4.0), Metres(3.0)),
            });
        var result = new PackingResult<Flt64>(
            Plans: new List<PackingPlan<Flt64>> { plan },
            Remnants: new List<Remnant<Flt64>> { new("bin_0", Metres(7.0), Metres(3.0)) },
            Materials: new Dictionary<string, ulong>(),
            Utilization: new Flt64(0.7));

        Result<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>> computed = result.Kpi(SIBaseUnits.Meter);

        computed.IsOk.Should().BeTrue(computed.ToString());
        PackingKpi<Flt64> kpi = ((Ok<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>>)computed).Value;
        kpi.ItemCount.Should().Be(3UL, "there are three placements; counting plans would give 1");
        kpi.BinCount.Should().Be(1UL);
        kpi.RemnantLength.Value.ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>**量纲不是长度时必须失败，不得静默跳过** / A non-length dimension must fail rather than be skipped.</summary>
    /// <remarks>
    /// 把箱长写成克：`To(Meter)` 失败。若实现静默跳过那一项，`ConsumedLength` 会**偏小**，
    /// 而偏小的消耗长度看起来像"装箱更紧凑"——**一个会美化结果的静默错误**。
    ///
    /// A gram-valued bin length makes `To(Meter)` fail. Silently skipping it would make `ConsumedLength` **smaller**, and a smaller consumed length looks like tighter
    /// packing — **a silent error that flatters the result**.
    /// </remarks>
    [Fact]
    public void Kpi_ShouldFailOnANonLengthDimension() {
        var result = new PackingResult<Flt64>(
            Plans: new List<PackingPlan<Flt64>> {
                new(new Bin<Flt64>("bin_0", new BinType<Flt64>(
                    "B1", Grams(1000.0), Grams(1000.0), new Flt64(1.0))), new List<Placement<Flt64>>()),
            },
            Remnants: new List<Remnant<Flt64>>(),
            Materials: new Dictionary<string, ulong>(),
            Utilization: new Flt64(0.5));

        Result<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>> computed = result.Kpi(SIBaseUnits.Meter);

        computed.IsFailed.Should().BeTrue("a gram-valued length is not a length, so the KPI must refuse to report");
    }

    /// <summary>**空结果的指标必须全为零，不抛异常** / An empty result yields zero indicators without throwing.</summary>
    [Fact]
    public void Kpi_ShouldHandleAnEmptyResult() {
        var result = new PackingResult<Flt64>(
            Plans: new List<PackingPlan<Flt64>>(),
            Remnants: new List<Remnant<Flt64>>(),
            Materials: new Dictionary<string, ulong>(),
            Utilization: new Flt64(0.0));

        Result<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>> computed = result.Kpi(SIBaseUnits.Meter);

        computed.IsOk.Should().BeTrue(computed.ToString());
        PackingKpi<Flt64> kpi = ((Ok<PackingKpi<Flt64>, ErrorCode, Error<ErrorCode>>)computed).Value;
        kpi.BinCount.Should().Be(0UL);
        kpi.ItemCount.Should().Be(0UL);
        kpi.ConsumedLength.Value.ToDouble().Should().Be(0.0);
        kpi.Cost.ToDouble().Should().Be(0.0);
    }

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造米量 / Build a metre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) =>
        new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
