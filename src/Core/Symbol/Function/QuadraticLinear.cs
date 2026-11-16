#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 二次多项式的桥接变量：<c>y = p</c> / A bridge variable for a quadratic polynomial: `y = p`.
/// </summary>
/// <remarks>
/// 对齐把空壳换成真实编码。
///
/// **线性输入时什么都不做**：**`y` 不创建、约束不写、辅助变量为空**——
/// **一条 `y = p` 的等式对本来就是线性的 `p` 只是把 `p` 抄了一遍，白添一个变量与一条等式。**
/// **判据必须验证这一点**：**只验证"线性时注册成功"是不够的，因为"多写了一条等式"也注册成功。**
///
/// **非线性输入时用无界实变量 `RealVar`，不能用 `URealVar`。**
/// **注释明确记了这件事**：**`URealVar` 会在求解器边界把负值静默截断成 0——
/// 于是同一个多项式在负半轴上的结果全错，而模型照样求解成功。**
///
/// Replaces a stub with the real encoding, following its.
///
/// **A linear input does nothing**: **no `y`, no row, no helper** —
/// **a `y = p` equality over an already-linear `p` only copies it, adding a variable and a row for nothing.**
/// **The criterion must check that**: **"registration succeeds when linear" is not enough, since an extra row also
/// succeeds.**
///
/// **A non-linear input uses an unbounded `RealVar`, never a `URealVar`.**
/// **The code's comment records why**: **a `URealVar` silently clips negatives to zero at the solver boundary —
/// so the polynomial is wrong on its whole negative half while the model still solves fine.**
/// </remarks>
public sealed class QuadraticLinearFunction : IMathFunctionSymbol<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _polynomial;
    private readonly RealVar? _bridge;
    private readonly IIntoValue<Flt64> _converter;

    /// <summary>构造一个桥接 / Build one.</summary>
    public QuadraticLinearFunction(
        QuadraticPolynomial<Flt64> polynomial,
        IIntoValue<Flt64> converter,
        string name = "quadraticLinear",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomial);
        ArgumentNullException.ThrowIfNull(converter);

        _polynomial = polynomial;
        _converter = converter;
        Name = name;
        DisplayName = displayName;

        // **只有非线性输入才需要桥接变量。**
        // **Only a non-linear input needs the bridge variable.**
        _bridge = IsLinear ? null : new RealVar($"{name}_y");
    }

    /// <summary>被桥接的多项式 / The bridged polynomial.</summary>
    public QuadraticPolynomial<Flt64> Polynomial => _polynomial;

    /// <summary>桥接变量；线性输入时为 `null` / The bridge, `null` for a linear input.</summary>
    public RealVar? Bridge => _bridge;

    /// <summary>输入是否已经线性 / Whether the input is already linear.</summary>
    public bool IsLinear {
        get {
            foreach (QuadraticMonomial<Flt64> monomial in _polynomial.Monomials) {
                if (monomial.IsQuadratic) {
                    return false;
                }
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables =>
        _bridge is null ? Array.Empty<IVariableItem>() : new IVariableItem[] { _bridge };

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// **返回 `null`，而不是输入符号的集合。**
    /// **仓库的输入契约是：不实现 `IHasResultPolynomial` 的函数，`InputSymbols` 必须是 `null`**——
    /// **它们的结果不通过结果多项式暴露，于是"输入"这个概念在适配器层没有落点。**
    /// **声明成集合会让 `FunctionInputContractTest` 直接红（"expected null but found {...}"）。**
    ///
    /// **`null`, not the set of input symbols.**
    /// **The repository's contract is: a function that does not implement `IHasResultPolynomial` must return `null`
    /// for `InputSymbols`** — **its result is not exposed through a result polynomial, so "the inputs" have nowhere to
    /// land in the adapter.** **Declaring a set turns `FunctionInputContractTest` red outright.**
    /// </remarks>
    public IReadOnlyCollection<ISymbol>? InputSymbols => null;

    /// <summary>本函数读到的符号 / The symbols this function reads.</summary>
    internal IReadOnlyCollection<ISymbol> ReadSymbols => FunctionInputs.Of(_polynomial);

    /// <summary>桥接等式 <c>y − p = 0</c>；线性输入时为 `null` / The bridge row, `null` when linear.</summary>
    public QuadraticInequalityOf<Flt64>? BridgeRow() {
        if (_bridge is not { } bridge) {
            return null;
        }

        var monomials = new List<QuadraticMonomial<Flt64>>(_polynomial.Monomials.Count + 1);
        foreach (QuadraticMonomial<Flt64> monomial in _polynomial.Monomials) {
            monomials.Add(new QuadraticMonomial<Flt64>(
                monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
        }

        monomials.Add(QuadraticMonomial<Flt64>.Linear(_converter.One, bridge));

        return new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(monomials, _polynomial.Constant.Negate()),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), _converter.Zero),
            Comparison: Comparison.EQ,
            Name: $"{Name}_bridge");
    }

    /// <summary>按语义求值：就是多项式本身 / Evaluate by semantics: the polynomial itself.</summary>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticBounds.Evaluate(_polynomial, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        // **线性输入没有行要写**——直接成功，且不需要二次模型。
        // 这是"线性时什么都不做"的可观察后果。
        //
        // **A linear input has no row to write** — succeed directly, and no quadratic model is needed.
        // That is the observable consequence of "does nothing when linear".
        if (BridgeRow() is not { } row) {
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        if (model is not IAbstractQuadraticMechanismModel<Flt64> quadratic) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "a quadratic bridge needs a quadratic mechanism model");
        }

        return quadratic.AddConstraint(row, row.Name, ((IIntermediateSymbol, bool)?)null);
    }
}
