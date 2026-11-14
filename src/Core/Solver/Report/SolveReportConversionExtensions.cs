#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Output;
namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>统一报告数值转换 / Numeric conversion of unified reports.</summary>
public static class SolveReportConversionExtensions {
    /// <summary>转换报告中的解和诊断数值 / Convert solution and diagnostic values in a report.</summary>
    /// <remarks>统计值保持 Flt64。整数必须可精确读取；非整数转换不验证幅值误差或可行性。非数值元数据仍共享，转换期间不得并发修改。循环尝试报告会失败。 / Statistics remain Flt64. Integers must be exactly readable; non-integer conversion does not verify magnitude error or feasibility. Non-numeric metadata remains shared; do not mutate during conversion. Cyclic attempt reports fail.</remarks>
    /// <typeparam name="V">目标数值类型 / Target numeric type.</typeparam>
    /// <param name="report">来源报告 / Source report.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <returns>转换后的报告或错误 / Converted report or an error.</returns>
    public static Result<SolveReport<V>, ErrorCode, Error<ErrorCode>> ConvertTo<V>(
        this SolveReport<Flt64>? report,
        IFlt64ValueConverter<V>? converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (report is null || converter is null) {
            return Failure<V>("报告或转换器为空 / Report or converter is null");
        }
        try {
            var converted = new Dictionary<SolveReport<Flt64>, SolveReport<V>>(ReferenceEqualityComparer.Instance);
            var visiting = new HashSet<SolveReport<Flt64>>(ReferenceEqualityComparer.Instance);
            var attempts = new Dictionary<SolveReport<Flt64>, SolveAttemptTrace<Flt64>[]>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<(SolveReport<Flt64> Report, bool Ready)>();
            var conversion = new ValueConversion<V>(converter);
            pending.Push((report, false));
            while (pending.TryPop(out (SolveReport<Flt64> Report, bool Ready) entry)) {
                if (converted.ContainsKey(entry.Report)) {
                    continue;
                }
                if (entry.Ready) {
                    SolveReport<V> result = ConvertReport(
                        source: entry.Report,
                        attempts: attempts[entry.Report],
                        converted: converted,
                        conversion: conversion
                    );
                    if (!conversion.Valid) {
                        return Failure<V>("报告数值无法转换 / Report value cannot be converted");
                    }
                    converted.Add(entry.Report, result);
                    visiting.Remove(entry.Report);
                    continue;
                }
                if (!visiting.Add(entry.Report)) {
                    return Failure<V>("尝试报告包含循环 / Attempt reports contain a cycle");
                }
                SolveAttemptTrace<Flt64>[] snapshot = entry.Report.Attempts.ToArray();
                attempts.Add(entry.Report, snapshot);
                pending.Push((entry.Report, true));
                for (int i = snapshot.Length - 1; i >= 0; i--) {
                    if (snapshot[i].Report is { } child) {
                        pending.Push((child, false));
                    }
                }
            }
            return Results.Ok(converted[report]);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure<V>($"报告转换失败：{error.Message} / Report conversion failed: {error.Message}");
        }
    }

