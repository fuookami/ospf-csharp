// Solomon 测试夹具 / Shared fixture for the Solomon tests.

using System;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Solomon 测试夹具 / Shared fixture for the Solomon tests.
/// </summary>
internal static class SolomonDataReaderTestsFixture {
    /// <summary>业务时间轴起点 / The business timeline start.</summary>
    internal static readonly DateTimeOffset WindowStart =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>解析一份合法实例 / Parse a valid instance.</summary>
    /// <returns>解析出的数据 / The parsed data.</returns>
    /// <exception cref="InvalidOperationException">夹具本身不合法 / The fixture itself is invalid.</exception>
    internal static SolomonData Valid() {
        Result<SolomonData, ErrorCode, Error<ErrorCode>> read =
            SolomonDataReader.Read(ValidText);
        if (read is not Ok<SolomonData, ErrorCode, Error<ErrorCode>> ok) {
            throw new InvalidOperationException(
                "夹具必须合法 / the fixture must be valid: "
                + ((Failed<SolomonData, ErrorCode, Error<ErrorCode>>)read).Error.Message);
        }
        return ok.Value;
    }

    /// <summary>把最后一行换成给定文本再解析 / Replace the last row, then parse.</summary>
    /// <param name="row">替换文本 / The replacement row.</param>
    /// <returns>解析出的数据 / The parsed data.</returns>
    internal static SolomonData WithLastRow(string row) {
        string text = ValidText.Replace(
            "3 3 0 1 0 100 0",
            row,
            StringComparison.Ordinal);
        Result<SolomonData, ErrorCode, Error<ErrorCode>> read = SolomonDataReader.Read(text);
        if (read is not Ok<SolomonData, ErrorCode, Error<ErrorCode>> ok) {
            throw new InvalidOperationException(
                "夹具必须合法 / the fixture must be valid: "
                + ((Failed<SolomonData, ErrorCode, Error<ErrorCode>>)read).Error.Message);
        }
        return ok.Value;
    }

    /// <summary>一份合法的 Solomon 文本 / A valid Solomon text.</summary>
    internal const string ValidText = """
        TINY
        VEHICLE
        NUMBER CAPACITY
        2 3
        CUSTOMER
        CUST NO. XCOORD. YCOORD. DEMAND READY TIME DUE DATE SERVICE TIME
        0 0 0 0 0 100 0
        1 1 0 1 0 100 0
        2 2 0 1 0 100 0
        3 3 0 1 0 100 0
        """;
}
