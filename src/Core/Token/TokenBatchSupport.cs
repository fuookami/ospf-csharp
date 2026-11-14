#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Token;

internal static class TokenBatchSupport {
    internal static Try Add<T>(
        IEnumerable<IVariableItem> items,
        Func<VariableItemKey, bool> contains,
        Action<VariableItemKey, Token<T>> add,
        Func<VariableItemKey, Action> captureRollback,
        bool checkTokenExists,
        HashSet<VariableItemKey>? skipExisting,
        int firstIndex,
        IFlt64ValueConverter<T>? converter,
        out int nextIndex,
        out bool replaced)
        where T : struct, IRealNumber<T> {
        nextIndex = firstIndex;
        replaced = false;
        var entries = new List<(VariableItemKey Key, IVariableItem Item, int Index)>();
        var keys = new HashSet<VariableItemKey>();
        bool hasReplacement = false;
        try {
            if (items is null) {
                return Invalid();
            }
            foreach (IVariableItem item in items) {
                if (item is null || item.Key is not { } key) {
                    return Invalid();
                }
                bool first = keys.Add(key);
                if (skipExisting is not null && (skipExisting.Contains(key) || !first)) {
                    continue;
                }
                bool duplicate = contains(key) || !first;
                if (checkTokenExists && duplicate) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.TokenExisted,
                        "变量 token 已存在。 / The variable token already exists."
                    ));
                }
                hasReplacement |= duplicate;
                int index = checked(firstIndex + entries.Count);
                entries.Add((key, item, index));
            }
            nextIndex = checked(firstIndex + entries.Count);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Failure("准备 token 批次 / Prepare token batch", error);
        }

        var undo = new List<Action>();
        try {
            foreach ((VariableItemKey key, IVariableItem item, int index) in entries) {
                undo.Add(captureRollback(key));
                add(key, new Token<T>(
                    variable: item,
                    solverIndex: index,
                    refreshCallbacks: new Dictionary<object, Action<bool>>(),
                    converter: converter
                ));
            }
            if (skipExisting is not null) {
                foreach ((VariableItemKey key, _, _) in entries) {
                    skipExisting.Add(key);
                }
            }
            replaced = hasReplacement;
            return Results.OkInstance;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            nextIndex = firstIndex;
            Try failure = Failure("添加 token 批次 / Add token batch", error);
            try {
                for (int i = undo.Count - 1; i >= 0; --i) {
                    undo[i]();
                }
                return failure;
            }
            catch (Exception rollback) when (rollback is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                return new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    ((Failed<Success, ErrorCode, Error<ErrorCode>>)failure).Error,
                    ((Failed<Success, ErrorCode, Error<ErrorCode>>)Failure("回滚 token 批次 / Roll back token batch", rollback)).Error
                );
            }
        }
    }

    private static Try Invalid() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "token 批次或变量无效。 / The token batch or a variable is invalid."
    ));

    private static Try Failure(string operation, Exception error) => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.ApplicationError,
        $"{operation} 失败：{error.Message} / {operation} failed: {error.Message}"
    ));
}
