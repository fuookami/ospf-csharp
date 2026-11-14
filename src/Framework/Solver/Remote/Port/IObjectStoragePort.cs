#nullable enable

using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver.Remote.Port;
/// <summary>
/// 对象存储端口接口 / Object storage port interface.
/// </summary>
public interface IObjectStoragePort {
    /// <summary>上传对象 / Put object.</summary>
    Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> PutAsync(
        ObjectPath path,
        byte[] data,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传对象并附带元数据 / Put an object together with metadata.
    /// </summary>
    /// <remarks>
    /// 固定 Kotlin 基线的 <c>storage.put(path, bytes, metadata)</c> 会把 <c>taskId</c>／<c>sliceId</c>／
    /// <c>tenantId</c> 作为对象元数据一起写入。本重载用**默认接口方法**提供，故现有实现无需改动即可
    /// 继续编译；未覆写时元数据不会被持久化，有能力的实现应覆写它。
    /// The pinned Kotlin baseline's <c>storage.put(path, bytes, metadata)</c> writes <c>taskId</c>,
    /// <c>sliceId</c> and <c>tenantId</c> as object metadata. This overload is a **default interface
    /// method**, so existing implementations keep compiling unchanged; without an override the metadata is
    /// not persisted, and a capable implementation should override it.
    /// </remarks>
    /// <param name="path">对象路径 / Object path.</param>
    /// <param name="data">对象内容 / Object content.</param>
    /// <param name="metadata">元数据 / Metadata.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>对象引用 / Object reference.</returns>
    Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> PutAsync(
        ObjectPath path,
        byte[] data,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken = default) =>
        PutAsync(
            path: path,
            data: data,
            contentType: null,
            cancellationToken: cancellationToken);

    /// <summary>下载对象 / Get object.</summary>
    Task<Result<StoredObject, ErrorCode, Error<ErrorCode>>> GetAsync(
        ObjectRef @ref,
        CancellationToken cancellationToken = default);

    /// <summary>删除对象 / Delete object.</summary>
    Task<Result<bool, ErrorCode, Error<ErrorCode>>> DeleteAsync(
        ObjectRef @ref,
        CancellationToken cancellationToken = default);

    /// <summary>检查对象是否存在 / Check if object exists.</summary>
    Task<Result<bool, ErrorCode, Error<ErrorCode>>> ExistsAsync(
        ObjectRef @ref,
        CancellationToken cancellationToken = default);
}
