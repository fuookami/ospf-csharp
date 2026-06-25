#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 生成宽度索引条目 / Generation width index entry.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="ProductId">产品标识 / Product identifier.</param>
    /// <param name="Width">宽度 / Width.</param>
    /// <param name="DemandUnit">需求单位 / Demand unit.</param>
    public sealed record GenerationProductWidthEntry<V>(
        string ProductId,
        Quantity<V> Width,
        PhysicalUnit DemandUnit
    ) where V : struct;

    /// <summary>
    /// 生成宽度索引，从需求派生 / Generation width index, derived from demands.
    ///
    /// 支持后缀最小宽度查询和需求单位查找。
    /// Supports suffix minimum width queries and demand unit lookups.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    internal sealed class GenerationWidthIndex<V> where V : struct, IComparable<V>
    {
        private readonly IReadOnlyList<GenerationProductWidthEntry<V>> _entries;
        private readonly IReadOnlyList<Dictionary<string, Quantity<V>>> _suffixMinWidthByUnit;
        private readonly Dictionary<(string, string, string), PhysicalUnit> _demandUnitIndex;

        private GenerationWidthIndex(
            IReadOnlyList<GenerationProductWidthEntry<V>> entries,
            IReadOnlyList<Dictionary<string, Quantity<V>>> suffixMinWidthByUnit,
            Dictionary<(string, string, string), PhysicalUnit> demandUnitIndex)
        {
            _entries = entries;
            _suffixMinWidthByUnit = suffixMinWidthByUnit;
            _demandUnitIndex = demandUnitIndex;
        }

        /// <summary>条目列表 / Entry list.</summary>
        public IReadOnlyList<GenerationProductWidthEntry<V>> Entries => _entries;

        /// <summary>是否为空 / Whether empty.</summary>
        public bool IsEmpty => _entries.Count == 0;

        /// <summary>
        /// 按谓词过滤 / Filter by predicate.
        /// </summary>
        public GenerationWidthIndex<V> Filter(Func<GenerationProductWidthEntry<V>, bool> predicate)
        {
            var filtered = new List<GenerationProductWidthEntry<V>>();
            foreach (var entry in _entries)
            {
                if (predicate(entry))
                {
                    filtered.Add(entry);
                }
            }
            return FromEntries(filtered);
        }

        /// <summary>
        /// 检查从指定索引开始是否有可容纳的条目 / Check if there are fittable entries from the specified index.
        /// </summary>
        public bool HasFittableFrom(int startIndex, Quantity<V> remainingWidth)
        {
            if (startIndex >= _entries.Count) return false;
            var unitKey = CanonicalUnitKey(remainingWidth.Unit);
            if (!_suffixMinWidthByUnit[startIndex].TryGetValue(unitKey, out var minWidth)) return false;
            return remainingWidth.Value.CompareTo(minWidth.Value) >= 0;
        }

        /// <summary>
        /// 获取需求单位 / Get demand unit for a product-width combination.
        /// </summary>
        public PhysicalUnit? DemandUnitFor(string productId, Quantity<V> width)
        {
            var key = (productId, width.Value.ToString() ?? "", CanonicalUnitKey(width.Unit));
            return _demandUnitIndex.TryGetValue(key, out var unit) ? unit : null;
        }

        /// <summary>
        /// 从需求列表构建宽度索引 / Build width index from demands.
        /// </summary>
        public static GenerationWidthIndex<V> FromDemands<TDemand>(
            IReadOnlyList<TDemand> demands,
            Func<TDemand, string> getProductId,
            Func<TDemand, IReadOnlyList<Quantity<V>>> getWidths,
            Func<TDemand, PhysicalUnit> getDemandUnit)
        {
            var seen = new HashSet<(string, string, string, string)>();
            var entries = new List<GenerationProductWidthEntry<V>>();

            foreach (var demand in demands)
            {
                var productId = getProductId(demand);
                var demandUnit = getDemandUnit(demand);
                foreach (var width in getWidths(demand))
                {
                    var key = (productId, width.Value.ToString() ?? "", CanonicalUnitKey(width.Unit), CanonicalUnitKey(demandUnit));
                    if (!seen.Add(key)) continue;
                    entries.Add(new GenerationProductWidthEntry<V>(productId, width, demandUnit));
                }
            }

            return FromEntries(entries);
        }

        private static GenerationWidthIndex<V> FromEntries(IReadOnlyList<GenerationProductWidthEntry<V>> entries)
        {
            var suffixMinWidthByUnit = BuildSuffixMinWidthByUnit(entries);
            var demandUnitIndex = BuildDemandUnitIndex(entries);
            return new GenerationWidthIndex<V>(entries, suffixMinWidthByUnit, demandUnitIndex);
        }

        private static IReadOnlyList<Dictionary<string, Quantity<V>>> BuildSuffixMinWidthByUnit(
            IReadOnlyList<GenerationProductWidthEntry<V>> entries)
        {
            var suffix = new Dictionary<string, Quantity<V>>[entries.Count];
            var current = new Dictionary<string, Quantity<V>>();

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var unitKey = CanonicalUnitKey(entry.Width.Unit);
                if (!current.TryGetValue(unitKey, out var existing) || entry.Width.Value.CompareTo(existing.Value) < 0)
                {
                    current[unitKey] = entry.Width;
                }
                suffix[i] = new Dictionary<string, Quantity<V>>(current);
            }

            return suffix;
        }

        private static Dictionary<(string, string, string), PhysicalUnit> BuildDemandUnitIndex(
            IReadOnlyList<GenerationProductWidthEntry<V>> entries)
        {
            var index = new Dictionary<(string, string, string), PhysicalUnit>();
            foreach (var entry in entries)
            {
                var key = (entry.ProductId, entry.Width.Value.ToString() ?? "", CanonicalUnitKey(entry.Width.Unit));
                index.TryAdd(key, entry.DemandUnit);
            }
            return index;
        }

        internal static string CanonicalUnitKey(PhysicalUnit unit)
        {
            return unit.Symbol ?? unit.Name ?? unit.ToString();
        }
    }
}
