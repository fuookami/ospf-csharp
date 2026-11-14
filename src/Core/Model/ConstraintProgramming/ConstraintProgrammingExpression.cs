#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>
/// CP 整数线性表达式 AST / Integer-linear CP expression AST.
/// </summary>
/// <remarks>
/// 表达式求值只使用精确 Int64 和 BigInteger，溢出通过 Result 返回。
/// Evaluation uses only exact Int64 and BigInteger; overflow is returned through Result.
/// </remarks>
public abstract record ConstraintProgrammingExpression {
    private static readonly FrozenSet<VariableId> NoVariables =
        Array.Empty<VariableId>().ToFrozenSet();

    /// <summary>表达式中引用的稳定变量 ID / Stable variable IDs referenced by the expression.</summary>
    public abstract IReadOnlySet<VariableId> Variables { get; }

    /// <summary>按稳定变量 ID 求值 / Evaluate with assignments keyed by stable variable ID.</summary>
    /// <param name="values">变量赋值 / Variable assignments.</param>
    /// <returns>精确整数值或结构化错误 / Exact integer value or a structured error.</returns>
    public abstract Result<Int64, ErrorCode, Error<ErrorCode>> Evaluate(IReadOnlyDictionary<VariableId, Int64> values);

    /// <summary>整数常量 / Integer constant.</summary>
    public sealed record Constant : ConstraintProgrammingExpression {
        /// <summary>创建整数常量 / Create an integer constant.</summary>
        /// <param name="value">常量值 / Constant value.</param>
        public Constant(Int64 value) {
            Value = value;
        }

        /// <summary>常量值 / Constant value.</summary>
        public Int64 Value { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => NoVariables;

        /// <inheritdoc/>
        public override Result<Int64, ErrorCode, Error<ErrorCode>> Evaluate(IReadOnlyDictionary<VariableId, Int64> values) =>
            Results.Ok(Value);
    }

    /// <summary>表达式构造失败时的延迟错误节点 / Deferred error node for invalid expression construction.</summary>
    public sealed record Invalid : ConstraintProgrammingExpression {
        /// <summary>创建延迟错误节点 / Create a deferred error node.</summary>
        /// <param name="message">结构化错误消息 / Structured error message.</param>
        public Invalid(string message) {
            Message = message;
        }

        /// <summary>结构化错误消息 / Structured error message.</summary>
        public string Message { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables => NoVariables;

        /// <inheritdoc/>
        public override Result<Int64, ErrorCode, Error<ErrorCode>> Evaluate(IReadOnlyDictionary<VariableId, Int64> values) =>
            Failure<Int64>(ErrorCode.IllegalArgument, Message);
    }

    /// <summary>
    /// 整数变量引用；CP 值域是建模边界，不会改写源变量范围。
    /// Integer-variable reference; the CP domain is a modeling boundary and never rewrites the source variable range.
    /// </summary>
    public sealed record Variable : ConstraintProgrammingExpression {
        /// <summary>创建变量引用 / Create a variable reference.</summary>
        /// <param name="variable">源整数变量 / Source integer variable.</param>
        /// <param name="domain">CP 值域 / CP domain.</param>
        /// <param name="id">绑定后的稳定 ID；为空时使用 model-local ID / Bound stable ID, or a model-local ID.</param>
        public Variable(
            IVariableItem variable,
            IntegerDomain domain,
            VariableId? id = null) {
            ArgumentNullException.ThrowIfNull(variable);
            ArgumentNullException.ThrowIfNull(domain);
            VariableItem = variable;
            Domain = domain;
            Id = id;
        }

        /// <summary>源整数变量 / Source integer variable.</summary>
        public IVariableItem VariableItem { get; }

        /// <summary>CP 值域 / CP domain.</summary>
        public IntegerDomain Domain { get; }

        /// <summary>绑定后的稳定 ID / Bound stable ID.</summary>
        public VariableId? Id { get; }

        /// <summary>实际使用的稳定变量 ID / Effective stable variable ID.</summary>
        public VariableId VariableId => Id ?? VariableIdOf(VariableItem);

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            new HashSet<VariableId> { VariableId }.ToFrozenSet();

        /// <inheritdoc/>
        public override Result<Int64, ErrorCode, Error<ErrorCode>> Evaluate(IReadOnlyDictionary<VariableId, Int64> values) {
            if (!values.TryGetValue(VariableId, out Int64 value)) {
                return Failure<Int64>(
                    ErrorCode.DataNotFound,
                    $"缺少 CP 变量赋值：{VariableId.Value} / Missing CP variable assignment: {VariableId.Value}");
            }
            return Domain.Contains(value)
                ? Results.Ok(value)
                : Failure<Int64>(
                    ErrorCode.IllegalArgument,
                    $"CP 变量赋值超出值域：{value} not in domain / " +
                    $"CP variable assignment is outside its domain: {value}");
        }
    }

