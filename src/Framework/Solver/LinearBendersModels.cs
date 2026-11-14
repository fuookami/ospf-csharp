#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
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
/// 线性 Benders 执行预算。
/// Linear Benders execution budget.
/// </summary>
public sealed record LinearBendersConfig {
    /// <summary>迭代上限 / Iteration limit.</summary>
    public int IterationLimit { get; init; } = 64;
    /// <summary>相对及绝对间隙容差 / Relative and absolute gap tolerance.</summary>
    public double Tolerance { get; init; } = 1e-6;
    /// <summary>总时间限制；为空表示不设限 / Total time limit; null means unbounded.</summary>
    public TimeSpan? TimeLimit { get; init; }
    /// <summary>无新割迭代上限 / No-new-cut iteration limit.</summary>
    public int StallLimit { get; init; } = 8;
}

/// <summary>
/// 扩展变量的分解阶段；整数变量只能属于主问题。
/// Extension-variable stage; integer variables may only stay in the master problem.
/// </summary>
public enum LinearBendersStage {
    /// <summary>主问题 / Master problem.</summary>
    Master,
    /// <summary>连续追索问题 / Continuous recourse problem.</summary>
    ContinuousRecourse,
}

/// <summary>
/// 已组装的主、子问题及共享变量身份。
/// Assembled master/sub models and shared variable identities.
/// </summary>
public sealed class LinearBendersModels : IDisposable {
    private bool _disposed;

    /// <summary>
    /// 创建已组装的 Benders 模型。
    /// Create assembled Benders models.
    /// </summary>
    /// <param name="master">主问题 / Master problem.</param>
    /// <param name="sub">连续子问题 / Continuous subproblem.</param>
    /// <param name="bindings">稳定 ID 到共享变量 / Stable IDs to shared variables.</param>
    /// <param name="theta">追索目标投影 / Recourse projection variable.</param>
    /// <param name="variables">完整来源变量顺序 / Complete source-variable order.</param>
    /// <param name="feasibilityOnly">子问题是否只检查可行性 / Whether the subproblem is feasibility-only.</param>
    /// <param name="verify">完整模型复核 / Full-model verification.</param>
    public LinearBendersModels(
        LinearMetaModel<Flt64> master,
        LinearMetaModel<Flt64> sub,
        IReadOnlyDictionary<string, IVariableItem> bindings,
        IVariableItem theta,
        IReadOnlyList<IVariableItem> variables,
        bool feasibilityOnly,
        Func<IReadOnlyDictionary<IVariableItem, Flt64>, Try> verify) {
        Master = master;
        Sub = sub;
        Bindings = new ReadOnlyDictionary<string, IVariableItem>(
            new Dictionary<string, IVariableItem>(bindings, StringComparer.Ordinal));
        Theta = theta;
        Variables = Array.AsReadOnly(variables.ToArray());
        FeasibilityOnly = feasibilityOnly;
        Verify = verify;
    }

    /// <summary>主问题 / Master problem.</summary>
    public LinearMetaModel<Flt64> Master { get; }
    /// <summary>连续子问题 / Continuous subproblem.</summary>
    public LinearMetaModel<Flt64> Sub { get; }
    /// <summary>稳定 ID 到共享变量 / Stable IDs to shared variables.</summary>
    public IReadOnlyDictionary<string, IVariableItem> Bindings { get; }
    /// <summary>追索目标投影 / Recourse projection variable.</summary>
    public IVariableItem Theta { get; }
    /// <summary>完整来源变量顺序 / Complete source-variable order.</summary>
    public IReadOnlyList<IVariableItem> Variables { get; }
    /// <summary>子问题是否只检查可行性 / Whether the subproblem is feasibility-only.</summary>
    public bool FeasibilityOnly { get; }
    /// <summary>合并解完整复核 / Full merged-solution verification.</summary>
    public Func<IReadOnlyDictionary<IVariableItem, Flt64>, Try> Verify { get; }

    /// <summary>是否已释放 / Whether the models have been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        Master.Dispose();
        Sub.Dispose();
    }
}

/// <summary>
/// 一轮 Benders 轨迹。
/// One Benders iteration snapshot.
/// </summary>
/// <param name="LowerBound">全局下界 / Global lower bound.</param>
/// <param name="UpperBound">已验证可行上界 / Verified feasible upper bound.</param>
/// <param name="Cuts">新增割数 / Newly added cuts.</param>
public sealed record LinearBendersIteration(
    double LowerBound,
    double? UpperBound,
    int Cuts);

