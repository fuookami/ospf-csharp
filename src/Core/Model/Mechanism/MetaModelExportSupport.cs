#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 元模型导出支持扩展 / Meta-model export support extensions.
/// <para>提供便捷的元模型 OPM 诊断导出方法，支持原式和展开格式。</para>
/// <para>Provides convenience methods for exporting meta-models in the OPM
/// diagnostic format, both raw and unfolded.</para>
/// </summary>
public static class MetaModelExportExtensions {
    /// <summary>
    /// 导出元模型为 OPM 诊断格式。
    /// Export meta-model to the OPM diagnostic format.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="model">元模型 / Meta-model</param>
    /// <param name="path">导出路径（可空，默认使用模型名称）/ Export path (nullable, defaults to model name)</param>
    /// <returns>操作结果 / Operation result</returns>
    public static Task<Try> ExportToOpmAsync<V>(
        this IMetaModel<V> model,
        string? path = null)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model.ExportAsync(path ?? model.Name);

    /// <summary>
    /// 导出元模型为展开的 OPM 诊断格式。
    /// Export meta-model to the unfolded OPM diagnostic format.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <param name="model">元模型 / Meta-model</param>
    /// <param name="path">导出路径 / Export path</param>
    /// <returns>操作结果 / Operation result</returns>
    public static Task<Try> ExportUnfoldedAsync<V>(
        this IMetaModel<V> model,
        string path)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        model.ExportAsync(path, true);
}

/// <summary>
/// 机制模型 OPM 文本导出器 / Mechanism-model OPM text exporter.
/// </summary>
internal static class MetaModelOpmExporter {
    internal static Task<Try> ExportAsync<V>(
        IMetaModel<V> model,
        string? path,
        MathUInt64 unfold)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (model is null || string.IsNullOrWhiteSpace(path)) {
            return Task.FromResult(Failure(
                ErrorCode.IllegalArgument,
                "模型或导出路径为空 / Model or export path is null"));
        }
        if (model.IsClosed) {
            return Task.FromResult(Failure(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }

        try {
            string filePath = Directory.Exists(path)
                ? Path.Combine(path, $"{model.Name}.opm")
                : path;
            using var writer = new StreamWriter(
                path: filePath,
                append: false,
                encoding: new UTF8Encoding(false)) {
                NewLine = "\n",
            };
            Write(writer: writer, model: model, unfold: unfold);
            return Task.FromResult(Results.Ok(Results.SuccessInstance));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Task.FromResult(Failure(
                ErrorCode.ApplicationFailed,
                $"导出 OPM 模型失败：{error.Message} / Cannot export OPM model: {error.Message}"));
        }
    }

    private static void Write<V>(
        TextWriter writer,
        IMetaModel<V> model,
        MathUInt64 unfold)
        where V : struct, IRealNumber<V>, INumberField<V> {
        writer.WriteLine($"Model Name: {model.Name}");
        writer.WriteLine();

        writer.WriteLine("Variables:");
        foreach (Token<V> token in model.Tokens.TokensInSolver) {
            string range = token.Range?.ToString() ?? "empty";
            writer.WriteLine($"{token.Name}, {token.Type}, {range}");
        }
        writer.WriteLine();

        writer.WriteLine("Symbols:");
        foreach (IIntermediateSymbol symbol in model.Tokens.Symbols
                     .ToArray()
                     .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)) {
            string range = symbol is IIntermediateSymbol<V> typed
                ? typed.Range.ToString()
                : "empty";
            writer.WriteLine(
                $"{SymbolText(symbol, unfold)} = {symbol.ToRawString(MathUInt64.One)}, {range}");
        }
        writer.WriteLine();

        writer.WriteLine("Objectives:");
        WriteObjectives(writer: writer, model: model, unfold: unfold);
        writer.WriteLine();

        writer.WriteLine("Subject to:");
        foreach (MathConstraint constraint in model.Constraints) {
            writer.WriteLine(ConstraintText(constraint, model.Converter, unfold));
        }
        writer.WriteLine();
    }

    private static void WriteObjectives<V>(
        TextWriter writer,
        IMetaModel<V> model,
        MathUInt64 unfold)
        where V : struct, IRealNumber<V>, INumberField<V> {
        switch (model) {
            case LinearMetaModel<V> linear:
                foreach (MetaSubObject<V> objective in linear.MetaSubObjects) {
                    writer.WriteLine(
                        $"{objective.Category} {objective.Name}: " +
                        $"{FormatLinear(objective.Polynomial, model.Converter, unfold)}");
                }
                return;

            case QuadraticMetaModel<V> quadratic:
                foreach (MetaSubObject<V> objective in quadratic.MetaSubObjects) {
                    writer.WriteLine(
                        $"{objective.Category} {objective.Name}: " +
                        $"{FormatLinear(objective.Polynomial, model.Converter, unfold)}");
                }
                foreach (QuadraticFlattenSubObject<V> objective in quadratic.FlattenSubObjects) {
                    var polynomial = new QuadraticPolynomial<V>(
                        Monomials: objective.FlattenData.Monomials,
                        Constant: objective.FlattenData.Constant);
                    writer.WriteLine(
                        $"{objective.Category} {objective.Name}: " +
                        $"{FormatQuadratic(polynomial, model.Converter, unfold)}");
                }
                return;

            default:
                foreach (SubObject<V> objective in model.SubObjects) {
                    string polynomial = objective switch {
                        LinearSubObject<V> linear => FormatLinear(
                            polynomial: new LinearPolynomial<V>(
                                Monomials: linear.LinearTerms(),
                                Constant: linear.Constant),
                            converter: model.Converter,
                            unfold: unfold),
                        QuadraticSubObject<V> quadratic => FormatQuadratic(
                            polynomial: new QuadraticPolynomial<V>(
                                Monomials: quadratic.QuadraticTerms(),
                                Constant: quadratic.Constant),
                            converter: model.Converter,
                            unfold: unfold),
                        _ => objective.ToString() ?? "",
                    };
                    writer.WriteLine($"{objective.Category} {objective.Name}: {polynomial}");
                }
                return;
        }
    }

