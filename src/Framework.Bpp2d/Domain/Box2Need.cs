#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain
{
    /// <summary>
    /// 二维盒体需求（轴对齐包围盒）
    /// 2D box need (axis-aligned bounding box).
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record Box2Need<V>(
        /// <summary>最小 X 坐标 / Minimum X coordinate</summary>
        Quantity<V> MinX,
        /// <summary>最小 Y 坐标 / Minimum Y coordinate</summary>
        Quantity<V> MinY,
        /// <summary>最大 X 坐标 / Maximum X coordinate</summary>
        Quantity<V> MaxX,
        /// <summary>最大 Y 坐标 / Maximum Y coordinate</summary>
        Quantity<V> MaxY
    ) where V : struct, IFloatingNumber<V>
    {
        /// <summary>宽度 / Width</summary>
        public Quantity<V> Width => QuantityArithmetic.Minus(MaxX, MinX);

        /// <summary>高度 / Height</summary>
        public Quantity<V> Height => QuantityArithmetic.Minus(MaxY, MinY);

        /// <summary>面积 / Area</summary>
        public Quantity<V> Area => QuantityArithmetic.Product(Width, Height);

        /// <summary>
        /// 判断是否与另一盒体重叠
        /// Check whether this box overlaps with another.
        /// </summary>
        public bool Overlaps(Box2Need<V> rhs)
        {
            if (QuantityArithmetic.Compare(MaxX, rhs.MinX, "maxX-rhsMinX") is not Order.Greater) { return false; }
            if (QuantityArithmetic.Compare(MinX, rhs.MaxX, "minX-rhsMaxX") is not Order.Less) { return false; }
            if (QuantityArithmetic.Compare(MaxY, rhs.MinY, "maxY-rhsMinY") is not Order.Greater) { return false; }
            if (QuantityArithmetic.Compare(MinY, rhs.MaxY, "minY-rhsMaxY") is not Order.Less) { return false; }
            return true;
        }

        /// <summary>
        /// 计算与另一盒体的交集
        /// Compute the intersection with another box; null when disjoint.
        /// </summary>
        public Box2Need<V>? Intersect(Box2Need<V> rhs)
        {
            var left = QuantityArithmetic.Max(MinX, rhs.MinX, "left");
            var right = QuantityArithmetic.Min(MaxX, rhs.MaxX, "right");
            var bottom = QuantityArithmetic.Max(MinY, rhs.MinY, "bottom");
            var top = QuantityArithmetic.Min(MaxY, rhs.MaxY, "top");
            if (QuantityArithmetic.Compare(left, right, "x-range") is not Order.Less) { return null; }
            if (QuantityArithmetic.Compare(bottom, top, "y-range") is not Order.Less) { return null; }
            return new Box2Need<V>(left, bottom, right, top);
        }

        /// <summary>
        /// 判断是否完全位于另一盒体内部
        /// Check whether this box is entirely inside another (inclusive bounds).
        /// </summary>
        public bool Inside(Box2Need<V> sheet)
        {
            var minXOrd = QuantityArithmetic.Compare(MinX, sheet.MinX, "sheet-minX");
            var minYOrd = QuantityArithmetic.Compare(MinY, sheet.MinY, "sheet-minY");
            var maxXOrd = QuantityArithmetic.Compare(MaxX, sheet.MaxX, "sheet-maxX");
            var maxYOrd = QuantityArithmetic.Compare(MaxY, sheet.MaxY, "sheet-maxY");
            return (minXOrd is Order.Greater or Order.Equal)
                && (minYOrd is Order.Greater or Order.Equal)
                && (maxXOrd is Order.Less or Order.Equal)
                && (maxYOrd is Order.Less or Order.Equal);
        }
    }
}