/// <summary>
/// 已复核的合并解及真实终态。
/// Verified merged solution and actual terminal state.
/// </summary>
public sealed record LinearBendersResult {
    /// <summary>
    /// 创建 Benders 结果。
    /// Create a Benders result.
    /// </summary>
    /// <param name="values">按变量身份合并的值 / Values merged by variable identity.</param>
    /// <param name="objective">完整归一化目标 / Complete normalized objective.</param>
    /// <param name="gap">全局上下界间隙 / Global bound gap.</param>
    /// <param name="iterations">迭代轨迹 / Iteration trace.</param>
    /// <param name="report">统一求解报告 / Unified solve report.</param>
    /// <param name="terminationDetail">终止细节 / Termination detail.</param>
    public LinearBendersResult(
        IReadOnlyDictionary<IVariableItem, Flt64> values,
        Flt64 objective,
        double gap,
        IReadOnlyList<LinearBendersIteration> iterations,
        SolveReport<Flt64> report,
        string terminationDetail) {
        Values = new ReadOnlyDictionary<IVariableItem, Flt64>(
            new Dictionary<IVariableItem, Flt64>(values));
        Objective = objective;
        Gap = gap;
        Iterations = Array.AsReadOnly(iterations.ToArray());
        Report = report;
        TerminationDetail = terminationDetail;
    }

    /// <summary>按变量身份合并的值 / Values merged by variable identity.</summary>
    public IReadOnlyDictionary<IVariableItem, Flt64> Values { get; }
    /// <summary>完整归一化目标 / Complete normalized objective.</summary>
    public Flt64 Objective { get; }
    /// <summary>全局上下界间隙 / Global bound gap.</summary>
    public double Gap { get; }
    /// <summary>迭代轨迹 / Iteration trace.</summary>
    public IReadOnlyList<LinearBendersIteration> Iterations { get; }
    /// <summary>统一求解报告 / Unified solve report.</summary>
    public SolveReport<Flt64> Report { get; }
    /// <summary>终止细节 / Termination detail.</summary>
    public string TerminationDetail { get; }

    /// <summary>问题结论 / Problem status.</summary>
    public ProblemStatus ProblemStatus => Report.ProblemStatus;
    /// <summary>终止原因 / Termination reason.</summary>
    public TerminationReason TerminationReason => Report.TerminationReason;
    /// <summary>解存在性 / Solution presence.</summary>
    public SolutionPresence SolutionPresence => Report.SolutionPresence;
    /// <summary>证明状态 / Proof status.</summary>
    public ProofStatus ProofStatus => Report.Proof.Status;
}

/// <summary>
/// 将完整线性模型安全划分为主问题和连续追索问题。
/// Safely partition a complete linear model into master and continuous recourse problems.
/// </summary>
public static class LinearBendersPartition {
    private const double VerifyTolerance = 1e-6;

