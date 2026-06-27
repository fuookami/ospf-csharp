#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 飞机类型。Aircraft type classification.
/// </summary>
public enum AircraftType
{
    B737,
    B757,
    B767,
    B747
}

/// <summary>
/// 飞机类型扩展方法。Extension methods for AircraftType.
/// </summary>
public static class AircraftTypeExtensions
{
    /// <summary>是否窄体 / Whether narrow body</summary>
    public static bool IsNarrowBody(this AircraftType type) =>
        type == AircraftType.B737 || type == AircraftType.B757;

    /// <summary>是否宽体 / Whether wide body</summary>
    public static bool IsWideBody(this AircraftType type) =>
        type == AircraftType.B767 || type == AircraftType.B747;

    /// <summary>是否需要压舱物 / Whether ballast is needed</summary>
    public static bool BallastNeeded(this AircraftType type) =>
        type == AircraftType.B757 || type == AircraftType.B767;

    /// <summary>是否偏好主甲板门空位 / Whether main deck door empty preferred</summary>
    public static bool MainDeckDoorEmptyPrefer(this AircraftType type) =>
        type == AircraftType.B757 || type == AircraftType.B767;
}

/// <summary>
/// 飞机型号。Aircraft model with type classification and physical unit definitions.
/// </summary>
public sealed class AircraftModel
{
    /// <summary>飞机类型（可能为 null 表示未知） / Aircraft type (null for unknown)</summary>
    public AircraftType? Type { get; }
    /// <summary>型号名称 / Model name</summary>
    public string ModelName { get; }
    /// <summary>子型号 / Minor model</summary>
    public Infrastructure.AircraftMinorModel MinorModel { get; }

    public AircraftModel(AircraftType? type, string modelName, Infrastructure.AircraftMinorModel minorModel)
    {
        Type = type;
        ModelName = modelName;
        MinorModel = minorModel;
    }

    /// <summary>
    /// 从子型号创建飞机型号。Create aircraft model from minor model.
    /// </summary>
    public static AircraftModel FromMinorModel(Infrastructure.AircraftMinorModel minorModel)
    {
        AircraftType? type = minorModel.Model switch
        {
            var s when s.Contains("B737") => AircraftType.B737,
            var s when s.Contains("B757") => AircraftType.B757,
            var s when s.Contains("B767") => AircraftType.B767,
            var s when s.Contains("B747") => AircraftType.B747,
            _ => (AircraftType?)null
        };
        return new AircraftModel(type, type?.ToString() ?? "Other", minorModel);
    }

    /// <summary>是否窄体 / Whether narrow body</summary>
    public bool NarrowBody => Type?.IsNarrowBody() ?? true;
    /// <summary>是否宽体 / Whether wide body</summary>
    public bool WideBody => Type?.IsWideBody() ?? false;
    /// <summary>是否需要压舱物 / Whether ballast is needed</summary>
    public bool BallastNeeded => Type?.BallastNeeded() ?? false;
    /// <summary>是否偏好主甲板门空位 / Whether main deck door empty preferred</summary>
    public bool MainDeckDoorEmptyPrefer => Type?.MainDeckDoorEmptyPrefer() ?? false;
}
