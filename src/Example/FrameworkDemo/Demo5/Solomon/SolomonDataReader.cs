#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon;

/// <summary>
/// Solomon 车辆段原始数据 / Raw Solomon vehicle-section data.
/// </summary>
/// <param name="Amount">车辆数量 / Vehicle amount.</param>
/// <param name="Capacity">车辆容量 / Vehicle capacity.</param>
public sealed record SolomonVehicleData(int Amount, FltX Capacity);

/// <summary>
/// Solomon 节点行原始数据 / Raw Solomon node-row data.
/// </summary>
/// <param name="Id">节点 ID / Node ID.</param>
/// <param name="X">横坐标 / X coordinate.</param>
/// <param name="Y">纵坐标 / Y coordinate.</param>
/// <param name="Demand">需求 / Demand.</param>
/// <param name="ReadyTime">就绪时间 / Ready time.</param>
/// <param name="DueTime">截止时间 / Due time.</param>
/// <param name="ServiceTime">服务时长 / Service time.</param>
public sealed record SolomonNodeData(
    string Id,
    FltX X,
    FltX Y,
    FltX Demand,
    FltX ReadyTime,
    FltX DueTime,
    FltX ServiceTime);

/// <summary>
/// Solomon 示例输入 / Solomon demo input.
/// </summary>
/// <param name="Name">实例名 / Instance name.</param>
/// <param name="Vehicle">车辆段数据 / Vehicle-section data.</param>
/// <param name="Depot">车辆段节点 / Depot node.</param>
/// <param name="Customers">客户节点 / Customer nodes.</param>
public sealed record SolomonData(
    string Name,
    SolomonVehicleData Vehicle,
    SolomonNodeData Depot,
    IReadOnlyList<SolomonNodeData> Customers);