    /// <summary>线性项 / Linear term.</summary>
    public sealed record Term {
        /// <summary>创建线性项 / Create a linear term.</summary>
        /// <param name="variable">源整数变量 / Source integer variable.</param>
        /// <param name="coefficient">整数系数 / Integer coefficient.</param>
        /// <param name="id">绑定后的稳定 ID；为空时使用 model-local ID / Bound stable ID, or a model-local ID.</param>
        public Term(
            IVariableItem variable,
            Int64 coefficient,
            VariableId? id = null) {
            ArgumentNullException.ThrowIfNull(variable);
            Variable = variable;
            Coefficient = coefficient;
            Id = id;
        }

        /// <summary>源整数变量 / Source integer variable.</summary>
        public IVariableItem Variable { get; }

        /// <summary>整数系数 / Integer coefficient.</summary>
        public Int64 Coefficient { get; }

        /// <summary>绑定后的稳定 ID / Bound stable ID.</summary>
        public VariableId? Id { get; }

        /// <summary>实际使用的稳定变量 ID / Effective stable variable ID.</summary>
        public VariableId VariableId => Id ?? VariableIdOf(Variable);
    }

    /// <summary>整数线性表达式 / Integer-linear expression.</summary>
    public sealed record Linear : ConstraintProgrammingExpression {
        /// <summary>创建线性表达式；输入集合会被冻结 / Create a linear expression and freeze its input.</summary>
        /// <param name="terms">线性项 / Linear terms.</param>
        /// <param name="constant">常数项 / Constant term.</param>
        public Linear(
            IEnumerable<Term> terms,
            Int64 constant = default) {
            ArgumentNullException.ThrowIfNull(terms);
            Terms = new ReadOnlyCollection<Term>(terms.ToList());
            ConstantTerm = constant;
        }

        /// <summary>线性项 / Linear terms.</summary>
        public IReadOnlyList<Term> Terms { get; }

        /// <summary>常数项 / Constant term.</summary>
        public Int64 ConstantTerm { get; }

        /// <inheritdoc/>
        public override IReadOnlySet<VariableId> Variables =>
            Terms.Select(term => term.VariableId).ToFrozenSet();

        /// <inheritdoc/>
        public override Result<Int64, ErrorCode, Error<ErrorCode>> Evaluate(IReadOnlyDictionary<VariableId, Int64> values) {
            BigInteger total = new(ConstantTerm.ToLong());
            foreach (Term term in Terms) {
                if (!values.TryGetValue(term.VariableId, out Int64 value)) {
                    return Failure<Int64>(
                        ErrorCode.DataNotFound,
                        $"缺少 CP 变量赋值：{term.VariableId.Value} / " +
                        $"Missing CP variable assignment: {term.VariableId.Value}");
                }
                total += new BigInteger(term.Coefficient.ToLong()) * new BigInteger(value.ToLong());
            }
            return ExactInt64(total);
        }
    }

    /// <summary>创建整数常量 / Create an integer constant.</summary>
    /// <param name="value">常量值 / Constant value.</param>
    /// <returns>整数常量表达式 / Integer constant expression.</returns>
    public static Constant CreateConstant(Int64 value) => new(value);

    /// <summary>创建 Long 整数常量 / Create a Long integer constant.</summary>
    /// <param name="value">常量值 / Constant value.</param>
    /// <returns>整数常量表达式 / Integer constant expression.</returns>
    public static Constant CreateConstant(long value) => new(new Int64(value));

    /// <summary>创建 Int 整数常量 / Create an Int integer constant.</summary>
    /// <param name="value">常量值 / Constant value.</param>
    /// <returns>整数常量表达式 / Integer constant expression.</returns>
    public static Constant CreateConstant(int value) => new(new Int64(value));

    /// <summary>创建并校验整数变量引用 / Create and validate an integer-variable reference.</summary>
    /// <param name="variable">源整数变量 / Source integer variable.</param>
    /// <param name="domain">可选 CP 值域 / Optional CP domain.</param>
    /// <returns>变量表达式或结构化错误 / Variable expression or a structured error.</returns>
    public static Result<Variable, ErrorCode, Error<ErrorCode>> CreateVariable(
        IVariableItem variable,
        IntegerDomain? domain = null) {
        ArgumentNullException.ThrowIfNull(variable);
        if (!variable.TypeKind.IsIntegerType) {
            return Failure<Variable>(
                ErrorCode.IllegalArgument,
                $"CP 表达式只支持整数变量：{variable.Name} / " +
                $"CP expressions only support integer variables: {variable.Name}");
        }

        IntegerDomain resolvedDomain = domain ?? DefaultDomain(variable);
        string? domainError = ValidateDomain(variable, resolvedDomain);
        return domainError is null
            ? Results.Ok(new Variable(variable, resolvedDomain))
            : Failure<Variable>(ErrorCode.IllegalArgument, domainError);
    }

