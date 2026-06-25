#nullable enable

using Fuookami.Ospf.Math;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Fuookami.Ospf.Math.Symbol.Expression.Operation;
/// <summary>求值上下文 / Evaluation Context. Provides path-to-value mapping.</summary>
public interface IEvaluationContext {
    object? this[PropertyPath path] { get; }
    bool Contains(PropertyPath path);
}

/// <summary>基于 Map 的求值上下文 / Map-based evaluation context.</summary>
public sealed class MapEvaluationContext : IEvaluationContext {
    private readonly IReadOnlyDictionary<PropertyPath, object?> _values;

    private MapEvaluationContext(IReadOnlyDictionary<PropertyPath, object?> values) => _values = values;

    public static MapEvaluationContext FromPathMap(IReadOnlyDictionary<PropertyPath, object?> values) => new(values);

    public static MapEvaluationContext FromStringMap(IReadOnlyDictionary<string, object?> values) =>
        new(values.ToDictionary(kv => PropertyPath.Parse(kv.Key), kv => kv.Value));

    public object? this[PropertyPath path] => _values.TryGetValue(path, out object? v) ? v : null;
    public bool Contains(PropertyPath path) => _values.ContainsKey(path);
}

/// <summary>空求值上下文 / Empty evaluation context.</summary>
public sealed record EmptyEvaluationContext : IEvaluationContext {
    public static readonly EmptyEvaluationContext Instance = new();
    public object? this[PropertyPath path] => null;
    public bool Contains(PropertyPath path) => false;
}

/// <summary>布尔表达式求值 / Boolean Expression Evaluation.</summary>
public static class EvaluateBoolean {
    public static Trivalent Evaluate(BooleanExpression expr, IEvaluationContext context) {
        if (expr is BooleanConstant c) {
            return c.Value;
        }

        if (expr is Comparison<object> co) {
            return EvaluateComparison(co, context);
        }

        if (expr is InExpression<object> ie) {
            return EvaluateIn(ie, context);
        }

        if (expr is PatternMatch<object> pm) {
            return EvaluatePatternMatch(pm, context);
        }

        if (expr is NullCheck nc) {
            return EvaluateNullCheck(nc, context);
        }

        if (expr is AndExpression a) {
            return EvaluateAnd(a, context);
        }

        if (expr is OrExpression o) {
            return EvaluateOr(o, context);
        }

        if (expr is NotExpression n) {
            return EvaluateNot(n, context);
        }

        return new Trivalent.Unknown();
    }

    public static bool? EvaluateOrNull(BooleanExpression expr, IEvaluationContext context) {
        Trivalent result = Evaluate(expr, context);
        if (result is Trivalent.True) {
            return true;
        }

        if (result is Trivalent.False) {
            return false;
        }

        return null;
    }

    private static Trivalent EvaluateComparison(Comparison<object> expr, IEvaluationContext context) {
        object? leftValue = EvaluateScalar(expr.Left, context);
        if (leftValue is null) {
            return new Trivalent.Unknown();
        }

        object? rightValue = EvaluateScalar(expr.Right, context);
        if (rightValue is null) {
            return new Trivalent.Unknown();
        }

        bool? result = CompareValues(leftValue, rightValue, expr.Operator);
        return result is null ? new Trivalent.Unknown() : Trivalent.Invoke(result.Value);
    }

    private static Trivalent EvaluateIn(InExpression<object> expr, IEvaluationContext context) {
        object? value = EvaluateScalar(expr.Value, context);
        if (value is null) {
            return new Trivalent.Unknown();
        }

        bool isIn = false;
        foreach (ScalarExpression<object> candidateExpr in expr.Candidates) {
            object? candidate = EvaluateScalar(candidateExpr, context);
            if (candidate is null) {
                continue;
            }

            if (ValuesEqual(value, candidate)) { isIn = true; break; }
        }

        return Trivalent.Invoke(expr.Negated ? !isIn : isIn);
    }

