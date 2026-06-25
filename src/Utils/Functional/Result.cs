#nullable enable
#pragma warning disable CS8604 // Possible null reference argument — T is non-null by usage pattern

using Fuookami.Ospf.Utils.Error;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Functional;
// Type aliases (documented for reference):
//   Try    = Result<Success, ErrorCode, Error<ErrorCode>>
//   Ret<T> = Result<T, ErrorCode, Error<ErrorCode>>
//   ExTry  = ExResult<Success, ErrorCode, Error<ErrorCode>>
//   ExRet<T>= ExResult<T, ErrorCode, Error<ErrorCode>>

// ==================== Result<T,C,E> ====================
public abstract record Result<T, C, E> where C : notnull where E : Error<C> {
    public virtual bool IsOk => false;
    public virtual bool IsFailed => false;
    public virtual T Value { get; init; } = default!;
    public abstract Result<U, C, E> Map<U>(Func<T, U> transform);
    public abstract Result<U, C, E> Bind<U>(Func<T, Result<U, C, E>> transform);
}

// ==================== ExResult<T,C,E> ====================
public interface ExResult<T, C, E> where C : notnull where E : Error<C> {
    bool IsOk { get; }
    bool IsFailed { get; }
    bool IsWarned { get; }
    T? Value { get; }
    ExResult<U, C, E> Map<U>(Func<T, U> transform);
}

// ==================== Ok ====================
public sealed record Ok<T, C, E> : Result<T, C, E>, ExResult<T, C, E>
    where C : notnull where E : Error<C> {
    public Ok(T value) { Value = value; }
    public override bool IsOk => true;
    public override T Value { get; init; }
    bool ExResult<T, C, E>.IsOk => true;
    bool ExResult<T, C, E>.IsFailed => false;
    bool ExResult<T, C, E>.IsWarned => false;
    T ExResult<T, C, E>.Value => Value;
    public U? GetAs<U>() => Value is U u ? u : default;
    public override Result<U, C, E> Map<U>(Func<T, U> f) => new Ok<U, C, E>(f(Value));
    public override Result<U, C, E> Bind<U>(Func<T, Result<U, C, E>> f) => f(Value);
    ExResult<U, C, E> ExResult<T, C, E>.Map<U>(Func<T, U> f) => new Ok<U, C, E>(f(Value));
}

// ==================== Failed ====================
public sealed record Failed<T, C, E>(E Error) : Result<T, C, E>, ExResult<T, C, E>
    where C : notnull where E : Error<C> {
    public Failed(ErrorCode code, string? message = null) : this((E)(object)new Err<ErrorCode>(code, message)) { }
    public Failed(ErrorCode code, string? message, object? value) : this((E)(object)new ExErr<ErrorCode, object>(code, message ?? code.ToReadableString(), value!)) { }
    public override bool IsFailed => true;
    bool ExResult<T, C, E>.IsOk => false;
    bool ExResult<T, C, E>.IsFailed => true;
    bool ExResult<T, C, E>.IsWarned => false;
    T? ExResult<T, C, E>.Value => default;
    public C Code => Error.Code;
    public string Message => Error.Message;
    public bool WithValue => Error.WithValue;
    public object? ErrValue => Error.Value;
    public override Result<U, C, E> Map<U>(Func<T, U> f) => new Failed<U, C, E>(Error);
    public override Result<U, C, E> Bind<U>(Func<T, Result<U, C, E>> f) => new Failed<U, C, E>(Error);
    ExResult<U, C, E> ExResult<T, C, E>.Map<U>(Func<T, U> f) => new Failed<U, C, E>(Error);
}

