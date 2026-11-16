#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol;

/// <summary>
/// **表达式符号以中间变量实现** / Expression symbols realized through an intermediate variable.
/// </summary>
/// <remarks>
/// `IntermediateSymbol` 有一个默认为 null 的 `intermediateVariable`，并在
/// `LinearExpressionSymbol`／`QuadraticExpressionSymbol` 上以 `useIntermediateVariable` 开关惰性创建辅助变量。
/// **这里做的是"能力可用"：** 槽位、开关、惰性辅助变量、令牌注册与展平当叶子；**模型层自动注册等式约束在 C# 侧还没有分派点，
/// 明确记为未接线**（`MechanismModel` 里按符号类型分派的那条路径在这里不存在）。
///
/// `intermediateVariable` defaults to null, and the expression symbols get a `useIntermediateVariable` switch that lazily
/// creates an auxiliary. **This delivers the capability: the slot, the switch, the lazy auxiliary, token registration and leaf treatment when flattening;
/// automatic equality registration at the model layer has no dispatch point on the C# side and is recorded as not wired** (dispatching per symbol
/// type inside `MechanismModel` does not exist here).
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

    /// <summary>**线性符号的等式：左边只有辅助变量，右边是定义式** / The linear equality has the auxiliary alone on the left.</summary>
    [Fact]
    public void TheLinearRealizationConstraint_ShouldPinTheAuxiliary() {
        LinearExpressionSymbol symbol = Expression("expr", realizeThroughVariable: true);

        symbol.RealizationConstraint().Should().NotBeNull("the switch promises an equality to pin the auxiliary");
        LinearExpressionSymbol off = Expression("plain");
        off.RealizationConstraint().Should().BeNull("without the switch there is no auxiliary to pin");
    }

    /// <summary>**二次符号也有等式，且左侧是二次多项式** / The quadratic symbol gets an equality with a quadratic left-hand side.</summary>
    /// <remarks>
    /// **只做到"开关 + 辅助变量 + 展平叶子"，二次等式当时留空。** **这里补上，并把"左侧必须是二次多项式"这件事写进判据：**
    /// **把辅助变量塞进线性多项式会让这条等式在二次模型里被当成阶数不一致而走另一条路径，而那条路径不报错。**
    ///
    /// **It stops at "switch + auxiliary + leaf when flattening" and left the quadratic equality out. It is added here, and the criterion states that the left-hand
    /// side has to be a quadratic polynomial: pushing the auxiliary into a linear one would look degree-mismatched in a quadratic model and take another path, silently.**
    /// </remarks>
    [Fact]
    public void TheQuadraticRealizationConstraint_ShouldExist() {
        RealVar x = new("x");
        var polynomial = new MutableQuadraticPolynomial<Flt64>(
            monomials: new[] { QuadraticMonomial<Flt64>.Quadratic(Flt64.One, x, x) },
            constant: Flt64.Zero);
        var symbol = new QuadraticExpressionSymbol(polynomial, name: "qexpr") {
            UseIntermediateVariable = true,
        };

        QuadraticInequalityOf<Flt64>? constraint = symbol.RealizationConstraint();
        constraint.Should().NotBeNull("the quadratic symbol carries the same switch and owes the same equality");
        constraint!.Comparison.Should().Be(Comparison.EQ, "a realization constraint is an equality");
        constraint.Lhs.Monomials.Should().Contain(
            monomial => ReferenceEquals(monomial.Symbol1, symbol.IntermediateVariable),
            "the auxiliary has to be the left-hand side, or the equality pins nothing");

        var off = new QuadraticExpressionSymbol(polynomial, name: "plain_q");
        off.RealizationConstraint().Should().BeNull();
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