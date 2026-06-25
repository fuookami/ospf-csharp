#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model
{
    /// <summary>
    /// 层列 / Layer column (CG column).
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record LayerColumn<V>(Quantity<V> Volume, int Index)
        where V : struct, IFloatingNumber<V>;

    /// <summary>
    /// 装载列 / Load column.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record LoadColumn<V>(Quantity<V> Amount, int Index)
        where V : struct, IFloatingNumber<V>;

    /// <summary>
    /// 分配列 / Assignment column.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record AssignmentColumn<V>(int BinIndex, int LayerIndex, Quantity<V> Amount)
        where V : struct, IFloatingNumber<V>;

    /// <summary>
    /// 层聚合接口 / Layer aggregation interface.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public interface ILayerAggregation<V> where V : struct, IFloatingNumber<V>
    {
        /// <summary>列集合 / Columns.</summary>
        IReadOnlyList<LayerColumn<V>> Columns { get; }
    }

    /// <summary>
    /// 层聚合 / Layer aggregation.
    /// 聚合若干层分配列，供列生成主问题求解 / Aggregates layer-assignment columns for the CG master problem.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed class LayerAggregation<V> : ILayerAggregation<V>
        where V : struct, IFloatingNumber<V>
    {
        private readonly List<LayerColumn<V>> _columns = new();

        /// <summary>列集合 / Columns.</summary>
        public IReadOnlyList<LayerColumn<V>> Columns => _columns;

        /// <summary>
        /// 批量追加列 / Append columns.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> AddColumns(IEnumerable<LayerColumn<V>> columns)
        {
            foreach (var column in columns)
            {
                if (column is null)
                {
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(Bpp3dErrors.NullLayerColumn, "Layer column is null."));
                }
                _columns.Add(column);
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>
        /// 移除单个列 / Remove a single column (by value equality).
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> RemoveColumn(LayerColumn<V> column)
        {
            if (column is null)
            {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(Bpp3dErrors.NullLayerColumn, "Layer column is null."));
            }
            _ = _columns.Remove(column);
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>
        /// 批量移除列 / Remove multiple columns.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> RemoveColumns(IEnumerable<LayerColumn<V>> columns)
        {
            var set = new HashSet<LayerColumn<V>>(columns);
            _columns.RemoveAll(c => set.Contains(c));
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }
}
