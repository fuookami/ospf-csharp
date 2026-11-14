#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 可复用的线性 Benders 迭代循环；不放松未固定的整数变量。
/// Reusable linear Benders loop that never relaxes unfixed integer variables.
/// </summary>
public sealed class LinearBendersAlgorithm {
    private readonly IBendersDecompositionSolver? _solver;

    /// <summary>
    /// 创建线性 Benders 算法。
    /// Create a linear Benders algorithm.
    /// </summary>
    /// <param name="solver">主、子问题后端 / Master and subproblem backend.</param>
    public LinearBendersAlgorithm(IBendersDecompositionSolver? solver) {
        _solver = solver;
    }

    /// <summary>
    /// 执行分解并返回已验证 incumbent 或结构化失败。
    /// Execute decomposition and return a verified incumbent or a structured failure.
    /// </summary>
    /// <param name="models">已注册模型 / Registered models.</param>
    /// <param name="config">执行预算 / Execution budget.</param>
    /// <returns>结果或结构化失败 / Result or structured failure.</returns>
    public async Task<Result<LinearBendersResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        LinearBendersModels models,
        LinearBendersConfig? config = null) {
        if (_solver is null) {
            return Failure("Benders 后端为空 / Benders backend is null");
        }
        if (models is null) {
            return Failure("Benders 模型为空 / Benders models are null");
        }
        if (models.IsDisposed) {
            return Failure("Benders 模型已释放 / Benders models are disposed");
        }
        config ??= new LinearBendersConfig();
        string? budgetFailure = ValidateBudget(config);
        if (budgetFailure is not null) {
            return Failure(budgetFailure);
        }
        if (models.Master.ObjectCategory != ObjectCategory.Minimum ||
            models.Sub.ObjectCategory != ObjectCategory.Minimum) {
            return Failure("主、子问题必须最小化 / Master and subproblem must minimize");
        }
        string? bindingFailure = ValidateBindings(models);
        if (bindingFailure is not null) {
            return Failure(bindingFailure);
        }

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expandedResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(models.Sub);
        if (expandedResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expandedOk) {
            return Propagate<LinearBendersResult, LinearMechanismModel<Flt64>>(expandedResult);
        }
        using LinearMechanismModel<Flt64> expandedSub = expandedOk.Value;
        string? integerFailure = ValidateIntegerVariables(models, expandedSub);
        if (integerFailure is not null) {
            return Failure(integerFailure);
        }

        (LinearPolynomial<Flt64> subObjective, Result<Success, ErrorCode, Error<ErrorCode>> objectiveResult) =
            NormalizeObjective(expandedSub);
        if (!objectiveResult.IsOk) {
            return Propagate<LinearBendersResult, Success>(objectiveResult);
        }

        IReadOnlyDictionary<MathConstraint, IConstraint<Flt64, LinearCategory>> constraintsByOrigin =
            BuildConstraintOriginMap(expandedSub);
        var trace = new List<LinearBendersIteration>();
        var cutKeys = new HashSet<string>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        double lower = double.NegativeInfinity;
        double upper = double.PositiveInfinity;
        Dictionary<IVariableItem, Flt64>? incumbent = null;
        SolveReport<Flt64>? lastMasterReport = null;
        SolveReport<Flt64>? lastSubReport = null;
        int stalls = 0;

        Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> Stop(
            TerminationReason reason,
            string detail,
            bool converged = false) {
            if (incumbent is null) {
                return Failure($"Benders 无 incumbent / Benders has no incumbent: {detail}");
            }
            return BuildResult(
                models: models,
                incumbent: incumbent,
                objective: new Flt64(upper),
                lower: lower,
                upper: upper,
                trace: trace,
                elapsed: stopwatch.Elapsed,
                reason: reason,
                detail: detail,
                converged: converged,
                masterReport: lastMasterReport,
                subReport: lastSubReport
            );
        }

        for (int iteration = 1; iteration <= config.IterationLimit; iteration++) {
            if (IsTimeExpired(config, stopwatch)) {
                return Stop(
                    reason: TerminationReason.TimeLimit,
                    detail: "time limit before master"
                );
            }
            TimeSpan remaining = Remaining(config, stopwatch);
            IBendersDecompositionSolver budgeted = remaining == Timeout.InfiniteTimeSpan
                ? _solver
                : _solver.WithTimeLimit(remaining);
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> masterResult =
                await budgeted.SolveMasterReportAsync(
                    name: models.Master.Name,
                    metaModel: models.Master,
                    toLogModel: false
                );
            if (masterResult is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> masterOk) {
                return Propagate<LinearBendersResult, SolveReport<Flt64>>(masterResult);
            }
            SolveReport<Flt64> masterReport = masterOk.Value;
            lastMasterReport = masterReport;
            if (masterReport.ProblemStatus == ProblemStatus.Infeasible) {
                if (incumbent is not null) {
                    return Failure("主问题不可行与已验证 incumbent 矛盾 / Infeasible master contradicts a verified incumbent");
                }
                return Results.Failed<LinearBendersResult>(new Err<ErrorCode>(
                    ErrorCode.ORModelInfeasible,
                    "Benders 主问题已证明不可行 / Benders master is proven infeasible"
                ));
            }
            if (masterReport.ProblemStatus == ProblemStatus.Unbounded) {
                return Results.Failed<LinearBendersResult>(new Err<ErrorCode>(
                    ErrorCode.ORModelUnbounded,
                    "Benders 主问题无界 / Benders master is unbounded"
                ));
            }
            if (masterReport.Solution is null) {
                return Stop(
                    reason: masterReport.TerminationReason,
                    detail: $"master has no incumbent: {masterReport.TerminationReason}"
                );
            }

            Result<Dictionary<IVariableItem, Flt64>, ErrorCode, Error<ErrorCode>> masterValuesResult =
                ReadMasterValues(models, masterReport);
            if (!masterValuesResult.IsOk) {
                return Propagate<LinearBendersResult, Dictionary<IVariableItem, Flt64>>(masterValuesResult);
            }
            Dictionary<IVariableItem, Flt64> masterValues = masterValuesResult.Value;
            if (masterReport.Solution.Objective is not { } masterObjective) {
                return Failure("主问题缺少目标值 / Master is missing its objective");
            }
            if (!IsFinite(masterObjective)) {
                return Failure("主问题目标非有限值 / Master objective is non-finite");
            }
            bool masterOptimal = IsOptimal(masterReport);
            Flt64? masterBound = masterReport.Statistics.BestBound;
            if (masterBound is null && masterOptimal) {
                masterBound = masterObjective;
            }
            if (masterBound is { } bound && IsFinite(bound)) {
                lower = System.Math.Max(lower, bound.ToDouble());
            }

            var bindingValues = new Dictionary<IVariableItem, Flt64>();
            foreach ((string id, IVariableItem variable) in models.Bindings) {
                if (!masterValues.TryGetValue(variable, out Flt64 value)) {
                    return Failure($"主问题缺少 binding / Master is missing binding: {id}");
                }
                bindingValues[variable] = value;
            }
            if (!masterValues.TryGetValue(models.Theta, out Flt64 thetaValue)) {
                return Failure("主问题缺少追索投影 / Master is missing the recourse projection");
            }

            TimeSpan subRemaining = Remaining(config, stopwatch);
            if (subRemaining != Timeout.InfiniteTimeSpan && subRemaining <= TimeSpan.Zero) {
                return Stop(
                    reason: TerminationReason.TimeLimit,
                    detail: "time limit before subproblem"
                );
            }
            IBendersDecompositionSolver subBudgeted = subRemaining == Timeout.InfiniteTimeSpan
                ? _solver
                : _solver.WithTimeLimit(subRemaining);
            Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> subResult =
                await subBudgeted.SolveSubAsync(
                    name: models.Sub.Name,
                    metaModel: models.Sub,
                    objectVariable: models.Theta,
                    fixedVariables: bindingValues,
                    toLogModel: false
                );
            if (subResult is not Ok<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> subOk) {
                return Propagate<LinearBendersResult, IBendersDecompositionSolver.LinearSubResult>(subResult);
            }

            List<LinearInequality<Flt64>> cuts;
            bool subOptimal;
            if (subOk.Value is IBendersDecompositionSolver.LinearFeasibleResult feasible) {
                lastSubReport = feasible.Report;
                if (feasible.Report is null) {
                    return Failure("可行子问题缺少统一报告 / Feasible subproblem is missing its unified report");
                }
                if (feasible.Report.Solution is null) {
                    return Failure("可行子问题缺少解向量 / Feasible subproblem is missing its solution");
                }
                Result<Dictionary<IVariableItem, Flt64>, ErrorCode, Error<ErrorCode>> mergedResult =
                    MergeSubValues(
                        models: models,
                        bindingValues: bindingValues,
                        report: feasible.Report
                    );
                if (!mergedResult.IsOk) {
                    return Propagate<LinearBendersResult, Dictionary<IVariableItem, Flt64>>(mergedResult);
                }
                Dictionary<IVariableItem, Flt64> merged = mergedResult.Value;
                foreach ((IVariableItem variable, Flt64 value) in masterValues) {
                    merged[variable] = value;
                }
                Try verification = models.Verify(merged);
                if (!verification.IsOk) {
                    return Propagate<LinearBendersResult, Success>(verification);
                }
                if (!IsFinite(feasible.Obj)) {
                    return Failure("子问题目标非有限值 / Subproblem objective is non-finite");
                }
                Flt64 completeObjective = models.FeasibilityOnly
                    ? masterObjective
                    : masterObjective - thetaValue + feasible.Obj;
                if (!IsFinite(completeObjective)) {
                    return Failure("合并目标非有限值 / Merged objective is non-finite");
                }
                if (completeObjective < new Flt64(upper)) {
                    upper = completeObjective.ToDouble();
                    incumbent = merged;
                }
                subOptimal = IsOptimal(feasible.Report);
                cuts = new List<LinearInequality<Flt64>>();
                if (!models.FeasibilityOnly && subOptimal) {
                    Result<Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>, ErrorCode, Error<ErrorCode>>
                        dualResult = ToConstraintDual(feasible.DualSolution, constraintsByOrigin);
                    if (!dualResult.IsOk) {
                        return Propagate<LinearBendersResult,
                            Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>>(dualResult);
                    }
                    Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> cutResult =
                        LinearBendersCuts.Optimal(
                            theta: models.Theta,
                            fixedVariables: bindingValues,
                            dual: dualResult.Value,
                            objective: subObjective,
                            value: feasible.Obj
                        );
                    if (!cutResult.IsOk) {
                        return Propagate<LinearBendersResult, LinearInequality<Flt64>>(cutResult);
                    }
                    cuts.Add(cutResult.Value);
                }
            }
            else if (subOk.Value is IBendersDecompositionSolver.LinearInfeasibleResult infeasible) {
                lastSubReport = null;
                Result<Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>, ErrorCode, Error<ErrorCode>>
                    dualResult = ToConstraintDual(infeasible.FarkasDualSolution, constraintsByOrigin);
                if (!dualResult.IsOk) {
                    return Propagate<LinearBendersResult,
                        Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>>(dualResult);
                }
                Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> cutResult =
                    LinearBendersCuts.Feasible(
                        fixedVariables: bindingValues,
                        dual: dualResult.Value
                    );
                if (!cutResult.IsOk) {
                    return Propagate<LinearBendersResult, LinearInequality<Flt64>>(cutResult);
                }
                cuts = new List<LinearInequality<Flt64>> { cutResult.Value };
                subOptimal = true;
            }
            else {
                return Failure("未知线性子问题结果类型 / Unknown linear subproblem result type");
            }

            double gap = ComputeGap(lower, upper);
            if (double.IsFinite(upper) &&
                lower > upper + config.Tolerance * System.Math.Max(1.0, System.Math.Abs(upper))) {
                return Failure($"无效上下界 / Invalid bounds: lower={lower} upper={upper}");
            }
            int added = 0;
            foreach (LinearInequality<Flt64> cut in cuts) {
                Result<int, ErrorCode, Error<ErrorCode>> addResult = AddCut(
                    models: models,
                    cut: cut,
                    masterValues: masterValues,
                    cutKeys: cutKeys
                );
                if (!addResult.IsOk) {
                    return Propagate<LinearBendersResult, int>(addResult);
                }
                added += addResult.Value;
            }
            trace.Add(new LinearBendersIteration(
                LowerBound: lower,
                UpperBound: double.IsFinite(upper) ? upper : null,
                Cuts: added
            ));

            if (!subOptimal) {
                TerminationReason reason = lastSubReport?.TerminationReason ?? TerminationReason.Completed;
                return Stop(
                    reason: reason,
                    detail: "nonoptimal recourse termination"
                );
            }
            if (!masterOptimal) {
                return Stop(
                    reason: masterReport.TerminationReason,
                    detail: "nonoptimal master termination"
                );
            }
            if (incumbent is not null && gap <= config.Tolerance) {
                return Stop(
                    reason: TerminationReason.Completed,
                    detail: "converged",
                    converged: true
                );
            }
            if (IsTimeExpired(config, stopwatch)) {
                return Stop(
                    reason: TerminationReason.TimeLimit,
                    detail: "time limit after subproblem"
                );
            }
            stalls = added == 0 ? stalls + 1 : 0;
            if (stalls >= config.StallLimit) {
                return Stop(
                    reason: TerminationReason.Stalled,
                    detail: $"stalled; lower={lower} upper={upper}"
                );
            }
        }

        return Stop(
            reason: TerminationReason.IterationLimit,
            detail: $"iteration limit {config.IterationLimit}; lower={lower} upper={upper}"
        );
    }

    private static string? ValidateBudget(LinearBendersConfig config) {
        if (config.IterationLimit < 1) {
            return "迭代上限必须为正 / Iteration limit must be positive";
        }
        if (!double.IsFinite(config.Tolerance) || config.Tolerance <= 0) {
            return "容差必须为有限正数 / Tolerance must be a finite positive number";
        }
        if (config.TimeLimit is { } timeLimit && timeLimit <= TimeSpan.Zero) {
            return "时间限制必须为正 / Time limit must be positive";
        }
        if (config.StallLimit < 1) {
            return "停滞上限必须为正 / Stall limit must be positive";
        }
        return null;
    }

    private static string? ValidateBindings(LinearBendersModels models) {
        var seen = new HashSet<IVariableItem>();
        foreach ((string id, IVariableItem variable) in models.Bindings) {
            if (string.IsNullOrWhiteSpace(id)) {
                return "binding ID 不能为空 / Binding ID cannot be blank";
            }
            if (!seen.Add(variable)) {
                return $"binding 变量重复 / Duplicate binding variable: {id}";
            }
            if (models.Master.Tokens.Find(variable) is null ||
                models.Sub.Tokens.Find(variable) is null) {
                return $"binding 不在主子问题中 / Binding is absent from master or sub: {id}";
            }
        }
        if (models.Master.Tokens.Find(models.Theta) is null) {
            return "追索投影不在主问题中 / The recourse projection is absent from master";
        }
        if (models.Bindings.Values.Contains(models.Theta)) {
            return "追索投影不能是 binding / The recourse projection cannot be a binding";
        }
        return null;
    }

    private static string? ValidateIntegerVariables(
        LinearBendersModels models,
        LinearMechanismModel<Flt64> expandedSub) {
        var fixedVariables = models.Bindings.Values.ToHashSet();
        IEnumerable<IVariableItem> variables = models.Sub.Tokens.Tokens
            .Select(token => token.Variable)
            .Concat(expandedSub.Tokens.Tokens.Select(token => token.Variable))
            .Distinct();
        foreach (IVariableItem variable in variables) {
            if (variable.TypeKind.IsIntegerType &&
                !fixedVariables.Contains(variable) &&
                !IsFixed(variable)) {
                return $"子问题包含未固定整数 / Subproblem contains an unfixed integer: {variable.Name}";
            }
        }
        return null;
    }

    private static (LinearPolynomial<Flt64> Objective, Result<Success, ErrorCode, Error<ErrorCode>> Result)
        NormalizeObjective(LinearMechanismModel<Flt64> model) {
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (LinearSubObject<Flt64> part in model.ObjectFunction.GetSubObjects<Flt64>()) {
            Flt64 sign = part.Category == ObjectCategory.Minimum ? Flt64.One : -Flt64.One;
            foreach (LinearMonomial<Flt64> term in part.LinearTerms()) {
                if (term.Symbol is not IVariableItem) {
                    return (
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                        Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.OREngineModelingException,
                            "子问题目标包含非变量符号 / Subproblem objective contains a non-variable symbol"
                        ))
                    );
                }
                monomials.Add(new LinearMonomial<Flt64>(sign * term.Coefficient, term.Symbol));
            }
            constant += sign * part.Constant;
        }
        return (
            new LinearPolynomial<Flt64>(monomials, constant),
            Results.OkInstance
        );
    }

    private static IReadOnlyDictionary<MathConstraint, IConstraint<Flt64, LinearCategory>>
        BuildConstraintOriginMap(LinearMechanismModel<Flt64> expandedSub) {
        var map = new Dictionary<MathConstraint, IConstraint<Flt64, LinearCategory>>(
            ReferenceEqualityComparer.Instance
        );
        foreach (IConstraint<Flt64, LinearCategory> constraint in
            ((IMechanismModel<Flt64>)expandedSub).Constraints) {
            if (constraint.Origin is { } origin) {
                map[origin] = constraint;
            }
        }
        return map;
    }

    private static Result<Dictionary<IVariableItem, Flt64>, ErrorCode, Error<ErrorCode>>
        ReadMasterValues(LinearBendersModels models, SolveReport<Flt64> report) {
        IReadOnlyList<Token<Flt64>> tokens = models.Master.Tokens.TokensInSolver;
        IReadOnlyList<Flt64> values = report.Solution?.Values.Values ?? Array.Empty<Flt64>();
        if (values.Count != tokens.Count) {
            return Results.Failed<Dictionary<IVariableItem, Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                $"主问题解长度不匹配 / Master solution length mismatch: {values.Count} != {tokens.Count}"
            ));
        }
        var result = new Dictionary<IVariableItem, Flt64>();
        for (int index = 0; index < tokens.Count; index++) {
            if (!IsFinite(values[index])) {
                return Results.Failed<Dictionary<IVariableItem, Flt64>>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "主问题解包含非有限值 / Master solution contains a non-finite value"
                ));
            }
            result[tokens[index].Variable] = values[index];
        }
        return Results.Ok(result);
    }

    private static Result<Dictionary<IVariableItem, Flt64>, ErrorCode, Error<ErrorCode>>
        MergeSubValues(
            LinearBendersModels models,
            IReadOnlyDictionary<IVariableItem, Flt64> bindingValues,
            SolveReport<Flt64> report) {
        IReadOnlyList<Token<Flt64>> tokens = models.Sub.Tokens.TokensInSolver;
        IReadOnlyList<Flt64> values = report.Solution?.Values.Values ?? Array.Empty<Flt64>();
        IReadOnlyList<Token<Flt64>> recourseTokens = tokens
            .Where(token => !bindingValues.ContainsKey(token.Variable))
            .ToList();
        var result = new Dictionary<IVariableItem, Flt64>(bindingValues);
        if (values.Count == tokens.Count) {
            for (int index = 0; index < tokens.Count; index++) {
                result[tokens[index].Variable] = values[index];
            }
        }
        else if (values.Count == recourseTokens.Count) {
            for (int index = 0; index < recourseTokens.Count; index++) {
                result[recourseTokens[index].Variable] = values[index];
            }
        }
        else {
            return Results.Failed<Dictionary<IVariableItem, Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                $"子问题解长度不匹配 / Subproblem solution length mismatch: {values.Count}"
            ));
        }
        if (result.Values.Any(value => !IsFinite(value))) {
            return Results.Failed<Dictionary<IVariableItem, Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "子问题解包含非有限值 / Subproblem solution contains a non-finite value"
            ));
        }
        models.Sub.SetSolution(result);
        return Results.Ok(result);
    }

    private static Result<Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>, ErrorCode, Error<ErrorCode>>
        ToConstraintDual(
            IReadOnlyDictionary<MathConstraint, Flt64> dual,
            IReadOnlyDictionary<MathConstraint, IConstraint<Flt64, LinearCategory>> constraintsByOrigin) {
        var result = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>(
            ReferenceEqualityComparer.Instance
        );
        foreach ((MathConstraint origin, Flt64 value) in dual) {
            if (!constraintsByOrigin.TryGetValue(origin, out IConstraint<Flt64, LinearCategory>? constraint)) {
                return Results.Failed<Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>>(
                    new Err<ErrorCode>(
                        ErrorCode.OREngineSolvingException,
                        $"对偶结果引用未知约束 / Dual result references an unknown constraint: {origin.Name}"
                    )
                );
            }
            result[constraint] = value;
        }
        return Results.Ok(result);
    }

    private static Result<int, ErrorCode, Error<ErrorCode>> AddCut(
        LinearBendersModels models,
        LinearInequality<Flt64> cut,
        IReadOnlyDictionary<IVariableItem, Flt64> masterValues,
        HashSet<string> cutKeys) {
        var coefficients = new Dictionary<IVariableItem, Flt64>();
        Flt64 constant = cut.Lhs.Constant - cut.Rhs.Constant;
        foreach (LinearMonomial<Flt64> term in cut.Lhs.Monomials) {
            if (term.Symbol is not IVariableItem variable || !masterValues.ContainsKey(variable)) {
                return Results.Failed<int>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "割包含非主问题符号 / Cut contains a non-master symbol"
                ));
            }
            coefficients[variable] = coefficients.TryGetValue(variable, out Flt64 existing)
                ? existing + term.Coefficient
                : term.Coefficient;
        }
        foreach (LinearMonomial<Flt64> term in cut.Rhs.Monomials) {
            if (term.Symbol is not IVariableItem variable || !masterValues.ContainsKey(variable)) {
                return Results.Failed<int>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "割包含非主问题符号 / Cut contains a non-master symbol"
                ));
            }
            coefficients[variable] = coefficients.TryGetValue(variable, out Flt64 existing)
                ? existing - term.Coefficient
                : -term.Coefficient;
        }
        if (!IsFinite(constant) || coefficients.Values.Any(value => !IsFinite(value))) {
            return Results.Failed<int>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "割包含非有限数值 / Cut contains non-finite values"
            ));
        }
        double scale = System.Math.Max(
            coefficients.Values.Select(value => System.Math.Abs(value.ToDouble())).DefaultIfEmpty(0).Max(),
            System.Math.Abs(constant.ToDouble())
        );
        if (scale <= 0 || !double.IsFinite(scale)) {
            scale = 1;
        }
        string key = string.Join(
            separator: "|",
            values: new[]
            {
                cut.Comparison.ToString(),
                Format(constant.ToDouble() / scale),
                string.Join(
                    separator: ";",
                    values: coefficients
                        .Where(pair => !pair.Value.Eq(Flt64.Zero))
                        .OrderBy(pair => pair.Key.Key.Identifier)
                        .ThenBy(pair => pair.Key.Key.Index)
                        .Select(pair => $"{pair.Key.Key.Identifier}:{pair.Key.Key.Index}={Format(pair.Value.ToDouble() / scale)}")
                ),
            }
        );
        if (!cutKeys.Add(key)) {
            return Results.Ok(0);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> addResult = models.Master.AddConstraint(
            relation: cut,
            group: null,
            name: $"benders_cut_{cutKeys.Count}"
        );
        return addResult.IsOk
            ? Results.Ok(1)
            : Propagate<int, Success>(addResult);
    }

    private static Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> BuildResult(
        LinearBendersModels models,
        IReadOnlyDictionary<IVariableItem, Flt64> incumbent,
        Flt64 objective,
        double lower,
        double upper,
        IReadOnlyList<LinearBendersIteration> trace,
        TimeSpan elapsed,
        TerminationReason reason,
        string detail,
        bool converged,
        SolveReport<Flt64>? masterReport,
        SolveReport<Flt64>? subReport) {
        var values = new List<Flt64>(models.Variables.Count);
        foreach (IVariableItem variable in models.Variables) {
            if (!incumbent.TryGetValue(variable, out Flt64 value)) {
                return Results.Failed<LinearBendersResult>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"合并解缺少变量 / Merged solution is missing a variable: {variable.Name}"
                ));
            }
            values.Add(value);
        }
        double gap = ComputeGap(lower, upper);
        SolveReport<Flt64>? sourceReport = masterReport ?? subReport;
        var report = new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = reason,
            SolutionPresence = converged
                ? SolutionPresence.Optimal
                : SolutionPresence.Incumbent,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(values),
                Objective: objective
            ),
            Proof = converged
                ? new SolveProof(ProofStatus.Claimed, "benders-convergence")
                : new SolveProof(ProofStatus.None),
            Statistics = new SolveStatistics(
                SolveTime: elapsed,
                Iterations: (ulong)trace.Count,
                BestBound: double.IsFinite(lower) ? new Flt64(lower) : null,
                Gap: double.IsFinite(gap) ? new Flt64(gap) : null
            ),
            Diagnostics = converged
                ? new SolveDiagnostics<Flt64>()
                : new SolveDiagnostics<Flt64>(
                    Warnings: new[] {
                        new SolveIssue(
                            Code: "benders-termination",
                            Category: SolveIssueCategory.Backend,
                            Message: detail
                        ),
                    }
                ),
            Provenance = sourceReport?.Provenance,
            Fingerprints = sourceReport?.Fingerprints ?? new SolveFingerprints(),
        };
        return Results.Ok(new LinearBendersResult(
            values: incumbent,
            objective: objective,
            gap: gap,
            iterations: trace,
            report: report,
            terminationDetail: detail
        ));
    }

    private static bool IsOptimal(SolveReport<Flt64> report) =>
        report.ProblemStatus == ProblemStatus.Feasible &&
        report.TerminationReason == TerminationReason.Completed &&
        report.SolutionPresence == SolutionPresence.Optimal;

    private static bool IsFixed(IVariableItem variable) {
        Flt64? lower = variable.LowerBound?.Value.UnwrapOrNull();
        Flt64? upper = variable.UpperBound?.Value.UnwrapOrNull();
        return lower is { } lowerValue &&
            upper is { } upperValue &&
            lowerValue.Eq(upperValue);
    }

    private static bool IsTimeExpired(LinearBendersConfig config, Stopwatch stopwatch) =>
        config.TimeLimit is { } limit && stopwatch.Elapsed >= limit;

    private static TimeSpan Remaining(LinearBendersConfig config, Stopwatch stopwatch) {
        if (config.TimeLimit is not { } limit) {
            return Timeout.InfiniteTimeSpan;
        }
        TimeSpan remaining = limit - stopwatch.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static double ComputeGap(double lower, double upper) {
        if (!double.IsFinite(upper) || !double.IsFinite(lower)) {
            return double.PositiveInfinity;
        }
        return System.Math.Max(0, upper - lower) / System.Math.Max(1, System.Math.Abs(upper));
    }

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static bool IsFinite(Flt64 value) => double.IsFinite(value.ToDouble());

    private static Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<LinearBendersResult>(new Err<ErrorCode>(
            ErrorCode.OREngineSolvingException,
            $"Benders 求解失败 / Benders solve failed: {message}"
        ));

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Benders 结果无效 / Invalid Benders result"
            )),
        };
    }
}
