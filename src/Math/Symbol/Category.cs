#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Symbol
{
    /// <summary>
    /// 表达式类型分类基类 / Expression Category base.
    /// Kotlin sealed class Category; C# abstract record + sealed subtypes.
    /// </summary>
    public abstract record Category(Algebra.Number.UInt64 Code)
    {
        /// <summary>返回两个分类中较高的一个 / Returns the higher of two categories (Kotlin infix op).</summary>
        public Category Op(Category rhs) => Code < rhs.Code ? rhs : this;
    }

    /// <summary>线性 / Linear (code = 1).</summary>
    public sealed record LinearCategory() : Category(Algebra.Number.UInt64.One)
    {
        /// <summary>单例实例 / Singleton instance.</summary>
        public static readonly LinearCategory Instance = new();
    }

    /// <summary>二次 / Quadratic (code = 2).</summary>
    public sealed record QuadraticCategory() : Category(new Algebra.Number.UInt64(2))
    {
        /// <summary>单例实例 / Singleton instance.</summary>
        public static readonly QuadraticCategory Instance = new();
    }

    /// <summary>标准 / Standard (code = 3).</summary>
    public sealed record StandardCategory() : Category(new Algebra.Number.UInt64(3))
    {
        /// <summary>单例实例 / Singleton instance.</summary>
        public static readonly StandardCategory Instance = new();
    }

    /// <summary>非线性 / Nonlinear (code = 10).</summary>
    public sealed record NonlinearCategory() : Category(new Algebra.Number.UInt64(10))
    {
        /// <summary>单例实例 / Singleton instance.</summary>
        public static readonly NonlinearCategory Instance = new();
    }

    /// <summary>
    /// 分类运算工具 / Category ordering/max helpers (Kotlin top-level ord/max/Collection.max/maxOrNull).
    /// </summary>
    public static class CategoryOps
    {
        /// <summary>三路比较两个分类 / Three-way comparison of two categories.</summary>
        public static Order Ord(Category lhs, Category rhs) => lhs.Code.PartialOrd(rhs.Code) ?? new Order.Equal();

        /// <summary>返回两个分类中较大的一个 / Returns the larger of two categories.</summary>
        public static Category Max(Category lhs, Category rhs) => lhs.Code > rhs.Code ? lhs : rhs;

        /// <summary>返回集合中最大的分类 / Returns the maximum category from a collection.</summary>
        public static Category Max(IEnumerable<Category> categories)
        {
            Category? best = null;
            foreach (var c in categories)
            {
                if (best is null || c.Code > best.Code) best = c;
            }
            return best ?? throw new InvalidOperationException("Cannot compute max of an empty category collection.");
        }

        /// <summary>返回集合中最大的分类，空集合返回 null / Returns the max category, null for empty.</summary>
        public static Category? MaxOrNull(IEnumerable<Category> categories)
        {
            Category? best = null;
            foreach (var c in categories)
            {
                if (best is null || c.Code > best.Code) best = c;
            }
            return best;
        }
    }
}
