// Solomon 实例读取器测试 / Tests for the Solomon instance reader.

using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Solomon 实例读取器 / The Solomon instance reader.
/// </summary>
public sealed class SolomonDataReaderTests {
    /// <summary>**标准实例解析出车辆、depot 与客户** / A standard instance parses into vehicles, a depot, and customers.</summary>
    [Fact]
    public void Read_ShouldParseAStandardInstance() {
        Result<SolomonData, ErrorCode, Error<ErrorCode>> read = SolomonDataReader.Read(Instance);

        read.IsOk.Should().BeTrue(read.ToString());
        SolomonData data = read.Value;
        data.Name.Should().Be("C101");
        data.Vehicle.Amount.Should().Be(25);
        data.Depot.Id.Should().Be("0");
        data.Depot.ReadyTime.ToDecimal().Should().Be(0m);
        data.Depot.DueTime.ToDecimal().Should().Be(1236m);
        data.Customers.Should().HaveCount(2);
        data.Customers.Select(node => node.Id).Should().Equal("1", "2");
    }

    /// <summary>
    /// **十进制字面量必须精确，不能经过 `double`** / Decimal literals must stay exact and never pass through `double`.
    /// </summary>
    /// <remarks>
    /// **夹具必须带超出 `double` 精度的位数**。第一版用的是 `12.3` / `45.6` / `0.7`，而这三个值经 `double`
    /// 往返**恰好还原**，于是"改成走 `double`"的变异存活了——**夹具太窄，断言根本没碰那条路径**。
    /// 这里改成 17 位有效数字：实测 `12.3456789012345678` 经 `double` 往返得到 `12.3456789012346`。
    ///
    /// **The fixture must carry digits beyond `double` precision.** The first version used `12.3` / `45.6` / `0.7`, and all three round-trip through
    /// `double` **exactly**, so the "go through `double` instead" mutation survived — **the fixture was too narrow and the assertion never touched that path**.
    /// This version uses 17 significant digits: measured, `12.3456789012345678` round-trips through `double` as `12.3456789012346`.
    /// </remarks>
    [Fact]
    public void Read_ShouldKeepDecimalLiteralsExact() {
        Result<SolomonData, ErrorCode, Error<ErrorCode>> read =
            SolomonDataReader.Read(HighPrecisionInstance);

        read.IsOk.Should().BeTrue(read.ToString());
        read.Value.Customers[0].X.ToDecimal().Should().Be(12.3456789012345678m);
        read.Value.Customers[0].Y.ToDecimal().Should().Be(45.6m);
        read.Value.Customers[0].ServiceTime.ToDecimal().Should().Be(0.7m);
    }

    /// <summary>**缺 VEHICLE 段被拒** / A missing VEHICLE section is rejected.</summary>
    [Fact]
    public void Read_ShouldRejectMissingVehicleSection() =>
        SolomonDataReader
            .Read("C101\nCUSTOMER\n1 0 0 0 0 1 0\n0 0 0 0 0 1 0")
            .Should().BeOfType<Failed<SolomonData, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**客户行列数不对被拒，且错误带行号** / A wrong customer column count is rejected and the error names the line.</summary>
    /// <remarks>
    /// 不带行号时，一份 100 行实例里的"第 7 列非法"无法定位，**而调用方拿到的只是一句"Solomon 数据无效"**。
    ///
    /// Without a line number, "column 7 is invalid" inside a 100-line instance is unlocatable, and **all the caller gets is "invalid Solomon data"**.
    /// </remarks>
    [Fact]
    public void Read_ShouldRejectWrongColumnCountWithLineNumber() {
        Result<SolomonData, ErrorCode, Error<ErrorCode>> read =
            SolomonDataReader.Read(InstanceWithBadRow);

        read.Should().BeOfType<Failed<SolomonData, ErrorCode, Error<ErrorCode>>>();
        ((Failed<SolomonData, ErrorCode, Error<ErrorCode>>)read).Error.Message
            .Should().Contain("第 6 行",
                "错误必须指向出错的那一行 / the error must point at the offending line");
    }

    /// <summary>**重复节点 ID 被拒** / Duplicate node IDs are rejected.</summary>
    [Fact]
    public void Read_ShouldRejectDuplicateNodeIds() =>
        SolomonDataReader
            .Read(Instance.Replace(
                "1 12.3 45.6 10 0 1236 0.7",
                "1 12.3 45.6 10 0 1236 0.7\n1 1 2 3 4 5 6",
                System.StringComparison.Ordinal))
            .Should().BeOfType<Failed<SolomonData, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**缺 depot 被拒** / A missing depot is rejected.</summary>
    [Fact]
    public void Read_ShouldRejectMissingDepot() =>
        SolomonDataReader
            .Read(Instance.Replace(
                "0 40 50 0 0 1236 0",
                "9 40 50 0 0 1236 0",
                System.StringComparison.Ordinal))
            .Should().BeOfType<Failed<SolomonData, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**时间窗倒置被拒** / An inverted time window is rejected.</summary>
    [Fact]
    public void Read_ShouldRejectInvertedTimeWindow() =>
        SolomonDataReader
            .Read("C101\nVEHICLE\n25 200\nCUSTOMER\n0 40 50 0 0 1236 0\n1 12.3 45.6 10 100 50 0.7")
            .Should().BeOfType<Failed<SolomonData, ErrorCode, Error<ErrorCode>>>();

    private const string Instance = """
        C101
        VEHICLE
        NUMBER CAPACITY
        25 200
        CUSTOMER
        CUST NO. XCOORD. YCOORD. DEMAND READY TIME DUE DATE SERVICE TIME
        0 40 50 0 0 1236 0
        1 12.3 45.6 10 0 1236 0.7
        2 20 30 7 0 1000 0.5
        """;

    private const string InstanceWithBadRow = """
        C101
        VEHICLE
        25 200
        CUSTOMER
        0 40 50 0 0 1236 0
        1 12.3 45.6 10 0 1236
        """;

    private const string HighPrecisionInstance = """
        C101
        VEHICLE
        25 200
        CUSTOMER
        0 40 50 0 0 1236 0
        1 12.3456789012345678 45.6 10 0 1236 0.7
        """;
}