// ==================== Fatal ====================
public sealed record Fatal<T, C, E>(IReadOnlyList<E> Errors) : Result<T, C, E>, ExResult<T, C, E>
    where C : notnull where E : Error<C> {
    public Fatal(params E[] errors) : this((IReadOnlyList<E>)errors) { }
    public Fatal(ErrorCode code, string? message = null) : this(new[] { (E)(object)new Err<ErrorCode>(code, message) }) { }
    public Fatal(ErrorCode code, string? message, object? value) : this(new[] { (E)(object)new ExErr<ErrorCode, object>(code, message ?? code.ToReadableString(), value!) }) { }
    public override bool IsFailed => true;
    bool ExResult<T, C, E>.IsOk => false;
    bool ExResult<T, C, E>.IsFailed => true;
    bool ExResult<T, C, E>.IsWarned => false;
    T? ExResult<T, C, E>.Value => default;
    public E? FirstError => Errors.FirstOrDefault();
    public int Size => Errors.Count;
    public bool IsEmpty => Errors.Count == 0;
    public override Result<U, C, E> Map<U>(Func<T, U> f) => new Fatal<U, C, E>(Errors);
    public override Result<U, C, E> Bind<U>(Func<T, Result<U, C, E>> f) => new Fatal<U, C, E>(Errors);
    ExResult<U, C, E> ExResult<T, C, E>.Map<U>(Func<T, U> f) => new Fatal<U, C, E>(Errors);
    public Fatal<T, C, E> Merge(Fatal<T, C, E> other) => new(Errors.Concat(other.Errors).ToList());
    public void ForEach(Action<E> action) {
        foreach (E e in Errors) {
            action(e);
        }
    }
}

// ==================== Warn ====================
public sealed record Warn<T, C, E>(T Value, IReadOnlyList<E> Warnings) : ExResult<T, C, E>
    where C : notnull where E : Error<C> {
    public Warn(T value, ErrorCode code, string? message = null) : this(value, new[] { (E)(object)new Err<ErrorCode>(code, message) }) { }
    bool ExResult<T, C, E>.IsOk => false;
    bool ExResult<T, C, E>.IsFailed => false;
    bool ExResult<T, C, E>.IsWarned => true;
    T? ExResult<T, C, E>.Value => Value;
    public E? FirstWarning => Warnings.FirstOrDefault();
    public int Size => Warnings.Count;
    public bool IsEmpty => Warnings.Count == 0;
    public C Code => FirstWarning!.Code;
    public string? WarningMessage => FirstWarning?.Message;
    public object? WarningValue => FirstWarning?.Value;
    public bool WithWarningValue => FirstWarning?.WithValue ?? false;
    public U? GetAs<U>() => Value is U u ? u : default;
    ExResult<U, C, E> ExResult<T, C, E>.Map<U>(Func<T, U> f) => new Warn<U, C, E>(f(Value), Warnings);
}

// ==================== Success ====================
public sealed record Success;

// ==================== Results (factory) ====================
public static class Results {
    public static readonly Success SuccessInstance = new();
    public static Result<Success, ErrorCode, Error<ErrorCode>> OkInstance => new Ok<Success, ErrorCode, Error<ErrorCode>>(SuccessInstance);
    public static ExResult<Success, ErrorCode, Error<ErrorCode>> ExOkInstance => (ExResult<Success, ErrorCode, Error<ErrorCode>>)OkInstance;
    public static Result<T, ErrorCode, Error<ErrorCode>> Ok<T>(T value) => new Ok<T, ErrorCode, Error<ErrorCode>>(value);
    public static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(Error<ErrorCode> error) => new Failed<T, ErrorCode, Error<ErrorCode>>(error);

