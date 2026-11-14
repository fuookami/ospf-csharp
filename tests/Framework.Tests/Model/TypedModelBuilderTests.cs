// 带类型模型构造器（B675）的判据 / Criteria for the typed model builder (B675).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Model;

/// <summary>
/// **带类型模型构造器（B675）** / The typed model builder (B675).
/// </summary>
/// <remarks>
/// 判据的重点：**约束归一成"左减右 + 右端常数"**（两个重载各自只有一种形状）、
/// **空块产出空模型而不是 null**、**`Build` 由子类实现且 `WithModel` 走它**、**`Weights` 一个输入一个输出**。
///
/// The criteria focus on **constraints normalising to "left minus right plus a right-hand constant"** (each overload producing a single shape), on **a null block yielding an empty model rather
/// than null**, on **`Build` being the subclass's job with `WithModel` routing through it**, and on **`Weights` producing one term per input**.
/// </remarks>
public class TypedModelBuilderTests {
    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), new Flt64(value));

    private sealed class Recorder : TypedModelBuilder<string, string> {
        public LinearMetaModel<Flt64>? Model2 { get; private set; }
        public string? Context { get; private set; }

        public override string Build(LinearMetaModel<Flt64> model, Action<string> block) {
            Model2 = model;
            var box = new List<string>();
            block("marker");
            Context = "built";
            return "done";
        }
    }

    /// <summary>
    /// **两个约束重载各自只有一种形状** / Each constraint overload produces exactly one shape.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 表达式对表达式：右端常数 **固定为零**，系数是两边之差（`5 - 2 = 3`）。
    /// 2. 表达式对常数：系数**原样保留**，右端是那个常数（`7`）。
    /// 3. 两种重载的比较符都**原样透传**（`LT` 进 `LT` 出）。
    /// 4. 相减的方向是**左减右**，反过来会得到 `-3`——判据钉住符号方向。
    ///
    /// Four paired points: expression against expression fixes **the constant at zero** with the coefficient being the difference (`5 - 2 = 3`); expression against a constant **keeps the
    /// coefficient as given** with the right-hand side being that constant (`7`); both overloads **pass the sign through** (`LT` in, `LT` out); and the subtraction direction is **left minus
    /// right**, its reverse giving `-3`, which is what the sign-direction criterion pins.
    /// </remarks>
    [Fact]
    public void Constraints_ShouldNormaliseToOneShape() {
        var builder = new Recorder();
        TypedExpression left = builder.Expr(Constant(5.0));
        TypedExpression right = builder.Expr(Constant(2.0));

        TypedConstraint bothSides = builder.Constraint(left, Comparison.LT, right);
        bothSides.Sign.Should().Be(Comparison.LT, "the sign passes through");
        bothSides.Rhs.ToDouble().Should().BeApproximately(0.0, 1e-12,
            "transposing folds the constant into the coefficient");
        bothSides.Coefficient.Constant.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-12,
            "left minus right, not the reverse");
        bothSides.Coefficient.Monomials.Should().BeEmpty();

        TypedConstraint againstConstant = builder.Constraint(left, Comparison.GE, new Flt64(7.0));
        againstConstant.Sign.Should().Be(Comparison.GE);
        againstConstant.Rhs.ToFlt64().ToDouble().Should().BeApproximately(7.0, 1e-12);
        againstConstant.Coefficient.Constant.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-12,
            "the coefficient is untouched in this overload");
    }

    /// <summary>
    /// **空块产出空模型，`WithModel` 走子类的 `Build`** / A null block yields an empty model, and `WithModel` routes through the subclass's `Build`.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 不带块时**不是 null**，是一个没有约束的模型。
    /// 2. `Weights` 三个输入给三个项（不是两个、也不是一个）。
    /// 3. `WithModel` 返回子类 `Build` 的返回值，且**把同一个模型实例交过去**（引用相等）。
    ///
    /// Three paired points: without a block the result is **not null** but a model with no constraints; `Weights` with three inputs gives three terms (not two, not one); and `WithModel`
    /// returns the subclass's `Build` result while **handing over the same model instance** (reference-equal).
    /// </remarks>
    [Fact]
    public void ModelAndBuild_ShouldFollowTheSubclass() {
        var builder = new Recorder();
        LinearMetaModel<Flt64> empty = builder.Model();
        empty.Should().NotBeNull("an absent block means an empty model, not null");

        IReadOnlyList<TypedTerm> terms = builder.Weights(Constant(1.0), Constant(2.0), Constant(3.0));
        terms.Should().HaveCount(3, "one term per input coefficient");

        LinearMetaModel<Flt64> model = builder.Model();
        string result = builder.WithModel(model, _ => { });
        result.Should().Be("done", "the subclass's Build provides the result");
        ReferenceEquals(builder.Model2, model).Should().BeTrue("WithModel passes the same model instance");
        builder.Context.Should().Be("built", "the subclass actually ran");
    }
}
