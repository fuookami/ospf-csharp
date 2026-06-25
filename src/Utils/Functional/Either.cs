#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>
    /// Either 类型（左或右）/ Either type (left or right).
    /// Mirrors ospf-kotlin sealed class Either&lt;L, R&gt;.
    /// </summary>
    public abstract record Either<L, R>
    {
        /// <summary>左值 / Left value.</summary>
        public sealed record Left(L Value) : Either<L, R>;

        /// <summary>右值 / Right value.</summary>
        public sealed record Right(R Value) : Either<L, R>;

        /// <summary>是否为左 / Whether left.</summary>
        public bool IsLeft => this is Left;

        /// <summary>是否为右 / Whether right.</summary>
        public bool IsRight => this is Right;

        /// <summary>左值（若为 Left）/ Left value (if Left).</summary>
        public L? LeftValue => this is Left l ? l.Value : default;

        /// <summary>右值（若为 Right）/ Right value (if Right).</summary>
        public R? RightValue => this is Right r ? r.Value : default;

        /// <summary>映射左值 / Map left value.</summary>
        public Either<L2, R> MapLeft<L2>(Func<L, L2> f) =>
            this is Left l ? new Either<L2, R>.Left(f(l.Value)) : new Either<L2, R>.Right(((Right)this).Value);

        /// <summary>映射右值 / Map right value.</summary>
        public Either<L, R2> MapRight<R2>(Func<R, R2> f) =>
            this is Right r ? new Either<L, R2>.Right(f(r.Value)) : new Either<L, R2>.Left(((Left)this).Value);

        /// <summary>匹配 / Match.</summary>
        public T Match<T>(Func<L, T> onLeft, Func<R, T> onRight) =>
            this is Left l ? onLeft(l.Value) : onRight(((Right)this).Value);

        /// <summary>匹配（Action）/ Match (Action).</summary>
        public void Match(Action<L> onLeft, Action<R> onRight)
        {
            if (this is Left l) onLeft(l.Value);
            else onRight(((Right)this).Value);
        }
    }
}
