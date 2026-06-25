#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Persistence.Expression;
/// <summary>
/// 表达式仓储接口 / Expression repository interface.
/// </summary>
/// <typeparam name="E">实体类型 / Entity type</typeparam>
public interface IExpressionRepository<E> where E : class {
    /// <summary>查找实体 / Find entities.</summary>
    Task<Result<IReadOnlyList<E>, ErrorCode, Error<ErrorCode>>> FindAsync(
        object? predicate = null,
        SortBy? sortBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>计数 / Count entities.</summary>
    Task<Result<long, ErrorCode, Error<ErrorCode>>> CountAsync(
        object? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>更新实体 / Update entities.</summary>
    Task<Result<long, ErrorCode, Error<ErrorCode>>> UpdateAsync(
        object? predicate,
        UpdateAssignments assignments,
        CancellationToken cancellationToken = default);

    /// <summary>删除实体 / Delete entities.</summary>
    Task<Result<long, ErrorCode, Error<ErrorCode>>> DeleteAsync(
        object? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>检查是否存在 / Check existence.</summary>
    Task<Result<bool, ErrorCode, Error<ErrorCode>>> ExistsAsync(
        object? predicate = null,
        CancellationToken cancellationToken = default);
}
