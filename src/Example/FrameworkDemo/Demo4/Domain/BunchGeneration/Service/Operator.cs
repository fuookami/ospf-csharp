#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 检查规则是否允许飞机两个任务之间的连接。
/// Checks if a rule allows the connection between two tasks for an aircraft.
/// </summary>
public delegate bool RuleChecker(Aircraft aircraft, FlightTask? prevTask, FlightTask task);

/// <summary>
/// 计算飞机两个任务之间的连接时间。
/// Calculates the connection time between two tasks for an aircraft.
/// </summary>
public delegate TimeSpan ConnectionTimeCalculator(Aircraft aircraft, FlightTask prevTask, FlightTask? succTask);

/// <summary>
/// 给定到达时间、连接时间和任务计算最早出发时间。
/// Calculates the minimum departure time given an arrival time, connection time, and task.
/// </summary>
public delegate DateTimeOffset MinimumDepartureTimeCalculator(DateTimeOffset arrivalTime, Aircraft aircraft, FlightTask task, TimeSpan connectionTime);

/// <summary>
/// 计算将任务分配给飞机的成本，如果不可行则返回 null。
/// Calculates the cost of assigning a task to an aircraft, or null if not feasible.
/// </summary>
public delegate ICost<Flt64>? BunchCostCalculator(Aircraft aircraft, FlightTask? prevTask, FlightTask task, FlightHour flightHour, FlightCycle flightCycle);

/// <summary>
/// 计算飞机任务序列的总成本，如果不可行则返回 null。
/// Calculates the total cost of a sequence of tasks for an aircraft, or null if not feasible.
/// </summary>
public delegate ICost<Flt64>? TotalCostCalculator(Aircraft aircraft, IReadOnlyList<FlightTask> tasks);
