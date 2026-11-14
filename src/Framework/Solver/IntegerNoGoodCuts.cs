#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>一个待排除的有界整数赋值 / One bounded integer assignment to exclude.</summary>
/// <param name="Key">稳定绑定键 / Stable binding key.</param>
/// <param name="Variable">主问题整数变量 / Master integer variable.</param>
/// <param name="Value">待排除赋值 / Assignment to exclude.</param>
/// <param name="Domain">用于推导精确边界的有限值域 / Finite domain used to derive exact bounds.</param>
public sealed record IntegerNoGoodVariable(
    string Key,
    IVariableItem Variable,
    Int64 Value,
    IntegerDomain Domain);

/// <summary>一个整数 no-good 赋值的线性精确编码 / Linear exact encoding of one integer no-good assignment.</summary>
public sealed class IntegerNoGoodEncoding {
    /// <summary>创建整数 no-good 编码 / Create an integer no-good encoding.</summary>
    /// <param name="name">稳定名称前缀 / Stable name prefix.</param>
    /// <param name="auxiliaryVariables">分支指示变量 / Branch-indicator variables.</param>
    /// <param name="constraints">实现 no-good 的线性约束 / Linear constraints implementing the no-good.</param>
    public IntegerNoGoodEncoding(
        string name,
        IEnumerable<BinVar> auxiliaryVariables,
        IEnumerable<LinearInequality<Flt64>> constraints) {
        ArgumentNullException.ThrowIfNull(auxiliaryVariables);
        ArgumentNullException.ThrowIfNull(constraints);
        Name = name;
        AuxiliaryVariables = Array.AsReadOnly(auxiliaryVariables.ToArray());
        Constraints = Array.AsReadOnly(constraints.ToArray());
    }

    /// <summary>稳定名称前缀 / Stable name prefix.</summary>
    public string Name { get; }

    /// <summary>分支指示变量 / Branch-indicator variables.</summary>
    public IReadOnlyList<BinVar> AuxiliaryVariables { get; }

    /// <summary>实现 no-good 的线性约束 / Linear constraints implementing the no-good.</summary>
    public IReadOnlyList<LinearInequality<Flt64>> Constraints { get; }

    /// <summary>将辅助变量和约束安装到主问题 / Install auxiliary variables and constraints into the master.</summary>
    /// <param name="master">接收生成模型项的可变主问题 / Mutable master receiving generated items.</param>
    /// <returns>安装结果 / Installation result.</returns>
    public Try Install(LinearMetaModel<Flt64> master) {
        ArgumentNullException.ThrowIfNull(master);
        var auxiliaryKeys = new HashSet<VariableItemKey>();
        foreach (BinVar variable in AuxiliaryVariables) {
            if (!auxiliaryKeys.Add(variable.Key)) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    $"整数 no-good 辅助变量键重复：{variable.Identifier}:{variable.Index} / " +
                    $"Integer no-good auxiliary variable key is duplicated: " +
                    $"{variable.Identifier}:{variable.Index}");
            }
        }
        var addedVariables = new List<BinVar>();
        foreach (BinVar variable in AuxiliaryVariables) {
            Try added = master.Add(variable);
            if (added.IsFailed) {
                RollbackVariables(master, addedVariables);
                return added;
            }
            addedVariables.Add(variable);
        }
        bool constraintAdded = false;
        for (int index = 0; index < Constraints.Count; index++) {
            Try added = master.AddConstraint(
                relation: Constraints[index],
                group: null,
                name: $"{Name}-{index}",
                displayName: $"{Name}-{index}");
            if (added.IsFailed) {
                if (!constraintAdded) {
                    RollbackVariables(master, addedVariables);
                }
                return added;
            }
            constraintAdded = true;
        }
        return Results.OkInstance;
    }

    private static void RollbackVariables(
        LinearMetaModel<Flt64> master,
        IReadOnlyList<BinVar> variables) {
        for (int index = variables.Count - 1; index >= 0; index--) {
            master.Remove(variables[index]);
        }
    }

    private static Try Failure(ErrorCode code, string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));
}