    private static SolveReport<V> ConvertReport<V>(
        SolveReport<Flt64> source,
        IReadOnlyList<SolveAttemptTrace<Flt64>> attempts,
        IReadOnlyDictionary<SolveReport<Flt64>, SolveReport<V>> converted,
        ValueConversion<V> conversion)
        where V : struct, IRealNumber<V>, INumberField<V> {
        SolveSolution<V>? solution = source.Solution is { } value
            ? new SolveSolution<V>(
                Values: ConvertSolution(value.Values, conversion),
                Objective: value.Objective is { } objective ? conversion.Convert(objective) : null,
                Pool: Array.AsReadOnly(value.Pool.Select(item => ConvertSolution(item, conversion)).ToArray())
            )
            : null;
        SolveDiagnostics<Flt64> diagnostics = source.Diagnostics;
        var convertedDiagnostics = new SolveDiagnostics<V>(
            ConstraintEvaluations: Array.AsReadOnly(diagnostics.ConstraintEvaluations.Select(item => new ConstraintEvaluation<V>(
                ConstraintId: item.ConstraintId,
                Lhs: conversion.Convert(item.Lhs),
                Rhs: conversion.Convert(item.Rhs),
                Relation: item.Relation,
                Slack: conversion.Convert(item.Slack),
                Violation: conversion.Convert(item.Violation),
                Tolerance: conversion.Convert(item.Tolerance),
                Satisfied: item.Satisfied,
                Dual: item.Dual is { } dual ? conversion.Convert(dual) : null
            )).ToArray()),
            VariableBoundEvaluations: Array.AsReadOnly(diagnostics.VariableBoundEvaluations.Select(item => new VariableBoundEvaluation<V>(
                VariableId: item.VariableId,
                Side: item.Side,
                Bound: conversion.Convert(item.Bound),
                Value: conversion.Convert(item.Value),
                Slack: conversion.Convert(item.Slack),
                Violation: conversion.Convert(item.Violation),
                Tolerance: conversion.Convert(item.Tolerance),
                Satisfied: item.Satisfied
            )).ToArray()),
            InfeasibilityEvidence: diagnostics.InfeasibilityEvidence,
            Warnings: diagnostics.Warnings,
            Errors: diagnostics.Errors
        );
        return new SolveReport<V> {
            SchemaVersion = source.SchemaVersion,
            RunId = source.RunId,
            ProblemStatus = source.ProblemStatus,
            TerminationReason = source.TerminationReason,
            SolutionPresence = source.SolutionPresence,
            Solution = solution,
            Proof = source.Proof,
            Statistics = source.Statistics,
            Diagnostics = convertedDiagnostics,
            Provenance = source.Provenance,
            Fingerprints = source.Fingerprints,
            Attempts = Array.AsReadOnly(attempts.Select(item => new SolveAttemptTrace<V>(
                AttemptId: item.AttemptId,
                BackendId: item.BackendId,
                ParentAttemptId: item.ParentAttemptId,
                Report: item.Report is { } nested ? converted[nested] : null,
                Elapsed: item.Elapsed,
                Errors: item.Errors,
                CancellationReason: item.CancellationReason
            )).ToArray()),
            SelectedAttemptId = source.SelectedAttemptId,
            SelectionReason = source.SelectionReason,
        };
    }

    private static Solution<V> ConvertSolution<V>(Solution<Flt64> solution, ValueConversion<V> conversion)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(Array.AsReadOnly(solution.Values.Select(conversion.Convert).ToArray()));

    private static Result<SolveReport<V>, ErrorCode, Error<ErrorCode>> Failure<V>(string message)
        where V : struct => Results.Failed<SolveReport<V>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private sealed class ValueConversion<V>(IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        private static readonly bool Integer = typeof(V).GetInterfaces()
            .Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IInteger<>));

        internal bool Valid { get; private set; } = true;

        internal V Convert(Flt64 source) {
            if (!Valid) {
                return default;
            }
            double original = source.ToDouble();
            if (double.IsNaN(original)) {
                Valid = false;
                return default;
            }
            V value = converter.IntoValue(source);
            double encoded = value.ToFlt64().ToDouble();
            double reverse = converter.FromValue(value).ToDouble();
            if (double.IsInfinity(original)) {
                Valid = !Integer && value.SupportsInfinity && encoded == original && reverse == original
                    && (original > 0 ? value.IsPositiveInfinity() : value.IsNegativeInfinity());
            }
            else {
                Valid = NumericValueValidation.IsValid(value) && double.IsFinite(encoded) && reverse == encoded
                    && (encoded != 0 || value.Eq(NumericConstantsRegistry.For<V>().Zero))
                    && (original == 0) == (encoded == 0)
                    && (original == 0 || System.Math.Sign(original) == System.Math.Sign(encoded))
                    && (!Integer || NumericValueValidation.IsExactIntegerConversion(value, source));
            }
            return value;
        }
    }
}
