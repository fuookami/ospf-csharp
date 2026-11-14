#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 二次极值三兄弟共用的桥接与委托 / The bridging and delegation shared by the three quadratic extremum adapters.
/// </summary>
/// <remarks>
/// B1162 按基线 `QuadraticMax.kt`、`QuadraticMaxMin.kt`、`QuadraticMinMax.kt` 补三个类。
///
/// **三者与 B1161 的 `QuadraticIf` 系列是同一个形状**：**把每个二次多项式桥接成一个实变量，
/// 再交给已有的线性 `Max`/`MaxMin`/`MinMax`。**
/// **极值编码本身没有任何新增。**
///
/// **空输入时基线塞一个零多项式**（`inputs.ifEmpty { listOf(LinearPolynomial(emptyList(), zero)) }`）——
/// **不是"拒绝"，而是"最大值/最小值的单位元是 0 对空集无意义，所以给一个零"。**
/// **这看起来像补丁，但它让 `MaxFunction` 不必处理空表**，
/// **而代价是"空输入"与"输入一个常数 0"无法区分。**
///
/// B1162 adds the three quadratic extremum adapters from the baseline.
///
/// **They share the shape of B1161's `QuadraticIf` family**: **bridge each quadratic polynomial to a real variable and
/// hand them to the existing linear `Max`/`MaxMin`/`MinMax`.**
/// **No extremum encoding is new.**
///
/// **On empty input the baseline substitutes a zero polynomial** — **not a rejection, but "the unit of a max/min over
/// nothing is meaningless, so give a zero".**
/// **It looks like a patch, but it keeps `MaxFunction` from having to handle an empty list**,
/// **at the cost of making "empty input" indistinguishable from "one constant zero".**
/// </remarks>
internal static class QuadraticExtremumBridging
{
    /// <summary>
    /// 把二次输入桥接成线性输入 / Bridge quadratic inputs into linear ones.
    /// </summary>
    /// <remarks>
    /// **空表补一个零多项式，与基线一致。**
    /// **`bridges` 与 `rows` 在空表时长度都为 1**——
    /// **因为补进去的那个零多项式也要有个桥接变量，否则委托出去的线性函数读的是一个不存在的符号。**
    ///
    /// **An empty list gets one zero polynomial, matching the baseline.**
    /// **`bridges` and `rows` both have length one then** — **because the substituted zero needs a bridge too, or the
    /// delegated linear function would read a symbol that does not exist.**
    /// </remarks>
    public static (LinearPolynomial<Flt64>[] Inputs, RealVar[] Bridges, QuadraticInequalityOf<Flt64>[] Rows)
        Bridge(IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials, IIntoValue<Flt64> converter, string name) {
        int count = polynomials.Count == 0 ? 1 : polynomials.Count;
        var inputs = new LinearPolynomial<Flt64>[count];
        var bridges = new RealVar[count];
        var rows = new QuadraticInequalityOf<Flt64>[count];

        for (int index = 0; index < count; ++index) {
            bridges[index] = new RealVar($"{name}_bridge_{index}");
            inputs[index] = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(converter.One, bridges[index]) }, converter.Zero);

            if (polynomials.Count == 0) {
                // 空表：`y = 0`，于是桥接变量被钉在零。
                // Empty list: `y = 0`, pinning the bridge to zero.
                rows[index] = new QuadraticInequalityOf<Flt64>(
                    Lhs: new QuadraticPolynomial<Flt64>(
                        new[] { QuadraticMonomial<Flt64>.Linear(converter.One, bridges[index]) }, converter.Zero),
                    Rhs: new QuadraticPolynomial<Flt64>(
                        Array.Empty<QuadraticMonomial<Flt64>>(), converter.Zero),
                    Comparison: Comparison.EQ,
                    Name: $"{name}_bridge_row_{index}");
                continue;
            }

            if (QuadraticBounds.Of(polynomials[index], converter) is { } bounds) {
                bridges[index].Range.Geq(converter.FromValue(bounds.Lower));
                bridges[index].Range.Leq(converter.FromValue(bounds.Upper));
            }

