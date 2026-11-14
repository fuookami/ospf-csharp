#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Core.Symbol;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Token;

internal static class TokenRegistrationTransactionSupport {
    internal static Try AddSymbol<V>(IAbstractMutableTokenTable<V> table, IIntermediateSymbol symbol)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        table is ConcurrentAutoTokenTable<V> or ConcurrentManualTokenTable<V>
            ? ((ConcurrentMutableTokenTable<V>)table).AddSymbolInTransaction(symbol)
            : table.Add(symbol);

    internal static Try Run<V>(IAbstractMutableTokenTable<V> table, Func<Try> operation)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<ITokenRegistrationTransaction, ErrorCode, Error<ErrorCode>> started;
        try {
            started = table.BeginTokenRegistration() ?? Results.Failed<ITokenRegistrationTransaction>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "符号表事务返回空结果。 / The token-table transaction returned a null result."
            ));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Failure("开始符号表事务 / Begin token-table transaction", error);
        }
        if (!started.IsOk) {
            return started.Map(_ => Results.SuccessInstance);
        }
        if (started.Value is not { } transaction) {
            return Failure("开始符号表事务 / Begin token-table transaction", null);
        }

        Try result;
        try {
            result = operation() ?? Failure("注册符号 / Register symbols", null);
            if (result.IsOk) {
                result = transaction.Commit() ?? Failure("提交符号表事务 / Commit token-table transaction", null);
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            result = Failure("注册符号 / Register symbols", error);
        }
        if (result.IsOk) {
            return result;
        }
        Try rollback;
        try {
            rollback = transaction.Rollback() ?? Failure("回滚符号表事务 / Roll back token-table transaction", null);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            rollback = Failure("回滚符号表事务 / Roll back token-table transaction", error);
        }
        return rollback.IsOk
            ? result
            : new Fatal<Success, ErrorCode, Error<ErrorCode>>(Errors(result).Concat(Errors(rollback)).ToArray());
    }

    private static IEnumerable<Error<ErrorCode>> Errors(Try result) => result switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> failed => new[] { failed.Error },
        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => fatal.Errors,
        _ => Array.Empty<Error<ErrorCode>>(),
    };

    private static Try Failure(string operation, Exception? error) => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.ApplicationError,
        $"{operation} 失败：{error?.Message ?? "无效事务"} / {operation} failed: {error?.Message ?? "invalid transaction"}"
    ));
}
