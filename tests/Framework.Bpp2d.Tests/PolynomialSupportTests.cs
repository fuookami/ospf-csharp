// 多项式构造辅助的判据 / Criteria for the polynomial construction helpers.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// **空负载是零多项式，零系数单项式不算负载** / An empty load is the zero polynomial, and zero-coefficient monomials do not count as load.
/// </summary>
/// <remarks>
/// 核心 `Sum` 在空列表上抛异常，而"尚未加列的空负载"在列生成里是正常状态。把它表示成错误会让每个调用方
/// 去处理一个不存在的失败；而只含零系数单项式的列表在 `Count` 上非空，却同样代表零负载。
///
/// The core `Sum` throws on an empty list, while "an empty load before any column is added" is a normal state in column generation. Representing it as an error forces
/// every caller to handle a failure that does not exist; and a list holding only zero-coefficient monomials is non-empty by `Count` while also representing zero load.
/// </remarks>
public class PolynomialSupportTests {
    /// **空列表返回零多项式** / An empty list yields the zero polynomial.
    [Fact]
    public void SafeLinearPolynomial_ShouldReturnZeroOnEmpty() {
        LinearPolynomial<FltX> polynomial =
            PolynomialSupport.SafeLinearPolynomial(new List<LinearMonomial<FltX>>());

        polynomial.Monomials.Should().BeEmpty();
        polynomial.Constant.Should().Be(FltX.Zero);
    }

    /// <summary>
    /// **非空列表与核心 `Sum` 一致** / A non-empty list matches the core `Sum`.
    /// </summary>
    /// <remarks>
    /// 这条防的是"忘记转发"：只用空列表测的话，一个恒返回零多项式的实现照样通过。
    ///
    /// This guards against "forgetting to delegate": with only the empty case tested, an implementation that always returns the zero polynomial would pass.
    /// </remarks>
    [Fact]
    public void SafeLinearPolynomial_ShouldDelegateForNonEmpty() {
        var symbol = new TestSymbol("x");
        var monomials = new List<LinearMonomial<FltX>> {
            new(new FltX(2.0), symbol),
            new(new FltX(3.0), symbol),
        };

        LinearPolynomial<FltX> polynomial = PolynomialSupport.SafeLinearPolynomial(monomials);

        polynomial.Monomials.Should().HaveCount(2, "the monomials are passed through, not collapsed");
        polynomial.Monomials[0].Coefficient.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// <summary>
    /// **零系数单项式被剔除，于是"只含零系数"读作空** / Zero-coefficient monomials are dropped, so "only zeros" reads as empty.
    /// </summary>
    /// <remarks>
    /// 这是本类的关键语义：一个只含零系数单项式的列表**在 `Count` 上非空**，但它代表的负载是零。
    /// 若不过滤，"还没加列"与"加了列但没装东西"会在判断处走同一条分支。
    ///
    /// This is the class's key semantic: a list holding only zero-coefficient monomials is **non-empty by `Count`** while representing zero load. Without the filter,
    /// "no column yet" and "a column that loaded nothing" take the same branch.
    /// </remarks>
    [Fact]
    public void SafeLinearMonomials_ShouldDropOnlyTheZeroCoefficients() {
        var symbol = new TestSymbol("x");
        var mixed = new List<LinearMonomial<FltX>> {
            new(FltX.Zero, symbol),
            new(new FltX(1.5), symbol),
            new(FltX.Zero, symbol),
        };

        IReadOnlyList<LinearMonomial<FltX>> kept = PolynomialSupport.SafeLinearMonomials(mixed);

        kept.Should().HaveCount(1, "the two zero-coefficient monomials are dropped");
        kept[0].Coefficient.ToFlt64().ToDouble().Should().BeApproximately(1.5, 1e-12);

        IReadOnlyList<LinearMonomial<FltX>> onlyZeros = PolynomialSupport.SafeLinearMonomials(
            new List<LinearMonomial<FltX>> { new(FltX.Zero, symbol) });
        onlyZeros.Should().BeEmpty("a load of only zeros is an empty load");
    }

    /// **全零输入的组合读作零多项式** / The all-zero input composes into the zero polynomial.
    [Fact]
    public void ZeroCoefficients_ShouldComposeIntoTheZeroPolynomial() {
        var symbol = new TestSymbol("x");
        var onlyZeros = new List<LinearMonomial<FltX>> {
            new(FltX.Zero, symbol),
            new(FltX.Zero, symbol),
        };

        LinearPolynomial<FltX> polynomial =
            PolynomialSupport.SafeLinearPolynomial(PolynomialSupport.SafeLinearMonomials(onlyZeros));

        polynomial.Monomials.Should().BeEmpty();
        polynomial.Constant.Should().Be(FltX.Zero);
    }
}

/// <summary>
/// 测试用符号 / A symbol for tests.
/// </summary>
/// <param name="Name">名称 / The name.</param>
/// <remarks>
/// `ISymbol` 只有 `Name` 和 `DisplayName` 两个成员，所以这里直接实现，不去构造 `RealVariable1`——
/// 那会把测试绑到 `Core` 的变量体系上，而本判据只关心多项式聚合。
///
/// `ISymbol` has only `Name` and `DisplayName`, so it is implemented directly rather than constructing a `RealVariable1`, which would bind the criteria to the `Core`
/// variable system when only polynomial aggregation is under test.
/// </remarks>
public sealed record TestSymbol(string Name) : ISymbol {
    /// <inheritdoc/>
    public string? DisplayName => null;
}