/// <summary>有限整数值域精确 no-good 约束构造器 / Exact no-good builder for finite integer domains.</summary>
public static class IntegerNoGoodCutEncoder {
    private static readonly BigInteger MaxExactDoubleInteger = new(9_007_199_254_740_991L);

    /// <summary>构造精确排除一个赋值的编码 / Build an encoding that excludes exactly one assignment.</summary>
    /// <param name="variables">构成待排除赋值的变量和值 / Variables and values forming the assignment to exclude.</param>
    /// <param name="name">稳定名称前缀 / Stable name prefix.</param>
    /// <returns>精确编码或校验错误 / Exact encoding or a validation error.</returns>
    public static Result<IntegerNoGoodEncoding, ErrorCode, Error<ErrorCode>> Encode(
        IReadOnlyList<IntegerNoGoodVariable> variables,
        string name = "integer-no-good") {
        ArgumentNullException.ThrowIfNull(variables);
        if (variables.Count == 0) {
            return Failure<IntegerNoGoodEncoding>(
                ErrorCode.IllegalArgument,
                "整数 no-good 变量不能为空 / Integer no-good variables must not be empty");
        }

        var auxiliary = new List<BinVar>();
        var constraints = new List<LinearInequality<Flt64>>();
        var active = new List<BinVar>();
        for (int index = 0; index < variables.Count; index++) {
            IntegerNoGoodVariable item = variables[index];
            if (!item.Domain.Contains(item.Value)) {
                return Failure<IntegerNoGoodEncoding>(
                    ErrorCode.IllegalArgument,
                    $"赋值超出整数值域：{item.Key}={item.Value} / " +
                    $"Assignment is outside integer domain: {item.Key}={item.Value}");
            }

            BigInteger lower = new(item.Domain.LowerBound.ToLong());
            BigInteger upper = new(item.Domain.UpperBound.ToLong());
            BigInteger valueInteger = new(item.Value.ToLong());
            BigInteger positiveM = upper - valueInteger;
            BigInteger negativeM = valueInteger - lower;
            if (positiveM.Sign <= 0 && negativeM.Sign <= 0) {
                continue;
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> valueResult =
                ExactFlt64(valueInteger, $"no-good assignment {item.Key}");
            if (valueResult is not Ok<Flt64, ErrorCode, Error<ErrorCode>> valueOk) {
                return Propagate<IntegerNoGoodEncoding, Flt64>(valueResult);
            }
            Result<Flt64?, ErrorCode, Error<ErrorCode>> positiveCoefficient =
                positiveM.Sign > 0
                    ? ExactNullableFlt64(positiveM, $"no-good positive Big-M {item.Key}")
                    : Results.Ok<Flt64?>(null);
            if (positiveCoefficient is not Ok<
                    Flt64?,
                    ErrorCode,
                    Error<ErrorCode>> positiveOk) {
                return Propagate<IntegerNoGoodEncoding, Flt64?>(positiveCoefficient);
            }
            Result<Flt64?, ErrorCode, Error<ErrorCode>> negativeCoefficient =
                negativeM.Sign > 0
                    ? ExactNullableFlt64(negativeM, $"no-good negative Big-M {item.Key}")
                    : Results.Ok<Flt64?>(null);
            if (negativeCoefficient is not Ok<
                    Flt64?,
                    ErrorCode,
                    Error<ErrorCode>> negativeOk) {
                return Propagate<IntegerNoGoodEncoding, Flt64?>(negativeCoefficient);
            }

            BinVar? positive = positiveOk.Value is { } positiveValue
                ? new BinVar($"{name}-{index}-positive")
                : null;
            BinVar? negative = negativeOk.Value is { } negativeValue
                ? new BinVar($"{name}-{index}-negative")
                : null;
            if (positive is not null) {
                auxiliary.Add(positive);
                active.Add(positive);
            }
            if (negative is not null) {
                auxiliary.Add(negative);
                active.Add(negative);
            }

            if (positive is not null && negative is not null) {
                constraints.Add(Inequality(
                    terms: new[] {
                        (variable: (IVariableItem)positive, coefficient: Flt64.One),
                        (variable: (IVariableItem)negative, coefficient: Flt64.One),
                    },
                    comparison: Comparison.LE,
                    rhs: Flt64.One));
            }

            var lowerTerms = new List<(IVariableItem Variable, Flt64 Coefficient)> {
                (item.Variable, Flt64.One),
            };
            if (positive is not null) {
                lowerTerms.Add((positive, -Flt64.One));
            }
            if (negative is not null && negativeOk.Value is { } negativeMagnitude) {
                lowerTerms.Add((negative, negativeMagnitude));
            }
            constraints.Add(Inequality(
                terms: lowerTerms,
                comparison: Comparison.GE,
                rhs: valueOk.Value));

            var upperTerms = new List<(IVariableItem Variable, Flt64 Coefficient)> {
                (item.Variable, Flt64.One),
            };
            if (positive is not null && positiveOk.Value is { } positiveMagnitude) {
                upperTerms.Add((positive, -positiveMagnitude));
            }
            if (negative is not null) {
                upperTerms.Add((negative, Flt64.One));
            }
            constraints.Add(Inequality(
                terms: upperTerms,
                comparison: Comparison.LE,
                rhs: valueOk.Value));
        }

        if (active.Count == 0) {
            return Failure<IntegerNoGoodEncoding>(
                ErrorCode.IllegalArgument,
                "所有整数变量都是固定值，无法构造 no-good / " +
                "All integer variables are fixed; no-good cannot be constructed");
        }
        constraints.Add(Inequality(
            terms: active.Select(variable => (Variable: (IVariableItem)variable, Coefficient: Flt64.One)),
            comparison: Comparison.GE,
            rhs: Flt64.One));
        return Results.Ok(new IntegerNoGoodEncoding(
            name: name,
            auxiliaryVariables: auxiliary,
            constraints: constraints));
    }

    private static LinearInequality<Flt64> Inequality(
        IEnumerable<(IVariableItem Variable, Flt64 Coefficient)> terms,
        Comparison comparison,
        Flt64 rhs) {
        var lhs = new LinearPolynomial<Flt64>(
            terms.Select(term => new LinearMonomial<Flt64>(
                term.Coefficient,
                term.Variable)).ToList(),
            Flt64.Zero);
        var right = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(),
            rhs);
        return new LinearInequality<Flt64>(lhs, right, comparison);
    }

