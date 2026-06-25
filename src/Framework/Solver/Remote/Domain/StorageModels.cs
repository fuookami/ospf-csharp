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
public sealed record CheckpointMetadata(
    TaskId TaskId,
    SliceId SliceId,
    ObjectRef ObjectRef,
    DateTime CreatedAt);
