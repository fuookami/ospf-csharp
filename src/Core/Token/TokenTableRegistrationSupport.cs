#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Token;

/// <summary>符号表注册与缓存预热支持 / Token-table registration and cache warm-up support.</summary>
public static class TokenTableRegistrationExtensions {
    /// <summary>按依赖层注册 Flt64 符号并准备缓存 / Register Flt64 symbols by dependency layer and prepare caches.</summary>
    /// <param name="symbols">待注册的符号 / Symbols to register.</param>
    /// <param name="tokenTable">可变符号表 / Mutable token table.</param>
    /// <param name="fixedValues">可选固定值映射 / Optional fixed values.</param>
    /// <param name="callBack">可选进度回调 / Optional progress callback.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public static Try Register(
        this IReadOnlyCollection<IIntermediateSymbol> symbols,
        IAbstractMutableTokenTable<Flt64> tokenTable,
        IReadOnlyDictionary<ISymbol, Flt64>? fixedValues = null,
        RegistrationStatusCallBack? callBack = null) => Register(
            symbols: symbols,
            tokenTable: tokenTable,
            converter: IIntoValue<Flt64>.Identity,
            fixedValues: fixedValues,
            callBack: callBack
        );

    /// <summary>按依赖层注册泛型符号并准备缓存 / Register generic symbols by dependency layer and prepare caches.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="symbols">待注册的符号 / Symbols to register.</param>
    /// <param name="tokenTable">可变符号表 / Mutable token table.</param>
    /// <param name="converter">缓存 token 值的转换器 / Converter for cached token values.</param>
    /// <param name="fixedValues">可选固定值映射 / Optional fixed values.</param>
    /// <param name="callBack">可选进度回调 / Optional progress callback.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public static Try Register<V>(
        this IReadOnlyCollection<IIntermediateSymbol> symbols,
        IAbstractMutableTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        IReadOnlyDictionary<ISymbol, V>? fixedValues = null,
        RegistrationStatusCallBack? callBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> => RegisterTransactional(
            symbols: symbols,
            tokenTable: tokenTable,
            converter: converter,
            fixedValues: fixedValues,
            callBack: callBack,
            hidden: null
        );

    internal static Try RegisterWithHiddenDependencies<V>(
        IReadOnlyCollection<IIntermediateSymbol> symbols,
        IAbstractMutableTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        IReadOnlyDictionary<ISymbol, V>? fixedValues,
        RegistrationStatusCallBack? callBack,
        IReadOnlySet<IIntermediateSymbol> hidden)
        where V : struct, IRealNumber<V>, INumberField<V> => RegisterTransactional(
            symbols: symbols,
            tokenTable: tokenTable,
            converter: converter,
            fixedValues: fixedValues,
            callBack: callBack,
            hidden: hidden
        );

    private static Try RegisterTransactional<V>(
        IReadOnlyCollection<IIntermediateSymbol> symbols,
        IAbstractMutableTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        IReadOnlyDictionary<ISymbol, V>? fixedValues,
        RegistrationStatusCallBack? callBack,
        IReadOnlySet<IIntermediateSymbol>? hidden)
        where V : struct, IRealNumber<V>, INumberField<V> => tokenTable is null
        ? InvalidSymbols()
        : TokenRegistrationTransactionSupport.Run(tokenTable, () => RegisterCore(
            symbols: symbols,
            tokenTable: tokenTable,
            converter: converter,
            fixedValues: fixedValues,
            callBack: callBack,
            hidden: hidden
        ));

    private static Try RegisterCore<V>(
        IReadOnlyCollection<IIntermediateSymbol> symbols,
        IAbstractMutableTokenTable<V> tokenTable,
        IFlt64ValueConverter<V> converter,
        IReadOnlyDictionary<ISymbol, V>? fixedValues,
        RegistrationStatusCallBack? callBack,
        IReadOnlySet<IIntermediateSymbol>? hidden)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (symbols is null || tokenTable is null || converter is null) {
            return InvalidSymbols();
        }
        ConcurrentMutableTokenTable<V>? working = hidden is null ? null : tokenTable as ConcurrentMutableTokenTable<V>;
        if (hidden is not null && working is null) {
            return InvalidSymbols();
        }
        var existing = working?.TokenList.Tokens.Select(token => token.Key).ToHashSet();
        try {
            if (!NumericValueValidation.IsValid(converter.Zero) ||
                fixedValues is not null && fixedValues.Any(pair => pair.Key is null || !NumericValueValidation.IsValid(pair.Value))) {
                return InvalidNumericValue();
            }
        }
        catch (ArithmeticException) {
            return InvalidNumericValue();
        }
        catch (InvalidOperationException) {
            return InvalidNumericValue();
        }
        var ordered = new List<IIntermediateSymbol<V>>(symbols.Count);
        var all = new HashSet<IIntermediateSymbol>();
        var functionInputs = new Dictionary<IIntermediateSymbol, IReadOnlySet<ISymbol>>();
        foreach (IIntermediateSymbol symbol in symbols) {
            if (symbol is not IIntermediateSymbol<V> typed || !all.Add(symbol)) {
                return InvalidSymbols();
            }
            if (typed is LinearFunctionSymbolAdapter<V> or IQuadraticMathFunctionSymbol<V>) {
                IReadOnlyCollection<ISymbol>? inputs = typed switch {
                    LinearFunctionSymbolAdapter<V> adapter => adapter.InputSymbols,
                    IQuadraticMathFunctionSymbol<V> quadratic => quadratic.InputSymbols,
                    _ => null,
                };
                if (inputs is null || inputs.Any(input => input is null)) {
                    return InvalidFunctionInputs();
                }
                functionInputs.Add(symbol, inputs.ToHashSet());
            }
            ordered.Add(typed);
        }

        List<IIntermediateSymbol<V>> empty;
        try {
            empty = ordered.Where(IsEmpty).ToList();
            if (fixedValues is not null && empty.Any(symbol =>
                    fixedValues.TryGetValue(symbol, out V value) && !value.Eq(converter.Zero))) {
                return InvalidFixedValue();
            }
        }
        catch (ArithmeticException) {
            return InvalidNumericValue();
        }
        catch (InvalidOperationException) {
            return InvalidNumericValue();
        }
        var emptySet = empty.Cast<IIntermediateSymbol>().ToHashSet();
        var remaining = new Dictionary<IIntermediateSymbol, int>();
        var dependents = new Dictionary<IIntermediateSymbol, List<IIntermediateSymbol>>();
        var depths = new Dictionary<IIntermediateSymbol, int>();
        foreach (IIntermediateSymbol<V> symbol in ordered.Where(symbol => !emptySet.Contains(symbol))) {
            int count = 0;
            IReadOnlySet<IIntermediateSymbol> dependencies = symbol switch {
                LinearFunctionSymbolAdapter<V> adapter => functionInputs[symbol]
                    .Concat(adapter.Polynomial.Monomials.Select(monomial => monomial.Symbol))
                    .OfType<IIntermediateSymbol>().ToHashSet(),
                IQuadraticMathFunctionSymbol<V> quadratic => functionInputs[symbol]
                    .Concat(quadratic.Polynomial.Monomials.SelectMany(monomial =>
                        monomial.Symbol2 is { } second ? new[] { monomial.Symbol1, second } : new[] { monomial.Symbol1 }))
                    .OfType<IIntermediateSymbol>().Concat(symbol.Dependencies).ToHashSet(),
                _ => symbol.Dependencies,
            };
            foreach (IIntermediateSymbol dependency in dependencies) {
                if (dependency is null || !all.Contains(dependency)) {
                    return InvalidDependencies();
                }
                if (emptySet.Contains(dependency)) {
                    continue;
                }
                count++;
                if (!dependents.TryGetValue(dependency, out List<IIntermediateSymbol>? list)) {
                    list = new List<IIntermediateSymbol>();
                    dependents.Add(dependency, list);
                }
                list.Add(symbol);
            }
            remaining.Add(symbol, count);
            depths.Add(symbol, 0);
        }

        var queue = new Queue<IIntermediateSymbol>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        int completed = 0;
        int maximumDepth = -1;
        while (queue.TryDequeue(out IIntermediateSymbol? symbol)) {
            completed++;
            int depth = depths[symbol];
            maximumDepth = System.Math.Max(maximumDepth, depth);
            if (dependents.TryGetValue(symbol, out List<IIntermediateSymbol>? list)) {
                foreach (IIntermediateSymbol dependent in list) {
                    depths[dependent] = System.Math.Max(depths[dependent], depth + 1);
                    if (--remaining[dependent] == 0) {
                        queue.Enqueue(dependent);
                    }
                }
            }
        }
        if (completed != remaining.Count) {
            return InvalidDependencies();
        }

        var proofSources = new HashSet<IIntermediateSymbol>();
        var proofPending = new Stack<IIntermediateSymbol>(ordered
            .OfType<LinearFunctionSymbolAdapter<V>>()
            .Where(adapter => adapter.Function is IModelAwareFunctionPreparation<V>)
            .SelectMany(adapter => adapter.Dependencies));
        while (proofPending.TryPop(out IIntermediateSymbol? dependency)) {
            if (proofSources.Add(dependency) && dependency is IIntermediateSymbol<V> typed) {
                foreach (IIntermediateSymbol nested in typed.Dependencies) {
                    proofPending.Push(nested);
                }
            }
        }

        var layers = Enumerable.Range(0, maximumDepth + 1)
            .Select(_ => new List<IIntermediateSymbol<V>>())
            .ToList();
        foreach (IIntermediateSymbol<V> symbol in ordered) {
            if (!emptySet.Contains(symbol)) {
                layers[depths[symbol]].Add(symbol);
            }
        }

        var values = new PreparedValues<V>(fixedValues);
        if (fixedValues is null || fixedValues.Count == 0) {
            try {
                foreach (Token<V> token in tokenTable.Tokens) {
                    if (token.ResultWith(converter) is { } value) {
                        if (!NumericValueValidation.IsValid(value)) {
                            return InvalidNumericValue();
                        }
                        values.TryAdd(token.Variable, value);
                    }
                }
            }
            catch (ArithmeticException) {
                return InvalidNumericValue();
            }
            catch (InvalidOperationException) {
                return InvalidNumericValue();
            }
        }
        var modelValues = values.Where(pair => pair.Key is IVariableItem)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var proofCandidates = new List<(LinearFunctionSymbolAdapter<V> Adapter, V Value)>();
        // 消费前重验先前层的证据 / Revalidate earlier-layer evidence before consumption.
        void RefreshProofs() {
            modelValues.Clear();
            foreach ((ISymbol key, V value) in values) {
                if (key is IVariableItem) {
                    modelValues.TryAdd(key, value);
                }
            }
            foreach ((LinearFunctionSymbolAdapter<V> adapter, V value) in proofCandidates) {
                switch (adapter.Function) {
                    case AbsFunction<V> abs when abs.ProvesResult(value, modelValues):
                        modelValues.TryAdd(abs.ResultVar, value);
                        break;
                    case IfFunction<V> condition when condition.ProvesResult(
                        result: value,
                        inputs: values,
                        modelValues: modelValues
                    ):
                        modelValues.TryAdd(condition.ResultVar, value);
                        break;
                    case SigmoidFunction<V> sigmoid when sigmoid.ProvesResult(
                        result: value,
                        inputs: values,
                        modelValues: modelValues
                    ):
                        modelValues.TryAdd(sigmoid.IndicatorVar, value);
                        break;
                    case IUnivariatePiecewiseFunction<V> piecewise when piecewise.Piecewise.ProvesResult(
                        result: value,
                        inputs: values,
                        modelValues: modelValues,
                        converter: converter
                    ):
                        modelValues.TryAdd(piecewise.Piecewise.ResultVar, value);
                        break;
                    case BivariateLinearPiecewiseFunction<V> surface when surface.TryGetEvidence(
                        result: value,
                        inputs: values,
                        modelValues: modelValues,
                        evidence: out IReadOnlyDictionary<ISymbol, V> inferred
                    ):
                        foreach ((ISymbol variable, V inferredValue) in inferred) {
                            modelValues.TryAdd(variable, inferredValue);
                        }
                        break;
                    case ICardinalityFunction<V> cardinality when cardinality.TryGetEvidence(
                        result: value,
                        inputs: values,
                        modelValues: modelValues,
                        converter: converter,
                        evidence: out IReadOnlyDictionary<ISymbol, V> inferred
                    ):
                        foreach ((ISymbol variable, V inferredValue) in inferred) {
                            modelValues.TryAdd(variable, inferredValue);
                        }
                        break;
                }
            }
        }
        foreach (IIntermediateSymbol<V> symbol in ordered) {
            if (hidden?.Contains(symbol) == true) {
                continue;
            }
            Try added = TokenRegistrationTransactionSupport.AddSymbol(tokenTable, symbol);
            if (added.IsFailed) {
                return added;
            }
        }

        foreach (IIntermediateSymbol<V> symbol in empty) {
            tokenTable.Cache(
                cacheKey: symbol,
                solution: (IReadOnlyList<V>?)null,
                value: converter.Zero
            );
            values.TryAdd(symbol, converter.Zero);
            CacheContext(tokenTable, symbol);
        }

        int emptyCount = empty.Count(symbol => hidden?.Contains(symbol) != true);
        int total = ordered.Count - (hidden?.Count ?? 0);
        int ready = emptyCount;
        Try status = Notify(
            callBack: callBack,
            empty: emptyCount,
            ready: ready,
            total: total
        );
        if (status.IsFailed) {
            return status;
        }

        foreach (List<IIntermediateSymbol<V>> layer in layers) {
            foreach (IIntermediateSymbol<V> symbol in layer) {
                Try auxiliary = working is not null && existing is not null
                    ? working.RegisterAuxiliaryTokensSkippingExisting(symbol, existing)
                    : symbol.RegisterAuxiliaryTokens(tokenTable);
                if (auxiliary.IsFailed) {
                    return auxiliary;
                }
            }

            foreach (IIntermediateSymbol<V> symbol in layer) {
                tokenTable.ClearCachedValue(symbol, (IReadOnlyList<V>?)null);
            }
            var preparedValues = new List<(IIntermediateSymbol<V> Symbol, V? Value)>();
            foreach (IIntermediateSymbol<V> symbol in layer) {
                FunctionInputValues<V>? functionValues = symbol is LinearFunctionSymbolAdapter<V> or IQuadraticMathFunctionSymbol<V>
                    ? new FunctionInputValues<V>(values, functionInputs[symbol])
                    : null;
                V? prepared = null;
                Try? preparationFailure = null;
                try {
                    // 输入来源已确定，空视图不得再次回退旧 token。 / The source is fixed; an empty view must not fall back to stale tokens.
                    if (symbol is LinearFunctionSymbolAdapter<V> adapter && functionValues is { } inputValues) {
                        if (adapter.Function is IModelAwareFunctionPreparation<V>) {
                            RefreshProofs();
                        }
                        Result<V?, ErrorCode, Error<ErrorCode>> evaluated = adapter.TryPrepare(
                            inputs: inputValues,
                            modelValues: modelValues,
                            converter: converter
                        );
                        if (evaluated is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
                            preparationFailure = Results.Failed<Success>(failed.Error);
                        }
                        else if (evaluated is Fatal<V?, ErrorCode, Error<ErrorCode>> fatal) {
                            preparationFailure = new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
                        }
                        else {
                            prepared = evaluated.Value;
                        }
                    }
                    else if (symbol is IQuadraticMathFunctionSymbol<V> quadratic && functionValues is { } quadraticInputs) {
                        Result<V?, ErrorCode, Error<ErrorCode>> evaluated = quadratic.TryEvaluate(quadraticInputs);
                        if (evaluated is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
                            preparationFailure = Results.Failed<Success>(failed.Error);
                        }
                        else if (evaluated is Fatal<V?, ErrorCode, Error<ErrorCode>> fatal) {
                            preparationFailure = new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
                        }
                        else {
                            prepared = evaluated.Value;
                        }
                    }
                    else {
                        prepared = symbol.Prepare(
                            values: values,
                            tokenTable: tokenTable,
                            converter: converter
                        );
                    }
                    if (functionValues?.MissingIndexer == true) {
                        prepared = null;
                    }
                    if (prepared is { } numericValue && !NumericValueValidation.IsValid(numericValue)) {
                        preparationFailure = InvalidNumericValue();
                    }
                    else if (prepared is { } value && fixedValues is not null &&
                        fixedValues.TryGetValue(symbol, out V fixedValue) &&
                        !(typeof(V) == typeof(RtnX)
                            ? StructuralValueEquality<V>.Same(fixedValue, value)
                            : fixedValue.Eq(value))) {
                        preparationFailure = InvalidFixedValue();
                    }
                }
                catch (KeyNotFoundException) when (functionValues?.MissingIndexer == true) {
                    prepared = null;
                }
                catch (ArithmeticException) {
                    preparationFailure = InvalidNumericValue();
                }
                catch (InvalidOperationException) {
                    preparationFailure = InvalidNumericValue();
                }
                if (functionValues?.Undeclared == true) {
                    return InvalidFunctionInputs();
                }
                if (preparationFailure is not null) {
                    return preparationFailure;
                }
                preparedValues.Add((symbol, prepared));
            }
            foreach ((IIntermediateSymbol<V> symbol, V? prepared) in preparedValues) {
                if (proofSources.Contains(symbol) &&
                    symbol is LinearFunctionSymbolAdapter<V> adapter &&
                    adapter.Function is AbsFunction<V> or IfFunction<V> or SigmoidFunction<V> or IUnivariatePiecewiseFunction<V> or BivariateLinearPiecewiseFunction<V> or ICardinalityFunction<V> &&
                    prepared is { } value) {
                    proofCandidates.Add((adapter, value));
                }
            }
            foreach ((IIntermediateSymbol<V> symbol, V? prepared) in preparedValues) {
                if (prepared is { } value) {
                    tokenTable.Cache(
                        cacheKey: symbol,
                        solution: (IReadOnlyList<V>?)null,
                        value: value
                    );
                    values.TryAdd(symbol, value);
                }
                else {
                    tokenTable.ClearCachedValue(symbol, (IReadOnlyList<V>?)null);
                }
                CacheContext(tokenTable, symbol);
            }
            int visible = layer.Count(symbol => hidden?.Contains(symbol) != true);
            if (visible == 0) {
                continue;
            }
            ready += visible;
            status = Notify(
                callBack: callBack,
                empty: emptyCount,
                ready: ready,
                total: total
            );
            if (status.IsFailed) {
                return status;
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private static bool IsEmpty<V>(IIntermediateSymbol<V> symbol)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        symbol is not IMathFunctionSymbolBase<V> &&
        symbol is not IQuadraticMathFunctionSymbolBase<V> &&
        symbol is ILinearIntermediateSymbol<V> linear &&
        linear.Polynomial is { } polynomial &&
        polynomial.Monomials.Count == 0 && polynomial.Constant.Eq(polynomial.Constant.Constants.Zero);

    private static Try Notify(RegistrationStatusCallBack? callBack, int empty, int ready, int total) =>
        callBack is null
            ? Results.OkInstance
            : callBack.Invoke(new RegistrationStatus(
                EmptySymbolAmount: new UInt64((ulong)empty),
                ReadySymbolAmount: new UInt64((ulong)ready),
                TotalSymbolAmount: new UInt64((ulong)total)
            )) ?? Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "注册回调返回空结果。 / The registration callback returned a null result."
            ));

    private static Try InvalidSymbols() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "符号重复或数值类型不受支持 / Duplicate symbol or unsupported numeric type"
    ));

    private static Try InvalidDependencies() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "符号依赖缺失或存在循环 / Symbol dependencies are missing or cyclic"
    ));

    private static Try InvalidFixedValue() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "固定符号值与计算结果不一致 / Fixed symbol value conflicts with the prepared result"
    ));

    private static Try InvalidNumericValue() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "符号准备值无效或溢出 / Invalid or overflowing symbol preparation value"
    ));

    private static Try InvalidFunctionInputs() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "函数输入未声明或不完整 / Function inputs are unknown or incomplete"
    ));

    private static void CacheContext<V>(IAbstractMutableTokenTable<V> table, IIntermediateSymbol<V> symbol)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (symbol is ILinearIntermediateSymbol<V> linear) {
            LinearPolynomial<V> polynomial = linear.Polynomial;
            table.CacheLinearFlatten(symbol, new LinearFlattenData<V>(
                polynomial.Monomials.ToArray(), polynomial.Constant));
        }
        if (symbol is IQuadraticIntermediateSymbol<V> quadratic) {
            QuadraticPolynomial<V> polynomial = quadratic.Polynomial;
            table.CacheQuadraticFlatten(symbol, new QuadraticFlattenData<V>(
                polynomial.Monomials.ToArray(), polynomial.Constant));
        }
        if (symbol.Range.Range is { } range) {
            table.CacheRange(
                cacheKey: symbol,
                solution: (IReadOnlyList<V>?)null,
                range: range
            );
        }
        else {
            table.ClearRange(symbol);
        }
    }

    private sealed class PreparedValues<V> : IReadOnlyDictionary<ISymbol, V> where V : struct {
        private readonly IReadOnlyDictionary<ISymbol, V> _fixedValues;
        private readonly Dictionary<ISymbol, V> _prepared = new();

        public PreparedValues(IReadOnlyDictionary<ISymbol, V>? fixedValues) {
            _fixedValues = fixedValues ?? new Dictionary<ISymbol, V>();
        }

        public int Count => _fixedValues.Count + _prepared.Count;
        public IEnumerable<ISymbol> Keys => _fixedValues.Keys.Concat(_prepared.Keys);
        public IEnumerable<V> Values => _fixedValues.Values.Concat(_prepared.Values);
        public V this[ISymbol key] => _fixedValues.TryGetValue(key, out V value)
            ? value
            : _prepared[key];

        public bool ContainsKey(ISymbol key) => TryGetValue(key, out _);

        public bool TryGetValue(ISymbol key, out V value) =>
            _fixedValues.TryGetValue(key, out value) || _prepared.TryGetValue(key, out value);

        public bool TryAdd(ISymbol key, V value) =>
            !_fixedValues.ContainsKey(key) && _prepared.TryAdd(key, value);

        public IEnumerator<KeyValuePair<ISymbol, V>> GetEnumerator() =>
            _fixedValues.Concat(_prepared).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FunctionInputValues<V> : IReadOnlyDictionary<ISymbol, V> where V : struct {
        private readonly IReadOnlyDictionary<ISymbol, V> _values;
        private readonly IReadOnlySet<ISymbol> _inputs;

        public FunctionInputValues(IReadOnlyDictionary<ISymbol, V> values, IReadOnlySet<ISymbol> inputs) {
            _values = values;
            _inputs = inputs;
        }

        public bool Undeclared { get; private set; }
        public bool MissingIndexer { get; private set; }
        public int Count => Keys.Count();
        public IEnumerable<ISymbol> Keys => _inputs.Where(_values.ContainsKey);
        public IEnumerable<V> Values => Keys.Select(key => _values[key]);
        public V this[ISymbol key] {
            get {
                if (TryGetValue(key, out V value)) {
                    return value;
                }
                MissingIndexer = true;
                throw new KeyNotFoundException();
            }
        }

        public bool ContainsKey(ISymbol key) => TryGetValue(key, out _);

        public bool TryGetValue(ISymbol key, out V value) {
            if (!_inputs.Contains(key)) {
                Undeclared = true;
                value = default;
                return false;
            }
            return _values.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<ISymbol, V>> GetEnumerator() =>
            Keys.Select(key => new KeyValuePair<ISymbol, V>(key, _values[key])).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
