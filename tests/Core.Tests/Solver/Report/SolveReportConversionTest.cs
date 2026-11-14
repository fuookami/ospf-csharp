#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Solver.Report;

public class SolveReportConversionTest {
    [Fact]
    public void ConvertTo_ShouldPreserveStatusMetadataDiagnosticsAndNestedAttempts() {
        var proof = new SolveProof(ProofStatus.Claimed, "optimality", "proof-ref");
        var statistics = new SolveStatistics(SolveTime: TimeSpan.FromSeconds(2), Iterations: 3, Nodes: 4, BestBound: new Flt64(1.25));
        var fingerprints = new SolveFingerprints(Model: new AuditFingerprint("1", "SHA-256", "abc"));
        var issue = new SolveIssue("warning", SolveIssueCategory.Numerical, "warning");
        var evidence = new InfeasibilityEvidence { Source = InfeasibilityEvidenceSource.None };
        var provenance = new SolverProvenance(new SolverDescriptor(
            SolverId: "fake",
            BackendName: "fake-backend",
            Capabilities: new SolverCapabilities(new HashSet<SolverModelType> { SolverModelType.Mip })
        ));
        SolveReport<Flt64> nested = EmptyReport(TerminationReason.Cancelled);
        var report = new SolveReport<Flt64> {
            SchemaVersion = "1.0",
            RunId = new SolveRunId("run"),
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.TimeLimit,
            SolutionPresence = SolutionPresence.Incumbent,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(new[] { new Flt64(0.5), new Flt64(-2) }),
                Objective: new Flt64(3.5),
                Pool: new[] { new Solution<Flt64>(new[] { new Flt64(1.5), new Flt64(-1) }) }
            ),
            Proof = proof,
            Statistics = statistics,
            Diagnostics = new SolveDiagnostics<Flt64>(
                ConstraintEvaluations: new[] {
                    new ConstraintEvaluation<Flt64>(
                        ConstraintId: new ConstraintId("constraint"),
                        Lhs: new Flt64(1.5),
                        Rhs: new Flt64(2),
                        Relation: SolveConstraintRelation.LessEqual,
                        Slack: new Flt64(0.5),
                        Violation: Flt64.Zero,
                        Tolerance: new Flt64(0.25),
                        Satisfied: true,
                        Dual: new Flt64(-0.5)
                    ),
                },
                VariableBoundEvaluations: new[] {
                    new VariableBoundEvaluation<Flt64>(
                        VariableId: new VariableId("variable"),
                        Side: BoundSide.Lower,
                        Bound: new Flt64(-2),
                        Value: new Flt64(-1),
                        Slack: Flt64.One,
                        Violation: Flt64.Zero,
                        Tolerance: new Flt64(0.25),
                        Satisfied: true
                    ),
                },
                InfeasibilityEvidence: evidence,
                Warnings: new[] { issue },
                Errors: new[] { issue }
            ),
            Provenance = provenance,
            Fingerprints = fingerprints,
            Attempts = new[] { new SolveAttemptTrace<Flt64>(
                AttemptId: new SolveAttemptId("attempt"),
                BackendId: "fake",
                ParentAttemptId: new SolveAttemptId("parent"),
                Report: nested,
                Elapsed: TimeSpan.FromSeconds(1),
                Errors: new[] { issue },
                CancellationReason: "cancelled"
            ) },
            SelectedAttemptId = new SolveAttemptId("attempt"),
            SelectionReason = SolveSelectionReason.FirstFeasible,
        };

        Result<SolveReport<FltX>, ErrorCode, Error<ErrorCode>> converted = report.ConvertTo(new DecimalConverter());

