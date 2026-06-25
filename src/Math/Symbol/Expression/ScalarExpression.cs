#nullable enable

using Fuookami.Ospf.Math.Symbol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Expression;
/// <summary>
/// 标量表达式 / Scalar Expression.
/// Kotlin sealed interface ScalarExpression&lt;out T&gt;; C# abstract record (out variance dropped).
/// </summary>
public abstract record ScalarExpression<T> {
    /// <summary>表达式类型名 / Expression type name.</summary>
    public abstract string TypeName { get; }

    /// <summary>子表达式列表 / Child expressions list.</summary>
    public abstract IReadOnlyList<ScalarExpression<T>> Children { get; }

    /// <summary>判断表达式是否是常量 / Check if expression is constant.</summary>
    public virtual bool IsConstant() => this switch {
        ScalarConstant<T> => true,
        ScalarReference<T> => false,
        ScalarSymbolReference<T> => false,
        ScalarUnary<T> u => u.Operand.IsConstant(),
        ScalarBinary<T> b => b.Left.IsConstant() && b.Right.IsConstant(),
        ScalarFunction<T> f => f.Arguments.All(a => a.IsConstant()),
        ScalarCustom<T> => false,
        _ => false,
    };

    /// <summary>判断表达式是否包含引用 / Check if expression contains references.</summary>
    public virtual bool ContainsReference() => this switch {
        ScalarConstant<T> => false,
        ScalarReference<T> => true,
        ScalarSymbolReference<T> => true,
        ScalarUnary<T> u => u.Operand.ContainsReference(),
        ScalarBinary<T> b => b.Left.ContainsReference() || b.Right.ContainsReference(),
        ScalarFunction<T> f => f.Arguments.Any(a => a.ContainsReference()),
        ScalarCustom<T> => true,
        _ => true,
    };

    /// <summary>获取所有引用路径 / Get all reference paths.</summary>
    public IReadOnlySet<PropertyPath> CollectReferences() {
        var refs = new HashSet<PropertyPath>();
        CollectReferencesInto(refs);
        return refs;
    }

    /// <summary>将引用路径收集到指定集合 / Collect reference paths into specified collection.</summary>
    public virtual void CollectReferencesInto(HashSet<PropertyPath> refs) {
        switch (this) {
            case ScalarReference<T> r: refs.Add(r.Path); break;
            case ScalarUnary<T> u: u.Operand.CollectReferencesInto(refs); break;
            case ScalarBinary<T> b:
                b.Left.CollectReferencesInto(refs);
                b.Right.CollectReferencesInto(refs);
                break;
            case ScalarFunction<T> f:
                foreach (ScalarExpression<T> a in f.Arguments) {
                    a.CollectReferencesInto(refs);
                }

                break;
        }
    }
}

/// <summary>标量常量 / Scalar Constant. 常量值 / Represents a constant value.</summary>
public sealed record ScalarConstant<T>(T Value) : ScalarExpression<T> {
    /// <inheritdoc/>
    public override string TypeName => "Constant";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children { get; } = Array.Empty<ScalarExpression<T>>();
    /// <inheritdoc/>
    public override string ToString() => $"Constant({Value})";
}

/// <summary>标量引用 / Scalar Reference. 对属性路径的引用 / Reference to a property path.</summary>
public sealed record ScalarReference<T>(PropertyPath Path, PathSymbol? Symbol = null) : ScalarExpression<T> {
    /// <summary>路径符号 / Path symbol.</summary>
    public PathSymbol Symbol { get; } = Symbol ?? Path.ToPathSymbol();
    /// <inheritdoc/>
    public override string TypeName => "Reference";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children { get; } = Array.Empty<ScalarExpression<T>>();
    /// <inheritdoc/>
    public override void CollectReferencesInto(HashSet<PropertyPath> refs) => refs.Add(Path);
    /// <inheritdoc/>
    public override string ToString() => $"Reference({Path.Value})";
}

/// <summary>标量符号引用 / Scalar Symbol Reference. 对符号的引用（非路径形式）/ Reference to a symbol (non-path).</summary>
public sealed record ScalarSymbolReference<T>(ISymbol Symbol) : ScalarExpression<T> {
    /// <inheritdoc/>
    public override string TypeName => "SymbolReference";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children { get; } = Array.Empty<ScalarExpression<T>>();
    /// <inheritdoc/>
    public override string ToString() => $"SymbolReference({Symbol.Name})";
}

/// <summary>标量一元操作 / Scalar Unary Operation. 如负号 / e.g. negation.</summary>
public sealed record ScalarUnary<T>(UnaryOperator Operator, ScalarExpression<T> Operand) : ScalarExpression<T> {
    /// <inheritdoc/>
    public override string TypeName => "Unary";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children { get; } = new[] { Operand };
    /// <inheritdoc/>
    public override string ToString() => $"{OperatorSymbols.Unary(Operator)}({Operand})";
}