    private static Result<Flt64, ErrorCode, Error<ErrorCode>> ExactFlt64(
        BigInteger value,
        string context) {
        Result<Flt64?, ErrorCode, Error<ErrorCode>> result = ExactNullableFlt64(value, context);
        if (result is Ok<Flt64?, ErrorCode, Error<ErrorCode>> ok) {
            return ok.Value is { } valueResult
                ? Results.Ok(valueResult)
                : Failure<Flt64>(
                    ErrorCode.ApplicationError,
                    $"{context} 精确转换返回空值 / Exact conversion returned null for {context}");
        }
        return Propagate<Flt64, Flt64?>(result);
    }

    private static Result<Flt64?, ErrorCode, Error<ErrorCode>> ExactNullableFlt64(
        BigInteger value,
        string context) {
        if (BigInteger.Abs(value) > MaxExactDoubleInteger) {
            return Failure<Flt64?>(
                ErrorCode.Other,
                $"{context} 超出 Flt64 精确整数范围 / " +
                $"{context} exceeds the exact Flt64 integer range");
        }
        return Results.Ok<Flt64?>(new Flt64((double)value));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                ErrorCode.ApplicationError,
                "整数 no-good 结果状态无效 / Invalid integer no-good result state"),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));
}

/// <summary>有界整数主问题的精确 no-good 割预言器 / Exact no-good oracle for bounded integer master assignments.</summary>
public sealed class IntegerNoGoodCutOracle : IBendersCutOracle {
    private readonly IReadOnlyList<IntegerBendersVariable> _variables;

