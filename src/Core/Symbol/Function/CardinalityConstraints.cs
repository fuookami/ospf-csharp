#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>二元变量的纯基数约束工厂 / Pure cardinality constraints for binary variables.</summary>
public static class CardinalityConstraints {
    /// <summary>构造二元变量之和不超过上限的约束 / Build a constraint limiting the sum of binary variables.</summary>
    /// <remarks>重复变量按出现次数计数；使用目标模型的转换器并保持变量类型及转换器不变。 / Repeated variables count per occurrence; use the target model's converter and keep variable types and the converter unchanged.</remarks>
    /// <typeparam name="V">系数数值类型 / Coefficient numeric type.</typeparam>
    /// <param name="indicators">二元变量列表 / Binary variable list.</param>
    /// <param name="limit">允许的最大数量 / Maximum allowed count.</param>
    /// <param name="converter">模型数值转换器 / Model value converter.</param>
    /// <param name="name">约束名称 / Constraint name.</param>
    /// <returns>单条约束或校验失败 / One constraint or validation failure.</returns>
    public static Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> AtMost<V>(
        IReadOnlyList<IVariableItem>? indicators,
        int limit,
        IIntoValue<V>? converter,
        string name = "at_most")
        where V : struct, IRealNumber<V>, INumberField<V> => Build(
            indicators: indicators,
            amount: limit,
            converter: converter,
            name: name,
            comparison: Comparison.LE
        );

    /// <summary>构造二元变量之和等于指定数量的约束 / Build a constraint fixing the sum of binary variables.</summary>
    /// <remarks>重复变量按出现次数计数；使用目标模型的转换器并保持变量类型及转换器不变。 / Repeated variables count per occurrence; use the target model's converter and keep variable types and the converter unchanged.</remarks>
    /// <typeparam name="V">系数数值类型 / Coefficient numeric type.</typeparam>
    /// <param name="indicators">二元变量列表 / Binary variable list.</param>
    /// <param name="amount">要求的数量 / Required count.</param>
    /// <param name="converter">模型数值转换器 / Model value converter.</param>
    /// <param name="name">约束名称 / Constraint name.</param>
    /// <returns>单条约束或校验失败 / One constraint or validation failure.</returns>
    public static Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> Exactly<V>(
        IReadOnlyList<IVariableItem>? indicators,
        int amount,
        IIntoValue<V>? converter,
        string name = "exactly")
        where V : struct, IRealNumber<V>, INumberField<V> => Build(
            indicators: indicators,
            amount: amount,
            converter: converter,
            name: name,
            comparison: Comparison.EQ
        );

    private static Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> Build<V>(
        IReadOnlyList<IVariableItem>? indicators,
        int amount,
        IIntoValue<V>? converter,
        string name,
        Comparison comparison)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (indicators is null || converter is null || name is null) {
                return Invalid<V>();
            }
            IVariableItem[] snapshot = indicators.ToArray();
            if (amount < 0 || amount > snapshot.Length || snapshot.Any(variable => variable is null || !variable.TypeKind.IsBinaryType)) {
                return Invalid<V>();
            }
            V zero = converter.Zero;
            V one = converter.One;
            V target = converter.IntoValue(new Flt64(amount));
            if (!IsInteger(
                    value: zero,
                    expected: 0,
                    converter: converter
                ) || !IsInteger(
                    value: one,
                    expected: 1,
                    converter: converter
                ) || !IsInteger(
                    value: target,
                    expected: amount,
                    converter: converter
                ) || !CanRepresent(snapshot.Length, converter) ||
                snapshot.Length > 0 && !CanRepresent(snapshot.Length - 1, converter) ||
                !LinearFlattenExtensions.Arithmetic<V>.TrySubtract(
                    lhs: zero,
                    rhs: target,
                    result: out V negative
                ) ||
                !IsInteger(
                    value: negative,
                    expected: -amount,
                    converter: converter
                ) ||
                !LinearFlattenExtensions.Arithmetic<V>.TryNegate(negative, out V restored) ||
                !IsInteger(
                    value: restored,
                    expected: amount,
                    converter: converter
                )) {
                return Invalid<V>();
            }
            foreach (IGrouping<VariableItemKey, IVariableItem> group in snapshot.GroupBy(variable => variable.Key)) {
                if (group.Key is null || !CanRepresent(group.Count(), converter)) {
                    return Invalid<V>();
                }
            }
            var lhs = new LinearPolynomial<V>(
                Array.AsReadOnly(snapshot.Select(variable => new LinearMonomial<V>(one, variable)).ToArray()), zero
            );
            var rhs = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), target);
            var row = new LinearInequality<V>(
                Lhs: lhs,
                Rhs: rhs,
                Comparison: comparison,
                Name: name
            );
            return Results.Ok<IReadOnlyList<LinearInequality<V>>>(Array.AsReadOnly(new[] { row }));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<V>();
        }
    }

    private static bool CanRepresent<V>(int value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        IsInteger(
            value: converter.IntoValue(new Flt64(value)),
            expected: value,
            converter: converter
        );

    private static bool IsInteger<V>(
        V value,
        int expected,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out ExactBoundValue exact
        ) &&
        exact.Numerator == (BigInteger)expected * exact.Denominator &&
        converter.FromValue(value).ToDouble() == expected;

    private static Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> Invalid<V>()
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Results.Failed<IReadOnlyList<LinearInequality<V>>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "基数约束输入或数值转换无效。 / Invalid cardinality inputs or numeric conversion."
        ));
}
