#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;

namespace Fuookami.Ospf.Core.Model.Basic
{
    /// <summary>
    /// 表达式值域，支持通过交集逐步收紧上下界
    /// Value range supporting progressive tightening via intersection.
    /// <para>
    /// 常量通过 NumericConstantsRegistry.For&lt;V&gt;() 获取，不使用 companion 反射。
    /// Constants are obtained via NumericConstantsRegistry.For&lt;V&gt;(), not companion reflection.
    /// </para>
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type</typeparam>
    public class ExpressionRange<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private ValueRange<V>? _range;
        private bool _set;

        /// <summary>常量提供器 / Constants provider</summary>
        protected virtual INumericConstants<V> Constants { get; }

        /// <summary>
        /// 使用指定常量提供器创建默认（全范围）值域 / Create default (full) range via the given constants
        /// </summary>
        public ExpressionRange(INumericConstants<V> constants)
            : this(ValueRange<V>.Of(constants.Minimum, constants.Maximum, new Interval.Closed(), new Interval.Closed()).Value!, constants)
        {
        }

        /// <summary>
        /// 使用指定值域创建 / Create with the specified value range
        /// </summary>
        public ExpressionRange(ValueRange<V> range, INumericConstants<V> constants)
        {
            _range = range;
            Constants = constants;
        }

        /// <summary>
        /// 创建默认值域（常量通过注册表解析）/ Create default range (constants resolved via registry)
        /// </summary>
        public static ExpressionRange<V> Create() => new(NumericConstantsRegistry.For<V>());

        /// <summary>
        /// 使用指定值域创建（常量通过注册表解析）/ Create with the specified range (constants via registry)
        /// </summary>
        public static ExpressionRange<V> Create(ValueRange<V> range) => new(range, NumericConstantsRegistry.For<V>());

        /// <summary>值域 / Value range</summary>
        public ValueRange<V>? Range => _range;

        /// <summary>Flt64 视图的值域 / Flt64 view of value range</summary>
        public ValueRange<Flt64>? ValueRangeFlt64 => _range?.ToFlt64();

        /// <summary>下界 / Lower bound</summary>
        public Bound<V>? LowerBound => _range?.LowerBound;

        /// <summary>上界 / Upper bound</summary>
        public Bound<V>? UpperBound => _range?.UpperBound;

        /// <summary>是否为空 / Whether empty</summary>
        public bool Empty => _range is null;

        /// <summary>是否为固定值 / Whether is a fixed value</summary>
        public bool Fixed => _range?.Fixed == true;

        /// <summary>固定值 / Fixed value</summary>
        public V? FixedValue => _range?.FixedValue;

        /// <summary>是否已设置 / Whether has been set</summary>
        internal bool Set => _set;

        /// <summary>设置值域 / Set range</summary>
        public void SetRange(ValueRange<V> range) { _set = true; _range = range; }

        /// <summary>与给定值域求交集 / Intersect with the given range</summary>
        public bool IntersectWith(ValueRange<V> range)
        {
            _set = true;
            _range = _range?.Intersect(range);
            return _range is not null;
        }

        /// <summary>设置上界（小于等于）/ Set upper bound (less-than-or-equal)</summary>
        public bool Ls(IInvariant<V> value) =>
            IntersectWith(ValueRange<V>.Leq(value.Value(), new Interval.Closed()).Value!);

        /// <summary>设置上界（小于等于）/ Set upper bound (less-than-or-equal)</summary>
        public bool Leq(IInvariant<V> value) => Ls(value);

        /// <summary>设置下界（大于等于）/ Set lower bound (greater-than-or-equal)</summary>
        public bool Gr(IInvariant<V> value) =>
            IntersectWith(ValueRange<V>.Geq(value.Value(), new Interval.Closed()).Value!);

        /// <summary>设置下界（大于等于）/ Set lower bound (greater-than-or-equal)</summary>
        public bool Geq(IInvariant<V> value) => Gr(value);

        /// <summary>设置相等约束 / Set equality constraint</summary>
        public bool Eq(IInvariant<V> value) =>
            IntersectWith(ValueRange<V>.Of(value.Value()).Value!);

        /// <summary>与给定上下界求交集 / Intersect with the given lower and upper bounds</summary>
        public bool IntersectWith(IInvariant<V> lb, IInvariant<V> ub) =>
            IntersectWith(ValueRange<V>.Of(lb.Value(), ub.Value(), new Interval.Closed(), new Interval.Closed()).Value!);

        /// <summary>设置上界 / Set upper bound</summary>
        public bool SetUb(IInvariant<V> value) => Leq(value);

        /// <summary>设置下界 / Set lower bound</summary>
        public bool SetLb(IInvariant<V> value) => Geq(value);

        /// <summary>字符串表示 / String representation</summary>
        public override string ToString() => _range?.ToString() ?? "empty";
    }
}
