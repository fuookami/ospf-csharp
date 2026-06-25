#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 数量缓存，缓存 repeatWidth 和 maxRepeatCount 计算结果 / Quantity cache for repeatWidth and maxRepeatCount computations.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    internal sealed class GenerationQuantityCache<V> where V : struct
    {
        private readonly Func<Quantity<V>, Quantity<V>, Quantity<V>> _addQuantity;
        private readonly Func<Quantity<V>, Quantity<V>, Quantity<V>> _subtractQuantity;
        private readonly Func<Quantity<V>, long, Quantity<V>> _repeatWidth;
        private readonly Func<Quantity<V>, Quantity<V>, long> _maxRepeatCount;

        private readonly Dictionary<(Quantity<V>, long), Quantity<V>> _repeatWidthCache = new();
        private readonly Dictionary<(Quantity<V>, Quantity<V>), long> _maxRepeatCountCache = new();

        private long _repeatWidthHits;
        private long _repeatWidthMisses;
        private long _maxRepeatCountHits;
        private long _maxRepeatCountMisses;

        /// <summary>
        /// 创建数量缓存 / Create quantity cache.
        /// </summary>
        /// <param name="addQuantity">量值加法 / Quantity addition.</param>
        /// <param name="subtractQuantity">量值减法 / Quantity subtraction.</param>
        /// <param name="repeatWidth">宽度重复计算 / Width repeat computation.</param>
        /// <param name="maxRepeatCount">最大重复次数计算 / Max repeat count computation.</param>
        public GenerationQuantityCache(
            Func<Quantity<V>, Quantity<V>, Quantity<V>> addQuantity,
            Func<Quantity<V>, Quantity<V>, Quantity<V>> subtractQuantity,
            Func<Quantity<V>, long, Quantity<V>> repeatWidth,
            Func<Quantity<V>, Quantity<V>, long> maxRepeatCount)
        {
            _addQuantity = addQuantity;
            _subtractQuantity = subtractQuantity;
            _repeatWidth = repeatWidth;
            _maxRepeatCount = maxRepeatCount;
        }

        /// <summary>总命中数 / Total hits.</summary>
        public long TotalHits => _repeatWidthHits + _maxRepeatCountHits;

        /// <summary>总未命中数 / Total misses.</summary>
        public long TotalMisses => _repeatWidthMisses + _maxRepeatCountMisses;

        /// <summary>
        /// 计算宽度重复值（带缓存）/ Compute repeated width (cached).
        /// </summary>
        /// <param name="width">宽度 / Width.</param>
        /// <param name="times">重复次数 / Repeat count.</param>
        /// <returns>重复后的宽度 / Repeated width.</returns>
        public Quantity<V> RepeatWidth(Quantity<V> width, long times)
        {
            var key = (width, times);
            if (_repeatWidthCache.TryGetValue(key, out var existing))
            {
                _repeatWidthHits++;
                return existing;
            }
            _repeatWidthMisses++;
            var result = _repeatWidth(width, times);
            _repeatWidthCache[key] = result;
            return result;
        }

        /// <summary>
        /// 计算最大重复次数（带缓存）/ Compute max repeat count (cached).
        /// </summary>
        /// <param name="width">宽度 / Width.</param>
        /// <param name="availableWidth">可用宽度 / Available width.</param>
        /// <returns>最大重复次数 / Max repeat count.</returns>
        public long MaxRepeatCount(Quantity<V> width, Quantity<V> availableWidth)
        {
            var key = (width, availableWidth);
            if (_maxRepeatCountCache.TryGetValue(key, out var existing))
            {
                _maxRepeatCountHits++;
                return existing;
            }
            _maxRepeatCountMisses++;
            var result = _maxRepeatCount(width, availableWidth);
            _maxRepeatCountCache[key] = result;
            return result;
        }
    }
}