    private static Trivalent EvaluatePatternMatch(PatternMatch<object> expr, IEvaluationContext context) {
        string? value = EvaluateScalar(expr.Value, context)?.ToString();
        if (value is null) {
            return new Trivalent.Unknown();
        }

        string? pattern = EvaluateScalar(expr.Pattern, context)?.ToString();
        if (pattern is null) {
            return new Trivalent.Unknown();
        }

        bool matches;
        switch (expr.Mode) {
            case PatternMatchMode.Exact: matches = value == pattern; break;
            case PatternMatchMode.Prefix: matches = value.StartsWith(pattern, StringComparison.Ordinal); break;
            case PatternMatchMode.Suffix: matches = value.EndsWith(pattern, StringComparison.Ordinal); break;
            case PatternMatchMode.Contains: matches = value.Contains(pattern, StringComparison.Ordinal); break;
            case PatternMatchMode.Like: matches = MatchLike(value, pattern); break;
            case PatternMatchMode.Regex:
                try { matches = Regex.IsMatch(value, pattern); }
                catch { matches = false; }
                break;
            default: matches = false; break;
        }

        return Trivalent.Invoke(expr.Negated ? !matches : matches);
    }

    private static Trivalent EvaluateNullCheck(NullCheck expr, IEvaluationContext context) {
        if (!context.Contains(expr.Path)) {
            return new Trivalent.Unknown();
        }

        object? value = context[expr.Path];
        bool isNull = value is null;
        return Trivalent.Invoke(expr.IsNull ? isNull : !isNull);
    }

    private static Trivalent EvaluateAnd(AndExpression expr, IEvaluationContext context) {
        bool hasUnknown = false;
        foreach (BooleanExpression operand in expr.Operands) {
            Trivalent result = Evaluate(operand, context);
            if (result is Trivalent.False) {
                return new Trivalent.False();
            }

            if (result is Trivalent.Unknown) {
                hasUnknown = true;
            }
        }
        return hasUnknown ? new Trivalent.Unknown() : new Trivalent.True();
    }

    private static Trivalent EvaluateOr(OrExpression expr, IEvaluationContext context) {
        bool hasUnknown = false;
        foreach (BooleanExpression operand in expr.Operands) {
            Trivalent result = Evaluate(operand, context);
            if (result is Trivalent.True) {
                return new Trivalent.True();
            }

            if (result is Trivalent.Unknown) {
                hasUnknown = true;
            }
        }
        return hasUnknown ? new Trivalent.Unknown() : new Trivalent.False();
    }

    private static Trivalent EvaluateNot(NotExpression expr, IEvaluationContext context) {
        Trivalent inner = Evaluate(expr.Operand, context);
        if (inner is Trivalent.True) {
            return new Trivalent.False();
        }

        if (inner is Trivalent.False) {
            return new Trivalent.True();
        }

        return new Trivalent.Unknown();
    }

    private static object? EvaluateScalar(ScalarExpression<object> expr, IEvaluationContext context) {
        if (expr is ScalarConstant<object> sc) {
            return sc.Value;
        }

        if (expr is ScalarReference<object> sr) {
            return context[sr.Path];
        }

        if (expr is ScalarSymbolReference<object>) {
            return null;
        }

        if (expr is ScalarUnary<object> su) {
            object? operand = EvaluateScalar(su.Operand, context);
            return operand is null ? null : NumericDispatcher.Instance.EvaluateUnary(su.Operator, operand);
        }
        if (expr is ScalarBinary<object> sb) {
            object? left = EvaluateScalar(sb.Left, context);
            object? right = EvaluateScalar(sb.Right, context);
            if (left is null || right is null) {
                return null;
            }

            return NumericDispatcher.Instance.EvaluateBinary(sb.Operator, left, right);
        }
        if (expr is ScalarFunction<object> sf) {
            return DefaultScalarFunctionEvaluator.Instance.Evaluate(
                sf.Name, sf.Arguments.Select(a => EvaluateScalar(a, context)).ToList());
        }

        return null;
    }

    private static bool? CompareValues(object left, object right, ComparisonOperator op) {
        if (left is null || right is null) {
            return null;
        }

        switch (op) {
            case ComparisonOperator.Eq: return ValuesEqual(left, right);
            case ComparisonOperator.Ne: return !ValuesEqual(left, right);
            case ComparisonOperator.Lt: { int? c = CompareOrder(left, right); return c.HasValue ? c.Value < 0 : null; }
            case ComparisonOperator.Le: { int? c = CompareOrder(left, right); return c.HasValue ? c.Value <= 0 : null; }
            case ComparisonOperator.Gt: { int? c = CompareOrder(left, right); return c.HasValue ? c.Value > 0 : null; }
            case ComparisonOperator.Ge: { int? c = CompareOrder(left, right); return c.HasValue ? c.Value >= 0 : null; }
            default: return null;
        }
    }