    /// <summary>创建单个线性项 / Create a single linear term.</summary>
    /// <param name="variable">源整数变量 / Source integer variable.</param>
    /// <param name="coefficient">整数系数 / Integer coefficient.</param>
    /// <returns>线性项或结构化错误 / Linear term or a structured error.</returns>
    public static Result<Term, ErrorCode, Error<ErrorCode>> CreateTerm(
        IVariableItem variable,
        Int64 coefficient) {
        ArgumentNullException.ThrowIfNull(variable);
        return variable.TypeKind.IsIntegerType
            ? Results.Ok(new Term(variable, coefficient))
            : Failure<Term>(
                ErrorCode.IllegalArgument,
                $"CP 线性项只支持整数变量：{variable.Name} / " +
                $"CP linear terms only support integer variables: {variable.Name}");
    }

    /// <summary>创建线性表达式，按稳定 ID 合并重复项并删除零系数 / Create a linear expression with stable-ID merging and zero removal.</summary>
    /// <param name="terms">线性项集合 / Linear terms.</param>
    /// <param name="constant">常数项 / Constant term.</param>
    /// <returns>规范线性表达式或结构化错误 / Normalized linear expression or a structured error.</returns>
    public static Result<Linear, ErrorCode, Error<ErrorCode>> CreateLinear(
        IEnumerable<Term> terms,
        Int64 constant = default) {
        ArgumentNullException.ThrowIfNull(terms);
        var merged = new Dictionary<VariableId, (Term Term, BigInteger Coefficient)>();
        foreach (Term term in terms) {
            if (!term.Variable.TypeKind.IsIntegerType) {
                return Failure<Linear>(
                    ErrorCode.IllegalArgument,
                    $"CP 线性表达式包含非整数变量：{term.Variable.Name} / " +
                    $"CP linear expression contains a non-integer variable: {term.Variable.Name}");
            }
            BigInteger coefficient = new(term.Coefficient.ToLong());
            if (merged.TryGetValue(term.VariableId, out (Term Term, BigInteger Coefficient) existing)) {
                merged[term.VariableId] = (existing.Term, existing.Coefficient + coefficient);
            }
            else {
                merged[term.VariableId] = (term, coefficient);
            }
        }

        var normalized = new List<Term>(merged.Count);
        foreach ((Term term, BigInteger coefficient) in merged.Values) {
            Result<Int64, ErrorCode, Error<ErrorCode>> coefficientResult = ExactInt64(coefficient);
            if (!coefficientResult.IsOk) {
                return Failure<Linear>(ErrorCode.IllegalArgument, ErrorMessage(coefficientResult));
            }
            if (coefficientResult.Value != Int64.Zero) {
                normalized.Add(new Term(term.Variable, coefficientResult.Value, term.Id));
            }
        }
        return Results.Ok(new Linear(normalized, constant));
    }

    /// <summary>将多个表达式相加 / Add multiple expressions.</summary>
    /// <param name="expressions">待相加表达式 / Expressions to add.</param>
    /// <returns>线性表达式或结构化错误 / Linear expression or a structured error.</returns>
    public static Result<Linear, ErrorCode, Error<ErrorCode>> Sum(IEnumerable<ConstraintProgrammingExpression> expressions) {
        ArgumentNullException.ThrowIfNull(expressions);
        var terms = new List<Term>();
        BigInteger constant = BigInteger.Zero;
        foreach (ConstraintProgrammingExpression expression in expressions) {
            switch (expression) {
                case Constant value:
                    constant += new BigInteger(value.Value.ToLong());
                    break;
                case Invalid invalid:
                    return Failure<Linear>(ErrorCode.IllegalArgument, invalid.Message);
                case Variable variable:
                    terms.Add(new Term(variable.VariableItem, Int64.One, variable.Id));
                    break;
                case Linear linear:
                    terms.AddRange(linear.Terms);
                    constant += new BigInteger(linear.ConstantTerm.ToLong());
                    break;
            }
        }

        Result<Int64, ErrorCode, Error<ErrorCode>> constantResult = ExactInt64(constant);
        return constantResult.IsOk
            ? CreateLinear(terms, constantResult.Value)
            : Failure<Linear>(ErrorCode.IllegalArgument, ErrorMessage(constantResult));
    }