    private static string ConstraintText<V>(
        MathConstraint constraint,
        IFlt64ValueConverter<V> converter,
        MathUInt64 unfold)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        constraint switch {
            LinearInequalityConstraint<V> linear =>
                $"{ConstraintPrefix(linear.Name, linear.DisplayName)}" +
                $"{FormatLinear(linear.Inequality.Lhs, converter, unfold)} " +
                $"{linear.Inequality.Comparison.Symbol()} " +
                $"{FormatLinear(linear.Inequality.Rhs, converter, unfold)}",
            QuadraticInequalityConstraint<V> quadratic =>
                $"{ConstraintPrefix(quadratic.Name, quadratic.DisplayName)}" +
                $"{FormatQuadratic(quadratic.Inequality.Lhs, converter, unfold)} " +
                $"{quadratic.Inequality.Comparison.Symbol()} " +
                $"{FormatQuadratic(quadratic.Inequality.Rhs, converter, unfold)}",
            _ => constraint.ToString() ?? "",
        };

    private static string FormatLinear<V>(
        LinearPolynomial<V> polynomial,
        IFlt64ValueConverter<V> converter,
        MathUInt64 unfold)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IEnumerable<string> terms = polynomial.Monomials
            .Select(term => (
                Coefficient: converter.FromValue(term.Coefficient).ToDouble(),
                Text: SymbolText(term.Symbol, unfold)))
            .Where(term => term.Coefficient != 0)
            .Select(term => FormatTerm(term.Coefficient, term.Text));
        return JoinTerms(
            terms: terms,
            constant: converter.FromValue(polynomial.Constant).ToDouble());
    }

    private static string FormatQuadratic<V>(
        QuadraticPolynomial<V> polynomial,
        IFlt64ValueConverter<V> converter,
        MathUInt64 unfold)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IEnumerable<string> terms = polynomial.Monomials
            .Select(term => (
                Coefficient: converter.FromValue(term.Coefficient).ToDouble(),
                Text: QuadraticTermText(term, unfold)))
            .Where(term => term.Coefficient != 0)
            .Select(term => FormatTerm(term.Coefficient, term.Text));
        return JoinTerms(
            terms: terms,
            constant: converter.FromValue(polynomial.Constant).ToDouble());
    }

    private static string QuadraticTermText<V>(
        QuadraticMonomial<V> term,
        MathUInt64 unfold)
        where V : struct, IRing<V> {
        string first = SymbolText(term.Symbol1, unfold);
        if (term.Symbol2 is null) {
            return first;
        }
        string second = SymbolText(term.Symbol2, unfold);
        return term.Symbol1.Equals(term.Symbol2)
            ? $"{first}^2"
            : $"{first} * {second}";
    }

    private static string FormatTerm(double coefficient, string text) {
        if (coefficient == 1) {
            return text;
        }
        if (coefficient == -1) {
            return $"-{text}";
        }
        return $"{FormatNumber(coefficient)} * {text}";
    }

    private static string JoinTerms(IEnumerable<string> terms, double constant) {
        var allTerms = terms.ToList();
        if (constant != 0) {
            allTerms.Add(FormatNumber(constant));
        }
        if (allTerms.Count == 0) {
            return "0";
        }

        var builder = new StringBuilder();
        for (int index = 0; index < allTerms.Count; index++) {
            string term = allTerms[index];
            if (index == 0) {
                builder.Append(term);
            }
            else if (term.StartsWith('-')) {
                builder.Append(" - ").Append(term[1..]);
            }
            else {
                builder.Append(" + ").Append(term);
            }
        }
        return builder.ToString();
    }

    private static string SymbolText(ISymbol symbol, MathUInt64 unfold) =>
        unfold != MathUInt64.Zero && symbol is IIntermediateSymbol intermediate
            ? intermediate.ToRawString(unfold - MathUInt64.One)
            : symbol.DisplayName ?? symbol.Name;

    private static string ConstraintPrefix(string name, string? displayName) {
        string? text = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : !string.IsNullOrWhiteSpace(name)
                ? name
                : null;
        return text is null ? "" : $"{text}: ";
    }

    private static string FormatNumber(double value) =>
        double.IsNaN(value)
            ? "nan"
            : double.IsPositiveInfinity(value)
                ? "inf"
                : double.IsNegativeInfinity(value)
                    ? "-inf"
                    : value.ToString("R", CultureInfo.InvariantCulture);

    private static Try Failure(ErrorCode code, string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));
}