/// <summary>标量二元操作 / Scalar Binary Operation. 如加法、减法 / e.g. addition, subtraction.</summary>
public sealed record ScalarBinary<T>(
    BinaryOperator Operator,
    ScalarExpression<T> Left,
    ScalarExpression<T> Right) : ScalarExpression<T> {
    /// <inheritdoc/>
    public override string TypeName => "Binary";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children { get; } = new[] { Left, Right };
    /// <inheritdoc/>
    public override string ToString() => $"({Left} {OperatorSymbols.Binary(Operator)} {Right})";
}

/// <summary>标量函数调用 / Scalar Function Call.</summary>
public sealed record ScalarFunction<T>(string Name, IReadOnlyList<ScalarExpression<T>> Arguments) : ScalarExpression<T> {
    /// <inheritdoc/>
    public override string TypeName => "Function";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children => Arguments;
    /// <inheritdoc/>
    public override string ToString() => $"{Name}({string.Join(", ", Arguments)})";
}

/// <summary>标量自定义表达式 / Scalar Custom Expression. 用于扩展 / For extension.</summary>
public sealed record ScalarCustom<T>(object Value, string? Description = null) : ScalarExpression<T> {
    /// <inheritdoc/>
    public override string TypeName => "Custom";
    /// <inheritdoc/>
    public override IReadOnlyList<ScalarExpression<T>> Children { get; } = Array.Empty<ScalarExpression<T>>();
    /// <inheritdoc/>
    public override string ToString() => Description ?? $"Custom({Value})";
}

/// <summary>
/// 标量表达式工厂 / Scalar Expression Factory.
/// Kotlin object; C# static class. 便捷构造方法 / Convenient constructors.
/// </summary>
public static class ScalarExpressionFactory {
    /// <summary>创建常量表达式 / Create constant expression.</summary>
    public static ScalarExpression<T> Constant<T>(T value) => new ScalarConstant<T>(value);
    /// <summary>创建引用表达式（路径）/ Create reference expression (path).</summary>
    public static ScalarExpression<T> Reference<T>(PropertyPath path) => new ScalarReference<T>(path);
    /// <summary>创建引用表达式（字符串）/ Create reference expression (string).</summary>
    public static ScalarExpression<T> Reference<T>(string path) => Reference<T>(PropertyPath.Parse(path));
    /// <summary>创建符号引用表达式 / Create symbol reference expression.</summary>
    public static ScalarExpression<T> Reference<T>(ISymbol symbol) => new ScalarSymbolReference<T>(symbol);
    /// <summary>创建一元操作表达式 / Create unary operation expression.</summary>
    public static ScalarExpression<T> Unary<T>(UnaryOperator op, ScalarExpression<T> operand) => new ScalarUnary<T>(op, operand);
    /// <summary>创建二元操作表达式 / Create binary operation expression.</summary>
    public static ScalarExpression<T> Binary<T>(BinaryOperator op, ScalarExpression<T> left, ScalarExpression<T> right) => new ScalarBinary<T>(op, left, right);
    /// <summary>创建函数调用表达式 / Create function call expression.</summary>
    public static ScalarExpression<T> Function<T>(string name, IReadOnlyList<ScalarExpression<T>> arguments) => new ScalarFunction<T>(name, arguments);
    /// <summary>创建加法表达式 / Create addition expression.</summary>
    public static ScalarExpression<T> Add<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Binary(BinaryOperator.Add, l, r);
    /// <summary>创建减法表达式 / Create subtraction expression.</summary>
    public static ScalarExpression<T> Subtract<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Binary(BinaryOperator.Subtract, l, r);
    /// <summary>创建乘法表达式 / Create multiplication expression.</summary>
    public static ScalarExpression<T> Multiply<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Binary(BinaryOperator.Multiply, l, r);
    /// <summary>创建除法表达式 / Create division expression.</summary>
    public static ScalarExpression<T> Divide<T>(ScalarExpression<T> l, ScalarExpression<T> r) => Binary(BinaryOperator.Divide, l, r);
}

/// <summary>标准标量函数名称 / Standard scalar function names.</summary>
public static class ScalarFunctionNames {
    public const string Abs = "abs";
    public const string Lower = "lower";
    public const string Upper = "upper";
    public const string Trim = "trim";
    public const string Length = "length";
    public const string Coalesce = "coalesce";
}

/// <summary>
/// 标量函数注册表 / Scalar function registry.
/// Translates scalar function calls to a target representation R.
/// </summary>
/// <typeparam name="R">中间表示类型 / Intermediate representation type.</typeparam>
public interface IScalarFunctionRegistry<R> {
    /// <summary>翻译标量函数调用 / Translate scalar function call; null if unsupported.</summary>
    R? Translate(string name, IReadOnlyList<R> arguments);
}

/// <summary>标量函数求值器 / Scalar function evaluator.</summary>
public interface IScalarFunctionEvaluator {
    /// <summary>求值标量函数 / Evaluate scalar function.</summary>
    object? Evaluate(string name, IReadOnlyList<object?> arguments);
}
