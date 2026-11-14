#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 二次掩码函数：<c>mask · p(x)</c> / A quadratic masking function.
/// </summary>
/// <remarks>
/// B1176 按基线 `QuadraticMasking.kt` 的 `QuadraticMaskingFunction` 补。
///
/// **与仓库里其他二次函数一样只支持 `Flt64`**：**桥接本身就是 `Flt64` 的**
/// （`QuadraticBridging.Bridge` 收 `QuadraticPolynomial&lt;Flt64&gt;`），
/// **把外层写成泛型只会换来一次 `V → Flt64` 的字符串转换，
/// 而那种转换在 `V` 是定点数或高精度数时会静默丢精度。**
/// **基线的泛型是它自己转换器体系的一部分，这里没有对应的东西——所以不假装有。**
///
/// **结构是"先桥接、再掩码"**：**二次输入 `p` 先被桥接变量 `y` 取代（`y − p = 0`），
/// 然后 `y` 作为线性输入交给 `MaskingFunction`。**
///
/// **这一步不是优化，是必需的**：**`MaskingFunction` 只接受线性输入**，
/// **直接把二次项塞进去无处分派——而"无处安放"有两种结局：
/// 抛异常（好），或静默丢项（坏，且当时不报错）。**
///
/// **桥接变量必须有范围**：**`MaskingFunction` 要从输入界推 Big-M 定掩码的上下界，
/// 无界输入会让它退回到 `1e6`——那个回退值能跑，但界宽了 6 个数量级，
/// 松弛变松、求解变慢，而且不报任何错。**
///
/// **桥接等式必须真的写进模型**：**不写的桥接变量是自由的，掩码读的是一个任意值，而模型完全可行。**
///
/// B1176, following the baseline's `QuadraticMaskingFunction`.
///
/// **`Flt64`-only, like every other quadratic function here**: **the bridge itself is `Flt64`**
/// (`QuadraticBridging.Bridge` takes `QuadraticPolynomial&lt;Flt64&gt;`), and **making the outer layer generic would only
/// buy a `V → Flt64` string conversion — one that silently loses precision when `V` is fixed-point or
/// high-precision.** **The baseline's genericity comes from its own converter system, which has no counterpart here —
/// so this does not pretend to have one.**
///
/// **The structure is "bridge first, then mask"**: **the quadratic input `p` is replaced by a bridge variable `y`
/// (`y − p = 0`), and `y` then goes to `MaskingFunction` as a linear input.**
///
/// **That step is not an optimisation, it is required**: **`MaskingFunction` takes linear input only**, and
/// **stuffing quadratic terms in has nowhere to dispatch them — with two possible outcomes: throw (fine), or drop the
/// terms silently (bad, and nothing reports it at the time).**
///
/// **The bridge must be bounded**: **`MaskingFunction` derives its big-M from the input's bounds to fix the mask's
/// range, and an unbounded input makes it fall back to `1e6` — a value that runs, but with the bounds six orders of
/// magnitude too wide: looser relaxation, slower solves, and no error at all.**
///
/// **The bridge equality must actually reach the model**: **an unregistered bridge is free, the mask reads an
/// arbitrary value, and the model is perfectly feasible.**
/// </remarks>
public sealed class QuadraticMaskingFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticInequalityOf<Flt64>[] _bridgeRows;
    private readonly MaskingFunction<Flt64> _function;

    /// <summary>原始的二次输入 / The original quadratic input.</summary>
    public QuadraticPolynomial<Flt64> QuadraticInput { get; }

    /// <summary>桥接后的线性输入 / The bridged linear input.</summary>
    public LinearPolynomial<Flt64> Input => _function.Input;

    /// <summary>二值掩码变量 / The binary mask variable.</summary>
    public IVariableItem Mask => _function.Mask;

    /// <summary>桥接变量 / The bridge variable.</summary>
    public RealVar Bridge { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables =>
        new IVariableItem[] { Bridge }.Concat(_function.HelperVariables).ToArray();

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        FunctionInputs.Of(QuadraticInput).Append(Mask).ToArray();

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => _function.ResultPolynomial;

    /// <summary>桥接等式 / The bridge equality.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows => _bridgeRows;

    /// <summary>
    /// 构造二次掩码函数 / Build a quadratic masking function.
    /// </summary>
    /// <param name="input">二次输入 / The quadratic input.</param>
    /// <param name="mask">二值掩码 / The binary mask.</param>
    /// <param name="converter">数值转换器 / The numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional big-M.</param>
    /// <param name="name">名称 / The name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public QuadraticMaskingFunction(
        QuadraticPolynomial<Flt64> input,
        IVariableItem mask,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_masking",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(converter);

        QuadraticInput = input;
        Name = name;
        DisplayName = displayName;

        (RealVar[] bridges, QuadraticInequalityOf<Flt64>[] rows) =
            QuadraticBridging.Bridge(new[] { input }, converter, name);

        Bridge = bridges[0];
        _bridgeRows = rows;

        _function = new MaskingFunction<Flt64>(
            input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(converter.One, Bridge) }, converter.Zero),
            mask: mask,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        _function.RegisterAuxiliaryTokens(tokens);

    /// <summary>先写桥接等式，再写掩码约束 / Write the bridge rows, then the masking rows.</summary>
    /// <remarks>
    /// **顺序不能反**：**掩码行读的是桥接变量，桥接等式不在模型里时它就是一个自由变量。**
    ///
    /// **而如果在这里就 `return`，掩码行永远写不进去——模型可行、结果全错。**
    ///
    /// **The order matters**: **the masking rows read the bridge variable, which is free unless the bridge equality is
    /// in the model.**
    ///
    /// **And returning here without the masking rows leaves a feasible model with wrong results.**
    /// </remarks>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        Try bridge = QuadraticBridging.RegisterBridgeRows(model, _bridgeRows);
        return bridge is Ok<Success, ErrorCode, Error<ErrorCode>>
            ? _function.RegisterConstraints(model)
            : bridge;
    }

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => _function.Evaluate(values);

    /// <inheritdoc/>
    public Result<Flt64?, ErrorCode, Error<ErrorCode>> TryEvaluate(
        IReadOnlyDictionary<ISymbol, Flt64> values) => _function.TryEvaluate(values);
}
