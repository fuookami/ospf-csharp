#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using ReportRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.ConstraintActivityReport, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 在一个不可变 CP snapshot 与基线赋值上计算活动性 / Computes activity at one immutable CP snapshot and baseline assignment.
/// </summary>
/// <remarks>
/// 与基线 `ConstraintActivityAnalyzer.kt`（566 行）的核心对应。**整数比较暴露精确的标量松弛度量；
/// 全局约束与稀疏值域按语义契约求值，绝不伪造 LP 风格松弛。**
///
/// **B970 只搬核心分析**：基线的三个 `analyze(session, …)` 重载要 `CriticalConstraintAnalysisSession`（缓存与生命周期），
/// 那一层还没搬，所以本批**只搬纯计算入口** `Analyze(snapshot, solution)`。**没搬的重载没有空跑版本。**
///
/// Mirrors the core of the baseline's `ConstraintActivityAnalyzer.kt` (566 lines). **Integer comparisons expose an exact scalar slack metric; global constraints and sparse
/// domains are evaluated through their semantic contract and never receive a fabricated LP-style slack.** **B970 ported the core analysis only**: the baseline's three
/// `analyze(session, …)` overloads need `CriticalConstraintAnalysisSession` (cache and lifecycle), which is not ported yet, so only the pure `Analyze(snapshot, solution)`
/// entry point came over. **The missing overloads were not stubbed.**
/// </remarks>
public sealed class ConstraintActivityAnalyzer
{
    /// <summary>默认绝对容差 / Default absolute tolerance.</summary>
    public const double DefaultTolerance = 1e-6;

    /// <summary>NearlyActive 默认阈值倍数 / Default multiplier used for NearlyActive.</summary>
    public const double DefaultNearlyActiveFactor = 10.0;

    /// <summary>默认归一化分母下限 / Default normalization floor.</summary>
    public const double DefaultNormalizationFloor = 1.0;

    /// <summary>构造分析器 / Construct the analyzer.</summary>
    /// <param name="tolerance">Active 的绝对容差 / Absolute active tolerance.</param>
    /// <param name="nearlyActiveTolerance">NearlyActive 的上限 / Upper threshold for NearlyActive.</param>
    /// <param name="normalizationFloor">归一化松弛的分母下限 / Denominator floor for normalized slack.</param>
    /// <exception cref="ArgumentException">参数不成立 / A parameter is not well formed.</exception>
    public ConstraintActivityAnalyzer(
        double tolerance = DefaultTolerance,
        double? nearlyActiveTolerance = null,
        double normalizationFloor = DefaultNormalizationFloor) {
        double nearlyActive = nearlyActiveTolerance ?? tolerance * DefaultNearlyActiveFactor;

        // **`NaN` 容差必须被拒**：它让每次比较都为假，于是**没有一条被判为 active 或 violated**，
        // 报告看起来只是"全部 inactive"。**而 `NaN` 的比较恒假这一点，不会在任何地方报错。**
        // **A `NaN` tolerance has to be refused**: every comparison is false, so **nothing is ever classified active or violated** and the report simply looks "all inactive".
        // **Nothing anywhere reports that a `NaN` comparison is always false.**
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0.0) {
            throw new ArgumentException("活动性容差必须有限且非负 / Activity tolerance must be finite and non-negative", nameof(tolerance));
        }
        if (double.IsNaN(nearlyActive) || double.IsInfinity(nearlyActive) || nearlyActive < tolerance) {
            throw new ArgumentException(
                "NearlyActive 容差必须有限且不小于活动性容差 / Nearly-active tolerance must be finite and no smaller than activity tolerance",
                nameof(nearlyActiveTolerance));
        }
        if (double.IsNaN(normalizationFloor) || double.IsInfinity(normalizationFloor) || normalizationFloor <= 0.0) {
            throw new ArgumentException(
                "归一化分母下限必须有限且为正 / Activity normalization floor must be finite and positive",
                nameof(normalizationFloor));
        }

