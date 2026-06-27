#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;

/// <summary>
/// 航班任务束编译类型别名容器。
/// Container for flight task bunch compilation type aliases.
/// </summary>
/// <remarks>
/// Kotlin uses top-level type aliases. C# uses full generic types directly;
/// this file documents the type mappings for reference.
/// Compilation = BunchCompilation&lt;FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment&gt;
/// TaskTime = BunchSchedulingTaskTime&lt;FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment&gt;
/// </remarks>
internal static class TypeAliases {
    // Compilation = BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>
    // TaskTime = BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>
}
