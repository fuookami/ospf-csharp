#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Token;

/// <summary>
/// 符号表注册与缓存预热支持 / Token table registration and cache warm-up support.
/// </summary>
public static class TokenTableRegistrationExtensions {
    /// <summary>
    /// 按依赖层级注册符号集合，同时预热缓存。
    /// Register symbol collection by dependency layers, warming caches concurrently.
    /// </summary>
    /// <param name="symbols">待注册的符号集合 / Symbol collection to register</param>
    /// <param name="tokenTable">可变符号表 / Mutable token table</param>
    /// <param name="fixedValues">可选固定值映射 / Optional fixed values map</param>
    /// <param name="callBack">可选进度回调 / Optional progress callback</param>
    /// <returns>注册结果 / Registration result</returns>
    public static Try Register(
        this IReadOnlyCollection<IIntermediateSymbol> symbols,
        IAbstractMutableTokenTable<Flt64> tokenTable,
        IReadOnlyDictionary<Fuookami.Ospf.Math.Symbol.ISymbol, Flt64>? fixedValues = null,
        Action<RegistrationStatus>? callBack = null) {
        if (symbols.Count == 0) {
            callBack?.Invoke(new RegistrationStatus(
                EmptySymbolAmount: UInt64.Zero,
                ReadySymbolAmount: UInt64.Zero,
                TotalSymbolAmount: UInt64.Zero));
            return Results.Ok(Results.SuccessInstance);
        }

        // 1. Register all symbols with the token table
        Try addResult = tokenTable.Add(symbols);
        if (addResult is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            return failed;
        }

        // 2. Classify symbols: empty (zero polynomial) vs. non-empty
        var emptySymbols = new HashSet<IIntermediateSymbol>();
        var nonEmptySymbols = new HashSet<IIntermediateSymbol>();
        foreach (IIntermediateSymbol symbol in symbols) {
            if (symbol is IIntermediateSymbol<Flt64> typedSymbol) {
                bool isEmpty = typedSymbol.Category == LinearCategory.Instance
                    && typedSymbol.LowerBound is null
                    && typedSymbol.UpperBound is null
                    && typedSymbol.FixedValue is null;
                if (isEmpty) {
                    emptySymbols.Add(symbol);
                }
                else {
                    nonEmptySymbols.Add(symbol);
                }
            }
            else {
                // Non-generic symbols are treated as empty (simple variables)
                emptySymbols.Add(symbol);
            }
        }

        // 3. Cache empty symbols immediately (zero polynomial → cache zero)
        foreach (IIntermediateSymbol symbol in emptySymbols) {
            tokenTable.Cache(symbol, (IReadOnlyList<Flt64>?)null, Flt64.Zero);
        }

        // 4. Build dependency graph for non-empty symbols (Kahn's algorithm)
        var dependencyMap = new Dictionary<IIntermediateSymbol, HashSet<IIntermediateSymbol>>();
        var reverseDeps = new Dictionary<IIntermediateSymbol, HashSet<IIntermediateSymbol>>();
        foreach (IIntermediateSymbol symbol in nonEmptySymbols) {
            dependencyMap[symbol] = new HashSet<IIntermediateSymbol>();
            if (!reverseDeps.ContainsKey(symbol)) {
                reverseDeps[symbol] = new HashSet<IIntermediateSymbol>();
            }
        }

        foreach (IIntermediateSymbol symbol in nonEmptySymbols) {
            if (symbol is IIntermediateSymbol<Flt64> typedSymbol) {
                foreach (IIntermediateSymbol dep in typedSymbol.Dependencies) {
                    if (nonEmptySymbols.Contains(dep)) {
                        dependencyMap[symbol].Add(dep);
                        if (!reverseDeps.ContainsKey(dep)) {
                            reverseDeps[dep] = new HashSet<IIntermediateSymbol>();
                        }
                        reverseDeps[dep].Add(symbol);
                    }
                }
            }
        }

        // 5. Topological processing with layer-based progress reporting
        var inDegree = new Dictionary<IIntermediateSymbol, int>();
        foreach (IIntermediateSymbol symbol in nonEmptySymbols) {
            inDegree[symbol] = dependencyMap.TryGetValue(symbol, out HashSet<IIntermediateSymbol>? deps)
                ? deps.Count
                : 0;
        }

        var queue = new Queue<IIntermediateSymbol>();
        foreach (IIntermediateSymbol symbol in nonEmptySymbols) {
            if (inDegree[symbol] == 0) {
                queue.Enqueue(symbol);
            }
        }

        var completedSymbols = new HashSet<IIntermediateSymbol>(emptySymbols);
        int readyCount = emptySymbols.Count;
        int totalCount = symbols.Count;

        while (queue.Count > 0) {
            IIntermediateSymbol current = queue.Dequeue();

            // Register auxiliary tokens if symbol supports it
            if (current is IIntermediateSymbol<Flt64> typedCurrent) {
                Try auxResult = typedCurrent.RegisterAuxiliaryTokens(tokenTable);
                if (auxResult is Failed<Success, ErrorCode, Error<ErrorCode>> auxFailed) {
                    return auxFailed;
                }

                // Evaluate fixed value or cache zero
                Flt64 cacheValue = Flt64.Zero;
                if (fixedValues is not null && typedCurrent.FixedValue is { } fv) {
                    cacheValue = fv;
                }
                tokenTable.Cache(current, (IReadOnlyList<Flt64>?)null, cacheValue);
            }
            else {
                tokenTable.Cache(current, (IReadOnlyList<Flt64>?)null, Flt64.Zero);
            }

            completedSymbols.Add(current);
            readyCount++;

            // Report progress
            callBack?.Invoke(new RegistrationStatus(
                EmptySymbolAmount: new UInt64((ulong)emptySymbols.Count),
                ReadySymbolAmount: new UInt64((ulong)readyCount),
                TotalSymbolAmount: new UInt64((ulong)totalCount)));

            // Process dependents
            if (reverseDeps.TryGetValue(current, out HashSet<IIntermediateSymbol>? dependents)) {
                foreach (IIntermediateSymbol dependent in dependents) {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0) {
                        queue.Enqueue(dependent);
                    }
                }
            }
        }

        // 6. Final progress report
        callBack?.Invoke(new RegistrationStatus(
            EmptySymbolAmount: new UInt64((ulong)emptySymbols.Count),
            ReadySymbolAmount: new UInt64((ulong)totalCount),
            TotalSymbolAmount: new UInt64((ulong)totalCount)));

        return Results.Ok(Results.SuccessInstance);
    }
}
