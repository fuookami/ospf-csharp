#nullable enable

using System;

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// CG 包络线约束接口。Interface for CG envelope constraints that define min/max index bounds per flight phase.
/// </summary>
public interface IEnvelope
{
    /// <summary>飞行阶段 / Flight phase</summary>
    string Phase { get; }
    /// <summary>名称 / Name</summary>
    string Name { get; }
    /// <summary>最小指数 / Minimum index</summary>
    double MinIndex { get; }
    /// <summary>最大指数 / Maximum index</summary>
    double MaxIndex { get; }

    /// <summary>
    /// 注册到模型。Register with the model.
    /// </summary>
    Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model);
}

/// <summary>
/// 包络线点。A point on the envelope.
/// </summary>
/// <param name="TotalWeight">总重量 / Total weight</param>
/// <param name="Index">指数 / Index</param>
public sealed record EnvelopePoint(double TotalWeight, double Index);

/// <summary>
/// 包络线侧类型。Side type of the envelope.
/// </summary>
public enum EnvelopeSideType
{
    /// <summary>左侧 / Left side</summary>
    Left,
    /// <summary>右侧 / Right side</summary>
    Right
}

/// <summary>
/// 包络线侧。A side of the envelope.
/// </summary>
/// <param name="Name">名称 / Name</param>
/// <param name="Type">侧类型 / Side type</param>
/// <param name="Points">点列表 / Points list</param>
public sealed record EnvelopeSide(string Name, EnvelopeSideType Type, IReadOnlyList<EnvelopePoint> Points);

/// <summary>
/// 具有定义最小/最大指数边界的左右两侧的标准 CG 包络线。Standard CG envelope with left and right sides defining min/max index bounds.
/// </summary>
public sealed class Envelope : IEnvelope
{
    /// <summary>飞行阶段 / Flight phase</summary>
    public string Phase { get; init; } = "";
    /// <summary>名称 / Name</summary>
    public string Name { get; init; } = "";
    /// <summary>左侧 / Left side</summary>
    public EnvelopeSide LhsSide { get; init; } = null!;
    /// <summary>右侧 / Right side</summary>
    public EnvelopeSide RhsSide { get; init; } = null!;
    /// <summary>最小指数 / Minimum index</summary>
    public double MinIndex { get; private set; }
    /// <summary>最大指数 / Maximum index</summary>
    public double MaxIndex { get; private set; }

    /// <inheritdoc />
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin Envelope.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}

/// <summary>
/// 具有基于运行时条件切换的条件边的 CG 包络线。CG envelope with conditional sides that switch based on a runtime condition.
/// </summary>
public sealed class ConditionalEnvelope : IEnvelope
{
    /// <summary>飞行阶段 / Flight phase</summary>
    public string Phase { get; init; } = "";
    /// <summary>名称 / Name</summary>
    public string Name { get; init; } = "";
    /// <summary>左侧 1 / Left side 1</summary>
    public EnvelopeSide LhsSide1 { get; init; } = null!;
    /// <summary>右侧 1 / Right side 1</summary>
    public EnvelopeSide RhsSide1 { get; init; } = null!;
    /// <summary>左侧 2 / Left side 2</summary>
    public EnvelopeSide LhsSide2 { get; init; } = null!;
    /// <summary>右侧 2 / Right side 2</summary>
    public EnvelopeSide RhsSide2 { get; init; } = null!;
    /// <summary>最小指数 / Minimum index</summary>
    public double MinIndex { get; private set; }
    /// <summary>最大指数 / Maximum index</summary>
    public double MaxIndex { get; private set; }

    /// <inheritdoc />
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(dynamic model)
    {
        // TODO: Port from Kotlin ConditionalEnvelope.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
