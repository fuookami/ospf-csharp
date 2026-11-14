#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>逻辑指示函数共享约束 / Shared constraints for logical indicator functions.</summary>
internal static class IndicatorSupport {
    public static readonly Flt64 DefaultTolerance = new(1e-10);
    public static readonly Flt64 StrictBoundary = new(
        (1e-10 * 16.0) + (System.Math.Pow(2.0, -52.0) * 16.0)
    );

    public static LinearPolynomial<Flt64> CopyInput(LinearPolynomial<Flt64> polynomial) => new(
        Array.AsReadOnly(polynomial.Monomials.ToArray()),
        polynomial.Constant
    );

    public static Try PrepareInput(
        IAbstractLinearMechanismModel<Flt64> model,
        IMathFunctionSymbol<Flt64> function,
        LinearPolynomial<Flt64> polynomial,
        Flt64? explicitBigM,
        out LinearPolynomial<Flt64> input,
        out Flt64 bigM,
        out BoundDependencySnapshot<Flt64>? dependencies) {
        input = polynomial;
        bigM = Flt64.Zero;
        dependencies = null;
        var functions = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
        Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> expanded =
            new SymbolFlatten.LinearFlattenData<Flt64>(polynomial.Monomials, polynomial.Constant)
                .ExpandIntermediateSymbols(tokens: model.Tokens, functions: functions);
        if (!expanded.IsOk) {
            return expanded.Map(_ => Results.SuccessInstance);
        }
        var helpers = function.HelperVariables.Select(variable => variable.Key).ToHashSet();
        if (functions.OfType<LinearFunctionSymbolAdapter<Flt64>>()
                .Any(adapter => ReferenceEquals(adapter.Function, function)) ||
            expanded.Value.Monomials.Any(term =>
                term.Symbol is IVariableItem variable && helpers.Contains(variable.Key))) {
            return InvalidValue();
        }
        input = new LinearPolynomial<Flt64>(expanded.Value.Monomials, expanded.Value.Constant);
        if (explicitBigM is null) {
            dependencies = new BoundDependencySnapshot<Flt64>(new[] { input });
        }
        bigM = explicitBigM ?? BigMValues.InferredBigM(input, IIntoValue<Flt64>.Identity) ?? Flt64.Zero;
        return dependencies is null || dependencies.IsCurrent()
            ? Results.Ok<Success>(Results.SuccessInstance)
            : InvalidValue();
    }

    public static Try AddCheckedConstraints(
        IAbstractLinearMechanismModel<Flt64> model,
        IReadOnlyList<LinearInequality<Flt64>> constraints,
        IReadOnlyList<BoundDependencySnapshot<Flt64>> dependencies) {
        var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
        if (constraints.Any(row => row.Lhs.Monomials.Concat(row.Rhs.Monomials).Any(term =>
                !term.Coefficient.Eq(Flt64.Zero) &&
                (term.Symbol is not IVariableItem variable || !registered.Contains(variable.Key)))) ||
            dependencies.Any(dependency => !dependency.IsCurrent())) {
            return InvalidValue();
        }
        return AddConstraints(
            model: model,
            constraints: constraints,
            afterWrite: () => {
                if (dependencies.Any(dependency => !dependency.IsCurrent())) {
                    return InvalidValue();
                }
                if (model is BasicMechanismModel<Flt64> mechanism) {
                    foreach (BoundDependencySnapshot<Flt64> dependency in dependencies) {
                        mechanism.TrackBoundDependencies(dependency);
                    }
                }
                return Results.OkInstance;
            }
        );
    }

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

    /// <summary>识别直接二值输入 / Recognize a direct binary input.</summary>
    public static BinVar? AsBinaryVariable(LinearPolynomial<Flt64> polynomial) =>
        polynomial.Constant.Eq(Flt64.Zero) &&
        polynomial.Monomials.Count == 1 &&
        polynomial.Monomials[0].Coefficient.Eq(Flt64.One) &&
        polynomial.Monomials[0].Symbol is BinVar variable
            ? variable
            : null;

    /// <summary>验证指示函数数值 / Validate indicator-function values.</summary>
    public static Try Validate(
        LinearPolynomial<Flt64> polynomial,
        Flt64 bigM,
        Flt64 tolerance,
        Flt64? strictBoundary = null) {
        // shortcut: 行运算的浮点间隔尚未验证；完成受检行编码时移除 / Row arithmetic gaps remain unchecked; remove after checked encoding.
        bool valid = IsUsable(polynomial) &&
            IsUsable(bigM) && bigM.Gr(Flt64.Zero) &&
            IsUsable(tolerance) && tolerance.Geq(Flt64.Zero);
        if (strictBoundary is { } strict) {
            valid = valid &&
                IsUsable(strict) &&
                strict.Gr(Flt64.Zero);
        }
        return valid
            ? Results.Ok<Success>(Results.SuccessInstance)
            : InvalidValue();
    }

