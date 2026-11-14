#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Activity;

/// <summary>
/// 活动排程结果 / An activity scheduling result.
/// </summary>
/// <param name="Report">求解报告 / The solve report.</param>
/// <param name="Schedule">排程，无解时为 null / The schedule, null when there is no solution.</param>
public sealed record ActivitySchedulingResult(
    SolveReport<Flt64> Report,
    ActivitySchedule<Flt64>? Schedule);

/// <summary>
/// 活动排程应用服务 / The activity scheduling application service.
/// </summary>
/// <remarks>
/// 与基线 `ActivitySchedulingApplicationService.solve` 对齐。四处照抄：
///
/// 1. **无解不是失败**：`SolutionPresence` 为 `None` 时返回**成功**，`Schedule` 为 null——
///    把"这个问题没有可行排程"报成错误，会让调用方无法区分"模型坏了"和"确实排不下"。
/// 2. **报告说有解就必须有数值**，否则是求解器实现的问题，返回 `ApplicationFailed`。
/// 3. **数值个数必须与模型变量数一致**且全为有限值，否则 `SetSolution` 之后取解会读到别人的值。
/// 4. **`extra` 在 `register` 之后调用**：调用方的附加约束要能看到已注册的选择变量。
///
/// Aligned with the baseline's `ActivitySchedulingApplicationService.solve`. Four things copied: **no solution is not a failure**, so `SolutionPresence.None` returns **success** with a null
/// `Schedule`, because reporting "this problem has no feasible schedule" as an error would leave the caller unable to tell a broken model from a genuinely infeasible one; **a report claiming an
/// incumbent must carry values**, otherwise the solver implementation is at fault and `ApplicationFailed` is returned; **the value count must match the model's variable count** and every value must
/// be finite, or extraction after `SetSolution` would read somebody else's numbers; and **`extra` runs after `register`**, so a caller's additional constraints can see the registered choice variables.
/// </remarks>
public sealed class ActivitySchedulingApplicationService
{
    private readonly IColumnGenerationSolver _solver;

    /// <summary>
    /// 构造 / Construct.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    public ActivitySchedulingApplicationService(IColumnGenerationSolver solver) {
        ArgumentNullException.ThrowIfNull(solver);
        _solver = solver;
    }

    /// <summary>
    /// 求解 / Solve.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="extra">附加建模，在注册之后调用 / Additional modelling, called after registration.</param>
    /// <param name="cancellationToken">取消 / Cancellation.</param>
    /// <returns>结果或失败 / The result, or a failure.</returns>
    public async Task<Result<ActivitySchedulingResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ActivitySchedulingProblem<Flt64> problem,
        Func<ActivitySchedulingContext<Flt64>, LinearMetaModel<Flt64>, Result<Success, ErrorCode, Error<ErrorCode>>>? extra = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(problem);

        Result<ActivitySchedulingContext<Flt64>, ErrorCode, Error<ErrorCode>> created =
            ActivitySchedulingContext<Flt64>.Create(problem);
        if (created is not Ok<ActivitySchedulingContext<Flt64>, ErrorCode, Error<ErrorCode>> okContext) {
            return Propagate<ActivitySchedulingResult, ActivitySchedulingContext<Flt64>>(created);
        }
        ActivitySchedulingContext<Flt64> context = okContext.Value;

        using var model = new LinearMetaModel<Flt64>("activity_scheduling");

        Result<Success, ErrorCode, Error<ErrorCode>> registered = context.Register(model);
        if (!registered.IsOk) {
            return Propagate<ActivitySchedulingResult, Success>(registered);
        }
        if (extra is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> extended = extra(context, model);
            if (!extended.IsOk) {
                return Propagate<ActivitySchedulingResult, Success>(extended);
            }
        }

        Result<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveMILPWithStatusAsync(model.Name, model).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (solved is not Ok<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>> okSolved) {
            return Propagate<ActivitySchedulingResult, IColumnGenerationSolver.MilpSolveResult>(solved);
        }

        SolveReport<Flt64> report = okSolved.Value switch {
            IColumnGenerationSolver.MilpSolveResult.Feasible feasible => feasible.Output.ToSolveReport(),
            IColumnGenerationSolver.MilpSolveResult.Infeasible infeasible => infeasible.Output.ToSolveReport(),
            _ => throw new InvalidOperationException("unreachable"),
        };

        if (report.SolutionPresence == SolutionPresence.None) {
            return Results.Ok(new ActivitySchedulingResult(report, null));
        }

        if (report.Solution is not { } solution) {
            return Results.Failed<ActivitySchedulingResult>(new Err<ErrorCode>(ErrorCode.ApplicationFailed,
                "the solve report claims an incumbent without values"));
        }
        IReadOnlyList<Flt64> values = solution.Values.Values;
        if (values.Count != model.Tokens.TokensInSolver.Count) {
            return Results.Failed<ActivitySchedulingResult>(new Err<ErrorCode>(ErrorCode.ApplicationFailed,
                "invalid solution dimension or non-finite values"));
        }
        foreach (Flt64 value in values) {
            if (!double.IsFinite(value.ToDouble())) {
                return Results.Failed<ActivitySchedulingResult>(new Err<ErrorCode>(ErrorCode.ApplicationFailed,
                    "invalid solution dimension or non-finite values"));
            }
        }
        model.SetSolution(values);

        Result<ActivitySchedule<Flt64>, ErrorCode, Error<ErrorCode>> extracted = context.ExtractSolution(model);
        if (extracted is not Ok<ActivitySchedule<Flt64>, ErrorCode, Error<ErrorCode>> okExtracted) {
            return Propagate<ActivitySchedulingResult, ActivitySchedule<Flt64>>(extracted);
        }
        return Results.Ok(new ActivitySchedulingResult(report, okExtracted.Value));
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, U>(
        Result<U, ErrorCode, Error<ErrorCode>> source) =>
        source is Failed<U, ErrorCode, Error<ErrorCode>> failed
            ? Results.Failed<T>(failed.Error)
            : new Fatal<T, ErrorCode, Error<ErrorCode>>(
                ((Fatal<U, ErrorCode, Error<ErrorCode>>)source).Errors);
}
