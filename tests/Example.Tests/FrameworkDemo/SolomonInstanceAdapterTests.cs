// Solomon 实例装配测试 / Tests for the Solomon instance adapter.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Solomon 实例装配 / The Solomon instance adapter.
/// </summary>
public sealed class SolomonInstanceAdapterTests {
    /// <summary>**装配出客户、车辆与两条不同 id 的仓库** / The adapter produces customers, a vehicle type, and two distinctly identified depots.</summary>
    [Fact]
    public void Create_ShouldAssembleCustomersAndVehicleType() {
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created = Create(SolomonDataReaderTestsFixture.Valid());

        created.IsOk.Should().BeTrue(created.ToString());
        VrptwInstance<Flt64> instance = created.Value;
        instance.Name.Should().Be("TINY");
        instance.Customers.Should().HaveCount(3);
        instance.Customers.Select(customer => customer.Id.Value).Should().Equal("1", "2", "3");
        instance.VehicleTypes.Should().ContainSingle();
        instance.VehicleTypes[0].Amount.Should().Be(2);
        instance.VehicleTypes[0].Capacity.Value.ToDouble().Should().Be(3.0);
        // **两条仓库的节点 id 必须不同**：共用同一个节点会让"回到仓库"与"离开仓库"变成同一条弧，
        // 而定价里这两条弧的容量与时间语义不同。
        // **The two depots must have different node ids**: sharing one node collapses "return to depot" and "leave depot" into the same arc, and pricing
        // treats those arcs with different capacity and timing semantics.
        instance.StartDepot.Node.Id.Value.Should().Be("depot-start");
        instance.EndDepot.Node.Id.Value.Should().Be("depot-end");
        instance.StartDepot.Node.Id.Should().NotBe(instance.EndDepot.Node.Id);
    }

    /// <summary>**原始时间按业务时间轴映射成绝对时刻与时长** / Raw times map onto the business timeline as absolute instants and durations.</summary>
    /// <remarks>
    /// 原始时间是"秒数偏移"，业务时间轴才给它们绝对意义。**把秒数当绝对时刻用不会有任何报错**，
    /// 只会让所有时间窗落在 1970 年附近。
    ///
    /// Raw times are second offsets; only the business timeline gives them absolute meaning. **Using the seconds as absolute instants throws nothing**,
    /// it only puts every time window somewhere around 1970.
    /// </remarks>
    [Fact]
    public void Create_ShouldMapRawTimesOntoTheBusinessTimeline() {
        SolomonData data = SolomonDataReaderTestsFixture.WithLastRow(
            "3 3 0 1 10 20 12");

        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created = Create(data);

        created.IsOk.Should().BeTrue(created.ToString());
        Customer<Flt64> last = created.Value.Customers[^1];
        last.TimeWindow.ReadyTime.Should().Be(
            SolomonDataReaderTestsFixture.WindowStart.AddSeconds(10),
            "原始 readyTime 是相对业务起点的秒数 / the raw readyTime is an offset from the business start");
        last.TimeWindow.DueTime.Should().Be(
            SolomonDataReaderTestsFixture.WindowStart.AddSeconds(20));
        last.ServiceTime.Should().Be(TimeSpan.FromSeconds(12));
    }

    /// <summary>**超出业务时间轴的时间窗被拒** / A time window outside the business timeline is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectTimeWindowOutsideTheTimeline() {
        SolomonData data = SolomonDataReaderTestsFixture.WithLastRow("3 3 0 1 0 3601 0");

        Create(data).Should()
            .BeOfType<Failed<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**FltX 数值路径可用** / The FltX numeric path works.</summary>
    /// <remarks>
    /// 转换器是**可替换的实现**，两种数值类型各走一遍才能证明装配不依赖 `Flt64` 的某个偶然行为。
    ///
    /// The converter is **a replaceable implementation**; running both numeric types is what shows the adapter does not lean on an accidental `Flt64`
    /// behaviour.
    /// </remarks>
    [Fact]
    public void Create_ShouldSupportBothNumericTypes() {
        Result<VrptwInstance<FltX>, ErrorCode, Error<ErrorCode>> created = Create(
            SolomonDataReaderTestsFixture.Valid(),
            new SolomonFltXValueConverter(),
            new FltX(10m));

        created.IsOk.Should().BeTrue(created.ToString());
        created.Value.Customers.Should().HaveCount(3);
        created.Value.Customers[0].Demand.Value.ToDecimal().Should().Be(1m);
    }

    /// <summary>**非有限值在装配边界被拒** / Non-finite values are rejected at the adapter boundary.</summary>
    /// <remarks>
    /// `FltX` 底层是 `decimal`、天然没有 `NaN`，所以必须用一个**真的会返回非有限值**的转换器才能覆盖这条路径——
    /// 用真实转换器写这条测试只会永远通过，**而 `NaN` 一旦流进时间窗，失败点会漂到某个"时间窗倒置"校验上**。
    ///
    /// `FltX` sits on `decimal` and has no `NaN`, so covering this path needs a converter that **really returns a non-finite value** — writing this test
    /// with the real converter would pass forever, and **once `NaN` flows into a time window the failure surfaces at some "inverted time window" check**.
    /// </remarks>
    [Fact]
    public void Create_ShouldRejectNonFiniteValues() {
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created = Create(
            SolomonDataReaderTestsFixture.Valid(),
            new NonFiniteConverter(),
            new Flt64(10.0));

        created.Should()
            .BeOfType<Failed<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>>)created).Error.Message
            .Should().Contain("finite",
                "错误必须指名「数值必须有限」 / the error must name the finiteness rule");
    }

    private static Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        SolomonData data) =>
        Create(data, new SolomonFlt64ValueConverter(), new Flt64(10.0));

    private static Result<VrptwInstance<V>, ErrorCode, Error<ErrorCode>> Create<V>(
        SolomonData data,
        ISolomonValueConverter<V> converter,
        V fixedCost) where V : struct, IRealNumber<V> {
        Result<VrptwUnits, ErrorCode, Error<ErrorCode>> units = VrptwUnits.Invoke(
            SIBaseUnits.Meter,
            SIBaseUnits.Kilogram,
            NoneUnit.Instance);
        units.IsOk.Should().BeTrue(units.ToString());
        return new SolomonInstanceAdapter<V>(converter).Create(
            data: data,
            units: units.Value,
            fixedVehicleCost: new Quantity<V>(fixedCost, NoneUnit.Instance),
            schedulingWindow: Window<V>());
    }

    private static TimeWindow<V> Window<V>() where V : struct, IRealNumber<V> =>
        new(
            Window: new TimeRange(
                SolomonDataReaderTestsFixture.WindowStart,
                SolomonDataReaderTestsFixture.WindowStart.AddHours(1)),
            Continues: true,
            Interval: TimeSpan.FromSeconds(1));

    /// <summary>总是返回非有限值的转换器 / A converter that always yields a non-finite value.</summary>
    private sealed class NonFiniteConverter : ISolomonValueConverter<Flt64> {
        public Flt64 Convert(FltX value) => new(double.NaN);
    }
}
