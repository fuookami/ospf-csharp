#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function
{
    /// <summary>
    /// 函数符号注册测试 / Function symbol registration tests.
    /// <para>验证所有函数原子类正确实现 IMathFunctionSymbol 接口，RegisterAuxiliaryTokens/RegisterConstraints 返回 Ok。</para>
    /// </summary>
    public class FunctionSymbolRegistrationTest
    {
        public static IEnumerable<object[]> AllFunctionTypes()
        {
            yield return new object[] { new AbsFunction() };
            yield return new object[] { new AndFunction() };
            yield return new object[] { new OrFunction() };
            yield return new object[] { new NotFunction() };
            yield return new object[] { new XorFunction() };
            yield return new object[] { new BalanceTernaryzationFunction() };
            yield return new object[] { new BigMFunction() };
            yield return new object[] { new BinaryzationFunction() };
            yield return new object[] { new BivariateLinearPiecewiseFunction() };
            yield return new object[] { new CeilingFunction() };
            yield return new object[] { new CosFunction() };
            yield return new object[] { new FirstFunction() };
            yield return new object[] { new FloorFunction() };
            yield return new object[] { new IfFunction() };
            yield return new object[] { new IfInFunction() };
            yield return new object[] { new IfThenFunction() };
            yield return new object[] { new ImplyFunction() };
            yield return new object[] { new InequalityFunction() };
            yield return new object[] { new InStepRangeFunction() };
            yield return new object[] { new MaskingFunction() };
            yield return new object[] { new MaskingWithPolyMaskFunction() };
            yield return new object[] { new MaskingRangeFunction() };
            yield return new object[] { new MaxFunction() };
            yield return new object[] { new MinFunction() };
            yield return new object[] { new MinMaxFunction() };
            yield return new object[] { new MaxMinFunction() };
            yield return new object[] { new ModFunction() };
            yield return new object[] { new OneOfFunction() };
            yield return new object[] { new ProductFunction() };
            yield return new object[] { new QuadraticInStepRangeFunction() };
            yield return new object[] { new QuadraticLinearFunction() };
            yield return new object[] { new QuadraticMaskingRangeFunction() };
            yield return new object[] { new QuadraticMinFunction() };
            yield return new object[] { new RoundingFunction() };
            yield return new object[] { new SameAsFunction() };
            yield return new object[] { new SatisfiedAmountFunction() };
            yield return new object[] { new AnyFunction() };
            yield return new object[] { new AllFunction() };
            yield return new object[] { new AtLeastInequalityFunction() };
            yield return new object[] { new NotAllFunction() };
            yield return new object[] { new NumerableFunction() };
            yield return new object[] { new SemiFunction() };
            yield return new object[] { new SigmoidFunction() };
            yield return new object[] { new SinFunction() };
            yield return new object[] { new SlackFunction() };
            yield return new object[] { new SlackRangeFunction() };
            yield return new object[] { new UnivariateLinearPiecewiseFunction() };
        }

        [Theory]
        [MemberData(nameof(AllFunctionTypes))]
        public void FunctionSymbol_ImplementsInterface(IMathFunctionSymbol<Flt64> function)
        {
            function.Should().NotBeNull();
            function.Name.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [MemberData(nameof(AllFunctionTypes))]
        public void FunctionSymbol_RegisterConstraints_ReturnsOk(IMathFunctionSymbol<Flt64> function)
        {
            var result = function.RegisterConstraints(new object());
            result.IsOk.Should().BeTrue();
        }

        [Fact]
        public void FunctionSymbol_Count_AtLeast37()
        {
            var functionTypes = typeof(AbsFunction).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IMathFunctionSymbol<Flt64>).IsAssignableFrom(t))
                .ToList();
            functionTypes.Count.Should().BeGreaterThanOrEqualTo(37);
        }

        [Fact]
        public void LinearPolynomialBounds_CanBeCreated()
        {
            var bounds = new LinearPolynomialBounds<Flt64>(
                LowerBound: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
                UpperBound: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.One));
            bounds.LowerBound.Should().NotBeNull();
            bounds.UpperBound.Should().NotBeNull();
        }
    }
}