    // Run
    public static Result<Success, ErrorCode, Error<ErrorCode>> Run(params Func<Result<Success, ErrorCode, Error<ErrorCode>>>[] blocks) {
        foreach (Func<Try> block in blocks) {
            Try r = block(); if (r.IsFailed) {
                return r;
            }
        }
        return OkInstance;
    }
    public static async Task<Result<Success, ErrorCode, Error<ErrorCode>>> SyncRun(params Func<Task<Result<Success, ErrorCode, Error<ErrorCode>>>>[] blocks) {
        foreach (Func<Task<Try>> block in blocks) {
            Try r = await block().ConfigureAwait(false); if (r.IsFailed) {
                return r;
            }
        }
        return OkInstance;
    }
    public static Result<Success, ErrorCode, Error<ErrorCode>> Run(IEnumerable<Func<Result<Success, ErrorCode, Error<ErrorCode>>>> blocks) => Run(blocks.ToArray());
    public static Result<T, ErrorCode, Error<ErrorCode>> Run<T>(Func<Result<Success, ErrorCode, Error<ErrorCode>>>[] blocks, Func<Result<T, ErrorCode, Error<ErrorCode>>> lastBlock) {
        foreach (Func<Try> block in blocks) {
            Try r = block();
            if (r.IsFailed) { return new Failed<T, ErrorCode, Error<ErrorCode>>(ExtractError(r)); }
        }
        return lastBlock();
    }
    public static async Task<Result<T, ErrorCode, Error<ErrorCode>>> SyncRun<T>(Func<Task<Result<Success, ErrorCode, Error<ErrorCode>>>>[] blocks, Func<Task<Result<T, ErrorCode, Error<ErrorCode>>>> lastBlock) {
        foreach (Func<Task<Try>> block in blocks) {
            Try r = await block().ConfigureAwait(false);
            if (r.IsFailed) { return new Failed<T, ErrorCode, Error<ErrorCode>>(ExtractError(r)); }
        }
        return await lastBlock().ConfigureAwait(false);
    }

    // ExRun
    public static ExResult<Success, ErrorCode, Error<ErrorCode>> ExRun(params Func<ExResult<Success, ErrorCode, Error<ErrorCode>>>[] blocks) {
        foreach (Func<ExResult<Success, ErrorCode, Error<ErrorCode>>> block in blocks) {
            ExResult<Success, ErrorCode, Error<ErrorCode>> r = block(); if (r.IsFailed) {
                return r;
            }
        }
        return ExOkInstance;
    }
    public static async Task<ExResult<Success, ErrorCode, Error<ErrorCode>>> ExSyncRun(params Func<Task<ExResult<Success, ErrorCode, Error<ErrorCode>>>>[] blocks) {
        foreach (Func<Task<ExResult<Success, ErrorCode, Error<ErrorCode>>>> block in blocks) {
            ExResult<Success, ErrorCode, Error<ErrorCode>> r = await block().ConfigureAwait(false); if (r.IsFailed) {
                return r;
            }
        }
        return ExOkInstance;
    }
    public static ExResult<T, ErrorCode, Error<ErrorCode>> ExRun<T>(Func<ExResult<Success, ErrorCode, Error<ErrorCode>>>[] blocks, Func<ExResult<T, ErrorCode, Error<ErrorCode>>> lastBlock) {
        foreach (Func<ExResult<Success, ErrorCode, Error<ErrorCode>>> block in blocks) {
            ExResult<Success, ErrorCode, Error<ErrorCode>> r = block();
            if (r.IsFailed) { return new Failed<T, ErrorCode, Error<ErrorCode>>(ExtractExError(r)); }
        }
        return lastBlock();
    }
    public static async Task<ExResult<T, ErrorCode, Error<ErrorCode>>> ExSyncRun<T>(Func<Task<ExResult<Success, ErrorCode, Error<ErrorCode>>>>[] blocks, Func<Task<ExResult<T, ErrorCode, Error<ErrorCode>>>> lastBlock) {
        foreach (Func<Task<ExResult<Success, ErrorCode, Error<ErrorCode>>>> block in blocks) {
            ExResult<Success, ErrorCode, Error<ErrorCode>> r = await block().ConfigureAwait(false);
            if (r.IsFailed) { return new Failed<T, ErrorCode, Error<ErrorCode>>(ExtractExError(r)); }
        }
        return await lastBlock().ConfigureAwait(false);
    }

