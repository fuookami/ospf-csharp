#nullable enable

using System;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;

/// <summary>
/// MAC（平均气动弦百分比）模型 / The mean aerodynamic chord percentage model.
/// </summary>
/// <remarks>
/// `Register` 把 `<c>mac</c>` 符号注册进模型，取值来自**起飞**指数符号经公式换算：
///
/// 1. **起飞总重已知**：`mac = formula.mac(index_takeoff, tow)`，指数符号保持线性（每个系数与常数项各算一遍）。
/// 2. **起飞总重未知**：`mac` 是**常数零**，即 `LinearExpressionSymbol(Flt64.zero)`——不是"跳过"，也不是"报错"。
///
/// 两处容易做错：
///
/// 1. **只看起飞阶段**。`computedTotalWeight[TakeOff]` 与 `index[TakeOff]` 都是固定取起飞，不遍历阶段。
/// 2. **注册是幂等的**。`if (!::mac.isInitialized)` 保证第二次调用不重建；C# 侧同样只在未建时构建，但**每次都 `Add`**——
///    重复 `Add` 同一个符号由模型决定成败，本类不额外拦。
///
/// `Register` puts the <c>mac</c> symbol into the model from the **take-off** index symbol through the formula: with a known take-off total weight
/// `mac = formula.mac(index_takeoff, tow)` keeping the index linear, and without one `mac` is the **constant zero** — literally `LinearExpressionSymbol(Flt64.zero)`,
/// it is neither skipped nor an error. Two easy mistakes: (1) **only the take-off phase is consulted**, both `computedTotalWeight[TakeOff]` and `index[TakeOff]`, with no loop over
/// phases; (2) **registration is idempotent** — the implementation guards with `if (!::mac.isInitialized)`; this builds the symbol once but **adds it every call**, leaving duplicate-add
/// semantics to the model rather than second-guessing them here.
/// </remarks>
public sealed class MacModel
{
    private readonly Formula _formula;
    private readonly TotalWeight _totalWeight;
    private readonly Torque _torque;

    /// <summary>
    /// 构造 MAC 模型 / Construct the MAC model.
    /// </summary>
    /// <param name="formula">力矩公式 / The moment formula.</param>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    /// <param name="torque">力矩模型 / The torque model.</param>
    public MacModel(Formula formula, TotalWeight totalWeight, Torque torque) {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(totalWeight);
        ArgumentNullException.ThrowIfNull(torque);
        _formula = formula;
        _totalWeight = totalWeight;
        _torque = torque;
    }

    /// <summary>MAC 符号；未注册时为 `null` / The MAC symbol, `null` before registration.</summary>
    public LinearExpressionSymbol? MacSymbol { get; private set; }

    /// <summary>
    /// 注册 MAC 符号 / Register the MAC symbol.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (MacSymbol is null) {
            if (_totalWeight.ComputedTotalWeight.TryGetValue(FlightPhase.TakeOff, out double tow)) {
                if (_torque.Index(FlightPhase.TakeOff) is not { } index) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "起飞指数符号未注册 / the take-off index symbol is not registered"));
                }
                LinearPolynomial<Flt64> mac = _formula.MacPolynomial(index.Polynomial, tow);
                MacSymbol = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(mac.Monomials, mac.Constant),
                    name: "mac");
            } else {
                // 起飞总重未知时是**常数零**，不是跳过：`mac` 这个符号必须存在，否则下游（水平安定面）找不到它。
                // Without a known take-off total weight the value is the **constant zero**, not a skip: the symbol must exist or downstream (the horizontal stabilizer) cannot
                // find it.
                MacSymbol = new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(constant: new Flt64(0.0)), name: "mac");
            }
        }
        return model.Add(MacSymbol);
    }
}
