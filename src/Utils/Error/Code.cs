#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Error;
/// <summary>
/// 标准错误码枚举 / Standard error code enum (mirrors ospf-kotlin ErrorCode).
/// </summary>
public enum ErrorCode : byte {
    /// <summary>无错误 / No error</summary>
    None = 0x00,
    /// <summary>认证错误 / Authentication error</summary>
    AuthenticationError = 0x01,
    /// <summary>不是文件 / Not a file</summary>
    NotAFile = 0x10,
    /// <summary>不是目录 / Not a directory</summary>
    NotADirectory = 0x11,
    /// <summary>文件未找到 / File not found</summary>
    FileNotFound = 0x12,
    /// <summary>目录不可用 / Directory unusable</summary>
    DirectoryUnusable = 0x13,
    /// <summary>文件扩展名不匹配 / File extension not matched</summary>
    FileExtensionNotMatched = 0x14,
    /// <summary>数据未找到 / Data not found</summary>
    DataNotFound = 0x15,
    /// <summary>数据为空 / Data empty</summary>
    DataEmpty = 0x16,
    /// <summary>枚举访问器为空 / Enum visitor empty</summary>
    EnumVisitorEmpty = 0x17,
    /// <summary>唯一盒子已锁定 / Unique box locked</summary>
    UniqueBoxLocked = 0x18,
    /// <summary>唯一引用已锁定 / Unique ref locked</summary>
    UniqueRefLocked = 0x19,
    /// <summary>序列化失败 / Serialization failed</summary>
    SerializationFailed = 0x1a,
    /// <summary>反序列化失败 / Deserialization failed</summary>
    DeserializationFailed = 0x1b,
    /// <summary>令牌已存在 / Token existed</summary>
    TokenExisted = 0x20,
    /// <summary>符号重复 / Symbol repetitive</summary>
    SymbolRepetitive = 0x21,
    /// <summary>缺少管线 / Lack of pipelines</summary>
    LackOfPipelines = 0x22,
    /// <summary>求解器未找到 / Solver not found</summary>
    SolverNotFound = 0x23,
    /// <summary>OR 引擎环境丢失 / OR engine environment lost</summary>
    OREngineEnvironmentLost = 0x24,
    /// <summary>OR 引擎连接超时 / OR engine connection overtime</summary>
    OREngineConnectionOvertime = 0x25,
    /// <summary>OR 引擎建模异常 / OR engine modeling exception</summary>
    OREngineModelingException = 0x26,
    /// <summary>OR 引擎求解异常 / OR engine solving exception</summary>
    OREngineSolvingException = 0x27,
    /// <summary>OR 引擎已终止 / OR engine terminated</summary>
    OREngineTerminated = 0x28,
    /// <summary>OR 模型不可行 / OR model infeasible</summary>
    ORModelInfeasible = 0x29,
    /// <summary>OR 模型无界 / OR model unbounded</summary>
    ORModelUnbounded = 0x2a,
    /// <summary>OR 模型不可行或无界 / OR model infeasible or unbounded</summary>
    ORModelInfeasibleOrUnbounded = 0x2b,
    /// <summary>OR 解无效 / OR solution invalid</summary>
    ORSolutionInvalid = 0x2c,
    /// <summary>应用失败 / Application failed</summary>
    ApplicationFailed = 0x30,
    /// <summary>应用错误 / Application error</summary>
    ApplicationError = 0x31,
    /// <summary>应用异常 / Application exception</summary>
    ApplicationException = 0x32,
    /// <summary>应用已停止 / Application stopped</summary>
    ApplicationStopped = 0x33,
    /// <summary>非法参数 / Illegal argument</summary>
    IllegalArgument = 0x34,
    /// <summary>其他 / Other</summary>
    Other = 0xfe,
    /// <summary>未知 / Unknown</summary>
    Unknown = 0xff,
}

/// <summary>
/// ErrorCode 扩展方法 / ErrorCode extension methods.
/// </summary>
public static class ErrorCodeExtensions {
    /// <summary>从字节值创建 ErrorCode / Create ErrorCode from byte value.</summary>
    public static ErrorCode FromByte(byte code) =>
        Enum.IsDefined(typeof(ErrorCode), code) ? (ErrorCode)code : ErrorCode.Unknown;

    /// <summary>从 ulong 值创建 ErrorCode / Create ErrorCode from ulong value.</summary>
    public static ErrorCode FromUInt64(ulong code) =>
        code <= (ulong)byte.MaxValue ? FromByte((byte)code) : ErrorCode.Unknown;

    /// <summary>转换为字节 / Convert to byte.</summary>
    public static byte ToByte(this ErrorCode code) => (byte)code;

    /// <summary>转换为 ulong / Convert to ulong.</summary>
    public static ulong ToUInt64(this ErrorCode code) => (ulong)(byte)code;

    /// <summary>返回可读名称 / Return readable name.</summary>
    public static string ToReadableString(this ErrorCode code) => code switch {
        ErrorCode.None => "None",
        ErrorCode.AuthenticationError => "AuthenticationError",
        ErrorCode.NotAFile => "NotAFile",
        ErrorCode.NotADirectory => "NotADirectory",
        ErrorCode.FileNotFound => "FileNotFound",
        ErrorCode.DirectoryUnusable => "DirectoryUnusable",
        ErrorCode.FileExtensionNotMatched => "FileExtensionNotMatched",
        ErrorCode.DataNotFound => "DataNotFound",
        ErrorCode.DataEmpty => "DataEmpty",
        ErrorCode.EnumVisitorEmpty => "EnumVisitorEmpty",
        ErrorCode.UniqueBoxLocked => "UniqueBoxLocked",
        ErrorCode.UniqueRefLocked => "UniqueRefLocked",
        ErrorCode.SerializationFailed => "SerializationFailed",
        ErrorCode.DeserializationFailed => "DeserializationFailed",
        ErrorCode.TokenExisted => "TokenExisted",
        ErrorCode.SymbolRepetitive => "SymbolRepetitive",
        ErrorCode.LackOfPipelines => "LackOfPipelines",
        ErrorCode.SolverNotFound => "SolverNotFound",
        ErrorCode.OREngineEnvironmentLost => "OREngineEnvironmentLost",
        ErrorCode.OREngineConnectionOvertime => "OREngineConnectionOvertime",
        ErrorCode.OREngineModelingException => "OREngineModelingException",
        ErrorCode.OREngineSolvingException => "OREngineSolvingException",
        ErrorCode.OREngineTerminated => "OREngineTerminated",
        ErrorCode.ORModelInfeasible => "ORModelInfeasible",
        ErrorCode.ORModelUnbounded => "ORModelUnbounded",
        ErrorCode.ORModelInfeasibleOrUnbounded => "ORModelInfeasibleOrUnbounded",
        ErrorCode.ORSolutionInvalid => "ORSolutionInvalid",
        ErrorCode.ApplicationFailed => "ApplicationFailed",
        ErrorCode.ApplicationError => "ApplicationError",
        ErrorCode.ApplicationException => "ApplicationException",
        ErrorCode.ApplicationStopped => "ApplicationStopped",
        ErrorCode.IllegalArgument => "IllegalArgument",
        ErrorCode.Other => "Other",
        ErrorCode.Unknown => "Unknown",
        _ => $"Unknown({(byte)code:X2})",
    };
}
