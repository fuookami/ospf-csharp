#nullable enable

using Fuookami.Ospf.Math.Symbol.Inequality;
using System;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Basic
{
    /// <summary>
    /// 约束关系（小于等于/等于/大于等于），含反转与比较器工厂
    /// Constraint relation (less-equal / equal / greater-equal) with reverse and comparator factory
    /// </summary>
    public abstract record ConstraintRelation
    {
        /// <summary>小于等于 / Less than or equal</summary>
        public static readonly ConstraintRelation LessEqual = new LessEqualRelation();
        /// <summary>等于 / Equal</summary>
        public static readonly ConstraintRelation Equal = new EqualRelation();
        /// <summary>大于等于 / Greater than or equal</summary>
        public static readonly ConstraintRelation GreaterEqual = new GreaterEqualRelation();

        /// <summary>反转关系（小于等于与大于等于互换，等于不变）/ Reverse relation</summary>
        public abstract ConstraintRelation Reverse { get; }

        /// <summary>对应比较器工厂 / Comparator factory</summary>
        public abstract Func<T, T, bool> Operator<T>() where T : struct, IOrd<T>;

        /// <summary>直接判断两值是否满足此关系 / Directly check whether two values satisfy this relation</summary>
        public bool Invoke<T>(T lhs, T rhs) where T : struct, IOrd<T> => Operator<T>()(lhs, rhs);

        /// <summary>转换为通用比较枚举 / Convert to the generic Comparison enum</summary>
        public abstract Comparison ToComparison();

        /// <summary>从比较运算创建约束关系，NE 返回 null / Create from Comparison; NE returns null</summary>
        public static ConstraintRelation? OfOrNull(Comparison comparison) => comparison switch
        {
            Comparison.LT => LessEqual,
            Comparison.LE => LessEqual,
            Comparison.EQ => Equal,
            Comparison.NE => null,
            Comparison.GT => GreaterEqual,
            Comparison.GE => GreaterEqual,
            _ => null,
        };

        /// <summary>从比较运算创建约束关系，NE 返回失败 / Create from Comparison; NE returns failure</summary>
        public static Result<ConstraintRelation, ErrorCode, Error<ErrorCode>> OfSafe(Comparison comparison) =>
            OfOrNull(comparison) is { } r
                ? Results.Ok<ConstraintRelation>(r)
                : Results.Failed<ConstraintRelation>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"No matched constraint sign for comparison: {comparison}."));

        private sealed record LessEqualRelation : ConstraintRelation
        {
            public override ConstraintRelation Reverse => GreaterEqual;
            public override Func<T, T, bool> Operator<T>() => (lhs, rhs) => lhs.CompareTo(rhs) <= 0;
            public override Comparison ToComparison() => Comparison.LE;
            public override string ToString() => "<=";
        }

        private sealed record EqualRelation : ConstraintRelation
        {
            public override ConstraintRelation Reverse => this;
            public override Func<T, T, bool> Operator<T>() => (lhs, rhs) => lhs.CompareTo(rhs) == 0;
            public override Comparison ToComparison() => Comparison.EQ;
            public override string ToString() => "=";
        }

        private sealed record GreaterEqualRelation : ConstraintRelation
        {
            public override ConstraintRelation Reverse => LessEqual;
            public override Func<T, T, bool> Operator<T>() => (lhs, rhs) => lhs.CompareTo(rhs) >= 0;
            public override Comparison ToComparison() => Comparison.GE;
            public override string ToString() => ">=";
        }
    }
}