    /// <summary>创建整数 no-good 预言器 / Create an integer no-good oracle.</summary>
    /// <param name="variables">用于构造编码的整数绑定 / Integer bindings used to build the encoding.</param>
    public IntegerNoGoodCutOracle(IEnumerable<IntegerBendersVariable> variables) {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = Array.AsReadOnly(variables.ToArray());
    }

    /// <inheritdoc/>
    public Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> FeasibilityCuts(
        InfeasibleSubproblemResult result,
        BendersCutContext context) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        if (_variables.Count == 0) {
            return Failure<List<BendersMasterCut>>(
                ErrorCode.IllegalArgument,
                "整数 no-good 绑定不能为空 / Integer no-good binding must not be empty");
        }

        var items = new List<IntegerNoGoodVariable>(_variables.Count);
        foreach (IntegerBendersVariable binding in _variables) {
            if (!result.Assignment.MasterValues.TryGetValue(binding.Key, out Flt64 raw)) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.DataNotFound,
                    $"整数 no-good 缺少主问题赋值：{binding.Key} / " +
                    $"Integer no-good is missing master assignment: {binding.Key}");
            }
            if (!BendersNumeric.TryExactInt64(raw, out Int64 value)) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.ORSolutionInvalid,
                    $"整数 no-good 收到非精确整数赋值：{binding.Key}={raw} / " +
                    $"Integer no-good received a non-exact integral assignment: {binding.Key}={raw}");
            }
            items.Add(new IntegerNoGoodVariable(
                Key: binding.Key,
                Variable: binding.MasterVariable,
                Value: value,
                Domain: binding.Domain));
        }

        Result<IntegerNoGoodEncoding, ErrorCode, Error<ErrorCode>> encodingResult =
            IntegerNoGoodCutEncoder.Encode(
                variables: items,
                name: $"integer-no-good-{context.Iteration}");
        if (encodingResult is not Ok<
                IntegerNoGoodEncoding,
                ErrorCode,
                Error<ErrorCode>> encodingOk) {
            return Propagate<List<BendersMasterCut>, IntegerNoGoodEncoding>(encodingResult);
        }
        IntegerNoGoodEncoding encoding = encodingOk.Value;
        if (encoding.Constraints.Count == 0) {
            return Failure<List<BendersMasterCut>>(
                ErrorCode.ApplicationError,
                "整数 no-good 编码未生成约束 / Integer no-good encoding produced no constraints");
        }
        return Results.Ok(new List<BendersMasterCut> {
            new(
                inequality: encoding.Constraints[0],
                kind: BendersCutKind.NoGood,
                validity: BendersCutValidity.Global,
                proofStatus: ProofStatus.Verified,
                source: "integer-no-good",
                additionalInequalities: encoding.Constraints.Skip(1),
                auxiliaryVariables: encoding.AuxiliaryVariables),
        });
    }

    /// <inheritdoc/>
    public Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> OptimalityCuts(
        FeasibleSubproblemResult result,
        BendersCutContext context) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        return Results.Ok(new List<BendersMasterCut>());
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                ErrorCode.ApplicationError,
                "整数 no-good 结果状态无效 / Invalid integer no-good result state"),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));
}

/// <summary>Benders 数值边界转换 / Benders numeric boundary conversion.</summary>
internal static class BendersNumeric {
    private const double MaxExactDoubleInteger = 9_007_199_254_740_991.0;

    /// <summary>尝试把浮点值转换为精确 Int64 / Try to convert a floating value to an exact Int64.</summary>
    /// <param name="value">待转换值 / Value to convert.</param>
    /// <param name="result">精确整数值 / Exact integer value.</param>
    /// <returns>是否为精确整数 / Whether the value is an exact integer.</returns>
    public static bool TryExactInt64(Flt64 value, out Int64 result) {
        double raw = value.ToDouble();
        if (!double.IsFinite(raw) ||
            System.Math.Abs(raw) > MaxExactDoubleInteger ||
            raw != System.Math.Round(raw)) {
            result = default;
            return false;
        }
        result = new Int64((long)raw);
        return true;
    }
}
