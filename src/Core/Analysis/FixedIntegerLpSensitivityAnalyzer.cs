#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using ReportRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.ConstraintActivityReport, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 固定整数 LP 敏感性分析 / Fixed-integer LP sensitivity analysis.
/// </summary>
/// <remarks>
/// **这是 `FixedIntegerLpSensitivityAnalyzer.analyze` 的编排移植。**
///
/// **它能在没有真实求解器的情况下跑通**：只要给一个 <see cref="IFixedIntegerLpBackend"/> 的假实现，
/// 整条链路——校验、固定值投影、能力门、求解、敏感性组装——都能走完。
///
/// **It runs without a real solver**: given a stub <see cref="IFixedIntegerLpBackend"/>, the whole chain — validation, fixed-value projection, capability gate, solve, sensitivity assembly — completes.
///
/// **四道早退，顺序固定：**
///
/// | 序 | 条件 | 状态 | 为什么排在这里 |
/// | --- | --- | --- | --- |
/// | 1 | 快照校验失败 | **失败** | 身份都错了，后面每一步都无意义 |
/// | 2 | 固定值求不出 | `Unknown` | **试过了，得不出**——不是后端的问题 |
/// | 3 | 无后端或能力未声明 | `Unsupported` | 这条路走不通 |
/// | 4 | 后端返回失败 | **原样带回** | 不吞成"零敏感度" |
///
/// **第 2 与第 3 的顺序是本文件最容易被改坏的地方**（B1038 已单独钉过 `FixedIntegerLpGates.Preflight`）。
///
/// **The order of 2 and 3 is the easiest thing here to break** (pinned separately by `FixedIntegerLpGates.Preflight` in B1038).
/// </remarks>
public sealed class FixedIntegerLpSensitivityAnalyzer
{
    private readonly IFixedIntegerLpBackend? _backend;
    private readonly ConstraintActivityAnalyzer _activityAnalyzer;

    /// <summary>
    /// 构造分析器 / Construct the analyzer.
    /// </summary>
    /// <param name="backend">LP 后端；<c>null</c> 表示没有可用的对偶来源 / The LP backend, or <c>null</c> when no dual source exists.</param>
    /// <param name="activityAnalyzer">活跃度分析器 / The activity analyzer.</param>
    public FixedIntegerLpSensitivityAnalyzer(
        IFixedIntegerLpBackend? backend = null,
        ConstraintActivityAnalyzer? activityAnalyzer = null) {
        _backend = backend;
        _activityAnalyzer = activityAnalyzer ?? new ConstraintActivityAnalyzer();
    }

