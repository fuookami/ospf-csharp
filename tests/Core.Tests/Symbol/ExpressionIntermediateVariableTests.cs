#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol;

/// <summary>
/// **表达式符号以中间变量实现** / Expression symbols realized through an intermediate variable.
/// </summary>
/// <remarks>
/// 同步 Kotlin `2e72a17c`。基线给 `IntermediateSymbol` 一个默认为 null 的 `intermediateVariable`，并在
/// `LinearExpressionSymbol`／`QuadraticExpressionSymbol` 上以 `useIntermediateVariable` 开关惰性创建辅助变量。
/// **这一批做的是"能力可用"：** 槽位、开关、惰性辅助变量、令牌注册与展平当叶子；**模型层自动注册等式约束在 C# 侧还没有分派点，
/// 明确记为未接线**（Kotlin 在 `MechanismModel` 里按符号类型分派，C# 没有这条路径）。
///
/// Syncs Kotlin `2e72a17c`. The baseline defaults `intermediateVariable` to null and gives the expression symbols a `useIntermediateVariable` switch that lazily
/// creates an auxiliary. **This batch delivers the capability: the slot, the switch, the lazy auxiliary, token registration and leaf treatment when flattening;
/// automatic equality registration at the model layer has no dispatch point on the C# side and is recorded as not wired** (the baseline dispatches per symbol
/// type inside `MechanismModel`, which C# does not have).
/// </remarks>
public class ExpressionIntermediateVariableTests
{
    /// <summary>**开关关闭时没有中间变量** / With the switch off there is no intermediate variable.</summary>
    [Fact]
    public void WithoutTheSwitch_ThereIsNoIntermediateVariable() {
        LinearExpressionSymbol symbol = Expression("plain");

        symbol.IntermediateVariable.Should().BeNull("the default has to be off, or every existing model would silently gain a variable");
    }

    /// <summary>**开关打开后有中间变量，且惰性创建跨读取稳定** / With the switch on there is one, and it is stable across reads.</summary>
    /// <remarks>
    /// **稳定性不是细节：** **符号会被转储、被注册进令牌表、被展平多次；**如果每次读取都新造一个变量，
    /// 令牌表与约束里就会出现两个同名的不同变量，**模型会静默变成两个自由变量加一条只钉住其中一个的等式。**
    ///
    /// **Stability is not a detail: the symbol is dumped, registered and flattened several times.** **A fresh variable per read would put two same-named
    /// distinct variables into the token table and the constraints, quietly turning the model into two free variables with an equality pinning one of them.**
    /// </remarks>
    [Fact]
    public void WithTheSwitchOn_TheIntermediateVariableIsStable() {
        LinearExpressionSymbol symbol = Expression("expr", realizeThroughVariable: true);

        symbol.IntermediateVariable.Should().NotBeNull("the switch asks for a realization variable");
        symbol.IntermediateVariable.Should().BeSameAs(
            symbol.IntermediateVariable,
            "two reads must yield the same variable, not two look-alikes");
        symbol.IntermediateVariable!.Name.Should().Contain(
            "__realization",
            "the name says what the variable is for; a bare auxiliary is untraceable in a dumped model");
    }

    /// <summary>**两个符号各自的辅助变量互不相同** / Two symbols do not share an auxiliary.</summary>
    [Fact]
    public void TwoSymbols_ShouldNotShareAnAuxiliary() {
        LinearExpressionSymbol first = Expression("a", realizeThroughVariable: true);
        LinearExpressionSymbol second = Expression("b", realizeThroughVariable: true);

        first.IntermediateVariable.Should().NotBeSameAs(second.IntermediateVariable);
        first.IntermediateVariable!.Name.Should().NotBe(second.IntermediateVariable!.Name);
    }

    private static LinearExpressionSymbol Expression(string name, bool realizeThroughVariable = false) {
        var item = new RealVar(name + "_x");
        var polynomial = new MutableLinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, item) },
            Flt64.Zero);
        return new LinearExpressionSymbol(polynomial, name: name) {
            UseIntermediateVariable = realizeThroughVariable,
        };
    }
}