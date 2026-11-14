#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Functional;
using ErrorCode = Fuookami.Ospf.Utils.Error.ErrorCode;
using RetError = Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>;
using RetBool = Fuookami.Ospf.Utils.Functional.Result<bool, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetBoolList = Fuookami.Ospf.Utils.Functional.Result<System.Collections.Generic.List<bool>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using ExRetBool = Fuookami.Ospf.Utils.Functional.ExResult<bool, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using ExRetBoolList = Fuookami.Ospf.Utils.Functional.ExResult<System.Collections.Generic.List<bool>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>并行 Find 扩展方法 / Parallel Find extension methods.</summary>
public static class ParallelFindExtensions {
    /// <summary>并行查找 / Parallel find.</summary>
    public static async Task<TSource?> FindParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        List<bool> results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        for (int i = 0; i < items.Count; i++) {
            if (results[i]) {
                return items[i];
            }
        }
        return default;
    }

    /// <summary>并行全满足 / Parallel all.</summary>
    public static async Task<bool> AllParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        List<bool> results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return results.All(x => x);
    }

    /// <summary>并行存在满足 / Parallel any.</summary>
    public static async Task<bool> AnyParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        List<bool> results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return results.Any(x => x);
    }

    /// <summary>并行无满足 / Parallel none.</summary>
    public static async Task<bool> NoneParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null)
        => !await source.AnyParallelly(predicate, concurrency).ConfigureAwait(false);

    /// <summary>并行计数 / Parallel count.</summary>
    public static async Task<int> CountParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        List<bool> results = await items.ExecuteWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return results.Count(x => x);
    }

    /// <summary>并行全满足（Result） / Parallel all with Result.</summary>
    public static async Task<RetBool> TryAllParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, RetBool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        RetBoolList results =
            await items.ExecuteTryWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return CombineTry(results, values => values.All(value => value));
    }

    /// <summary>并行存在满足（Result） / Parallel any with Result.</summary>
    public static async Task<RetBool> TryAnyParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, RetBool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        RetBoolList results =
            await items.ExecuteTryWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return CombineTry(results, values => values.Any(value => value));
    }

    /// <summary>并行无满足（Result） / Parallel none with Result.</summary>
    public static async Task<RetBool> TryNoneParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, RetBool> predicate,
        int? concurrency = null) {
        RetBool result = await source.TryAnyParallelly(predicate, concurrency).ConfigureAwait(false);
        return result.Map(value => !value);
    }

    /// <summary>并行全满足（收集全部错误） / Parallel all while collecting all errors.</summary>
    public static async Task<ExRetBool> ExTryAllParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, RetBool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        ExRetBoolList results =
            await items.ExecuteExTryWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return CombineEx(results, values => values.All(value => value));
    }

    /// <summary>并行存在满足（收集全部错误） / Parallel any while collecting all errors.</summary>
    public static async Task<ExRetBool> ExTryAnyParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, RetBool> predicate,
        int? concurrency = null) {
        var items = source.ToList();
        int c = concurrency ?? items.DefaultConcurrentAmount();
        ExRetBoolList results =
            await items.ExecuteExTryWithWorkerPool(c, (_, item) => Task.FromResult(predicate(item))).ConfigureAwait(false);
        return CombineEx(results, values => values.Any(value => value));
    }

    /// <summary>并行无满足（收集全部错误） / Parallel none while collecting all errors.</summary>
    public static async Task<ExRetBool> ExTryNoneParallelly<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, RetBool> predicate,
        int? concurrency = null) {
        ExRetBool result = await source.ExTryAnyParallelly(predicate, concurrency).ConfigureAwait(false);
        return result.Map(value => !value);
    }

    private static RetBool CombineTry(RetBoolList result, Func<List<bool>, bool> combine) =>
        result switch {
            Ok<List<bool>, ErrorCode, RetError> ok => Results.Ok(combine(ok.Value)),
            Failed<List<bool>, ErrorCode, RetError> failed => new Failed<bool, ErrorCode, RetError>(failed.Error),
            Fatal<List<bool>, ErrorCode, RetError> fatal => new Fatal<bool, ErrorCode, RetError>(fatal.Errors),
            _ => throw new InvalidOperationException("Unexpected result type."),
        };

    private static ExRetBool CombineEx(ExRetBoolList result, Func<List<bool>, bool> combine) =>
        result switch {
            Ok<List<bool>, ErrorCode, RetError> ok => new Ok<bool, ErrorCode, RetError>(combine(ok.Value)),
            Fatal<List<bool>, ErrorCode, RetError> fatal => new Fatal<bool, ErrorCode, RetError>(fatal.Errors),
            _ => throw new InvalidOperationException("Unexpected result type."),
        };
}
