#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Persistence.Expression
{
    /// <summary>
    /// 排序方向 / Sort direction.
    /// </summary>
    public enum SortDirection
    {
        /// <summary>升序 / Ascending</summary>
        Asc,
        /// <summary>降序 / Descending</summary>
        Desc
    }

    /// <summary>
    /// 空值排序 / Nulls ordering.
    /// </summary>
    public enum NullsOrder
    {
        /// <summary>空值在前 / Nulls first</summary>
        First,
        /// <summary>空值在后 / Nulls last</summary>
        Last
    }

    /// <summary>
    /// 空值排序支持 / Nulls order support.
    /// </summary>
    public enum NullsOrderSupport
    {
        /// <summary>不支持 / Not supported</summary>
        None,
        /// <summary>支持空值在前 / Nulls first supported</summary>
        First,
        /// <summary>支持空值在后 / Nulls last supported</summary>
        Last,
        /// <summary>同时支持 / Both supported</summary>
        Both
    }

    /// <summary>
    /// 排序项 / Sort item.
    /// </summary>
    public sealed record SortItem(
        string Field,
        SortDirection Direction = SortDirection.Asc,
        NullsOrder? Nulls = null);

    /// <summary>
    /// 排序规范 / Sort specification.
    /// </summary>
    public sealed record SortBy
    {
        private readonly List<SortItem> _items = new();

        /// <summary>排序项列表 / Sort items.</summary>
        public IReadOnlyList<SortItem> Items => _items;

        /// <summary>按指定字段升序排序 / Sort ascending by field.</summary>
        public static SortBy Asc(string field) => new SortBy().ThenAsc(field);

        /// <summary>按指定字段降序排序 / Sort descending by field.</summary>
        public static SortBy Desc(string field) => new SortBy().ThenDesc(field);

        /// <summary>追加升序排序 / Append ascending sort.</summary>
        public SortBy ThenAsc(string field)
        {
            _items.Add(new SortItem(field, SortDirection.Asc));
            return this;
        }

        /// <summary>追加降序排序 / Append descending sort.</summary>
        public SortBy ThenDesc(string field)
        {
            _items.Add(new SortItem(field, SortDirection.Desc));
            return this;
        }
    }
}
