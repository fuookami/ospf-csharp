#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
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
/// 二次条件的三件套共用的桥接步骤 / The bridging step shared by the three quadratic-condition functions.
/// </summary>
/// <remarks>
/// B1161 按基线 `QuadraticIf.kt`、`QuadraticIfThen.kt`、`QuadraticIfIn.kt` 补三个类。
///
/// **三个类都只做一件事**：**把二次多项式桥接成一个新的实变量 `y`（等式 `y − p = 0`），
/// 然后把 `y` 交给<b>已有的</b>线性 `IfFunction`/`IfThenFunction`/`IfInFunction`。**
///
/// **所以它们不是"二次的分支编码"，而是"二次输入 → 线性分支"的适配器。**
/// **分支编码本身没有任何新增**——**新增的只有那条桥接等式与一个变量。**
/// **这正是基线 `QuadraticFunctionSymbol.createFunction(inputs)` 的含义**：
/// **`inputs` 已经是线性多项式了。**
///
/// **`y` 必须是无界 `RealVar`**：**条件可能取到负值**，
/// **用 `URealVar` 会在求解器边界把负值截断成 0，于是 `x &lt; 0` 这类条件永远为假。**
///
/// B1161, following the baseline's three quadratic-condition adapters.
///
/// **All three do one thing**: **bridge a quadratic polynomial to a fresh real variable `y` (the equality
/// `y − p = 0`) and hand `y` to the <b>existing</b> linear `IfFunction`/`IfThenFunction`/`IfInFunction`.**
///
/// **So they are not "quadratic branch encodings" but "quadratic input → linear branch" adapters.**
/// **Nothing about the branch encoding is new** — **only the bridge equality and one variable are.**
/// **That is what the baseline's `QuadraticFunctionSymbol.createFunction(inputs)` means**: **`inputs` are already
/// linear by then.**
///
/// **`y` must be an unbounded `RealVar`**: **the condition can take negative values**,
/// **and a `URealVar` would clip them to zero at the solver boundary, so a condition like `x &lt; 0` would never
/// hold.**
/// </remarks>
internal static class QuadraticBridging
{
    /// <summary>
    /// 为每个二次多项式造一个桥接变量与一条等式 / One bridge variable and equality per polynomial.
    /// </summary>
    /// <remarks>
    /// **等式必须是二次行，不能是 `LinearInequality`。**
    /// **第一版把它写成 `LinearInequality`，于是"二次项无处安放"直接抛异常**——
    /// **而如果当时选择了"丢项"，条件就会静默变成错的，且没有任何一步报错。**
    ///
    /// **The equality must be a quadratic row, not a `LinearInequality`.**
    /// **The first version wrote it as a `LinearInequality`, so "a quadratic term has nowhere to go" threw
    /// outright** — **and had it dropped the terms instead, the condition would have gone silently wrong with nothing
    /// reporting it.**
    /// </remarks>
    public static (RealVar[] Bridges, QuadraticInequalityOf<Flt64>[] Rows) Bridge(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IIntoValue<Flt64> converter,
        string name) {
        var bridges = new RealVar[polynomials.Count];
        var rows = new QuadraticInequalityOf<Flt64>[polynomials.Count];

        for (int index = 0; index < polynomials.Count; ++index) {
            bridges[index] = new RealVar($"{name}_bridge_{index}");

            // **桥接变量必须有范围**：**`IfFunction.Create` 需要从条件推出界来定大 M，
            // 无界条件会被它直接拒绝，而拒绝的表现是 `.Value` 为 `null`（不是抛异常）。**
            //
            // **The bridge must be bounded**: **`IfFunction.Create` derives a big-M from the condition's bounds, and an
            // unbounded condition is rejected outright — the rejection shows up as a `null` `.Value`, not an
            // exception.**
            if (QuadraticBounds.Of(polynomials[index], converter) is { } bounds) {
                bridges[index].Range.Geq(converter.FromValue(bounds.Lower));
                bridges[index].Range.Leq(converter.FromValue(bounds.Upper));
            }

            // `y − p = 0`：把 `p` 取反后加上 `y`。
            // `y - p = 0`: negate p and add y.
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

        return (bridges, rows);
    }

    /// <summary>
    /// 把桥接等式写进模型 / Write the bridge rows into the model.
    /// </summary>
    /// <remarks>
    /// **没有这一步，桥接变量是自由的**——**它不出现在任何约束里，于是分支判断读的是一个任意值。**
    /// **而 `Inner` 的约束全部可满足、模型可行、目标值合理。**
    ///
    /// **Without this step the bridge variable is free** — **it appears in no constraint, so the branch reads an
    /// arbitrary value.** **Meanwhile every `Inner` row is satisfiable, the model feasible and the objective
    /// plausible.**
    /// </remarks>
    public static Try RegisterBridgeRows(
        IAbstractLinearMechanismModel<Flt64> model,
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows) {
        if (rows.Count == 0) {
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        if (model is not IAbstractQuadraticMechanismModel<Flt64> quadratic) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "a quadratic condition needs a quadratic mechanism model");
        }

        foreach (QuadraticInequalityOf<Flt64> row in rows) {
            Try added = quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return failure;
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <summary>收集一组二次多项式的输入符号 / Collect the input symbols of several quadratic polynomials.</summary>
    /// <remarks>
    /// **`FunctionInputs.Of` 只有单个二次多项式的重载**，所以多元素要自己合并。
    ///
    /// **`FunctionInputs.Of` only has the single-quadratic overload**, so several must be merged here.
    /// </remarks>
    public static IReadOnlyCollection<ISymbol> SymbolsOf(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials) {
        var symbols = new HashSet<ISymbol>();
        foreach (QuadraticPolynomial<Flt64> polynomial in polynomials) {
            foreach (ISymbol symbol in FunctionInputs.Of<Flt64>(polynomial)) {
                symbols.Add(symbol);
            }
        }

        return symbols;
    }

    /// <summary>对一组二次多项式求值 / Evaluate a set of quadratic polynomials.</summary>
    public static bool TryEvaluateAll(
        IReadOnlyList<QuadraticPolynomial<Flt64>> polynomials,
        IReadOnlyDictionary<ISymbol, Flt64> values,
        out Flt64[] evaluated) {
        evaluated = new Flt64[polynomials.Count];
        for (int index = 0; index < polynomials.Count; ++index) {
            if (QuadraticBounds.Evaluate(polynomials[index], values) is not { } value) {
                return false;
            }

            evaluated[index] = value;
        }

        return true;
    }
}

/// <summary>
/// 二次条件的 <c>If</c>：桥接后交给线性 `IfFunction` / A quadratic-condition `If`, bridged into the linear one.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticIf.kt`。
///
/// **本类不含任何分支编码**：**它只加一个桥接变量与一条等式，然后把桥接变量交给 `IfFunction`。**
/// **判据必须验证这一点**：**辅助变量 = 桥接变量 + `IfFunction` 自己的辅助变量**，
/// **而 `ResultPolynomial` 与"用同一桥接变量构造的 `IfFunction`"的完全相同。**
///
/// B1161, following the baseline's `QuadraticIf.kt`.
///
/// **This class holds no branch encoding**: **it adds one bridge variable and one equality, then hands the bridge to
/// `IfFunction`.** **The criterion must check that**: **the helpers are the bridge plus `IfFunction`'s own, and the
/// `ResultPolynomial` is identical to that of an `IfFunction` built over the same bridge.**
/// </remarks>
public sealed class QuadraticIfFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _condition;
    private readonly RealVar _bridge;

    /// <summary>创建二次条件的 `If` / Create one.</summary>
    public QuadraticIfFunction(
        QuadraticPolynomial<Flt64> condition,
        IIntoValue<Flt64> converter,
        Flt64 strictBoundary,
        Comparison relation = Comparison.GT,
        Flt64? delta = null,
        string name = "quadratic_if",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(converter);

        _condition = condition;
        Condition = condition;
        Converter = converter;
        StrictBoundary = strictBoundary;
        Relation = relation;
        Name = name;
        DisplayName = displayName;

        (RealVar[] bridges, IReadOnlyList<QuadraticInequalityOf<Flt64>> rows) = QuadraticBridging.Bridge(
            new[] { condition }, converter, name);
        _bridge = bridges[0];
        BridgeRows = rows;

        Inner = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(converter.One, _bridge) }, converter.Zero),
            converter: converter,
            strictBoundary: strictBoundary,
            relation: relation,
            delta: delta,
            name: name,
            displayName: displayName) is { IsOk: true } created
                ? created.Value
                : throw new ArgumentException(
                    "the linear IfFunction rejected the bridged condition: its bounds or strict boundary are unusable",
                    nameof(condition));
    }

    /// <summary>条件多项式 / The condition polynomial.</summary>
    public QuadraticPolynomial<Flt64> Condition { get; }

    /// <summary>值转换器 / The converter.</summary>
    public IIntoValue<Flt64> Converter { get; }

    /// <summary>严格边界 / The strict boundary.</summary>
    public Flt64 StrictBoundary { get; }

    /// <summary>比较关系 / The relation.</summary>
    public Comparison Relation { get; }

    /// <summary>桥接变量 / The bridge variable.</summary>
    public RealVar BridgeVar => _bridge;

    /// <summary>桥接等式 / The bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public IfFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { _bridge };
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => QuadraticBridging.SymbolsOf(new[] { _condition });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (QuadraticBounds.Evaluate(_condition, values) is not { } condition) {
            return null;
        }

        return Inner.Evaluate(new Dictionary<ISymbol, Flt64> { [_bridge] = condition });
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 二次条件的 <c>IfThen</c>：桥接后交给线性 `IfThenFunction` / A quadratic-condition `IfThen`, bridged.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticIfThen.kt`。
///
/// B1161, following the baseline's `QuadraticIfThen.kt`. **One bridge per polynomial.**
/// </remarks>
public sealed class QuadraticIfThenFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _condition;
    private readonly QuadraticPolynomial<Flt64> _then;
    private readonly RealVar _conditionBridge;
    private readonly RealVar _thenBridge;

    /// <summary>创建二次条件的 `IfThen` / Create one.</summary>
    public QuadraticIfThenFunction(
        QuadraticPolynomial<Flt64> condition,
        QuadraticPolynomial<Flt64> then,
        IIntoValue<Flt64> converter,
        Flt64 strictBoundary,
        Comparison relation = Comparison.GT,
        Flt64? delta = null,
        string name = "quadratic_ifthen",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);
        ArgumentNullException.ThrowIfNull(converter);

        _condition = condition;
        _then = then;
        Condition = condition;
        Then = then;
        Converter = converter;
        StrictBoundary = strictBoundary;
        Relation = relation;
        Name = name;
        DisplayName = displayName;

        (RealVar[] bridges, IReadOnlyList<QuadraticInequalityOf<Flt64>> rows) = QuadraticBridging.Bridge(
            new[] { condition, then }, converter, name);
        _conditionBridge = bridges[0];
        _thenBridge = bridges[1];
        BridgeRows = rows;

        Inner = IfThenFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(converter.One, _conditionBridge) }, converter.Zero),
            then: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(converter.One, _thenBridge) }, converter.Zero),
            converter: converter,
            strictBoundary: strictBoundary,
            relation: relation,
            delta: delta,
            name: name,
            displayName: displayName).Value;
    }

    /// <summary>条件多项式 / The condition.</summary>
    public QuadraticPolynomial<Flt64> Condition { get; }

    /// <summary>取值多项式 / The then-branch value.</summary>
    public QuadraticPolynomial<Flt64> Then { get; }

    /// <summary>值转换器 / The converter.</summary>
    public IIntoValue<Flt64> Converter { get; }

    /// <summary>严格边界 / The strict boundary.</summary>
    public Flt64 StrictBoundary { get; }

    /// <summary>比较关系 / The relation.</summary>
    public Comparison Relation { get; }

    /// <summary>条件的桥接变量 / The condition bridge.</summary>
    public RealVar ConditionBridge => _conditionBridge;

    /// <summary>取值的桥接变量 / The then bridge.</summary>
    public RealVar ThenBridge => _thenBridge;

    /// <summary>两条桥接等式 / The two bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public IfThenFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { _conditionBridge, _thenBridge };
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => QuadraticBridging.SymbolsOf(new[] { _condition, _then });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (!QuadraticBridging.TryEvaluateAll(new[] { _condition, _then }, values, out Flt64[] evaluated)) {
            return null;
        }

        return Inner.Evaluate(new Dictionary<ISymbol, Flt64> {
            [_conditionBridge] = evaluated[0],
            [_thenBridge] = evaluated[1],
        });
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 二次输入的区间判断 <c>IfIn</c>：桥接后交给线性 `IfInFunction` / A quadratic-input `IfIn`, bridged.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticIfIn.kt`。
///
/// **本类与 `QuadraticInStepRangeFunction` 的区别**：
/// **后者的容差带返回 `null`（"不知道"），而本类在对 `lower`/`upper` 的判断上不做容差——
/// 它把整件事交给 `IfInFunction` 的 `strictBoundary` 与 `delta`。**
///
/// B1161, following the baseline's `QuadraticIfIn.kt`. **One bridge.**
///
/// **The difference from `QuadraticInStepRangeFunction`**: **that one returns `null` inside a tolerance band
/// ("unknown"), while this one applies no tolerance to the `lower`/`upper` decision — it defers the whole matter to
/// `IfInFunction`'s `strictBoundary` and `delta`.**
/// </remarks>
public sealed class QuadraticIfInFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _x;
    private readonly RealVar _bridge;

    /// <summary>创建二次输入的 `IfIn` / Create one.</summary>
    public QuadraticIfInFunction(
        QuadraticPolynomial<Flt64> input,
        Flt64 lower,
        Flt64 upper,
        IIntoValue<Flt64> converter,
        Flt64 strictBoundary,
        Flt64? delta = null,
        string name = "quadratic_ifin",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(converter);

        if (lower.CompareTo(upper) > 0) {
            throw new ArgumentException(
                $"the lower bound {lower} exceeds the upper bound {upper}", nameof(lower));
        }

        _x = input;
        Input = input;
        Lower = lower;
        Upper = upper;
        Converter = converter;
        StrictBoundary = strictBoundary;
        Name = name;
        DisplayName = displayName;

        (RealVar[] bridges, IReadOnlyList<QuadraticInequalityOf<Flt64>> rows) = QuadraticBridging.Bridge(
            new[] { input }, converter, name);
        _bridge = bridges[0];
        BridgeRows = rows;

        Inner = IfInFunction<Flt64>.Create(
            x: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(converter.One, _bridge) }, converter.Zero),
            lower: lower,
            upper: upper,
            converter: converter,
            strictBoundary: strictBoundary,
            delta: delta,
            name: name,
            displayName: displayName).Value;
    }

    /// <summary>输入多项式 / The input.</summary>
    public QuadraticPolynomial<Flt64> Input { get; }

    /// <summary>下界 / The lower bound.</summary>
    public Flt64 Lower { get; }

    /// <summary>上界 / The upper bound.</summary>
    public Flt64 Upper { get; }

    /// <summary>值转换器 / The converter.</summary>
    public IIntoValue<Flt64> Converter { get; }

    /// <summary>严格边界 / The strict boundary.</summary>
    public Flt64 StrictBoundary { get; }

    /// <summary>桥接变量 / The bridge.</summary>
    public RealVar BridgeVar => _bridge;

    /// <summary>桥接等式 / The bridge row.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的线性函数 / The delegated linear function.</summary>
    public IfInFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem> { _bridge };
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => QuadraticBridging.SymbolsOf(new[] { _x });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (QuadraticBounds.Evaluate(_x, values) is not { } input) {
            return null;
        }

        return Inner.Evaluate(new Dictionary<ISymbol, Flt64> { [_bridge] = input });
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}
