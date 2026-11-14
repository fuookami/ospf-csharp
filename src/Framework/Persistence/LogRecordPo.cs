#nullable enable

using System;
using Fuookami.Ospf.Framework.Log;

namespace Fuookami.Ospf.Framework.Persistence;
/// <summary>
/// 字节数组日志持久化对象 / Byte array log persistence object.
/// </summary>
public sealed record LogRecordBytePo(
    string App,
    string Version,
    string ServiceId,
    string Step,
    LogRecordType Type,
    DateTime Time,
    DateTime AvailableTime,
    byte[] Value);

/// <summary>
/// 字符串日志持久化对象 / String log persistence object.
/// </summary>
public sealed record LogRecordStringPo(
    string App,
    string Version,
    string ServiceId,
    string Step,
    LogRecordType Type,
    DateTime Time,
    DateTime AvailableTime,
    string Value);
