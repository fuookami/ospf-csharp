#nullable enable

using Gurobi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 原生 IIS 分析器（二次模型）/ Gurobi native IIS analyzer for quadratic models.
/// </summary>
/// <remarks>
/// 补完了 `GurobiInfeasibilityAnalyzers.kt`（518 行）的最后一个类**，Gurobi 三个基线分析器到齐。
///
/// **二次版本不是线性版本的复制品**：约束是 `GRBQConstr`，成员判定读 `IISQConstr` 而不是 `IISConstr`。
/// 抄线性那份会在**每一行都不算成员**的地方悄悄返回空 IIS——看起来像"没有不可行子系统"。
///
/// **completes the last class of `GurobiInfeasibilityAnalyzers.kt` (518 lines)**; all three baseline Gurobi analyzers are now present. **The quadratic version is not a copy of
/// the linear one**: constraints are `GRBQConstr` and membership reads `IISQConstr`, not `IISConstr`. Copying the linear version would silently return an empty IIS **because
/// no row ever counts as a member**, which reads like "there is no infeasible subsystem".
/// </remarks>
internal sealed class GurobiQuadraticNativeIisInfeasibilityAnalyzer : INativeIisAnalyzer<IQuadraticTetradModelView> {
    private readonly GurobiQuadraticSolver _solver;
    private readonly IisConfig _iisConfig;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.NativeIis,
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Qp, SolverModelType.Qcp },
        Exact: true);

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.NativeIis;

    /// <summary>创建 Gurobi 二次原生 IIS 分析器 / Create a Gurobi quadratic native IIS analyzer.</summary>
    /// <param name="solver">发起诊断的 Gurobi 二次求解器 / Gurobi quadratic solver that owns the diagnostic run.</param>
    /// <param name="iisConfig">诊断配置 / Diagnostic configuration.</param>
    internal GurobiQuadraticNativeIisInfeasibilityAnalyzer(GurobiQuadraticSolver solver, IisConfig iisConfig) {
        _solver = solver;
        _iisConfig = iisConfig;
    }

    /// <inheritdoc/>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        IQuadraticTetradModelView model,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(model);

        var stopwatch = Stopwatch.StartNew();
        using var run = new GurobiQuadraticDiagnosticRun(_solver, _iisConfig);
        return await run.AnalyzeIisAsync(model, stopwatch, cancellationToken);
    }
}

/// <summary>
/// 一次隔离的 Gurobi 二次原生 IIS 诊断 / One isolated Gurobi quadratic native IIS diagnostic.
/// </summary>
internal sealed class GurobiQuadraticDiagnosticRun : GurobiSolver {
    private readonly GurobiQuadraticSolver _owner;
    private readonly IisConfig _iisConfig;

    internal GurobiQuadraticDiagnosticRun(GurobiQuadraticSolver owner, IisConfig iisConfig) {
        _owner = owner;
        _iisConfig = iisConfig;
    }

