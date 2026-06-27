#nullable enable

using Fuookami.Ospf.Utils.Concept;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;

/// <summary>
/// 枚举舱位（具有索引和短字符串表示）。
/// Enumerates the passenger classes with their index and short string representation.
/// </summary>
public enum PassengerClass {
    /// <summary>头等舱 / First class.</summary>
    First,
    /// <summary>商务舱 / Business class.</summary>
    Business,
    /// <summary>经济舱 / Economy class.</summary>
    Economy
}

/// <summary>
/// 乘客舱位扩展方法。
/// Extension methods for passenger class.
/// </summary>
public static class PassengerClassExtensions {
    /// <summary>
    /// 获取短字符串表示。
    /// Gets the short string representation.
    /// </summary>
    public static string ToShortString(this PassengerClass cls) => cls switch {
        PassengerClass.First => "F",
        PassengerClass.Business => "B",
        PassengerClass.Economy => "E",
        _ => throw new System.ArgumentOutOfRangeException(nameof(cls))
    };
}