/// <summary>
/// Solomon 示例文本读取器 / Solomon demo text reader.
/// </summary>
/// <remarks>
/// 与基线 `framework_demo/demo5/infrastructure/io/SolomonDataReader.kt`（6181 B）对应。
///
/// **数值一律按十进制字面量解析**（`decimal`），不经过 `double`。Solomon 实例里的坐标与时间窗是
/// 十进制小数，走 `double` 会让 `12.3` 变成 `12.300000000000000710…`，而**下游做的是精确相等比较**。
///
/// **Numbers are parsed as decimal literals** (`decimal`), never through `double`. Solomon coordinates and time windows are decimal fractions, and going through
/// `double` turns `12.3` into `12.300000000000000710…`, while **downstream code compares for exact equality**.
///
/// **失败消息带行号**。不带行号时，一份 100 行实例里的"第 7 列非法"无法定位，**而调用方拿到的只是一句
/// "Solomon 数据无效"**。
///
/// **Failures carry the line number.** Without one, "column 7 is invalid" inside a 100-line instance is unlocatable, and **all the caller gets is
/// "invalid Solomon data"**.
/// </remarks>
public static class SolomonDataReader {
    /// <summary>
    /// 读取标准 Solomon 文本 / Read standard Solomon text.
    /// </summary>
    /// <param name="text">Solomon 文本 / Solomon text.</param>
    /// <returns>原始示例数据，或带行号的解析失败 / Raw demo data, or a line-aware parsing failure.</returns>
    public static Result<SolomonData, ErrorCode, Error<ErrorCode>> Read(string text) {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<SourceLine>();
        int number = 0;
        foreach (string raw in text.Split('\n')) {
            ++number;
            string trimmed = raw.Trim();
            if (trimmed.Length > 0) {
                lines.Add(new SourceLine(number, trimmed));
            }
        }
        if (lines.Count == 0) {
            return Failure<SolomonData>(
                "读取 Solomon 数据失败：输入为空 / Failed to read Solomon data: input is empty");
        }

        int vehicleHeader = lines.FindIndex(
            line => string.Equals(line.Text, "VEHICLE", StringComparison.OrdinalIgnoreCase));
        int customerHeader = lines.FindIndex(
            line => string.Equals(line.Text, "CUSTOMER", StringComparison.OrdinalIgnoreCase));
        if (vehicleHeader < 0 || customerHeader <= vehicleHeader) {
            return Failure<SolomonData>(
                "读取 Solomon 数据失败：缺少 VEHICLE 或 CUSTOMER 段 / " +
                "Failed to read Solomon data: VEHICLE or CUSTOMER section is missing");
        }

        SourceLine? vehicleLine = null;
        for (int index = vehicleHeader + 1; index < customerHeader; ++index) {
            List<string> candidate = Fields(lines[index].Text);
            if (candidate.Count == 2 &&
                int.TryParse(candidate[0], out _) &&
                Decimal(candidate[1]) is not null) {
                vehicleLine = lines[index];
                break;
            }
        }
        if (vehicleLine is null) {
            return Failure<SolomonData>(
                "读取 Solomon 数据失败：VEHICLE 段缺少车辆数量和容量 / " +
                "Failed to read Solomon data: vehicle amount and capacity are missing");
        }
        List<string> vehicleFields = Fields(vehicleLine.Text);
        int? amount = int.TryParse(vehicleFields[0], out int parsedAmount)
            ? parsedAmount
            : null;
        decimal? capacity = Decimal(vehicleFields[1]);
        if (amount is null || amount <= 0 || capacity is null || capacity.Value <= 0m) {
            return LineFailure<SolomonData>(
                vehicleLine,
                "车辆数量和容量必须为正数",
                "vehicle amount and capacity must be positive");
        }

        var nodeRows = new List<SolomonNodeData>();
        for (int index = customerHeader + 1; index < lines.Count; ++index) {
            SourceLine line = lines[index];
            List<string> columns = Fields(line.Text);
            if (columns.Count == 0 || Decimal(columns[0]) is null) {
                continue;
            }
            if (columns.Count != 7) {
                return LineFailure<SolomonData>(
                    line,
                    $"客户数据必须恰好包含 7 列，实际为 {columns.Count} 列",
                    $"customer data must contain exactly 7 columns, got {columns.Count}");
            }
            var numbers = new List<decimal?>(6);
            for (int column = 1; column < 7; ++column) {
                numbers.Add(Decimal(columns[column]));
            }
            if (numbers.Any(value => value is null)) {
                return LineFailure<SolomonData>(
                    line,
                    "客户数据包含非法数值",
                    "customer data contains an invalid number");
            }
            var values = numbers.Select(value => value!.Value).ToList();
            if (values[2] < 0m || values[3] < 0m || values[4] < values[3] || values[5] < 0m) {
                return LineFailure<SolomonData>(
                    line,
                    "需求、时间窗或服务时间非法",
                    "demand, time window, or service time is invalid");
            }
            nodeRows.Add(new SolomonNodeData(
                Id: columns[0],
                X: new FltX(values[0]),
                Y: new FltX(values[1]),
                Demand: new FltX(values[2]),
                ReadyTime: new FltX(values[3]),
                DueTime: new FltX(values[4]),
                ServiceTime: new FltX(values[5])));
        }
        if (nodeRows.Count == 0) {
            return Failure<SolomonData>(
                "读取 Solomon 数据失败：CUSTOMER 段没有节点数据 / " +
                "Failed to read Solomon data: CUSTOMER section contains no node data");
        }
        string? duplicateId = nodeRows
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicateId is not null) {
            return Failure<SolomonData>(
                $"读取 Solomon 数据失败：节点 ID {duplicateId} 重复 / " +
                $"Failed to read Solomon data: duplicate node ID {duplicateId}");
        }
        SolomonNodeData[] depots = nodeRows
            .Where(node => node.Id == "0")
            .ToArray();
        if (depots.Length != 1) {
            return Failure<SolomonData>(
                "读取 Solomon 数据失败：必须且只能存在一个 ID 为 0 的 depot / " +
                "Failed to read Solomon data: exactly one depot with ID 0 is required");
        }
        SolomonNodeData depot = depots[0];
        return Results.Ok(new SolomonData(
            Name: lines[0].Text,
            Vehicle: new SolomonVehicleData(amount.Value, new FltX(capacity.Value)),
            Depot: depot,
            Customers: Array.AsReadOnly(
                nodeRows.Where(node => node.Id != depot.Id).ToArray())));
    }

    private static List<string> Fields(string line) =>
        line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static decimal? Decimal(string value) =>
        decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out decimal parsed)
            ? parsed
            : null;

    private static Result<T, ErrorCode, Error<ErrorCode>> LineFailure<T>(
        SourceLine line,
        string chinese,
        string english) =>
        Failure<T>(
            $"读取 Solomon 数据失败：第 {line.Number} 行{chinese} / " +
            $"Failed to read Solomon data: line {line.Number} {english}");

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private sealed record SourceLine(int Number, string Text);
}
