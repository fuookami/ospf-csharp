#nullable enable

using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
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