    /// <summary>将两个表达式相加 / Add two expressions.</summary>
    /// <param name="lhs">左侧表达式 / Left-hand expression.</param>
    /// <param name="rhs">右侧表达式 / Right-hand expression.</param>
    /// <returns>线性表达式或结构化错误 / Linear expression or a structured error.</returns>
    public static Result<Linear, ErrorCode, Error<ErrorCode>> Add(
        ConstraintProgrammingExpression lhs,
        ConstraintProgrammingExpression rhs) =>
        Sum(new[] { lhs, rhs });

    /// <summary>将两个表达式相减 / Subtract two expressions.</summary>
    /// <param name="lhs">左侧表达式 / Left-hand expression.</param>
    /// <param name="rhs">右侧表达式 / Right-hand expression.</param>
    /// <returns>线性表达式或结构化错误 / Linear expression or a structured error.</returns>
    public static Result<Linear, ErrorCode, Error<ErrorCode>> Subtract(
        ConstraintProgrammingExpression lhs,
        ConstraintProgrammingExpression rhs) =>
        Sum(new[] { lhs, Negate(rhs) });

    /// <summary>取表达式相反数；Int64 最小值溢出返回延迟错误节点 / Negate an expression, returning a deferred error for Int64 minimum.</summary>
    /// <param name="expression">待取反表达式 / Expression to negate.</param>
    /// <returns>相反数表达式 / Negated expression.</returns>
    public static ConstraintProgrammingExpression Negate(
        ConstraintProgrammingExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        const string message =
            "CP 整数表达式取负溢出 Int64 / Negating CP integer expression overflows Int64";
        return expression switch {
            Constant constant => NegateExact(constant.Value) is { } value
                ? new Constant(value)
                : new Invalid(message),
            Invalid invalid => invalid,
            Variable variable => new Linear(
                new[] { new Term(variable.VariableItem, new Int64(-1), variable.Id) },
                Int64.Zero),
            Linear linear => NegateLinear(linear, message),
            _ => new Invalid("未知 CP 表达式节点 / Unknown CP expression node"),
        };
    }

    private static ConstraintProgrammingExpression NegateLinear(Linear linear, string message) {
        if (NegateExact(linear.ConstantTerm) is not { } constant) {
            return new Invalid(message);
        }
        var terms = new List<Term>(linear.Terms.Count);
        foreach (Term term in linear.Terms) {
            if (NegateExact(term.Coefficient) is not { } coefficient) {
                return new Invalid(message);
            }
            terms.Add(new Term(term.Variable, coefficient, term.Id));
        }
        return new Linear(terms, constant);
    }

    private static VariableId VariableIdOf(IVariableItem variable) =>
        new($"{variable.Identifier}:{variable.Index}");

    private static IntegerDomain DefaultDomain(IVariableItem variable) {
        if (variable.TypeKind.IsBinaryType) {
            return IntegerDomain.Boolean;
        }
        return variable.TypeKind.IsUnsignedType
            ? new IntegerDomain.Interval(Int64.Zero, new Int64(long.MaxValue))
            : new IntegerDomain.Interval(new Int64(long.MinValue), new Int64(long.MaxValue));
    }

    private static string? ValidateDomain(IVariableItem variable, IntegerDomain domain) {
        if (domain.LowerBound > domain.UpperBound || domain.Cardinality == 0) {
            return "CP 变量值域不能为空 / CP variable domain must not be empty";
        }
        if (variable.TypeKind.IsBinaryType &&
            (domain.LowerBound < Int64.Zero || domain.UpperBound > Int64.One)) {
            return "二值变量值域必须位于 [0, 1] / Binary variable domain must be within [0, 1]";
        }
        if (variable.TypeKind.IsUnsignedType && domain.LowerBound < Int64.Zero) {
            return "无符号变量值域下界不得为负 / Unsigned variable domain cannot have a negative lower bound";
        }
        return null;
    }

    private static Result<Int64, ErrorCode, Error<ErrorCode>> ExactInt64(BigInteger value) {
        return value < long.MinValue || value > long.MaxValue
            ? Failure<Int64>(
                ErrorCode.IllegalArgument,
                "CP 整数表达式溢出 Int64 / CP integer expression overflows Int64")
            : Results.Ok(new Int64((long)value));
    }

    private static Int64? NegateExact(Int64 value) =>
        value.ToLong() == long.MinValue ? null : new Int64(-value.ToLong());

    private static string ErrorMessage(Result<Int64, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<Int64, ErrorCode, Error<ErrorCode>> failed
            ? failed.Error.Message
            : "CP 整数表达式求值失败 / CP integer expression evaluation failed";

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(ErrorCode code, string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}
