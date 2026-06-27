#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;

/// <summary>
/// 航班恢复调度演示的输出数据传输对象。
/// Output data transfer object for the flight recovery scheduling demo.
/// </summary>
public sealed class Output {
    /// <summary>已调度航班列表 / Scheduled flight list.</summary>
    public List<ScheduledFlight> ScheduledFlights { get; set; } = new();

    /// <summary>已取消任务 ID 列表 / Canceled task ID list.</summary>
    public List<string> CanceledTaskIds { get; set; } = new();

    /// <summary>总任务束数 / Total bunch count.</summary>
    public int TotalBunches { get; set; }

    /// <summary>总已调度任务数 / Total scheduled task count.</summary>
    public int TotalScheduledTasks { get; set; }

    /// <summary>总已取消任务数 / Total canceled task count.</summary>
    public int TotalCanceledTasks { get; set; }

    /// <summary>
    /// 已调度航班信息。
    /// Scheduled flight information.
    /// </summary>
    public sealed class ScheduledFlight {
        /// <summary>任务 ID / Task ID.</summary>
        public string TaskId { get; set; } = "";

        /// <summary>航班号 / Flight number.</summary>
        public string FlightNo { get; set; } = "";

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

        /// <summary>是否已恢复 / Whether recovered.</summary>
        public bool Recovered { get; set; }
    }
}