    private static int? CompareOrder(object left, object right) {
        if (IsNumericType(left) && IsNumericType(right)) {
            return CompareNumbers(left, right);
        }

        if (left is string ls && right is string rs) {
            return string.Compare(ls, rs, StringComparison.Ordinal);
        }

        if (left is IComparable && left.GetType() == right.GetType()) {
            try { return ((IComparable)left).CompareTo(right); }
            catch { return null; }
        }
        return null;
    }

    private static bool IsNumericType(object value) =>
        value is int || value is long || value is double || value is float ||
        value is decimal || value is short || value is byte;

    private static bool ValuesEqual(object left, object right) {
        if (left is null && right is null) {
            return true;
        }

        if (left is null || right is null) {
            return false;
        }

        if (IsNumericType(left) && IsNumericType(right)) {
            return CompareNumbers(left, right) == 0;
        }

        return left.Equals(right);
    }

    private static int? CompareNumbers(object left, object right) {
        try {
            decimal ld = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
            decimal rd = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
            return ld.CompareTo(rd);
        }
        catch { return null; }
    }

    private static bool MatchLike(string value, string pattern) {
        var regexPattern = new System.Text.StringBuilder("^");
        foreach (char c in pattern) {
            switch (c) {
                case '%': regexPattern.Append(".*"); break;
                case '_': regexPattern.Append("."); break;
                default:
                    if (char.IsLetterOrDigit(c)) {
                        regexPattern.Append(c);
                    }
                    else { regexPattern.Append('\\'); regexPattern.Append(c); }
                    break;
            }
        }
        regexPattern.Append('$');
        try { return Regex.IsMatch(value, regexPattern.ToString()); }
        catch { return false; }
    }
}

/// <summary>默认标量函数求值器 / Default scalar function evaluator.</summary>
public sealed class DefaultScalarFunctionEvaluator : IScalarFunctionEvaluator {
    public static readonly DefaultScalarFunctionEvaluator Instance = new();
    private DefaultScalarFunctionEvaluator() { }

    public object? Evaluate(string name, IReadOnlyList<object?> arguments) {
        switch (name.ToLowerInvariant()) {
            case ScalarFunctionNames.Abs: return EvaluateAbs(arguments);
            case ScalarFunctionNames.Lower: return EvaluateStringUnary(arguments, s => s.ToLowerInvariant());
            case ScalarFunctionNames.Upper: return EvaluateStringUnary(arguments, s => s.ToUpperInvariant());
            case ScalarFunctionNames.Trim: return EvaluateStringUnary(arguments, s => s.Trim());
            case ScalarFunctionNames.Length: return EvaluateStringUnary(arguments, s => s.Length);
            case ScalarFunctionNames.Coalesce: return arguments.FirstOrDefault(a => a is not null);
            default: return null;
        }
    }

    private static object? EvaluateAbs(IReadOnlyList<object?> arguments) {
        if (arguments.Count != 1) {
            return null;
        }

        object? value = arguments[0];
        if (value is null) {
            return null;
        }

        if (value is int i) {
            return global::System.Math.Abs(i);
        }

        if (value is long l) {
            return global::System.Math.Abs(l);
        }

        if (value is float f) {
            return global::System.Math.Abs(f);
        }

        if (value is double d) {
            return global::System.Math.Abs(d);
        }

        if (value is short s) {
            return (short)global::System.Math.Abs(s);
        }

        if (value is decimal dc) {
            return global::System.Math.Abs(dc);
        }

        return null;
    }

    private static object? EvaluateStringUnary(IReadOnlyList<object?> arguments, Func<string, object> operation) {
        if (arguments.Count != 1) {
            return null;
        }

        if (arguments[0] is not string s) {
            return null;
        }

        return operation(s);
    }
}

/// <summary>便捷扩展：用 Map 上下文求值 / Evaluate-with-Map extensions.</summary>
public static class EvaluateBooleanExtensions {
    public static Trivalent EvaluateWith(this BooleanExpression expr, IReadOnlyDictionary<string, object?> values)
        => EvaluateBoolean.Evaluate(expr, MapEvaluationContext.FromStringMap(values));

    public static bool? EvaluateWithOrNull(this BooleanExpression expr, IReadOnlyDictionary<string, object?> values)
        => EvaluateBoolean.EvaluateOrNull(expr, MapEvaluationContext.FromStringMap(values));
}