    /// <summary>
    /// 自动划分完整已展开模型；所有目标变量和整数变量默认保留在主问题。
    /// Partition an expanded full model, keeping objective and integer variables in master by default.
    /// </summary>
    /// <param name="source">完整模型 / Full source model.</param>
    /// <param name="stages">扩展显式声明的变量阶段 / Explicit variable stages declared by extensions.</param>
    /// <returns>主子模型与独立完整模型复核器 / Models and an independent full-model verifier.</returns>
    public static async Task<Result<LinearBendersModels, ErrorCode, Error<ErrorCode>>> BuildAsync(
        LinearMetaModel<Flt64> source,
        IReadOnlyDictionary<IVariableItem, LinearBendersStage>? stages = null) {
        if (source is null) {
            return Invalid("来源模型为空 / Source model is null");
        }

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expandedResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(source);
        if (expandedResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> expandedOk) {
            return Propagate<LinearBendersModels, LinearMechanismModel<Flt64>>(expandedResult);
        }

        using LinearMechanismModel<Flt64> expanded = expandedOk.Value;
        LinearMetaModel<Flt64>? master = null;
        LinearMetaModel<Flt64>? sub = null;
        bool keepModels = false;
        try {
            master = new LinearMetaModel<Flt64>(name: $"{source.Name}_master");
            sub = new LinearMetaModel<Flt64>(name: $"{source.Name}_sub");
            IReadOnlyDictionary<IVariableItem, LinearBendersStage> declaredStages =
                stages ?? new Dictionary<IVariableItem, LinearBendersStage>();
            var variables = expanded.Tokens.Tokens
                .Select(token => token.Variable)
                .Distinct()
                .ToList();

            foreach (IVariableItem variable in declaredStages.Keys) {
                if (!variables.Contains(variable)) {
                    return Invalid($"分解阶段引用未知变量 / Stage references an unknown variable: {variable.Name}");
                }
            }
            foreach ((IVariableItem variable, LinearBendersStage stage) in declaredStages) {
                if (variable.TypeKind.IsIntegerType && stage == LinearBendersStage.ContinuousRecourse) {
                    return Invalid($"整数变量不能作为连续追索 / Integer variable cannot be continuous recourse: {variable.Name}");
                }
            }

            (LinearPolynomial<Flt64> objective, Result<Success, ErrorCode, Error<ErrorCode>> objectiveResult) =
                NormalizeObjective(expanded);
            if (!objectiveResult.IsOk) {
                return Propagate<LinearBendersModels, Success>(objectiveResult);
            }

            var objectiveVariables = objective.Monomials
                .Where(monomial => !monomial.Coefficient.Eq(Flt64.Zero))
                .Select(monomial => monomial.Symbol)
                .OfType<IVariableItem>()
                .ToHashSet();
            var decisions = variables
                .Where(variable =>
                    variable.TypeKind.IsIntegerType ||
                    declaredStages.TryGetValue(variable, out LinearBendersStage stage) &&
                    stage == LinearBendersStage.Master ||
                    objectiveVariables.Contains(variable) &&
                    (!declaredStages.TryGetValue(variable, out LinearBendersStage objectiveStage) ||
                        objectiveStage != LinearBendersStage.ContinuousRecourse))
                .ToHashSet();
            var masterTerms = objective.Monomials
                .Where(monomial => monomial.Symbol is IVariableItem variable && decisions.Contains(variable))
                .ToList();
            var recourseTerms = objective.Monomials
                .Where(monomial => monomial.Symbol is IVariableItem variable && !decisions.Contains(variable))
                .ToList();
            var masterObjective = new LinearPolynomial<Flt64>(masterTerms, objective.Constant);
            var recourseObjective = new LinearPolynomial<Flt64>(recourseTerms, Flt64.Zero);
            bool feasibilityOnly = recourseTerms.All(monomial => monomial.Coefficient.Eq(Flt64.Zero));

            Flt64 recourseLowerBound = Flt64.Zero;
            foreach (LinearMonomial<Flt64> term in recourseTerms) {
                if (term.Coefficient.Eq(Flt64.Zero) || term.Symbol is not IVariableItem variable) {
                    continue;
                }
                Flt64? bound = term.Coefficient > Flt64.Zero
                    ? variable.LowerBound?.Value.UnwrapOrNull()
                    : variable.UpperBound?.Value.UnwrapOrNull();
                if (bound is null || !IsFinite(bound.Value)) {
                    return Invalid($"连续追索目标缺少可证明的有限下界 / Continuous recourse objective has no proven finite lower bound: {variable.Name}");
                }
                recourseLowerBound += term.Coefficient * bound.Value;
            }
            if (!IsFinite(recourseLowerBound)) {
                return Invalid("连续追索下界非有限值 / Non-finite recourse lower bound");
            }

            foreach (IVariableItem variable in variables) {
                Result<Success, ErrorCode, Error<ErrorCode>> subAdd = sub.Add(variable);
                if (!subAdd.IsOk) {
                    return Propagate<LinearBendersModels, Success>(subAdd);
                }
                if (decisions.Contains(variable)) {
                    Result<Success, ErrorCode, Error<ErrorCode>> masterAdd = master.Add(variable);
                    if (!masterAdd.IsOk) {
                        return Propagate<LinearBendersModels, Success>(masterAdd);
                    }
                }
                if (source.Tokens.Find(variable) is null) {
                    Result<Success, ErrorCode, Error<ErrorCode>> sourceAdd = source.Add(variable);
                    if (!sourceAdd.IsOk) {
                        return Propagate<LinearBendersModels, Success>(sourceAdd);
                    }
                }
            }

            var rows = new List<(LinearInequality<Flt64> Row, string Name)>();
            foreach (IConstraint<Flt64, LinearCategory> constraint in
                ((IMechanismModel<Flt64>)expanded).Constraints) {
                Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> rowResult =
                    ToInequality(constraint);
                if (!rowResult.IsOk) {
                    return Propagate<LinearBendersModels, LinearInequality<Flt64>>(rowResult);
                }
                LinearInequality<Flt64> row = rowResult.Value;
                rows.Add((row, constraint.Name));
                bool masterOnly = constraint.Lhs.All(cell =>
                    cell is ILinearCell<Flt64> linear &&
                    decisions.Contains(linear.Token.Variable));
                LinearMetaModel<Flt64> target = masterOnly ? master : sub;
                Result<Success, ErrorCode, Error<ErrorCode>> rowAdd = target.AddConstraint(
                    relation: row,
                    group: null,
                    lazy: constraint.Lazy,
                    name: constraint.Name
                );
                if (!rowAdd.IsOk) {
                    return Propagate<LinearBendersModels, Success>(rowAdd);
                }
            }

            foreach (IVariableItem variable in variables.Where(variable => !decisions.Contains(variable))) {
                Flt64? lower = variable.LowerBound?.Value.UnwrapOrNull();
                if (lower is { } lowerValue && IsFinite(lowerValue)) {
                    Result<Success, ErrorCode, Error<ErrorCode>> lowerAdd = sub.AddConstraint(
                        relation: new LinearPolynomial<Flt64>(
                            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
                            Flt64.Zero
                        ).Ge(lowerValue),
                        group: null,
                        name: $"bound_lb_{variable.Key.Identifier}_{variable.Key.Index}"
                    );
                    if (!lowerAdd.IsOk) {
                        return Propagate<LinearBendersModels, Success>(lowerAdd);
                    }
                }
                Flt64? upper = variable.UpperBound?.Value.UnwrapOrNull();
                if (upper is { } upperValue && IsFinite(upperValue)) {
                    Result<Success, ErrorCode, Error<ErrorCode>> upperAdd = sub.AddConstraint(
                        relation: new LinearPolynomial<Flt64>(
                            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
                            Flt64.Zero
                        ).Le(upperValue),
                        group: null,
                        name: $"bound_ub_{variable.Key.Identifier}_{variable.Key.Index}"
                    );
                    if (!upperAdd.IsOk) {
                        return Propagate<LinearBendersModels, Success>(upperAdd);
                    }
                }
            }

            var theta = new RealVar("benders_recourse_projection");
            bool rangeSet = feasibilityOnly
                ? theta.Range.Eq(Flt64.Zero)
                : theta.Range.Geq(recourseLowerBound);
            if (!rangeSet) {
                return Invalid("无法设置追索投影变量范围 / Cannot set recourse projection range");
            }
            Result<Success, ErrorCode, Error<ErrorCode>> thetaAdd = master.Add(theta);
            if (!thetaAdd.IsOk) {
                return Propagate<LinearBendersModels, Success>(thetaAdd);
            }
            LinearPolynomial<Flt64> completeMasterObjective = feasibilityOnly
                ? masterObjective
                : AddPolynomial(masterObjective, new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, theta) },
                    Flt64.Zero
                ));
            Result<Success, ErrorCode, Error<ErrorCode>> masterObjectiveAdd = master.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: completeMasterObjective,
                name: "complete_objective",
                displayName: null
            );
            if (!masterObjectiveAdd.IsOk) {
                return Propagate<LinearBendersModels, Success>(masterObjectiveAdd);
            }
            Result<Success, ErrorCode, Error<ErrorCode>> recourseObjectiveAdd = sub.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: recourseObjective,
                name: "recourse",
                displayName: null
            );
            if (!recourseObjectiveAdd.IsOk) {
                return Propagate<LinearBendersModels, Success>(recourseObjectiveAdd);
            }

            var bindings = decisions.ToDictionary(
                variable => $"{variable.Key.Identifier}:{variable.Key.Index}",
                variable => variable,
                StringComparer.Ordinal
            );
            Try Verify(IReadOnlyDictionary<IVariableItem, Flt64> values) =>
                VerifySolution(values, variables, rows);

            var models = new LinearBendersModels(
                master: master,
                sub: sub,
                bindings: bindings,
                theta: theta,
                variables: variables,
                feasibilityOnly: feasibilityOnly,
                verify: Verify
            );
            keepModels = true;
            return Results.Ok(models);
        }
        finally {
            if (!keepModels) {
                master?.Dispose();
                sub?.Dispose();
            }
        }
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
                            "目标包含非变量符号 / Objective contains a non-variable symbol"
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

    private static Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> ToInequality(
        IConstraint<Flt64, LinearCategory> constraint) {
        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (ICell<Flt64> cell in constraint.Lhs) {
            if (cell is not ILinearCell<Flt64> linear) {
                return Results.Failed<LinearInequality<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    "线性约束包含非线性格 / Linear constraint contains a nonlinear cell"
                ));
            }
            monomials.Add(new LinearMonomial<Flt64>(linear.Coefficient, linear.Token.Variable));
        }
        var lhs = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), constraint.Rhs);
        return Results.Ok(constraint.Sign.ToComparison() switch {
            Comparison.LE => lhs.Le(rhs),
            Comparison.EQ => lhs.Eq(rhs),
            Comparison.GE => lhs.Ge(rhs),
            _ => lhs.Le(rhs),
        });
    }

    private static Try VerifySolution(
        IReadOnlyDictionary<IVariableItem, Flt64> values,
        IReadOnlyList<IVariableItem> variables,
        IReadOnlyList<(LinearInequality<Flt64> Row, string Name)> rows) {
        foreach (IVariableItem variable in variables) {
            if (!values.TryGetValue(variable, out Flt64 value) || !IsFinite(value)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound,
                    $"合并解缺少有限变量值 / Merged solution is missing a finite value: {variable.Name}"
                ));
            }
            Flt64? lower = variable.LowerBound?.Value.UnwrapOrNull();
            Flt64? upper = variable.UpperBound?.Value.UnwrapOrNull();
            double number = value.ToDouble();
            if (lower is { } lowerValue && number < lowerValue.ToDouble() - VerifyTolerance ||
                upper is { } upperValue && number > upperValue.ToDouble() + VerifyTolerance) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"合并解违反变量范围 / Merged solution violates bounds: {variable.Name}"
                ));
            }
            if (variable.TypeKind.IsIntegerType &&
                System.Math.Abs(number - System.Math.Round(number)) > VerifyTolerance) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"合并解违反整数性 / Merged solution violates integrality: {variable.Name}"
                ));
            }
        }

        foreach ((LinearInequality<Flt64> row, string name) in rows) {
            if (!TryEvaluate(row.Lhs, values, out Flt64 lhs) ||
                !TryEvaluate(row.Rhs, values, out Flt64 rhs)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound,
                    $"合并解无法求值约束 / Merged solution cannot evaluate constraint: {name}"
                ));
            }
            double difference = (lhs - rhs).ToDouble();
            bool valid = row.Comparison switch {
                Comparison.LE => difference <= VerifyTolerance,
                Comparison.GE => difference >= -VerifyTolerance,
                Comparison.EQ => System.Math.Abs(difference) <= VerifyTolerance,
                _ => false,
            };
            if (!valid) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"合并解违反约束 / Merged solution violates constraint: {name}"
                ));
            }
        }
        return Results.OkInstance;
    }

    private static bool TryEvaluate(
        LinearPolynomial<Flt64> polynomial,
        IReadOnlyDictionary<IVariableItem, Flt64> values,
        out Flt64 value) {
        value = polynomial.Constant;
        foreach (LinearMonomial<Flt64> term in polynomial.Monomials) {
            if (term.Symbol is not IVariableItem variable ||
                !values.TryGetValue(variable, out Flt64 variableValue)) {
                return false;
            }
            value += term.Coefficient * variableValue;
        }
        return IsFinite(value);
    }

    private static LinearPolynomial<Flt64> AddPolynomial(
        LinearPolynomial<Flt64> left,
        LinearPolynomial<Flt64> right) {
        return new LinearPolynomial<Flt64>(
            left.Monomials.Concat(right.Monomials).ToList(),
            left.Constant + right.Constant
        );
    }

    private static bool IsFinite(Flt64 value) => double.IsFinite(value.ToDouble());

    private static Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> Invalid(string reason) =>
        Results.Failed<LinearBendersModels>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            $"Benders 划分失败 / Benders partition failed: {reason}"
        ));

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "Benders 划分结果无效 / Invalid Benders partition result"
            )),
        };
    }
}
