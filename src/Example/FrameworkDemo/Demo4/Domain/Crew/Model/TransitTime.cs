#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 基于飞机和机场关系枚举中转时间场景。
/// Enumerates the transit time scenarios based on aircraft and airport relationships.
/// </summary>
public enum TransitTimeScene {
    /// <summary>同一飞机 / Same aircraft.</summary>
    SameAircraft,
    /// <summary>国内不同飞机 / Domestic different aircraft.</summary>
    DomesticNotSameAircraft,
    /// <summary>国际不同飞机 / International different aircraft.</summary>
    InternationalNotSameAircraft
}

/// <summary>
/// 中转时间场景扩展方法。
/// Extension methods for transit time scene.
/// </summary>
public static class TransitTimeSceneExtensions {
    /// <summary>
    /// 判定给定连续航班任务的中转时间场景。
    /// Determines the transit time scene for the given consecutive flight tasks.
    /// </summary>
    /// <param name="prevTask">前一个任务 / Previous task.</param>
    /// <param name="nextTask">后一个任务 / Next task.</param>
    /// <returns>中转时间场景，若不适用则为 null / The scene, or null if not applicable.</returns>
    public static TransitTimeScene? Determine(FlightTask prevTask, FlightTask nextTask) {
        if (prevTask.Aircraft == nextTask.Aircraft) return TransitTimeScene.SameAircraft;
        if (prevTask.Arr == nextTask.Dep) {
            return prevTask.Arr.Type.IsDomesticType()
                ? TransitTimeScene.DomesticNotSameAircraft
                : TransitTimeScene.InternationalNotSameAircraft;
        }
        return null;
    }
}

/// <summary>
/// 将场景与其所需时长关联的中转时间条目。
/// A transit time entry associating a scene with its required duration.
/// </summary>
/// <param name="Scene">场景 / The scene.</param>
/// <param name="Duration">时长 / The duration.</param>
public sealed record TransitTime(TransitTimeScene Scene, TimeSpan Duration);

/// <summary>
/// 中转时间映射扩展方法。
/// Extension methods for transit time map.
/// </summary>
public static class TransitTimeMapExtensions {
    /// <summary>
    /// 查找给定连续航班任务的中转时间。
    /// Looks up the transit time for the given consecutive flight tasks.
    /// </summary>
    public static TransitTime? GetTransitTime(this IReadOnlyDictionary<TransitTimeScene, TransitTime> map, FlightTask prevTask, FlightTask nextTask) {
        var scene = TransitTimeSceneExtensions.Determine(prevTask, nextTask);
        return scene is not null && map.TryGetValue(scene.Value, out var entry) ? entry : null;
    }
}
