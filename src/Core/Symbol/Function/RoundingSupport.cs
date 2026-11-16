#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>取整函数共享约束构造 / Shared constraint construction for rounding functions.</summary>
internal static class RoundingSupport {
    public const double Tolerance = 1e-10;
    public const double Half = 0.5;

    /// <summary>求值线性多项式 / Evaluate a linear polynomial.</summary>
    public static V? Evaluate<V>(
        LinearPolynomial<V> polynomial,
        IReadOnlyDictionary<ISymbol, V> values)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V result = polynomial.Constant;
        foreach (LinearMonomial<V> monomial in polynomial.Monomials) {
            if (!values.TryGetValue(monomial.Symbol, out V value)) {
                return null;
            }
            result = result.Plus(monomial.Coefficient.Times(value));
        }
        return result;
    }

    /// <summary>验证输入和 Big-M 可用于约束生成 / Check that input and Big-M are usable for row generation.</summary>
    public static bool IsUsable(LinearPolynomial<Flt64> polynomial, params Flt64[] values) =>
        BigMValues.IsUsable(polynomial.Constant) &&
        polynomial.Monomials.All(monomial => BigMValues.IsUsable(monomial.Coefficient)) &&
        values.All(BigMValues.IsUsable);

    public static V Floor<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        value.IsSelfWithinBounds() ? value.IntDiv(converter.One) : value;

    public static V Ceiling<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V floor = Floor(value, converter);
        if (!value.IsSelfWithinBounds() || value.Eq(floor)) {
            return floor;
        }
        V result = floor.Plus(converter.One);
        return result.Eq(converter.Zero) ? value.Times(converter.Zero) : result;
    }

    public static V Round<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V floor = Floor(value, converter);
        if (!value.IsSelfWithinBounds() || value.Eq(floor)) {
            return floor;
        }
        V fraction = value.Minus(floor);
        V twiceFraction = fraction.Plus(fraction);
        V two = converter.One.Plus(converter.One);
        // 求值按点取偶，模型中点则向上 / Evaluation rounds ties to even; the model rounds ties up.
        bool roundUp = twiceFraction.Gr(converter.One) ||
            (twiceFraction.Eq(converter.One) && !floor.Rem(two).Eq(converter.Zero));
        V result = roundUp ? floor.Plus(converter.One) : floor;
        return result.Eq(converter.Zero) ? value.Times(converter.Zero) : result;
    }

    public static bool TryConstants<V>(
        IIntoValue<V> converter,
        V constant,
        bool ceiling,
        bool rounding,
        out V epsilon,
        out V half)
        where V : struct, IRealNumber<V>, INumberField<V> {
        epsilon = converter.Zero;
        half = converter.Zero;
        try {
            epsilon = converter.IntoValue(new Flt64(Tolerance));
            half = rounding ? converter.IntoValue(new Flt64(Half)) : converter.Zero;
            V one = converter.One;
            V negativeOne = one.Negate();
            V baseline = ceiling ? negativeOne : one;
            V strict = ceiling ? baseline.Plus(epsilon) : baseline.Minus(epsilon);
            // 扁平化常量时也必须保留间隔 / The gap must survive constant flattening too.
            V flatBaseline = constant.Minus(baseline);
            V flatStrict = constant.Minus(strict);
            return BigMValues.IsUsable(epsilon, converter) &&
                epsilon.Gr(converter.Zero) && epsilon.Ls(one) &&
                one.Minus(epsilon).Ls(one) &&
                negativeOne.Plus(epsilon).Gr(negativeOne) &&
                BigMValues.IsUsable(flatBaseline, converter) &&
                BigMValues.IsUsable(flatStrict, converter) &&
                (ceiling ? flatStrict.Ls(flatBaseline) : flatStrict.Gr(flatBaseline)) &&
                (!rounding ||
                    (BigMValues.IsUsable(half, converter) &&
                     half.Plus(half).Eq(one) &&
                     epsilon.Ls(half) && half.Minus(epsilon).Ls(half)));
        }
        catch (OverflowException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    /// <summary>构造 FLOOR 或 CEILING 约束 / Build FLOOR or CEILING rows.</summary>
    public static IReadOnlyList<LinearInequality<V>> CreateIntegerRows<V>(
        LinearPolynomial<V> polynomial,
        IntVar integerVar,
        IntVar resultVar,
        string name,
        bool ceiling,
        IIntoValue<V> converter,
        V epsilon)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V zero = converter.Zero;
        V one = converter.One;
        IReadOnlyList<LinearMonomial<V>> shift = Shift(
            polynomial: polynomial,
            integerVar: integerVar,
            one: one
        );
        string suffix = ceiling ? "ceil" : "floor";

        return new[] {
            ceiling
                ? Row(
                    monomials: shift,
                    constant: polynomial.Constant,
                    comparison: Comparison.LE,
                    rhs: zero,
                    name: $"{name}_{suffix}_ub"
                )
                : Row(
                    monomials: shift,
                    constant: polynomial.Constant,
                    comparison: Comparison.GE,
                    rhs: zero,
                    name: $"{name}_{suffix}_lb"
                ),
            ceiling
                ? Row(
                    monomials: shift,
                    constant: polynomial.Constant,
                    comparison: Comparison.GE,
                    rhs: one.Negate().Plus(epsilon),
                    name: $"{name}_{suffix}_lb"
                )
                : Row(
                    monomials: shift,
                    constant: polynomial.Constant,
                    comparison: Comparison.LE,
                    rhs: one.Minus(epsilon),
                    name: $"{name}_{suffix}_ub"
                ),
            Row(
                monomials: new[] {
                    new LinearMonomial<V>(one, resultVar),
                    new LinearMonomial<V>(one.Negate(), integerVar),
                },
                constant: zero,
                comparison: Comparison.EQ,
                rhs: zero,
                name: $"{name}_{suffix}_result"
            ),
        };
    }

    /// <summary>构造 ROUND 约束 / Build ROUND rows.</summary>
    public static IReadOnlyList<LinearInequality<V>> CreateRoundingRows<V>(
        LinearPolynomial<V> polynomial,
        IntVar integerVar,
        BinVar roundUpVar,
        URealVar fractionVar,
        IntVar resultVar,
        V bigM,
        string name,
        IIntoValue<V> converter,
        V epsilon,
        V half)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V zero = converter.Zero;
        V one = converter.One;
        IReadOnlyList<LinearMonomial<V>> shift = Shift(
            polynomial: polynomial,
            integerVar: integerVar,
            one: one
        );
        var differenceMonomials = new List<LinearMonomial<V>>(polynomial.Monomials.Count + 1) {
            new(one, fractionVar),
            new(one, integerVar),
        };
        differenceMonomials.AddRange(polynomial.Monomials.Select(monomial =>
            new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol)));

        return new[] {
            Row(
                monomials: shift,
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: zero,
                name: $"{name}_round_k_lb"
            ),
            Row(
                monomials: shift,
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: one.Minus(epsilon),
                name: $"{name}_round_k_ub"
            ),
            Row(
                monomials: differenceMonomials,
                constant: polynomial.Constant.Negate(),
                comparison: Comparison.EQ,
                rhs: zero,
                name: $"{name}_round_decompose"
            ),
            Row(
                monomials: new[] { new LinearMonomial<V>(one, fractionVar) },
                constant: zero,
                comparison: Comparison.LE,
                rhs: one.Minus(epsilon),
                name: $"{name}_round_b_ub"
            ),
            Row(
                monomials: new[] {
                    new LinearMonomial<V>(one, fractionVar),
                    new LinearMonomial<V>(half.Negate(), roundUpVar),
                },
                constant: zero,
                comparison: Comparison.GE,
                rhs: zero,
                name: $"{name}_round_r_lb"
            ),
            Row(
                monomials: new[] {
                    new LinearMonomial<V>(one, fractionVar),
                    new LinearMonomial<V>(bigM.Negate(), roundUpVar),
                },
                constant: zero,
                comparison: Comparison.LE,
                rhs: half.Minus(epsilon),
                name: $"{name}_round_r_ub"
            ),
            Row(
                monomials: new[] {
                    new LinearMonomial<V>(one, resultVar),
                    new LinearMonomial<V>(one.Negate(), integerVar),
                    new LinearMonomial<V>(one.Negate(), roundUpVar),
                },
                constant: zero,
                comparison: Comparison.EQ,
                rhs: zero,
                name: $"{name}_round_result"
            ),
        };
    }

    /// <summary>以事务注册约束 / Register constraints in a transaction.</summary>
    public static Try AddConstraints<V>(
        IAbstractLinearMechanismModel<V> model,
        IReadOnlyList<LinearInequality<V>> constraints)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        IndicatorSupport.AddConstraints(model, constraints);

    /// <summary>创建不可用输入错误 / Create an invalid-input error.</summary>
    public static Try InvalidValue(string function) =>
        Results.Failed<Success>(
            new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"{function} 输入或 Big-M 含非有限、不可表示值。 / {function} input or Big-M contains a non-finite or unrepresentable value."
            )
        );

    public static Try InvalidResolution(string function) =>
        Results.Failed<Success>(
            new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"{function} 数值类型无法表示取整约束间隔。 / {function} numeric type cannot represent the rounding constraint gap."
            )
        );

    private static IReadOnlyList<LinearMonomial<V>> Shift<V>(
        LinearPolynomial<V> polynomial,
        IntVar integerVar,
        V one)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var monomials = new List<LinearMonomial<V>>(polynomial.Monomials.Count + 1);
        monomials.AddRange(polynomial.Monomials);
        monomials.Add(new LinearMonomial<V>(one.Negate(), integerVar));
        return monomials;
    }

    internal static LinearInequality<V> Row<V>(
        IReadOnlyList<LinearMonomial<V>> monomials,
        V constant,
        Comparison comparison,
        V rhs,
        string name)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(
            Lhs: new LinearPolynomial<V>(monomials, constant),
            Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), rhs),
            Comparison: comparison,
            Name: name
        );
}
