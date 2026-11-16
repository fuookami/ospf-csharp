#nullable enable

using System;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain;
/// <summary>
/// 对象引用 / Object reference.
/// </summary>
public sealed record ObjectRef(
    ObjectPath Path,
    ObjectVersion? Version = null,
    ObjectEtag? Etag = null);

/// <summary>
/// 存储对象 / Stored object.
/// </summary>
public sealed record StoredObject(
    ObjectRef Ref,
    byte[] Data,
    string? ContentType = null,
    DateTime? CreatedAt = null);

/// <summary>
/// 检查点元数据 / Checkpoint metadata.
/// </summary>
/// <remarks>
/// 字段名必须与**逐字**一致：<c>ref</c>（不是 <c>objectRef</c>）与
/// <c>createdAtEpochMs</c>（整型毫秒，不是 ISO 字符串）。此前 C# 侧写成 <c>objectRef</c>／
/// <c>createdAt</c>，两端互相读不到对方的字段。
/// Field names must match the reference **verbatim**: this implementation uses <c>ref</c> (not
/// <c>objectRef</c>) and <c>createdAtEpochMs</c> (integer milliseconds, not an ISO string). C# previously
/// used <c>objectRef</c>/<c>createdAt</c>, so neither side could read the other's fields.
/// </remarks>
/// <param name="TaskId">任务标识 / Task identifier.</param>
/// <param name="SliceId">切片标识 / Slice identifier.</param>
/// <param name="Ref">检查点引用 / Checkpoint reference.</param>
/// <param name="CreatedAtEpochMs">创建时刻的 epoch 毫秒 / Creation time in epoch milliseconds.</param>
public sealed record CheckpointMetadata(
    TaskId TaskId,
    SliceId SliceId,
    [property: System.Text.Json.Serialization.JsonPropertyName("ref")] ObjectRef Ref,
    [property: System.Text.Json.Serialization.JsonPropertyName("createdAtEpochMs")] long CreatedAtEpochMs);