            var monomials = new List<QuadraticMonomial<Flt64>>(polynomials[index].Monomials.Count + 1);
            foreach (QuadraticMonomial<Flt64> monomial in polynomials[index].Monomials) {
                monomials.Add(new QuadraticMonomial<Flt64>(
                    monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
            }

            monomials.Add(QuadraticMonomial<Flt64>.Linear(converter.One, bridges[index]));

            rows[index] = new QuadraticInequalityOf<Flt64>(
                Lhs: new QuadraticPolynomial<Flt64>(monomials, polynomials[index].Constant.Negate()),
                Rhs: new QuadraticPolynomial<Flt64>(
                    Array.Empty<QuadraticMonomial<Flt64>>(), converter.Zero),
                Comparison: Comparison.EQ,
                Name: $"{name}_bridge_row_{index}");
        }

        return (inputs, bridges, rows);
    }

    /// <summary>把桥接行写进模型 / Write the bridge rows.</summary>
    public static Try RegisterBridgeRows(
        IAbstractLinearMechanismModel<Flt64> model,
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows) {
        if (rows.Count == 0) {
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        if (model is not IAbstractQuadraticMechanismModel<Flt64> quadratic) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "a quadratic input needs a quadratic mechanism model");
        }

        foreach (QuadraticInequalityOf<Flt64> row in rows) {
            Try added = quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return failure;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <summary>求值并把桥接值交给委托函数 / Evaluate and hand the bridged values to the delegate.</summary>
    public static Flt64? EvaluateDelegated(
        IMathFunctionSymbol<Flt64> inner,
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IReadOnlyList<RealVar> bridges,
        IReadOnlyDictionary<ISymbol, Flt64> values) {
        var bridged = new Dictionary<ISymbol, Flt64>(bridges.Count);

        if (polynomials.Count == 0) {
            bridged[bridges[0]] = Flt64.Zero;
            return inner.Evaluate(bridged);
        }

        for (int index = 0; index < polynomials.Count; ++index) {
            if (QuadraticBounds.Evaluate(polynomials[index], values) is not { } evaluated) {
                return null;
            }

            bridged[bridges[index]] = evaluated;
        }

        return inner.Evaluate(bridged);
    }

    /// <summary>收集输入符号 / Collect the input symbols.</summary>
    public static IReadOnlyCollection<ISymbol> SymbolsOf(IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials) {
        var symbols = new HashSet<ISymbol>();
        foreach (QuadraticPolynomial<Flt64> polynomial in polynomials) {
            foreach (ISymbol symbol in FunctionInputs.Of<Flt64>(polynomial)) {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }
}

/// <summary>
/// 二次输入的 <c>max</c>：桥接后交给线性 `MaxFunction` / A quadratic-input `max`, bridged.
/// </summary>
/// <remarks>
/// B1162 按基线 `QuadraticMax.kt` 补。**不含任何极值编码。**
///
/// B1162, following the baseline's `QuadraticMax.kt`. **No extremum encoding here.**
/// </remarks>
public sealed class QuadraticMaxFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64>[] _polynomials;

    /// <summary>创建二次输入的 `max` / Create one.</summary>
    public QuadraticMaxFunction(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_max",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomials);
        ArgumentNullException.ThrowIfNull(converter);

        _polynomials = new QuadraticPolynomial<Flt64>[polynomials.Count];
        for (int index = 0; index < polynomials.Count; ++index) {
            _polynomials[index] = polynomials[index];
        }

        (LinearPolynomial<Flt64>[] inputs, RealVar[] bridges, QuadraticInequalityOf<Flt64>[] rows) =
            QuadraticExtremumBridging.Bridge(polynomials, converter, name);
        Bridges = bridges;
        BridgeRows = rows;

        Inner = new MaxFunction<Flt64>(
            polynomials: inputs,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName);
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>被比较的各个多项式 / The polynomials being compared.</summary>
    public IReadOnlyList<QuadraticPolynomial<Flt64>> Polynomials => _polynomials;

    /// <summary>各桥接变量 / The bridge variables.</summary>
    public IReadOnlyList<RealVar> Bridges { get; }

    /// <summary>桥接等式 / The bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public MaxFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            helpers.AddRange(Bridges);
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => QuadraticExtremumBridging.SymbolsOf(_polynomials);

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticExtremumBridging.EvaluateDelegated(Inner, _polynomials, Bridges, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticExtremumBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 二次输入的 <c>max min</c>：桥接后交给线性 `MaxMinFunction` / A quadratic-input `max min`, bridged.
/// </summary>
/// <remarks>
/// B1162 按基线 `QuadraticMaxMin.kt` 补。
///
/// B1162, following the baseline's `QuadraticMaxMin.kt`.
/// </remarks>
public sealed class QuadraticMaxMinFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64>[] _polynomials;

    /// <summary>创建二次输入的 `max min` / Create one.</summary>
    public QuadraticMaxMinFunction(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_maxmin",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomials);
        ArgumentNullException.ThrowIfNull(converter);

        _polynomials = new QuadraticPolynomial<Flt64>[polynomials.Count];
        for (int index = 0; index < polynomials.Count; ++index) {
            _polynomials[index] = polynomials[index];
        }

        (LinearPolynomial<Flt64>[] inputs, RealVar[] bridges, QuadraticInequalityOf<Flt64>[] rows) =
            QuadraticExtremumBridging.Bridge(polynomials, converter, name);
        Bridges = bridges;
        BridgeRows = rows;

        Inner = new MaxMinFunction<Flt64>(
            polynomials: inputs,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName);
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>被比较的各个多项式 / The polynomials.</summary>
    public IReadOnlyList<QuadraticPolynomial<Flt64>> Polynomials => _polynomials;

    /// <summary>各桥接变量 / The bridges.</summary>
    public IReadOnlyList<RealVar> Bridges { get; }

    /// <summary>桥接等式 / The bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public MaxMinFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            helpers.AddRange(Bridges);
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => QuadraticExtremumBridging.SymbolsOf(_polynomials);

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticExtremumBridging.EvaluateDelegated(Inner, _polynomials, Bridges, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticExtremumBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 二次输入的 <c>min max</c>：桥接后交给线性 `MinMaxFunction` / A quadratic-input `min max`, bridged.
/// </summary>
/// <remarks>
/// B1162 按基线 `QuadraticMinMax.kt` 补。
///
/// **`max min` 与 `min max` 不是同一个量**：**前者是先取最小再取最大，后者相反。**
/// **两者的判据必须给出<b>不同</b>的期望值，否则"写反了"测不出来。**
///
/// B1162, following the baseline's `QuadraticMinMax.kt`.
///
/// **`max min` and `min max` are different quantities**: **the first minimises then maximises, the second the other
/// way round.** **Their criteria must expect <b>different</b> values, or a swap goes unnoticed.**
/// </remarks>
public sealed class QuadraticMinMaxFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64>[] _polynomials;

    /// <summary>创建二次输入的 `min max` / Create one.</summary>
    public QuadraticMinMaxFunction(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_minmax",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomials);
        ArgumentNullException.ThrowIfNull(converter);

        _polynomials = new QuadraticPolynomial<Flt64>[polynomials.Count];
        for (int index = 0; index < polynomials.Count; ++index) {
            _polynomials[index] = polynomials[index];
        }

        (LinearPolynomial<Flt64>[] inputs, RealVar[] bridges, QuadraticInequalityOf<Flt64>[] rows) =
            QuadraticExtremumBridging.Bridge(polynomials, converter, name);
        Bridges = bridges;
        BridgeRows = rows;

        Inner = new MinMaxFunction<Flt64>(
            polynomials: inputs,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName);
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>被比较的各个多项式 / The polynomials.</summary>
    public IReadOnlyList<QuadraticPolynomial<Flt64>> Polynomials => _polynomials;

    /// <summary>各桥接变量 / The bridges.</summary>
    public IReadOnlyList<RealVar> Bridges { get; }

    /// <summary>桥接等式 / The bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public MinMaxFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            helpers.AddRange(Bridges);
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => QuadraticExtremumBridging.SymbolsOf(_polynomials);

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticExtremumBridging.EvaluateDelegated(Inner, _polynomials, Bridges, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticExtremumBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}
