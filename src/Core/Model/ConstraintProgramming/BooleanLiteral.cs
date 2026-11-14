#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>
/// 二值变量正文字面量、否定字面量或布尔常量 / Positive binary literal, negated literal, or Boolean constant.
/// </summary>
public abstract record BooleanLiteral {
    /// <summary>关联二值变量；常量时为空 / Associated binary variable, or null for a constant.</summary>
    public abstract BinVar? Variable { get; }

    /// <summary>是否取反 / Whether the literal is negated.</summary>
    public abstract bool Negated { get; }

    /// <summary>常量值；变量文字时为空 / Constant value, or null for a variable literal.</summary>
    public abstract bool? Constant { get; }

    /// <summary>是否为常量 / Whether this is a constant.</summary>
    public bool IsConstant => Constant is not null;

    /// <summary>是否为正文字 / Whether this is a positive literal.</summary>
    public bool Positive => !Negated;

    /// <summary>稳定变量标识；常量时为空 / Stable variable ID, or null for a constant.</summary>
    public virtual VariableId? VariableId => Variable is null
        ? null
        : new VariableId($"{Variable.Identifier}:{Variable.Index}");

    /// <summary>返回逻辑否定 / Return the logical negation.</summary>
    /// <returns>否定后的字面量 / Negated literal.</returns>
    public abstract BooleanLiteral Negate();

    /// <summary>变量文字 / Variable literal.</summary>
    public sealed record VariableLiteral : BooleanLiteral {
        /// <summary>创建变量文字 / Create a variable literal.</summary>
        /// <param name="variable">关联二值变量 / Associated binary variable.</param>
        /// <param name="negated">是否取反 / Whether the literal is negated.</param>
        /// <param name="id">绑定后的稳定 ID；为空时使用 model-local ID / Stable ID after binding, or a model-local ID.</param>
        public VariableLiteral(
            BinVar variable,
            bool negated = false,
            VariableId? id = null) {
            Variable = variable;
            Negated = negated;
            Id = id;
        }

        /// <inheritdoc/>
        public override BinVar Variable { get; }

        /// <inheritdoc/>
        public override bool Negated { get; }

        /// <summary>绑定后的稳定 ID / Stable ID after binding.</summary>
        public VariableId? Id { get; }

        /// <inheritdoc/>
        public override bool? Constant => null;

        /// <inheritdoc/>
        public override VariableId? VariableId =>
            Id ?? new VariableId($"{Variable.Identifier}:{Variable.Index}");

        /// <inheritdoc/>
        public override BooleanLiteral Negate() => new VariableLiteral(Variable, !Negated, Id);
    }

    /// <summary>布尔常量文字 / Boolean constant literal.</summary>
    public sealed record ConstantLiteral : BooleanLiteral {
        /// <summary>创建布尔常量文字 / Create a Boolean constant literal.</summary>
        /// <param name="constant">常量值 / Constant value.</param>
        public ConstantLiteral(bool constant) {
            Constant = constant;
        }

        /// <inheritdoc/>
        public override BinVar? Variable => null;

        /// <inheritdoc/>
        public override bool Negated => !Constant!.Value;

        /// <inheritdoc/>
        public override bool? Constant { get; }

        /// <inheritdoc/>
        public override BooleanLiteral Negate() => new ConstantLiteral(!Constant!.Value);
    }

    /// <summary>真常量 / True constant.</summary>
    public static ConstantLiteral True { get; } = new(true);

    /// <summary>假常量 / False constant.</summary>
    public static ConstantLiteral False { get; } = new(false);

    /// <summary>使用布尔赋值求值 / Evaluate against Boolean assignments.</summary>
    /// <param name="values">按稳定变量 ID 编索引的赋值 / Assignments indexed by stable variable ID.</param>
    /// <returns>求值结果或缺少赋值错误 / Value or missing-assignment error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Evaluate(
        IReadOnlyDictionary<VariableId, bool> values) {
        if (Constant is { } constant) {
            return Results.Ok(constant);
        }
        if (VariableId is not { } id) {
            return Failure(ErrorCode.IllegalArgument, "变量文字缺少变量标识 / Variable literal has no variable identifier");
        }
        if (!values.TryGetValue(id, out bool value)) {
            return Failure(
                ErrorCode.DataNotFound,
                $"缺少布尔变量赋值：{id.Value} / Missing Boolean variable assignment: {id.Value}");
        }
        return Results.Ok(Negated ? !value : value);
    }

    /// <summary>使用 0/1 整数赋值求值 / Evaluate against 0/1 integer assignments.</summary>
    /// <param name="values">按稳定变量 ID 编索引的赋值 / Assignments indexed by stable variable ID.</param>
    /// <returns>求值结果或结构化错误 / Value or a structured error.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> EvaluateInteger(
        IReadOnlyDictionary<VariableId, Int64> values) {
        if (Constant is { } constant) {
            return Results.Ok(constant);
        }
        if (VariableId is not { } id) {
            return Failure(ErrorCode.IllegalArgument, "变量文字缺少变量标识 / Variable literal has no variable identifier");
        }
        if (!values.TryGetValue(id, out Int64 raw)) {
            return Failure(
                ErrorCode.DataNotFound,
                $"缺少布尔变量赋值：{id.Value} / Missing Boolean variable assignment: {id.Value}");
        }
        if (raw != Int64.Zero && raw != Int64.One) {
            return Failure(
                ErrorCode.IllegalArgument,
                $"布尔变量赋值必须为 0 或 1：{raw} / Boolean variable assignment must be 0 or 1: {raw}");
        }
        bool result = raw == Int64.One;
        return Results.Ok(Negated ? !result : result);
    }

    private static Result<bool, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<bool>(new Err<ErrorCode>(code, message));
}
