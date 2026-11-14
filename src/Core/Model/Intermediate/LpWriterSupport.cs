#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>线性与二次 LP writer 的共享格式辅助 / Shared formatting support for linear and quadratic LP writers.</summary>
internal static class LpWriterSupport {
    /// <summary>校验模型生命周期、变量索引和约束元数据 / Validate model lifecycle, variable indexes and constraint metadata.</summary>
    /// <typeparam name="TCell">约束单元格类型 / Constraint cell type.</typeparam>
    /// <param name="writer">输出 writer / Output writer.</param>
    /// <param name="disposed">模型是否已关闭 / Whether the model is closed.</param>
    /// <param name="variables">模型变量 / Model variables.</param>
    /// <param name="constraints">模型约束 / Model constraints.</param>
    /// <returns>成功或校验错误 / Success or a validation error.</returns>
    public static Try Validate<TCell>(
        StreamWriter? writer,
        bool disposed,
        IReadOnlyList<ModelViewVariable>? variables,
        ModelConstraint<TCell>? constraints)
        where TCell : IConstraintCell<TCell>, ICopyable<TCell> {
        if (writer is null) {
            return Failure(ErrorCode.IllegalArgument, "LP writer 为空 / LP writer is null");
        }
        if (disposed) {
            return Failure(ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed");
        }
        if (variables is null || constraints is null) {
            return Failure(ErrorCode.IllegalArgument, "模型变量或约束为空 / Model variables or constraints are null");
        }
        Try shape = constraints.ValidateShape();
        if (!shape.IsOk) {
            return shape;
        }
        for (int index = 0; index < variables.Count; index++) {
            ModelViewVariable? variable = variables[index];
            if (variable is null || variable.Index != index || variable.Name is null) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    $"LP 变量索引或名称为空：{index} / LP variable index or name is invalid: {index}");
            }
        }
        return Results.OkInstance;
    }

    /// <summary>校验系数和列索引 / Validate a coefficient and its column indexes.</summary>
    /// <param name="coefficient">系数 / Coefficient.</param>
    /// <param name="variableCount">变量数量 / Variable count.</param>
    /// <param name="columns">列索引 / Column indexes.</param>
    /// <returns>成功或参数错误 / Success or an argument error.</returns>
    public static Try ValidateCell(Flt64 coefficient, int variableCount, params int[] columns) {
        if (!double.IsFinite(coefficient.ToDouble())) {
            return Failure(ErrorCode.IllegalArgument, "LP 系数必须有限 / LP coefficients must be finite");
        }
        foreach (int column in columns) {
            if (column < 0 || column >= variableCount) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    $"LP 列索引越界：{column} / LP column index is out of range: {column}");
            }
        }
        return Results.OkInstance;
    }

    /// <summary>按 Kotlin 基线的符号与单位系数规则追加一项 / Append a term using the pinned Kotlin sign and unit-coefficient rules.</summary>
    /// <param name="builder">目标缓冲区 / Destination buffer.</param>
    /// <param name="hasTerm">是否已经写入一项 / Whether a term was already written.</param>
    /// <param name="coefficient">系数 / Coefficient.</param>
    /// <param name="expression">变量表达式 / Variable expression.</param>
    public static void AppendTerm(
        StringBuilder builder,
        ref bool hasTerm,
        Flt64 coefficient,
        string expression) {
        if (coefficient == Flt64.Zero) {
            return;
        }
        if (hasTerm) {
            builder.Append(coefficient < Flt64.Zero ? " - " : " + ");
            coefficient = coefficient.Abs();
        }
        if (coefficient != Flt64.One) {
            builder.Append(Format(coefficient)).Append(' ');
        }
        builder.Append(expression);
        hasTerm = true;
    }

    /// <summary>追加兼容标准 LP 二次语法的表达式 / Append an expression using standard quadratic LP syntax.</summary>
    /// <typeparam name="TCell">二次单元格类型 / Quadratic cell type.</typeparam>
    /// <param name="builder">目标缓冲区 / Destination buffer.</param>
    /// <param name="variables">模型变量 / Model variables.</param>
    /// <param name="cells">表达式单元格 / Expression cells.</param>
    /// <param name="firstColumn">首列选择器 / First-column selector.</param>
    /// <param name="secondColumn">次列选择器 / Second-column selector.</param>
    /// <param name="coefficient">系数选择器 / Coefficient selector.</param>
    public static void AppendQuadraticExpression<TCell>(
        StringBuilder builder,
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyList<TCell> cells,
        Func<TCell, int> firstColumn,
        Func<TCell, int> secondColumn,
        Func<TCell, Flt64> coefficient) {
        bool hasTerm = false;
        foreach (TCell cell in cells) {
            if (secondColumn(cell) >= 0) {
                continue;
            }
            AppendTerm(
                builder: builder,
                hasTerm: ref hasTerm,
                coefficient: coefficient(cell),
                expression: variables[firstColumn(cell)].Name
            );
        }

        bool hasQuadraticTerm = false;
        foreach (TCell cell in cells) {
            int col1 = firstColumn(cell);
            int col2 = secondColumn(cell);
            if (col2 < 0 || coefficient(cell) == Flt64.Zero) {
                continue;
            }
            if (!hasQuadraticTerm) {
                if (hasTerm) {
                    builder.Append(" + ");
                }
                builder.Append("[ ");
            }
            string expression = col1 == col2
                ? $"{variables[col1].Name} ^ 2"
                : $"{variables[col1].Name} * {variables[col2].Name}";
            AppendTerm(
                builder: builder,
                hasTerm: ref hasQuadraticTerm,
                coefficient: coefficient(cell),
                expression: expression
            );
        }
        if (hasQuadraticTerm) {
            builder.Append(" ]");
            hasTerm = true;
        }
        if (!hasTerm) {
            builder.Append('0');
        }
    }

    /// <summary>追加 Bounds 块 / Append the Bounds block.</summary>
    /// <param name="builder">目标缓冲区 / Destination buffer.</param>
    /// <param name="variables">模型变量 / Model variables.</param>
    /// <param name="literalInfinity">是否按字面无穷判断（二次基线）/ Whether to test literal infinity (quadratic baseline).</param>
    public static void AppendBounds(
        StringBuilder builder,
        IReadOnlyList<ModelViewVariable> variables,
        bool literalInfinity) {
        builder.Append("Bounds\n");
        foreach (ModelViewVariable variable in variables) {
            bool negativeInfinity = literalInfinity
                ? double.IsNegativeInfinity(variable.LowerBound.ToDouble())
                : variable.NegativeFree;
            bool positiveInfinity = literalInfinity
                ? double.IsPositiveInfinity(variable.UpperBound.ToDouble())
                : variable.PositiveFree;
            if (!literalInfinity && variable.Free) {
                builder.Append(' ').Append(variable.Name).Append(" free\n");
            }
            else if (!literalInfinity && variable.PositiveNormalized) {
                builder.Append(' ').Append(variable.Name).Append(" >= 0\n");
            }
            else if (!literalInfinity && variable.NegativeNormalized) {
                builder.Append(' ').Append(variable.Name).Append(" <= 0\n");
            }
            else if (negativeInfinity && positiveInfinity) {
                builder.Append(' ').Append(variable.Name).Append(" free\n");
            }
            else if (negativeInfinity) {
                builder.Append(' ')
                    .Append(variable.Name)
                    .Append(" <= ")
                    .Append(Format(variable.UpperBound))
                    .Append('\n');
            }
            else if (positiveInfinity) {
                builder.Append(' ')
                    .Append(variable.Name)
                    .Append(" >= ")
                    .Append(Format(variable.LowerBound))
                    .Append('\n');
            }
            else if (variable.LowerBound == variable.UpperBound) {
                builder.Append(' ')
                    .Append(variable.Name)
                    .Append(" = ")
                    .Append(Format(variable.LowerBound))
                    .Append('\n');
            }
            else {
                builder.Append(' ')
                    .Append(Format(variable.LowerBound))
                    .Append(" <= ")
                    .Append(variable.Name)
                    .Append(" <= ")
                    .Append(Format(variable.UpperBound))
                    .Append('\n');
            }
        }
        builder.Append('\n');
    }

    /// <summary>追加 Binaries 和 Generals 块 / Append the Binaries and Generals blocks.</summary>
    /// <param name="builder">目标缓冲区 / Destination buffer.</param>
    /// <param name="variables">模型变量 / Model variables.</param>
    public static void AppendIntegrality(StringBuilder builder, IReadOnlyList<ModelViewVariable> variables) {
        if (Contains(variables, variable => variable.Type.IsBinaryType)) {
            builder.Append("Binaries\n");
            foreach (ModelViewVariable variable in variables) {
                if (variable.Type.IsBinaryType) {
                    builder.Append(' ').Append(variable.Name);
                }
            }
            builder.Append('\n');
        }
        if (Contains(variables, variable => variable.Type.IsNotBinaryIntegerType)) {
            builder.Append("Generals\n");
            foreach (ModelViewVariable variable in variables) {
                if (variable.Type.IsNotBinaryIntegerType) {
                    builder.Append(' ').Append(variable.Name);
                }
            }
            builder.Append('\n');
        }
    }

    /// <summary>将完整 LP 文本写入 writer，并把外部 I/O 失败转换为 Result / Write the complete LP text and convert external I/O failures to Result.</summary>
    /// <param name="writer">输出 writer / Output writer.</param>
    /// <param name="build">LP 文本构建器 / LP text builder.</param>
    /// <returns>成功或 I/O 失败 / Success or an I/O failure.</returns>
    public static Try Write(StreamWriter writer, Action<StringBuilder> build) {
        try {
            var builder = new StringBuilder();
            build(builder);
            writer.Write(builder.ToString());
            return Results.OkInstance;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Failure(
                ErrorCode.ApplicationFailed,
                $"写入 LP 失败：{error.Message} / Cannot write LP: {error.Message}");
        }
    }

    /// <summary>以不变文化格式化 LP 数值 / Format an LP number with the invariant culture.</summary>
    /// <param name="value">数值 / Number.</param>
    /// <returns>稳定文本 / Stable text.</returns>
    public static string Format(Flt64 value) =>
        value.ToDouble().ToString("R", CultureInfo.InvariantCulture);

    private static bool Contains(
        IReadOnlyList<ModelViewVariable> variables,
        Func<ModelViewVariable, bool> predicate) {
        foreach (ModelViewVariable variable in variables) {
            if (predicate(variable)) {
                return true;
            }
        }
        return false;
    }

    private static Try Failure(ErrorCode code, string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));
}
