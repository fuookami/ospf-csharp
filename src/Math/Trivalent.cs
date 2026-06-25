#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math
{
    /// <summary>
    /// 三值逻辑类型 / Three-valued logic type
    /// </summary>
    /// <remarks>
    /// 表示 True、False、Unknown 三种状态。
    /// Represents True, False, Unknown states.
    /// </remarks>
    public abstract record Trivalent
    {
        /// <summary>
        /// 获取数值表示 / Get numeric representation
        /// </summary>
        public abstract URtn8 Value { get; }

        /// <summary>
        /// 是否为真 / Whether this is true
        /// </summary>
        /// <remarks>
        /// 返回布尔值，Unknown 返回 null。
        /// Returns boolean value, Unknown returns null.
        /// </remarks>
        public abstract bool? IsTrue { get; }

        /// <summary>
        /// 真值 / True value
        /// </summary>
        public sealed record True : Trivalent
        {
            public override URtn8 Value => URtn8.One;
            public override bool? IsTrue => true;
        }

        /// <summary>
        /// 假值 / False value
        /// </summary>
        public sealed record False : Trivalent
        {
            public override URtn8 Value => URtn8.Zero;
            public override bool? IsTrue => false;
        }

        /// <summary>
        /// 未知值 / Unknown value
        /// </summary>
        public sealed record Unknown : Trivalent
        {
            public override URtn8 Value => new URtn8(UInt8.One, UInt8.Two);
            public override bool? IsTrue => null;
        }

        /// <summary>
        /// 从布尔值创建 / Create from boolean
        /// </summary>
        /// <param name="value">布尔值 / Boolean value</param>
        /// <returns>对应的三值逻辑 / Corresponding trivalent value</returns>
        public static Trivalent Invoke(bool value) => value ? new True() : new False();

        /// <summary>
        /// 从可空布尔值创建 / Create from nullable boolean
        /// </summary>
        /// <param name="value">可空布尔值 / Nullable boolean value</param>
        /// <returns>对应的三值逻辑 / Corresponding trivalent value</returns>
        public static Trivalent Invoke(bool? value) => value switch
        {
            true => new True(),
            false => new False(),
            null => new Unknown(),
        };

        /// <summary>
        /// 从 BalancedTrivalent 创建 / Create from BalancedTrivalent
        /// </summary>
        /// <param name="value">平衡三值逻辑 / Balanced trivalent value</param>
        /// <returns>对应的三值逻辑 / Corresponding trivalent value</returns>
        public static Trivalent Invoke(BalancedTrivalent value) => value switch
        {
            BalancedTrivalent.True => new True(),
            BalancedTrivalent.False => new False(),
            BalancedTrivalent.Unknown => new Unknown(),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

        /// <summary>
        /// 三值逻辑与运算（Kleene 语义）/ Three-valued AND (Kleene semantics)
        /// </summary>
        /// <param name="other">另一个操作数 / Other operand</param>
        /// <returns>与运算结果 / AND result</returns>
        public Trivalent And(Trivalent other) => (this, other) switch
        {
            (False, _) or (_, False) => new False(),
            (True, True) => new True(),
            _ => new Unknown(),
        };

        /// <summary>
        /// 三值逻辑或运算（Kleene 语义）/ Three-valued OR (Kleene semantics)
        /// </summary>
        /// <param name="other">另一个操作数 / Other operand</param>
        /// <returns>或运算结果 / OR result</returns>
        public Trivalent Or(Trivalent other) => (this, other) switch
        {
            (True, _) or (_, True) => new True(),
            (False, False) => new False(),
            _ => new Unknown(),
        };

        /// <summary>
        /// 三值逻辑非运算 / Three-valued NOT
        /// </summary>
        /// <returns>非运算结果 / NOT result</returns>
        public Trivalent Not() => this switch
        {
            True => new False(),
            False => new True(),
            _ => new Unknown(),
        };
    }

    /// <summary>
    /// 平衡三值逻辑类型 / Balanced three-valued logic type
    /// </summary>
    /// <remarks>
    /// 表示 True、False、Unknown 三种状态，使用 Int8 表示（+1, -1, 0）。
    /// Represents True, False, Unknown states using Int8 (+1, -1, 0).
    /// </remarks>
    public abstract record BalancedTrivalent
    {
        /// <summary>
        /// 获取数值表示 / Get numeric representation
        /// </summary>
        public abstract Int8 Value { get; }

        /// <summary>
        /// 是否为真 / Whether this is true
        /// </summary>
        public abstract bool? IsTrue { get; }

        /// <summary>
        /// 真值 (+1) / True value (+1)
        /// </summary>
        public sealed record True : BalancedTrivalent
        {
            public override Int8 Value => Int8.One;
            public override bool? IsTrue => true;
        }

        /// <summary>
        /// 假值 (-1) / False value (-1)
        /// </summary>
        public sealed record False : BalancedTrivalent
        {
            public override Int8 Value => -Int8.One;
            public override bool? IsTrue => false;
        }

        /// <summary>
        /// 未知值 (0) / Unknown value (0)
        /// </summary>
        public sealed record Unknown : BalancedTrivalent
        {
            public override Int8 Value => Int8.Zero;
            public override bool? IsTrue => null;
        }

        /// <summary>
        /// 从布尔值创建 / Create from boolean
        /// </summary>
        public static BalancedTrivalent Invoke(bool value) => value ? new True() : new False();

        /// <summary>
        /// 从可空布尔值创建 / Create from nullable boolean
        /// </summary>
        public static BalancedTrivalent Invoke(bool? value) => value switch
        {
            true => new True(),
            false => new False(),
            null => new Unknown(),
        };

        /// <summary>
        /// 从 Trivalent 创建 / Create from Trivalent
        /// </summary>
        public static BalancedTrivalent Invoke(Trivalent value) => value switch
        {
            Trivalent.True => new True(),
            Trivalent.False => new False(),
            Trivalent.Unknown => new Unknown(),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

        /// <summary>
        /// 三值逻辑与运算（Kleene 语义）/ Three-valued AND (Kleene semantics)
        /// </summary>
        public BalancedTrivalent And(BalancedTrivalent other) => (this, other) switch
        {
            (False, _) or (_, False) => new False(),
            (True, True) => new True(),
            _ => new Unknown(),
        };

        /// <summary>
        /// 三值逻辑或运算（Kleene 语义）/ Three-valued OR (Kleene semantics)
        /// </summary>
        public BalancedTrivalent Or(BalancedTrivalent other) => (this, other) switch
        {
            (True, _) or (_, True) => new True(),
            (False, False) => new False(),
            _ => new Unknown(),
        };

        /// <summary>
        /// 三值逻辑非运算 / Three-valued NOT
        /// </summary>
        public BalancedTrivalent Not() => this switch
        {
            True => new False(),
            False => new True(),
            _ => new Unknown(),
        };
    }
}