        Assert.True(converted.IsOk, converted.ToString());
        SolveReport<FltX> actual = converted.Value;
        Assert.Equal(report.RunId, actual.RunId);
        Assert.Equal(report.ProblemStatus, actual.ProblemStatus);
        Assert.Equal(report.TerminationReason, actual.TerminationReason);
        Assert.Equal(report.SolutionPresence, actual.SolutionPresence);
        Assert.Same(proof, actual.Proof);
        Assert.Same(statistics, actual.Statistics);
        Assert.Same(fingerprints, actual.Fingerprints);
        Assert.Same(provenance, actual.Provenance);
        Assert.Equal(new[] { new FltX(0.5m), new FltX(-2m) }, actual.Solution!.Values.Values);
        Assert.Equal(new FltX(3.5m), actual.Solution.Objective);
        Assert.Equal(new[] { new FltX(1.5m), new FltX(-1m) }, actual.Solution.Pool[0].Values);
        ConstraintEvaluation<FltX> constraint = Assert.Single(actual.Diagnostics.ConstraintEvaluations);
        Assert.Equal(new ConstraintId("constraint"), constraint.ConstraintId);
        Assert.Equal(new FltX(1.5m), constraint.Lhs);
        Assert.Equal(new FltX(2m), constraint.Rhs);
        Assert.Equal(new FltX(0.5m), constraint.Slack);
        Assert.Equal(FltX.Zero, constraint.Violation);
        Assert.Equal(new FltX(0.25m), constraint.Tolerance);
        Assert.Equal(new FltX(-0.5m), constraint.Dual);
        Assert.True(constraint.Satisfied);
        VariableBoundEvaluation<FltX> bound = Assert.Single(actual.Diagnostics.VariableBoundEvaluations);
        Assert.Equal(new VariableId("variable"), bound.VariableId);
        Assert.Equal(BoundSide.Lower, bound.Side);
        Assert.Equal(new FltX(-2m), bound.Bound);
        Assert.Equal(new FltX(-1m), bound.Value);
        Assert.Equal(FltX.One, bound.Slack);
        Assert.Equal(FltX.Zero, bound.Violation);
        Assert.Equal(new FltX(0.25m), bound.Tolerance);
        Assert.Same(evidence, actual.Diagnostics.InfeasibilityEvidence);
        Assert.Same(issue, Assert.Single(actual.Diagnostics.Warnings));
        Assert.Same(issue, Assert.Single(actual.Diagnostics.Errors));
        SolveAttemptTrace<FltX> attempt = Assert.Single(actual.Attempts);
        Assert.Equal(TerminationReason.Cancelled, attempt.Report!.TerminationReason);
        Assert.Equal(new SolveAttemptId("parent"), attempt.ParentAttemptId);
        Assert.Equal(TimeSpan.FromSeconds(1), attempt.Elapsed);
        Assert.Same(issue, Assert.Single(attempt.Errors));
        Assert.Equal("cancelled", attempt.CancellationReason);
        Assert.Equal(report.SelectedAttemptId, actual.SelectedAttemptId);
        Assert.Equal(report.SelectionReason, actual.SelectionReason);
    }

    [Fact]
    public void ConvertTo_ShouldPreserveNoIncumbentAndInfinity() {
        SolveReport<Flt64> report = EmptyReport(TerminationReason.Interrupted) with {
            Diagnostics = new SolveDiagnostics<Flt64>(VariableBoundEvaluations: new[] {
                new VariableBoundEvaluation<Flt64>(
                    VariableId: new VariableId("x"),
                    Side: BoundSide.Upper,
                    Bound: new Flt64(double.PositiveInfinity),
                    Value: Flt64.Zero,
                    Slack: new Flt64(double.PositiveInfinity),
                    Violation: Flt64.Zero,
                    Tolerance: Flt64.Zero,
                    Satisfied: true
                ),
            }),
        };

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> converted = report.ConvertTo(IIntoValue<Flt64>.Identity);

        Assert.True(converted.IsOk, converted.ToString());
        Assert.Null(converted.Value.Solution);
        Assert.Equal(SolutionPresence.None, converted.Value.SolutionPresence);
        Assert.Equal(double.PositiveInfinity, converted.Value.Diagnostics.VariableBoundEvaluations[0].Bound.ToDouble());
        Assert.True(report.ConvertTo(new DecimalConverter()).IsFailed);
        Assert.True(report.ConvertTo(new RationalConverter()).IsFailed);
    }

    [Fact]
    public void ConvertTo_ShouldRejectFractionalIntegerAndNonfiniteValues() {
        SolveReport<Flt64> fractional = WithValue(new Flt64(1.5));
        Assert.True(fractional.ConvertTo(new IntegerConverter()).IsFailed);
        Assert.True(WithValue(new Flt64(double.NaN)).ConvertTo(IIntoValue<Flt64>.Identity).IsFailed);
        Assert.True(WithValue(new Flt64(1e-50)).ConvertTo(new FloatConverter()).IsFailed);
        Assert.True(WithValue(new Flt64(0.1)).ConvertTo(new FloatConverter()).IsOk);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ConvertTo_ShouldRejectFiniteIntegerThatOverflowsToInfinity(double value) {
        Assert.True(WithValue(new Flt64(value)).ConvertTo(new MisleadingIntegerConverter()).IsFailed);
        Assert.True(WithValue(new Flt64(value)).ConvertTo(new FloatConverter()).IsOk);
    }

    [Theory]
    [InlineData(9007199254740992d)]
    [InlineData(-9007199254740992d)]
    public void ConvertTo_ShouldRejectIntegerThatOnlyRoundsBackToSource(double value) {
        var converter = new MisleadingIntegerConverter();
        Assert.True(WithValue(new Flt64(value)).ConvertTo(converter).IsFailed);
        Assert.True(WithValue(new Flt64(2)).ConvertTo(converter).IsOk);
    }

    [Fact]
    public void ConvertTo_ShouldReturnFailureWhenConverterThrows() {
        SolveReport<Flt64> report = WithValue(new Flt64(2));
        Assert.True(report.ConvertTo(new ThrowingConverter()).IsFailed);
        Assert.Equal(new Flt64(2), report.Solution!.Values[0]);
    }

    [Fact]
    public void ConvertTo_ShouldRejectCyclicAttemptReports() {
        var attempts = new List<SolveAttemptTrace<Flt64>>();
        SolveReport<Flt64> report = EmptyReport(TerminationReason.Completed) with { Attempts = attempts };
        attempts.Add(new SolveAttemptTrace<Flt64>(new SolveAttemptId("self"), "fake", Report: report));
        Assert.True(report.ConvertTo(IIntoValue<Flt64>.Identity).IsFailed);
    }

    [Fact]
    public void ConvertTo_ShouldHandleSharedAndDeepAttemptReportsWithoutRecursion() {
        SolveReport<Flt64> child = EmptyReport(TerminationReason.Completed);
        SolveReport<Flt64> shared = EmptyReport(TerminationReason.Completed) with {
            Attempts = new[] {
                new SolveAttemptTrace<Flt64>(new SolveAttemptId("first"), "fake", Report: child),
                new SolveAttemptTrace<Flt64>(new SolveAttemptId("second"), "fake", Report: child),
            },
        };
        SolveReport<Flt64> convertedShared = shared.ConvertTo(IIntoValue<Flt64>.Identity).Value;
        Assert.Same(convertedShared.Attempts[0].Report, convertedShared.Attempts[1].Report);

        SolveReport<Flt64> deep = child;
        for (int i = 0; i < 10000; i++) {
            deep = EmptyReport(TerminationReason.Completed) with {
                Attempts = new[] { new SolveAttemptTrace<Flt64>(new SolveAttemptId("next"), "fake", Report: deep) },
            };
        }
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = deep.ConvertTo(IIntoValue<Flt64>.Identity);
        Assert.True(result.IsOk);
        SolveReport<Flt64> cursor = result.Value;
        for (int i = 0; i < 10000; i++) {
            cursor = Assert.Single(cursor.Attempts).Report!;
        }
        Assert.Empty(cursor.Attempts);
    }

    [Fact]
    public void ConvertTo_ShouldRejectInvalidInputsAndPreserveIdentityNegativeZero() {
        SolveReport<Flt64>? missing = null;
        Assert.True(missing.ConvertTo(IIntoValue<Flt64>.Identity).IsFailed);
        Assert.True(WithValue(Flt64.Zero).ConvertTo<Flt64>(null).IsFailed);
        Flt64 negativeZero = new(BitConverter.Int64BitsToDouble(long.MinValue));
        SolveReport<Flt64> converted = WithValue(negativeZero).ConvertTo(IIntoValue<Flt64>.Identity).Value;
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(converted.Solution!.Values[0].ToDouble()));
    }

    [Fact]
    public void ConvertTo_ShouldSnapshotConvertedCollections() {
        Flt64[] values = { new Flt64(2) };
        var pool = new List<Solution<Flt64>> { new(values) };
        var attempts = new List<SolveAttemptTrace<Flt64>> {
            new(new SolveAttemptId("attempt"), "fake"),
        };
        SolveReport<Flt64> report = WithValue(Flt64.One) with {
            Solution = new SolveSolution<Flt64>(new Solution<Flt64>(values), Pool: pool),
            Attempts = attempts,
        };
        SolveReport<FltX> converted = report.ConvertTo(new DecimalConverter()).Value;

        values[0] = new Flt64(7);
        pool.Clear();
        attempts.Clear();
        Assert.Equal(new FltX(2m), converted.Solution!.Values[0]);
        Assert.Equal(new FltX(2m), Assert.Single(converted.Solution.Pool)[0]);
        Assert.Single(converted.Attempts);
        Assert.True(((ICollection<FltX>)converted.Solution.Values.Values).IsReadOnly);
        Assert.True(((ICollection<Solution<FltX>>)converted.Solution.Pool).IsReadOnly);
        Assert.True(((ICollection<SolveAttemptTrace<FltX>>)converted.Attempts).IsReadOnly);
    }

    private static SolveReport<Flt64> EmptyReport(TerminationReason reason) => new() {
        ProblemStatus = ProblemStatus.Unknown,
        TerminationReason = reason,
        SolutionPresence = SolutionPresence.None,
    };

    private static SolveReport<Flt64> WithValue(Flt64 value) => new() {
        ProblemStatus = ProblemStatus.Feasible,
        TerminationReason = TerminationReason.Completed,
        SolutionPresence = SolutionPresence.Incumbent,
        Solution = new SolveSolution<Flt64>(new Solution<Flt64>(new[] { value })),
    };

    private sealed class DecimalConverter : IIntoValue<FltX> {
        public FltX Zero => FltX.Zero;
        public FltX One => FltX.One;
        public FltX IntoValue(Flt64 value) => value.ToFltX();
        public Flt64 FromValue(FltX value) => value.ToFlt64();
    }

    private sealed class IntegerConverter : IIntoValue<NInt64> {
        public NInt64 Zero => NInt64.Zero;
        public NInt64 One => NInt64.One;
        public NInt64 IntoValue(Flt64 value) => value.ToInt64();
        public Flt64 FromValue(NInt64 value) => value.ToFlt64();
    }

    private sealed class MisleadingIntegerConverter : IIntoValue<IntX> {
        public IntX Zero => IntX.Zero;
        public IntX One => IntX.One;
        public IntX IntoValue(Flt64 value) {
            double number = value.ToDouble();
            if (double.IsInfinity(number)) {
                var huge = BigInteger.Pow(10, 400);
                return new IntX(number > 0 ? huge : -huge);
            }
            BigInteger result = new(number);
            return new IntX(System.Math.Abs(number) == 9007199254740992d
                ? result + System.Math.Sign(number)
                : result);
        }
        public Flt64 FromValue(IntX value) => value.ToFlt64();
    }

    private sealed class FloatConverter : IIntoValue<Flt32> {
        public Flt32 Zero => new(0f);
        public Flt32 One => new(1f);
        public Flt32 IntoValue(Flt64 value) => value.ToFlt32();
        public Flt64 FromValue(Flt32 value) => value.ToFlt64();
    }

    private sealed class RationalConverter : IIntoValue<RtnX> {
        public RtnX Zero => new(0, 1);
        public RtnX One => new(1, 1);
        public RtnX IntoValue(Flt64 value) => new(value.ToDouble(), 1);
        public Flt64 FromValue(RtnX value) => value.ToFlt64();
    }

    private sealed class ThrowingConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => throw new InvalidOperationException("test converter");
        public Flt64 FromValue(Flt64 value) => value;
    }
}