        Tolerance = tolerance;
        NearlyActiveTolerance = nearlyActive;
        NormalizationFloor = normalizationFloor;
    }

    /// <summary>Active 的绝对容差 / Absolute active tolerance.</summary>
    public double Tolerance { get; }

    /// <summary>NearlyActive 的上限 / Upper threshold for NearlyActive.</summary>
    public double NearlyActiveTolerance { get; }

    /// <summary>归一化松弛的分母下限 / Denominator floor for normalized slack.</summary>
    public double NormalizationFloor { get; }

    /// <summary>
    /// 分析 snapshot；solution 为 <c>null</c> 时生成 `Unknown` 记录 / Analyze a snapshot; a null solution yields `Unknown` records.
    /// </summary>
    /// <param name="snapshot">不可变 CP snapshot / Immutable CP snapshot.</param>
    /// <param name="solution">要分析的赋值，可为 <c>null</c> / Assignment to analyze, or <c>null</c>.</param>
    /// <returns>活动性报告结果 / Activity report result.</returns>
    public ReportRet Analyze(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? solution = null) {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Validate().IsOk) {
            return Results.Failed<ConstraintActivityReport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "CP snapshot 身份清单无效 / CP snapshot identity manifest is invalid"));
        }
        return Results.Ok(BuildReport(snapshot, solution));
    }

    /// <summary>
    /// 使用稳定变量 ID 作为键的值映射分析 snapshot / Analyze a snapshot with values keyed by stable variable ID.
    /// </summary>
    /// <param name="snapshot">不可变 CP snapshot / Immutable CP snapshot.</param>
    /// <param name="values">按稳定变量 ID 索引的值 / Values keyed by stable variable ID.</param>
    /// <returns>活动性报告结果 / Activity report result.</returns>
    public ReportRet Analyze(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlyDictionary<VariableId, Int64>? values) {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (values is null) {
            return Analyze(snapshot, (ConstraintProgrammingSolution?)null);
        }
        return Analyze(snapshot, new ConstraintProgrammingSolution(values));
    }

    private ConstraintActivityReport BuildReport(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? solution) {
        var activities = new List<ConstraintActivity>(snapshot.Constraints.Count + (snapshot.Variables.Count * 2));

        foreach (ConstraintProgrammingConstraintSnapshot definition in snapshot.Constraints) {
            activities.Add(ActivityForConstraint(definition, solution));
        }
        foreach (ConstraintProgrammingVariableSnapshot variable in snapshot.Variables) {
            activities.Add(ActivityForBound(variable.Id, domain: variable.Domain, BoundSide.Lower, solution));
            activities.Add(ActivityForBound(variable.Id, domain: variable.Domain, BoundSide.Upper, solution));

            // **`Boolean` 值域不再单独登记稀疏值域**：它的两侧边界已经完整描述了取值，再登记一条会让
            // 同一个变量在报告里出现三次，而第三次没有任何新信息。
            // **A `Boolean` domain gets no extra sparse-domain record**: its two bounds already describe the value, and a third record adds nothing but a third row.
            if (variable.Domain is IntegerDomain.Values values && values != IntegerDomain.Boolean) {
                activities.Add(ActivityForSparseDomain(variable.Id, values, solution));
            }
        }

        return new ConstraintActivityReport(
            activities,
            SummarizeGroups(activities),
            Tolerance,
            NearlyActiveTolerance);
    }

    private ConstraintActivity ActivityForConstraint(
        ConstraintProgrammingConstraintSnapshot definition,
        ConstraintProgrammingSolution? solution) {
        var source = new DiagnosticSource.Constraint(definition.Id);

        if (solution is null) {
            return UnknownActivity(definition.Id, definition.GroupName, source,
                ActivityEvidenceKind.Unknown, "缺少基线 CP 解 / Baseline CP solution is missing");
        }

        IReadOnlyDictionary<VariableId, Int64> values = solution.Values;

        if (definition.Constraint is ConstraintProgrammingConstraint.IntegerComparison comparison) {
            Fuookami.Ospf.Utils.Functional.Result<Int64, ErrorCode, Error<ErrorCode>> evaluated =
                comparison.Expression.Evaluate(values);
            if (!evaluated.IsOk) {
                return UnknownActivity(definition.Id, definition.GroupName, source,
                    ActivityEvidenceKind.Unknown, ResultMessage(evaluated));
            }

            Int64 lhs = evaluated.Value;
            BigInteger exactSlack = ComparisonSlack(lhs, comparison.Rhs, comparison.Comparison);
            double slack = ReportMetric(exactSlack);
            double denominator = global::System.Math.Max(
                NormalizationFloor,
                global::System.Math.Max(global::System.Math.Abs(lhs.ToFlt64().ToDouble()), global::System.Math.Abs(comparison.Rhs.ToFlt64().ToDouble())));
            double normalizedSlack = slack / denominator;

            bool satisfied = comparison.Comparison switch {
                ConstraintProgrammingComparison.Equal => lhs == comparison.Rhs,
                ConstraintProgrammingComparison.LessOrEqual => lhs.ToLong() <= comparison.Rhs.ToLong(),
                _ => lhs.ToLong() >= comparison.Rhs.ToLong(),
            };
            ActivityStatus status = comparison.Comparison switch {
                ConstraintProgrammingComparison.Equal => satisfied ? ActivityStatus.Active : ActivityStatus.Violated,
                _ => ClassifySlack(slack),
            };

            return new ConstraintActivity(
                definition.Id,
                definition.GroupName,
                status,
                evidence: new ActivityEvidence(
                    source,
                    ActivityEvidenceKind.Comparison,
                    Lhs: lhs.ToFlt64().ToDouble(),
                    Rhs: comparison.Rhs.ToFlt64().ToDouble(),
                    Relation: comparison.Comparison,
                    ExactLhs: lhs.ToLong().ToString(),
                    ExactRhs: comparison.Rhs.ToLong().ToString(),
                    Satisfied: satisfied),
                slack: slack,
                normalizedSlack: normalizedSlack,
                tolerance: Tolerance);
        }

        Fuookami.Ospf.Utils.Functional.Result<bool, ErrorCode, Error<ErrorCode>> satisfaction =
            definition.Constraint.IsSatisfied(values);
        if (!satisfaction.IsOk) {
            return UnknownActivity(definition.Id, definition.GroupName, source,
                ActivityEvidenceKind.Unknown, ResultMessage(satisfaction));
        }
        bool constraintSatisfied = satisfaction.Value;

        // **只有语义上存在无歧义标量"边界距离"的全局约束才给出 margin**；其余保持 `SatisfiedWithoutSlackMetric`，
        // **绝不伪造 slack。**
        // **Only global constraints with an unambiguous scalar boundary distance receive a margin**; the rest keep `SatisfiedWithoutSlackMetric` and **never receive fabricated slack.**
        double? margin = AllDifferentMargin(definition.Constraint, values);
        if (margin is { } bound) {
            return new ConstraintActivity(
                definition.Id,
                definition.GroupName,
                ClassifySlack(bound),
                evidence: new ActivityEvidence(source, ActivityEvidenceKind.Semantic, Satisfied: constraintSatisfied),
                slack: bound,
                normalizedSlack: bound,
                tolerance: Tolerance);
        }

        return new ConstraintActivity(
            definition.Id,
            definition.GroupName,
            constraintSatisfied ? ActivityStatus.SatisfiedWithoutSlackMetric : ActivityStatus.Violated,
            evidence: new ActivityEvidence(
                source,
                ActivityEvidenceKind.Semantic,
                Satisfied: constraintSatisfied,
                Message: constraintSatisfied ? null : "CP 语义约束未满足 / CP semantic constraint is not satisfied"),
            slack: null,
            normalizedSlack: null,
            tolerance: null);
    }

    private ConstraintActivity ActivityForBound(
        VariableId variableId,
        IntegerDomain domain,
        BoundSide side,
        ConstraintProgrammingSolution? solution) {
        DiagnosticSource source = side == BoundSide.Lower
            ? new DiagnosticSource.VariableLowerBound(variableId)
            : new DiagnosticSource.VariableUpperBound(variableId);

        Int64? maybeValue = solution is not null && solution.Values.TryGetValue(variableId, out Int64 found)
            ? found
            : null;
        if (maybeValue is not { } value) {
            return UnknownActivity(null, null, source, ActivityEvidenceKind.Unknown,
                "缺少变量赋值 / Variable assignment is missing: " + variableId.Value);
        }

        Int64 boundary = side == BoundSide.Lower ? domain.LowerBound : domain.UpperBound;
        BigInteger exactSlack = side == BoundSide.Lower
            ? Difference(value, boundary)
            : Difference(boundary, value);
        double slack = ReportMetric(exactSlack);
        double denominator = global::System.Math.Max(
            NormalizationFloor,
            global::System.Math.Max(global::System.Math.Abs(value.ToFlt64().ToDouble()), global::System.Math.Abs(boundary.ToFlt64().ToDouble())));

        return new ConstraintActivity(
            null,
            null,
            ClassifySlack(slack),
            evidence: new ActivityEvidence(
                source,
                ActivityEvidenceKind.VariableBound,
                Value: value.ToFlt64().ToDouble(),
                Boundary: boundary.ToFlt64().ToDouble(),
                ExactValue: value.ToLong().ToString(),
                ExactBoundary: boundary.ToLong().ToString(),
                Satisfied: slack >= 0.0),
            slack: slack,
            normalizedSlack: slack / denominator,
            tolerance: Tolerance);
    }

    private ConstraintActivity ActivityForSparseDomain(
        VariableId variableId,
        IntegerDomain.Values domain,
        ConstraintProgrammingSolution? solution) {
        var source = new DiagnosticSource.SparseDomain(variableId);

        Int64? maybeValue = solution is not null && solution.Values.TryGetValue(variableId, out Int64 found)
            ? found
            : null;
        if (maybeValue is not { } value) {
            return UnknownActivity(null, null, source, ActivityEvidenceKind.Unknown,
                "缺少变量赋值 / Variable assignment is missing: " + variableId.Value);
        }

        bool satisfied = domain.Contains(value);
        return new ConstraintActivity(
            null,
            null,
            satisfied ? ActivityStatus.SatisfiedWithoutSlackMetric : ActivityStatus.Violated,
            evidence: new ActivityEvidence(
                source,
                ActivityEvidenceKind.SparseDomain,
                Value: value.ToFlt64().ToDouble(),
                ExactValue: value.ToLong().ToString(),
                Satisfied: satisfied,
                Message: satisfied ? null : "变量值不在稀疏值域中 / Variable value is outside the sparse domain"),
            slack: null,
            normalizedSlack: null,
            tolerance: null);
    }

    /// <summary>
    /// CP 全局约束的自然 margin / Natural margin for a CP global constraint.
    /// </summary>
    /// <param name="constraint">约束 / The constraint.</param>
    /// <param name="values">变量取值 / The variable values.</param>
    /// <returns>自然 margin，无歧义度量不存在时为 <c>null</c> / The natural margin, or <c>null</c> when no unambiguous metric exists.</returns>
    /// <remarks>
    /// **只有 `AllDifferent` 有**：整数取值下它的自然标量距离是"最小的两两间距减一"——还需要缩小多少间距才会违反约束。
    /// 全异时 `minGap ≥ 1`、margin `≥ 0`（`0` 表示已贴边）；出现重复时 `minGap = 0`、margin `= -1`。
    ///
    /// **间距用 `BigInteger` 算**：两个合法 `Int64` 值的差距可能超过 `long.MaxValue`，
    /// 用 `long` 算会**回绕成负 margin**——那会让一条满足的约束显示为违反。
    ///
    /// **Only `AllDifferent` has one**: with integer values its natural scalar distance is "the smallest pairwise gap minus one". **The gap is computed in `BigInteger`** because two
    /// valid `Int64` values can be further apart than `long.MaxValue`, and `long` arithmetic would **wrap into a negative margin** — showing a satisfied constraint as violated.
    /// </remarks>
    private static double? AllDifferentMargin(
        ConstraintProgrammingConstraint constraint,
        IReadOnlyDictionary<VariableId, Int64> values) {
        if (constraint is not ConstraintProgrammingConstraint.AllDifferent allDifferent) {
            return null;
        }
        if (allDifferent.Expressions.Count < 2) {
            // 少于两个表达式时不存在两两间距。 / Fewer than two expressions means no pairwise gap.
            return null;
        }

        var evaluated = new List<BigInteger>(allDifferent.Expressions.Count);
        foreach (ConstraintProgrammingExpression expression in allDifferent.Expressions) {
            Fuookami.Ospf.Utils.Functional.Result<Int64, ErrorCode, Error<ErrorCode>> result =
                expression.Evaluate(values);
            if (!result.IsOk) {
                return null;
            }
            evaluated.Add(new BigInteger(result.Value.ToLong()));
        }
        evaluated.Sort();

        BigInteger minGap = evaluated
            .Zip(evaluated.Skip(1), (left, right) => right - left)
            .Min();
        return ReportMetric(minGap - BigInteger.One);
    }

    private ConstraintActivity UnknownActivity(
        ConstraintId? constraintId,
        string? group,
        DiagnosticSource source,
        ActivityEvidenceKind kind,
        string? message) =>
        new(
            constraintId,
            group,
            ActivityStatus.Unknown,
            evidence: new ActivityEvidence(
                source,
                kind,
                Message: string.IsNullOrWhiteSpace(message)
                    ? "CP 成员无法求值 / CP member could not be evaluated"
                    : message),
            slack: null,
            normalizedSlack: null,
            tolerance: null);

    private ActivityStatus ClassifySlack(double slack) {
        if (slack < -Tolerance) {
            return ActivityStatus.Violated;
        }
        double magnitude = global::System.Math.Abs(slack);
        if (magnitude <= Tolerance) {
            return ActivityStatus.Active;
        }
        return magnitude <= NearlyActiveTolerance ? ActivityStatus.NearlyActive : ActivityStatus.Inactive;
    }

    private static IReadOnlyList<ActivityGroupSummary> SummarizeGroups(
        IReadOnlyList<ConstraintActivity> activities) {
        // **组汇总只统计约束实例**：变量边界与稀疏值域留在 `Activities` 里但不进组计数。
        // **Group summaries count constraint instances only**: variable bounds and sparse domains stay in `Activities` but out of the group counts.
        var grouped = new List<(string? Group, List<ConstraintActivity> Members)>();
        var index = new Dictionary<string, int>();

        foreach (ConstraintActivity activity in activities) {
            if (activity.ConstraintId is null) {
                continue;
            }
            string key = activity.Group ?? "\u0000ungrouped";
            if (!index.TryGetValue(key, out int position)) {
                position = grouped.Count;
                index[key] = position;
                grouped.Add((activity.Group, []));
            }
            grouped[position].Members.Add(activity);
        }

        return grouped.Select(entry => new ActivityGroupSummary(
            entry.Group,
            entry.Members.Count,
            entry.Members.Count(member => member.Status == ActivityStatus.Active),
            entry.Members.Count(member => member.Status == ActivityStatus.NearlyActive),
            entry.Members.Count(member => member.Status == ActivityStatus.Inactive),
            entry.Members.Count(member => member.Status == ActivityStatus.SatisfiedWithoutSlackMetric),
            entry.Members.Count(member => member.Status == ActivityStatus.Violated),
            entry.Members.Count(member => member.Status == ActivityStatus.Unknown))).ToArray();
    }

    private static BigInteger ComparisonSlack(
        Int64 lhs,
        Int64 rhs,
        ConstraintProgrammingComparison comparison) {
        var left = new BigInteger(lhs.ToLong());
        var right = new BigInteger(rhs.ToLong());

        // **等式没有单侧 slack**：非零残差必须作为违反处理，所以它的"松弛"是残差的绝对值。
        // 照抄 `right - left` 会让一个残差为 `-5` 的等式看起来比残差为 `+1` 的更松弛。
        // **Equality has no one-sided slack**: a non-zero residual is a violation, so its "slack" is the absolute residual. Copying `right - left` would make an equality off by
        // `-5` look slacker than one off by `+1`.
        return comparison switch {
            ConstraintProgrammingComparison.Equal => BigInteger.Abs(right - left),
            ConstraintProgrammingComparison.LessOrEqual => right - left,
            _ => left - right,
        };
    }

    private static BigInteger Difference(Int64 left, Int64 right) =>
        new BigInteger(left.ToLong()) - new BigInteger(right.ToLong());

    /// <summary>
    /// 将精确松弛投影为报告使用的标量度量 / Project an exact slack onto the report's scalar metric.
    /// </summary>
    /// <param name="value">精确松弛 / The exact slack.</param>
    /// <returns>标量度量 / The scalar metric.</returns>
    /// <remarks>
    /// **分类始终使用精确值**；这个转换只是让公开报告能暴露一个标量，**不引入第二套容差边界**。
    /// **Classification always uses the exact value**; this conversion only lets the public report expose a scalar and **introduces no second tolerance boundary**.
    /// </remarks>
    private static double ReportMetric(BigInteger value) => double.Parse(
        value.ToString(),
        System.Globalization.CultureInfo.InvariantCulture);

    private static string ResultMessage<T>(Fuookami.Ospf.Utils.Functional.Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Fuookami.Ospf.Utils.Functional.Failed<T, ErrorCode, Error<ErrorCode>> failed => failed.Error.Message,
            Fuookami.Ospf.Utils.Functional.Fatal<T, ErrorCode, Error<ErrorCode>> fatal =>
                string.Join("; ", fatal.Errors.Select(error => error.Message)),
            _ => "CP 求值结果无效 / CP evaluation returned an invalid result",
        };
}