    /// <summary>构造正数指示约束 / Build positive-indicator rows.</summary>
    public static IReadOnlyList<LinearInequality<V>> PositiveConstraints<V>(
        LinearPolynomial<V> polynomial,
        BinVar indicator,
        V bigM,
        V tolerance,
        string name,
        V zero)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V lowerRelaxM = bigM.Plus(tolerance.Abs());
        var upperMonomials = new List<LinearMonomial<V>>(polynomial.Monomials.Count + 1);
        upperMonomials.AddRange(polynomial.Monomials);
        upperMonomials.Add(new LinearMonomial<V>(bigM.Negate(), indicator));
        var lowerMonomials = new List<LinearMonomial<V>>(polynomial.Monomials.Count + 1);
        lowerMonomials.AddRange(polynomial.Monomials);
        lowerMonomials.Add(new LinearMonomial<V>(lowerRelaxM.Negate(), indicator));

        return new[] {
            Row(
                monomials: upperMonomials,
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: zero,
                name: $"{name}_positive_ub"
            ),
            Row(
                monomials: lowerMonomials,
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: tolerance.Minus(lowerRelaxM),
                name: $"{name}_positive_lb"
            ),
        };
    }

    /// <summary>构造非零指示约束 / Build nonzero-indicator rows.</summary>
    public static IReadOnlyList<LinearInequality<V>> NonzeroConstraints<V>(
        LinearPolynomial<V> polynomial,
        BinVar indicator,
        BinVar side,
        V bigM,
        V tolerance,
        V strictBoundary,
        string name)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V bandM = bigM.Plus(tolerance.Abs());
        V outM = bigM.Plus(strictBoundary.Abs());
        return new[] {
            Row(
                monomials: Shift(
                    polynomial,
                    new LinearMonomial<V>(bandM.Negate(), indicator)
                ),
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: tolerance,
                name: $"{name}_band_ub"
            ),
            Row(
                monomials: Shift(
                    polynomial,
                    new LinearMonomial<V>(bandM, indicator)
                ),
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: tolerance.Negate(),
                name: $"{name}_band_lb"
            ),
            Row(
                monomials: Shift(
                    polynomial: polynomial,
                    additional: new[] {
                        new LinearMonomial<V>(outM.Negate(), indicator),
                        new LinearMonomial<V>(outM.Negate(), side),
                    }
                ),
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: strictBoundary.Minus(outM).Minus(outM),
                name: $"{name}_out_lb"
            ),
            Row(
                monomials: Shift(
                    polynomial: polynomial,
                    additional: new[] {
                        new LinearMonomial<V>(outM, indicator),
                        new LinearMonomial<V>(outM.Negate(), side),
                    }
                ),
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: strictBoundary.Negate().Plus(outM),
                name: $"{name}_out_ub"
            ),
        };
    }

    /// <summary>以事务注册约束 / Register constraints in a transaction.</summary>
    public static Try AddConstraints<V>(
        IAbstractLinearMechanismModel<V> model,
        IEnumerable<LinearInequality<V>> constraints,
        Func<Try>? afterWrite = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> started;
        try {
            started = model.BeginConstraintWrite() ?? Results.Failed<IConstraintWriteTransaction>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "约束事务返回空结果。 / The constraint transaction returned a null result."
            ));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return WriteFailure("开始约束事务 / Begin constraint transaction", error);
        }
        if (!started.IsOk) {
            return started.Map(_ => Results.SuccessInstance);
        }
        if (started.Value is not { } transaction) {
            return WriteFailure("开始约束事务 / Begin constraint transaction", null);
        }

        Try result = Results.OkInstance;
        try {
            foreach (LinearInequality<V> constraint in constraints) {
                result = model.AddConstraint(
                    relation: constraint,
                    name: constraint.Name,
                    from: ((IIntermediateSymbol Symbol, bool Direction)?)null
                ) ?? WriteFailure("写入约束 / Write constraints", null);
                if (!result.IsOk) {
                    break;
                }
            }
            if (result.IsOk && afterWrite is not null) {
                result = afterWrite() ?? WriteFailure("校验约束 / Validate constraints", null);
            }
            if (result.IsOk) {
                result = transaction.Commit() ?? WriteFailure("提交约束事务 / Commit constraint transaction", null);
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            result = WriteFailure("写入约束 / Write constraints", error);
        }
        if (result.IsOk) {
            return result;
        }
        Try rollback;
        try {
            rollback = transaction.Rollback() ?? WriteFailure("回滚约束 / Roll back constraints", null);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            rollback = WriteFailure("回滚约束 / Roll back constraints", error);
        }
        if (rollback.IsOk) {
            return result;
        }
        return new Fatal<Success, ErrorCode, Error<ErrorCode>>(
            Errors(result).Concat(Errors(rollback)).ToArray()
        );
    }

    private static IEnumerable<Error<ErrorCode>> Errors(Try result) => result switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> failed => new[] { failed.Error },
        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => fatal.Errors,
        _ => Array.Empty<Error<ErrorCode>>(),
    };

    private static Try WriteFailure(string operation, Exception? error) => Results.Failed<Success>(
        new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            $"{operation} 失败：{error?.Message ?? "无效事务"} / {operation} failed: {error?.Message ?? "invalid transaction"}"
        )
    );

    /// <summary>构造零值指示约束 / Build zero-indicator rows.</summary>
    /// <remarks>
    /// 对应 Kotlin `zeroIndicatorConstraints`：indicator=1 时多项式落在 ±tolerance 带内，
    /// indicator=0 时多项式必须越过 strictBoundary，side 选择正负方向。
    /// Mirrors Kotlin `zeroIndicatorConstraints`: indicator=1 keeps the polynomial inside the
    /// ±tolerance band, indicator=0 forces it past strictBoundary with `side` picking the direction.
    /// </remarks>
    public static IReadOnlyList<LinearInequality<V>> ZeroConstraints<V>(
        LinearPolynomial<V> polynomial,
        BinVar indicator,
        BinVar side,
        V bigM,
        V tolerance,
        V strictBoundary,
        string name)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V bandM = bigM.Plus(tolerance.Abs());
        V outM = bigM.Plus(strictBoundary.Abs());
        return new[] {
            Row(
                monomials: Shift(
                    polynomial,
                    new LinearMonomial<V>(bandM, indicator)
                ),
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: tolerance.Plus(bandM),
                name: $"{name}_zero_band_ub"
            ),
            Row(
                monomials: Shift(
                    polynomial,
                    new LinearMonomial<V>(bandM.Negate(), indicator)
                ),
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: tolerance.Negate().Minus(bandM),
                name: $"{name}_zero_band_lb"
            ),
            Row(
                monomials: Shift(
                    polynomial: polynomial,
                    additional: new[] {
                        new LinearMonomial<V>(outM, indicator),
                        new LinearMonomial<V>(outM.Negate(), side),
                    }
                ),
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: strictBoundary.Minus(outM),
                name: $"{name}_zero_out_lb"
            ),
            Row(
                monomials: Shift(
                    polynomial: polynomial,
                    additional: new[] {
                        new LinearMonomial<V>(outM.Negate(), indicator),
                        new LinearMonomial<V>(outM.Negate(), side),
                    }
                ),
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: strictBoundary.Negate(),
                name: $"{name}_zero_out_ub"
            ),
        };
    }

    /// <summary>不等式两侧的差值多项式 lhs-rhs / Difference polynomial lhs-rhs of an inequality.</summary>
    public static LinearPolynomial<Flt64> Difference(LinearInequality<Flt64> inequality) {
        var monomials = new List<LinearMonomial<Flt64>>(
            inequality.Lhs.Monomials.Count + inequality.Rhs.Monomials.Count
        );
        monomials.AddRange(inequality.Lhs.Monomials);
        monomials.AddRange(inequality.Rhs.Monomials.Select(monomial =>
            new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol)));
        return new LinearPolynomial<Flt64>(
            monomials,
            inequality.Lhs.Constant.Minus(inequality.Rhs.Constant)
        );
    }

    /// <summary>构造约束行 / Build a constraint row.</summary>
    public static LinearInequality<V> Row<V>(
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

    /// <summary>创建指示输入错误 / Create an indicator-input error.</summary>
    public static Try InvalidValue() =>
        Results.Failed<Success>(
            new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "逻辑指示输入或参数含非有限、不可表示或越界值。 / Logical-indicator input or parameter contains a non-finite, unrepresentable, or invalid value."
            )
        );

    private static bool IsUsable(LinearPolynomial<Flt64> polynomial) =>
        BigMValues.IsUsable(polynomial.Constant) &&
        polynomial.Monomials.All(monomial => BigMValues.IsUsable(monomial.Coefficient));

    private static bool IsUsable(Flt64 value) => BigMValues.IsUsable(value);

    private static IReadOnlyList<LinearMonomial<V>> Shift<V>(
        LinearPolynomial<V> polynomial,
        params LinearMonomial<V>[] additional)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var monomials = new List<LinearMonomial<V>>(
            polynomial.Monomials.Count + additional.Length
        );
        monomials.AddRange(polynomial.Monomials);
        monomials.AddRange(additional);
        return monomials;
    }
}