    internal async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeIisAsync(
        IQuadraticTetradModelView model,
        Stopwatch stopwatch,
        CancellationToken cancellationToken) {
        Result<Success, ErrorCode, Error<ErrorCode>> init = await InitDiagnosticAsync(model.Name);
        if (init is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<InfeasibilityEvidence>(init);
        }
        try {
            var variables = new List<GRBVar>(model.Variables.Count);
            var constraints = new List<GRBQConstr>(model.Constraints.Size);
            Result<Success, ErrorCode, Error<ErrorCode>> dumped = Dump(model, variables, constraints);
            if (dumped is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<InfeasibilityEvidence>(dumped);
            }
            return SolveIis(model, variables, constraints, stopwatch, cancellationToken);
        } catch (GRBException error) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"Gurobi 二次原生 IIS 诊断失败：{error.Message} / Gurobi quadratic native IIS diagnosis failed: {error.Message}"));
        } catch (Exception error) when (error is not OutOfMemoryException) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"Gurobi 二次原生 IIS 诊断失败：{error.Message} / Gurobi quadratic native IIS diagnosis failed: {error.Message}"));
        }
    }

    private async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitDiagnosticAsync(string name) {
        var connection = _owner.Config.BackendConfiguration as GurobiSolverConfig;
        if (connection?.Server is { } server
            && connection.Password is { } password
            && connection.ConnectionTime is { } connectionTime) {
            return await InitAsync(server, password, connectionTime, $"{name}:quadratic-iis-diagnostic");
        }
        return await InitAsync($"{name}:quadratic-iis-diagnostic");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> Dump(
        IQuadraticTetradModelView model,
        List<GRBVar> variables,
        List<GRBQConstr> constraints) {
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (identity is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return identity;
        }
        try {
            foreach (ModelViewVariable variable in model.Variables) {
                double lower = variable.LowerBound.ToDouble();
                double upper = variable.UpperBound.ToDouble();
                if (lower <= -1e100) {
                    lower = -GRB.INFINITY;
                }
                if (upper >= 1e100) {
                    upper = GRB.INFINITY;
                }
                variables.Add(GrbModel.AddVar(lower, upper, 0.0, GRB.CONTINUOUS, $"diagnostic-var-{variable.Index}"));
            }

            ModelConstraint<QuadraticConstraintCell> rows = model.Constraints;
            for (int row = 0; row < rows.Size; row++) {
                var lhs = new GRBQuadExpr();
                foreach (QuadraticConstraintCell cell in rows.Lhs[row]) {
                    // `Col2 < 0` 是线性项的哨兵值，与既有的二次建模代码保持一致。
                    // `Col2 < 0` is the sentinel for a linear term, matching the existing quadratic modelling code.
                    if (cell.Col2 < 0) {
                        lhs.AddTerm(cell.Coefficient.ToDouble(), variables[cell.Col1]);
                    } else {
                        lhs.AddTerm(cell.Coefficient.ToDouble(), variables[cell.Col1], variables[cell.Col2]);
                    }
                }
                constraints.Add(GrbModel.AddQConstr(
                    lhs,
                    GurobiConstraintSignExtensions.From(rows.Signs[row]).ToGurobiChar(),
                    rows.Rhs[row].ToDouble(),
                    $"diagnostic-row-{row}"));
            }
            GrbModel.Update();
            GrbModel.Set(GRB.DoubleParam.TimeLimit, _iisConfig.Time.TotalSeconds);
            GrbModel.Set(GRB.IntParam.Threads, (int)_iisConfig.ThreadCount);
            // **`DualReductions` 关掉、`InfUnbdInfo` 不开**：这里只求 IIS，不读 Farkas 证书。
            // **`DualReductions` off and `InfUnbdInfo` left alone**: this only computes an IIS and reads no Farkas certificate.
            GrbModel.Set(GRB.IntParam.DualReductions, 0);
            return Results.OkInstance;
        } catch (GRBException error) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, error.Message));
        }
    }

    private Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> SolveIis(
        IQuadraticTetradModelView model,
        List<GRBVar> variables,
        List<GRBQConstr> constraints,
        Stopwatch stopwatch,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "Gurobi 二次原生 IIS 诊断已取消 / The Gurobi quadratic native IIS diagnosis was cancelled"));
        }
        GrbModel.Optimize();
        if ((int)GrbModel.Get(GRB.IntAttr.Status) != GRB.Status.INFEASIBLE) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.ORModelInfeasible,
                "Gurobi 原生 IIS 要求模型状态为不可行 / Gurobi native IIS requires an infeasible model"));
        }
        GrbModel.ComputeIIS();
        bool minimal = (int)GrbModel.Get(GRB.IntAttr.IISMinimal) != 0;

        var rowIds = new HashSet<ConstraintId>();
        var members = new HashSet<InfeasibilityMember>();
        for (int row = 0; row < constraints.Count; row++) {
            // **二次成员读 `IISQConstr`**：读 `IISConstr` 会永远是 0，于是每一行都不算成员。
            // **Quadratic membership reads `IISQConstr`**: reading `IISConstr` is always 0, so no row ever counts as a member.
            if ((int)constraints[row].Get(GRB.IntAttr.IISQConstr) != 0) {
                ConstraintId id = model.DiagnosticConstraintId(row);
                rowIds.Add(id);
                members.Add(new InfeasibilityMember.ConstraintMember(id));
            }
        }

        var bounds = new HashSet<VariableBoundRef>();
        for (int column = 0; column < variables.Count; column++) {
            ModelViewVariable variable = model.Variables[column];
            if ((int)variables[column].Get(GRB.IntAttr.IISLB) != 0) {
                bounds.Add(new VariableBoundRef(variable.DiagnosticVariableId(), BoundSide.Lower));
            }
            if ((int)variables[column].Get(GRB.IntAttr.IISUB) != 0) {
                bounds.Add(new VariableBoundRef(variable.DiagnosticVariableId(), BoundSide.Upper));
            }
        }
        foreach (VariableBoundRef bound in bounds) {
            members.Add(new InfeasibilityMember.VariableBoundMember(bound));
        }
        if (members.Count == 0) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Gurobi 二次原生 IIS 未归约出任何成员 / Gurobi quadratic native IIS reduced to no members"));
        }

        stopwatch.Stop();
        return Results.Ok(new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.NativeIis,
            Exactness = minimal ? EvidenceExactness.Irreducible : EvidenceExactness.Heuristic,
            Completeness = EvidenceCompleteness.Complete,
            ConstraintIds = rowIds,
            VariableBoundIds = new HashSet<VariableId>(bounds.Select(bound => bound.VariableId)),
            Elapsed = stopwatch.Elapsed,
            Validity = EvidenceValidity.Verified,
            Minimality = minimal ? EvidenceMinimality.Irreducible : EvidenceMinimality.Partial,
            VariableBoundRefs = bounds,
            Members = members,
            Reference = "gurobi.computeIIS",
        });
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) => result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "Gurobi 二次原生 IIS 诊断返回了无效状态 / The Gurobi quadratic native IIS diagnosis returned an invalid state")),
        };
}
