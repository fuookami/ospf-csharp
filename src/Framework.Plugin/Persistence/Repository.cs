#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Persistence;
/// <summary>
/// 通用仓储基接口 / Generic repository base interface.
///
/// 定义 CRUD 操作的契约，所有具体仓储均应实现此接口。
/// Defines the CRUD contract that all concrete repositories should implement.
/// </summary>
/// <typeparam name="T">实体类型 / Entity type</typeparam>
public interface IRepository<T> where T : class {
    /// <summary>
    /// 根据 ID 查找实体 / Find entity by identifier.
    /// </summary>
    /// <param name="id">实体标识 / Entity identifier</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实体或 null / Entity or null</returns>
    Task<Result<T?, ErrorCode, Error<ErrorCode>>> FindByIdAsync(
        object id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询所有实体 / Find all entities.
    /// </summary>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实体列表 / Entity list</returns>
    Task<Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>> FindAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 插入实体 / Insert entity.
    /// </summary>
    /// <param name="entity">实体实例 / Entity instance</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入结果 / Insert result</returns>
    Task<Result<Success, ErrorCode, Error<ErrorCode>>> InsertAsync(
        T entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量插入实体 / Insert entities in batch.
    /// </summary>
    /// <param name="entities">实体列表 / Entity list</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入结果 / Insert result</returns>
    Task<Result<Success, ErrorCode, Error<ErrorCode>>> InsertManyAsync(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新实体 / Update entity.
    /// </summary>
    /// <param name="entity">实体实例 / Entity instance</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>受影响行数 / Affected row count</returns>
    Task<Result<long, ErrorCode, Error<ErrorCode>>> UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除实体 / Delete entity.
    /// </summary>
    /// <param name="id">实体标识 / Entity identifier</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>删除结果 / Delete result</returns>
    Task<Result<Success, ErrorCode, Error<ErrorCode>>> DeleteAsync(
        object id,
        CancellationToken cancellationToken = default);
}
