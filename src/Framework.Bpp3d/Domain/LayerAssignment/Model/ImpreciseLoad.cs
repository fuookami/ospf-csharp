#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model
{
    /// <summary>
    /// 装载接口 / Load interface.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public interface ILoad<V> where V : struct, IFloatingNumber<V>
    {
        /// <summary>装载列集合 / Load columns.</summary>
        IReadOnlyList<LoadColumn<V>> Columns { get; }
    }

    /// <summary>
    /// 未缩放装载 / Imprecise (unscaled) load.
    /// 缩放前的装载累加 / Pre-scaling load accumulation.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed class ImpreciseLoad<V> : ILoad<V>
        where V : struct, IFloatingNumber<V>
    {
        private readonly List<LoadColumn<V>> _columns = new();

        /// <summary>装载列集合 / Load columns.</summary>
        public IReadOnlyList<LoadColumn<V>> Columns => _columns;

        /// <summary>
        /// 注册单个装载列 / Register a single load column.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> Register(LoadColumn<V> column)
        {
            if (column is null)
            {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(Bpp3dErrors.NullLoadColumn, "Load column is null."));
            }
            _columns.Add(column);
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>
        /// 批量追加装载列 / Append load columns.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> AddColumns(IEnumerable<LoadColumn<V>> columns)
        {
            foreach (var column in columns)
            {
                var r = Register(column);
                if (r.IsFailed)
                {
                    return r;
                }
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }
}