    private static Error<ErrorCode> ExtractError(Result<Success, ErrorCode, Error<ErrorCode>> result) => result switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> f => f.Error,
        Fatal<Success, ErrorCode, Error<ErrorCode>> fat => fat.FirstError!,
        _ => new Err<ErrorCode>(ErrorCode.Unknown),
    };
    private static Error<ErrorCode> ExtractExError(ExResult<Success, ErrorCode, Error<ErrorCode>> result) => result switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> f => f.Error,
        Fatal<Success, ErrorCode, Error<ErrorCode>> fat => fat.FirstError!,
        _ => new Err<ErrorCode>(ErrorCode.Unknown),
    };
}

// ==================== Extension methods ====================
public static class ResultExtensions {
    public static Result<T, C, E> IfOk<T, C, E>(this Result<T, C, E> r, Action<Ok<T, C, E>> a) where C : notnull where E : Error<C> { if (r is Ok<T, C, E> ok) { a(ok); } return r; }
    public static Result<T, C, E> IfFailed<T, C, E>(this Result<T, C, E> r, Action<Failed<T, C, E>> a) where C : notnull where E : Error<C> { if (r is Failed<T, C, E> f) { a(f); } return r; }
    public static Result<T, C, E> IfFatal<T, C, E>(this Result<T, C, E> r, Action<Fatal<T, C, E>> a) where C : notnull where E : Error<C> { if (r is Fatal<T, C, E> fat) { a(fat); } return r; }
    public static void IfOk<T, C, E>(this ExResult<T, C, E> r, Action<Ok<T, C, E>> a) where C : notnull where E : Error<C> {
        if (r is Ok<T, C, E> ok) {
            a(ok);
        }
    }
    public static void IfFailed<T, C, E>(this ExResult<T, C, E> r, Action<Failed<T, C, E>> a) where C : notnull where E : Error<C> {
        if (r is Failed<T, C, E> f) {
            a(f);
        }
    }
    public static void IfFatal<T, C, E>(this ExResult<T, C, E> r, Action<Fatal<T, C, E>> a) where C : notnull where E : Error<C> {
        if (r is Fatal<T, C, E> fat) {
            a(fat);
        }
    }
    public static void IfWarned<T, C, E>(this ExResult<T, C, E> r, Action<Warn<T, C, E>> a) where C : notnull where E : Error<C> {
        if (r is Warn<T, C, E> w) {
            a(w);
        }
    }

    // LINQ (rules §8.3)
    public static Result<U, C, E> Select<T, U, C, E>(this Result<T, C, E> r, Func<T, U> s) where C : notnull where E : Error<C> => r.Map(s);
    public static Result<V, C, E> SelectMany<T, U, V, C, E>(this Result<T, C, E> r, Func<T, Result<U, C, E>> s, Func<T, U, V> p) where C : notnull where E : Error<C> => r.Bind(t => s(t).Map(u => p(t, u)));

    // Async (rules §8.4)
    public static async Task<Result<U, C, E>> BindAsync<T, U, C, E>(this Result<T, C, E> r, Func<T, Task<Result<U, C, E>>> f) where C : notnull where E : Error<C> =>
        r switch { Ok<T, C, E> ok => await f(ok.Value).ConfigureAwait(false), Failed<T, C, E> fd => new Failed<U, C, E>(fd.Error), Fatal<T, C, E> fat => new Fatal<U, C, E>(fat.Errors), _ => throw new InvalidOperationException() };
    public static async Task<Result<U, C, E>> MapAsync<T, U, C, E>(this Result<T, C, E> r, Func<T, Task<U>> f) where C : notnull where E : Error<C> =>
        r switch { Ok<T, C, E> ok => new Ok<U, C, E>(await f(ok.Value).ConfigureAwait(false)), Failed<T, C, E> fd => new Failed<U, C, E>(fd.Error), Fatal<T, C, E> fat => new Fatal<U, C, E>(fat.Errors), _ => throw new InvalidOperationException() };
}
#pragma warning restore CS8604