    /// <summary>
    /// 分析一个快照 / Analyze one snapshot.
    /// </summary>
    /// <param name="snapshot">模型快照 / The model snapshot.</param>
    /// <param name="solution">基线 CP 解 / The baseline CP solution.</param>
    /// <param name="baselineObjective">基线目标值 / The baseline objective.</param>
    /// <param name="capabilityMatrix">能力矩阵 / The capability matrix.</param>
    /// <param name="options">选项 / The options.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>报告或错误 / The report, or an error.</returns>
    public async Task<Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? solution,
        Flt64? baselineObjective = null,
        CapabilityMatrix? capabilityMatrix = null,
        FixedIntegerLpSensitivityOptions? options = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);

        // **空的能力矩阵 = 没有任何声明 = 不额外禁止。**
        // **An empty matrix means no declarations, which forbids nothing extra.**
        capabilityMatrix ??= new CapabilityMatrix(new HashSet<SolverModelType>());
        options ??= new FixedIntegerLpSensitivityOptions();

        if (!snapshot.Validate().IsOk) {
            return Results.Failed<FixedIntegerLpSensitivityReport>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "CP snapshot 身份或目标语义无效 / CP snapshot identity or objective semantics is invalid"));
        }

        // **活跃度先算，它不依赖后端。** 即使后面早退，报告里也带着"哪些约束在解里出现过"。
        // **Activity is computed first because it needs no backend.** Even on an early exit the report carries "which constraints appeared in the solution".
        IReadOnlyList<ConstraintActivity?> activities = Activities(snapshot, solution);

        IReadOnlyDictionary<VariableId, Int64>? fixedValues = FixedValues(snapshot, solution, out string? fixedValuesFailure);

        bool backendAvailable = _backend is not null
            && ConstraintPerturbationSupport.CapabilityAllowed(
                capabilityMatrix, AnalysisCapability.FixedIntegerLpSensitivity);

        AnalysisStatus? preflight = FixedIntegerLpGates.Preflight(
            fixedValuesAvailable: fixedValuesFailure is null,
            backendAvailable: backendAvailable);

        IReadOnlyList<ConstraintId> constraintIds = snapshot.Constraints.Select(constraint => constraint.Id).ToArray();
        IReadOnlyList<LocalConstraintSensitivity> baseSensitivities = constraintIds
            .Select((id, index) => new LocalConstraintSensitivity(
                ConstraintId: id,
                Activity: activities[index]))
            .ToArray();

        if (preflight is { } status) {
            return Results.Ok(new FixedIntegerLpSensitivityReport(
                Status: status,
                BaselineObjective: baselineObjective,
                FixedValues: fixedValues,
                Sensitivities: baseSensitivities,
                Message: fixedValuesFailure
                    ?? "当前求解器未声明固定整数 LP 对偶能力 / No fixed-integer LP dual backend is declared"));
        }

        // **走到这里 `solution` 一定非空**：`FixedValues` 在解缺失时会给失败原因。
        // **`solution` is non-null here**: `FixedValues` reports a failure when it is missing.
        var request = new FixedIntegerLpRequest(
            Snapshot: snapshot,
            FixedValues: fixedValues!,
            BaselineSolution: solution!,
            Options: options);

        Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> solved =
            await _backend!.SolveAsync(request, cancellationToken).ConfigureAwait(false);

        // **后端失败原样带回。** 吞成"空敏感性报告"会让调用方以为敏感度为零。
        // **A backend failure is carried through.** Swallowing it into an empty report makes the caller think sensitivity is zero.
        if (solved is Failed<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<FixedIntegerLpSensitivityReport>(failed.Error);
        }
        if (solved is not Ok<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Failed<FixedIntegerLpSensitivityReport>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "固定整数 LP 后端返回了无效的结果状态 / The fixed-integer LP backend returned an invalid result state"));
        }

        FixedIntegerLpSolveResult result = ok.Value;
        IReadOnlyList<LocalConstraintSensitivity> sensitivities = FixedIntegerLpGates.AssembleSensitivities(
            constraintIds, result, activities, options.DualTolerance);

        return Results.Ok(new FixedIntegerLpSensitivityReport(
            Status: result.Status,
            BaselineObjective: baselineObjective,
            FixedValues: fixedValues,
            Sensitivities: sensitivities,
            ObjectiveValue: result.ObjectiveValue,
            SolveTime: result.SolveTime,
            Message: result.Message));
    }

    private IReadOnlyList<ConstraintActivity?> Activities(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? solution) {
        ReportRet activity = _activityAnalyzer.Analyze(snapshot, solution);

        // **活跃度算不出来不是致命错误**：它只是报告里的附加信息，
        // **而早退的路径上更不该因为它而整个失败。**
        // **A failed activity pass is not fatal**: it is supplementary information, **and an early-exit path certainly should not fail because of it.**
        if (activity is not Ok<ConstraintActivityReport, ErrorCode, Error<ErrorCode>> ok) {
            return snapshot.Constraints.Select(_ => (ConstraintActivity?)null).ToArray();
        }

        ConstraintActivityReport report = ok.Value;
        return snapshot.Constraints
            .Select(constraint => report.Constraint(constraint.Id))
            .ToArray();
    }

    private static IReadOnlyDictionary<VariableId, Int64>? FixedValues(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? solution,
        out string? failure) {
        failure = null;

        if (solution is null) {
            failure = "缺少 baseline CP 解 / Baseline CP solution is missing";
            return null;
        }

        IReadOnlyDictionary<VariableId, Int64> values = solution.Values;

        // **未知变量先查**：多出来的值意味着这份解来自另一个模型，
        // 而"缺少变量"的检查会先撞上它，指向错误的原因。
        // **Unknown variables first**: an extra value means the solution belongs to another model, and the "missing variable" check would trip on it first and name the wrong cause.
        VariableId? unknown = values.Keys
            .Where(id => snapshot.Variable(id) is null)
            .Select(id => (VariableId?)id)
            .FirstOrDefault();
        if (unknown is { } unknownId) {
            failure = "baseline CP 解包含未知变量 / Baseline CP solution contains an unknown variable: " + unknownId.Value;
            return null;
        }

        ConstraintProgrammingVariableSnapshot? missing = snapshot.Variables
            .FirstOrDefault(variable => !values.ContainsKey(variable.Id));
        if (missing is not null) {
            failure = "缺少整数变量赋值 / Missing integer assignment: " + missing.Id.Value;
            return null;
        }

        ConstraintProgrammingVariableSnapshot? outside = snapshot.Variables
            .FirstOrDefault(variable => !variable.Domain.Contains(values[variable.Id]));
        if (outside is not null) {
            failure = "整数变量赋值超出值域 / Integer assignment is outside its domain: " + outside.Id.Value;
            return null;
        }

        return snapshot.Variables.ToDictionary(variable => variable.Id, variable => values[variable.Id]);
    }
}
