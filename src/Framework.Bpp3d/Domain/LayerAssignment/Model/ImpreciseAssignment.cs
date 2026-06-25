#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;
/// <summary>
/// 分配接口 / Assignment interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IAssignment<V> where V : struct, IFloatingNumber<V> {
    /// <summary>分配列集合 / Assignment columns.</summary>
    IReadOnlyList<AssignmentColumn<V>> Columns { get; }
}

/// <summary>
/// 未缩放分配 / Imprecise (unscaled) assignment.
/// 缩放前的分配累加 / Pre-scaling assignment accumulation.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class ImpreciseAssignment<V> : IAssignment<V>
    where V : struct, IFloatingNumber<V> {
    private readonly List<AssignmentColumn<V>> _columns = new();

    /// <summary>分配列集合 / Assignment columns.</summary>
    public IReadOnlyList<AssignmentColumn<V>> Columns => _columns;

    /// <summary>
    /// 注册单个分配列 / Register a single assignment column.
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> Register(AssignmentColumn<V> column) {
        if (column is null) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(Bpp3dErrors.NullAssignmentColumn, "Assignment column is null."));
        }
        _columns.Add(column);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 批量追加分配列 / Append assignment columns.
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> AddColumns(IEnumerable<AssignmentColumn<V>> columns) {
        foreach (AssignmentColumn<V> column in columns) {
            Result<Success, ErrorCode, Error<ErrorCode>> r = Register(column);
            if (r.IsFailed) {
                return r;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
