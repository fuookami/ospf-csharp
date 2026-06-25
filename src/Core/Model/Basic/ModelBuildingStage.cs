#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Model.Basic;
/// <summary>
/// 模型构建阶段 / Model building stage
/// </summary>
public enum ModelBuildingStage {
    /// <summary>注册令牌 / Register tokens</summary>
    RegisterTokens,
    /// <summary>注册线性约束 / Register linear constraints</summary>
    RegisterLinearConstraints,
    /// <summary>注册二次约束 / Register quadratic constraints</summary>
    RegisterQuadraticConstraints,
    /// <summary>注册符号 / Register symbols</summary>
    RegisterSymbols,
    /// <summary>展平线性模型 / Flatten linear model</summary>
    FlattenLinearModel,
    /// <summary>展平二次模型 / Flatten quadratic model</summary>
    FlattenQuadraticModel,
    /// <summary>构建目标函数 / Build objective</summary>
    BuildObjective,
}

/// <summary>
/// 模型构建状态 / Model building status
/// </summary>
/// <param name="ModelName">模型名称 / Model name</param>
/// <param name="Stage">当前阶段 / Current stage</param>
/// <param name="Ready">已完成数量 / Ready count</param>
/// <param name="Total">总数量 / Total count</param>
public sealed record ModelBuildingStatus(
    string ModelName,
    ModelBuildingStage Stage,
    UInt64 Ready,
    UInt64 Total) {
    /// <summary>进度 / Progress</summary>
    public Flt64 Progress => Total != UInt64.Zero
        ? Ready.ToFlt64() / Total.ToFlt64()
        : Flt64.One;
}

/// <summary>
/// 模型构建状态回调委托 / Model building status callback delegate
/// </summary>
public delegate Try ModelBuildingStatusCallBack(ModelBuildingStatus status);

/// <summary>
/// 注册状态 / Registration status
/// </summary>
/// <param name="EmptySymbolAmount">空符号数量 / Empty symbol count</param>
/// <param name="ReadySymbolAmount">就绪符号数量 / Ready symbol count</param>
/// <param name="TotalSymbolAmount">总符号数量 / Total symbol count</param>
public sealed record RegistrationStatus(
    UInt64 EmptySymbolAmount,
    UInt64 ReadySymbolAmount,
    UInt64 TotalSymbolAmount) {
    /// <summary>非空符号数量 / Non-empty symbol count</summary>
    public UInt64 NotEmptySymbolAmount => TotalSymbolAmount - EmptySymbolAmount;

    /// <summary>就绪的非空符号数量 / Ready non-empty symbol count</summary>
    public UInt64 ReadyNotEmptySymbolAmount => ReadySymbolAmount - EmptySymbolAmount;

    /// <summary>总进度 / Total progress</summary>
    public Flt64 TotalProgress => TotalSymbolAmount != UInt64.Zero
        ? ReadySymbolAmount.ToFlt64() / TotalSymbolAmount.ToFlt64()
        : Flt64.One;

    /// <summary>非空进度 / Non-empty progress</summary>
    public Flt64 NotEmptyProgress => NotEmptySymbolAmount != UInt64.Zero
        ? ReadyNotEmptySymbolAmount.ToFlt64() / NotEmptySymbolAmount.ToFlt64()
        : Flt64.One;
}

/// <summary>
/// 注册状态回调委托 / Registration status callback delegate
/// </summary>
public delegate Try RegistrationStatusCallBack(RegistrationStatus status);

/// <summary>
/// RegistrationStatus 扩展方法 / RegistrationStatus extension methods
/// </summary>
public static class RegistrationStatusExtensions {
    /// <summary>转换为模型构建状态 / Convert to model building status</summary>
    public static ModelBuildingStatus ToModelBuildingStatus(this RegistrationStatus status, string modelName) =>
        new(modelName, ModelBuildingStage.RegisterTokens, status.ReadySymbolAmount, status.TotalSymbolAmount);
}
