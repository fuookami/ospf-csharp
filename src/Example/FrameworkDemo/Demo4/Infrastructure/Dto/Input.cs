#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;

/// <summary>
/// 航班恢复调度演示的输入数据传输对象。
/// Input data transfer object for the flight recovery scheduling demo.
/// </summary>
public sealed class Input {
    /// <summary>问题标识 / Problem identifier.</summary>
    public string? ProblemId { get; set; }

    /// <summary>恢复窗口开始时间 / Recovery window start time.</summary>
    public DateTimeOffset? WindowStart { get; set; }

    /// <summary>恢复窗口结束时间 / Recovery window end time.</summary>
    public DateTimeOffset? WindowEnd { get; set; }

    /// <summary>任务取消成本 / Task cancel cost coefficient.</summary>
    public double? TaskCancelCost { get; set; }

    /// <summary>机场列表 / Airport list.</summary>
    public List<AirportInput>? Airports { get; set; }

    /// <summary>飞机列表 / Aircraft list.</summary>
    public List<AircraftInput>? Aircrafts { get; set; }

    /// <summary>航班航段列表 / Flight leg list.</summary>
    public List<FlightLegInput>? FlightLegs { get; set; }

    /// <summary>
    /// 机场输入数据。
    /// Airport input data.
    /// </summary>
    public sealed class AirportInput {
        /// <summary>ICAO 代码 / ICAO code.</summary>
        public string Icao { get; set; } = "";

        /// <summary>机场类型 / Airport type.</summary>
        public AirportType? Type { get; set; }
    }

    /// <summary>
    /// 飞机输入数据。
    /// Aircraft input data.
    /// </summary>
    public sealed class AircraftInput {
        /// <summary>注册号 / Register number.</summary>
        public string RegNo { get; set; } = "";

        /// <summary>飞机类型代码 / Aircraft type code.</summary>
        public string AircraftTypeCode { get; set; } = "";

        /// <summary>子类型代码 / Minor type code.</summary>
        public string MinorTypeCode { get; set; } = "";

        /// <summary>当前位置 ICAO / Current location ICAO.</summary>
        public string LocationIcao { get; set; } = "";

        /// <summary>每小时成本 / Cost per hour.</summary>
        public double? CostPerHour { get; set; }

        /// <summary>可用时间 / Enabled time.</summary>
        public DateTimeOffset? EnabledTime { get; set; }
    }

    /// <summary>
    /// 航班航段输入数据。
    /// Flight leg input data.
    /// </summary>
    public sealed class FlightLegInput {
        /// <summary>任务 ID / Task ID.</summary>
        public string Id { get; set; } = "";

        /// <summary>航班号 / Flight number.</summary>
        public string? FlightNo { get; set; }

        /// <summary>飞机注册号 / Aircraft register number.</summary>
        public string AircraftRegNo { get; set; } = "";

        /// <summary>出发机场 ICAO / Departure airport ICAO.</summary>
        public string DepIcao { get; set; } = "";

        /// <summary>到达机场 ICAO / Arrival airport ICAO.</summary>
        public string ArrIcao { get; set; } = "";

        /// <summary>计划出发时间 / Scheduled departure time.</summary>
        public DateTimeOffset? ScheduledDepTime { get; set; }

        /// <summary>计划到达时间 / Scheduled arrival time.</summary>
        public DateTimeOffset? ScheduledArrTime { get; set; }

        /// <summary>估计时间 / Estimated time.</summary>
        public TimeRange? EstimatedTime { get; set; }

        /// <summary>实际时间 / Actual time.</summary>
        public TimeRange? ActualTime { get; set; }

        /// <summary>推出时间 / Out time.</summary>
        public DateTimeOffset? OutTime { get; set; }

        /// <summary>日期 / Date.</summary>
        public DateTimeOffset? Date { get; set; }
    }
}
