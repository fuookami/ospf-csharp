#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Error
{
    /// <summary>
    /// 错误接口 / Error interface.
    /// </summary>
    public interface IError<out C>
        where C : notnull
    {
        C Code { get; }
        string Message { get; }
        object? Value { get; }
        bool WithValue => Value is not null;
    }

    /// <summary>
    /// 错误基类（抽象记录）/ Error base class (abstract record).
    /// </summary>
    public abstract record Error<C> : IError<C>
        where C : notnull
    {
        public virtual C Code { get; init; } = default!;
        public virtual string Message { get; init; } = "";
        public virtual object? Value => null;
        public bool WithValue => Value is not null;

        public static Err<C> Invoke(C code, string message) => new(code, message);

        public override string ToString() =>
            Value is null ? $"{Code}: {Message}" : $"{Code}: {Message}({Value})";
    }

    /// <summary>
    /// 基本错误 / Basic error.
    /// </summary>
    public record Err<C> : Error<C>
        where C : notnull
    {
        public Err(C code, string? message = null)
        {
            Code = code;
            Message = message ?? code.ToString() ?? "";
        }

        public override C Code { get; init; }
        public override string Message { get; init; }

        public static Err<C> Invoke(C code, string? message = null) => new(code, message);
    }

    /// <summary>
    /// 延迟消息错误 / Lazy message error.
    /// </summary>
    public record LazyErr<C> : Err<C>
        where C : notnull
    {
        private readonly Lazy<string> _messageLazy;

        public LazyErr(C code, Lazy<string> messageLazy)
            : base(code, "")
        {
            _messageLazy = messageLazy;
        }

        public LazyErr(C code, Func<string> message)
            : this(code, new Lazy<string>(message))
        {
        }

        public override string Message => _messageLazy.Value;
    }

    /// <summary>
    /// 带关联值的错误 / Error with associated value.
    /// </summary>
    public record ExErr<C, T> : Error<C>
        where C : notnull
    {
        private readonly T _value;

        public ExErr(C code, string message, T value)
        {
            Code = code;
            Message = message;
            _value = value;
        }

        public ExErr(C code, T value)
            : this(code, code.ToString() ?? "", value)
        {
        }

        public override C Code { get; init; }
        public override string Message { get; init; }

        /// <summary>强类型关联值 / Strongly-typed associated value.</summary>
        public T TypedValue => _value;

        public override object? Value => _value;
    }

    /// <summary>
    /// 延迟消息和值的错误 / Error with lazy message and value.
    /// </summary>
    public record LazyExErr<C, T> : Error<C>
        where C : notnull
    {
        private readonly Lazy<string> _messageLazy;
        private readonly Lazy<T> _valueLazy;

        public LazyExErr(C code, Lazy<string> messageLazy, Lazy<T> valueLazy)
        {
            Code = code;
            _messageLazy = messageLazy;
            _valueLazy = valueLazy;
        }

        public LazyExErr(C code, Func<string> message, Func<T> value)
            : this(code, new Lazy<string>(message), new Lazy<T>(value))
        {
        }

        public LazyExErr(C code, Lazy<T> valueLazy)
            : this(code, new Lazy<string>(() => code.ToString() ?? ""), valueLazy)
        {
        }

        public LazyExErr(C code, Func<T> value)
            : this(code, new Lazy<T>(value))
        {
        }

        public override C Code { get; init; }
        public override string Message => _messageLazy.Value;

        /// <summary>强类型关联值 / Strongly-typed associated value.</summary>
        public T TypedValue => _valueLazy.Value;

        public override object? Value => _valueLazy.Value;
    }

    /// <summary>
    /// 外部库互操作异常桥接（仅限协议边界）/ External-library interop exception bridge (protocol boundary only).
    /// Per .rules §4.2.
    /// </summary>
    public sealed class ApplicationException : Exception
    {
        public Error<ErrorCode> Error { get; }

        public ApplicationException(Error<ErrorCode> error)
            : base(error.Message)
        {
            Error = error;
        }

        public override string ToString() => Error.ToString();
    }
}
