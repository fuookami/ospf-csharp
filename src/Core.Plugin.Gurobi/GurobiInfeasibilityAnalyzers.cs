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
/// 基于 Gurobi Farkas 对偶的连续线性分析器。
/// Continuous-LP Farkas analyzer backed by Gurobi's Farkas dual.
/// </summary>
/// <remarks>
/// 与 `ScipFarkasInfeasibilityAnalyzer` 同构：诊断在自己的 Gurobi 实例上重放模型，整数列直接拒绝，
/// 因为 LP 证书不能证明混合整数模型不可行。
///
/// 补的是（518 行）里的 Farkas 那一条**。
/// 个文件有三个类：`GurobiNativeIISAnalyzer`（纯 IIS）、`GurobiFarkasAnalyzer`（这里）、
/// `GurobiQuadraticNativeIISAnalyzer`（二次 IIS）。**两次 IIS 都需要 `ComputeIIS` 的隔离实例，
/// 这里没实现，也没写空跑版本。**
///
/// Mirrors `ScipFarkasInfeasibilityAnalyzer`: the diagnosis replays the model on its own Gurobi instance and rejects integer columns, because an LP certificate cannot prove a
/// mixed-integer model infeasible. **The Farkas member is implemented here**. It has three classes — a native IIS one,
/// this Farkas one, and a quadratic IIS one. **Both IIS classes need an isolated `ComputeIIS` instance; they were neither implemented nor stubbed.**
/// </remarks>
internal sealed class GurobiFarkasInfeasibilityAnalyzer : IFarkasInfeasibilityAnalyzer<ILinearTriadModelView> {
    /// <summary>Farkas 成分非零判定容差 / Tolerance for a nonzero Farkas component.</summary>
    internal const double FarkasTolerance = 1.0e-9;

    private readonly GurobiLinearSolver _solver;
    private readonly IisConfig _iisConfig;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.Farkas,
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp },
        Exact: true);

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.Farkas;

    /// <summary>创建 Gurobi Farkas 分析器 / Create a Gurobi Farkas analyzer.</summary>
    /// <param name="solver">发起诊断的 Gurobi 线性求解器 / Gurobi linear solver that owns the diagnostic run.</param>
    /// <param name="iisConfig">诊断配置 / Diagnostic configuration.</param>
    internal GurobiFarkasInfeasibilityAnalyzer(GurobiLinearSolver solver, IisConfig iisConfig) {
        _solver = solver;
        _iisConfig = iisConfig;
    }

    /// <inheritdoc/>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(model);

        // **整数列在花任何后端开销之前就拒**：LP 证书证明不了整数模型不可行，
        // 而一个"跑了一遍才发现"的拒绝会让调用方以为后端尝试过。
        //
        // **Integer columns are refused before any backend cost**: an LP certificate cannot prove an integer model infeasible, and a refusal that only comes after a full solve
        // would leave the caller thinking the backend tried.
        if (model.Variables.Any(variable => variable.Type.IsIntegerType)) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "Gurobi Farkas 证书仅适用于连续线性模型 / Gurobi Farkas certificates require a continuous linear model"));
        }

        var stopwatch = Stopwatch.StartNew();
        using var run = new GurobiLinearDiagnosticRun(_solver, _iisConfig);
        return await run.AnalyzeAsync(model, stopwatch, cancellationToken);
    }
}

/// <summary>
/// 一次隔离的 Gurobi Farkas 诊断 / One isolated Gurobi Farkas diagnostic.
/// </summary>
/// <remarks>
/// 诊断必须**另起实例**：主求解运行的预处理和分支定界会改变行序与对偶值，
/// 而 Farkas 乘子是逐行的，**行序一旦变了，证书指向的就是别的约束。**
///
/// The diagnosis must **run its own instance**: presolving and branch-and-bound of the primary run would change row order and dual values, and Farkas multipliers are per-row —
/// **once the row order shifts, the certificate points at different constraints.**
/// </remarks>
internal sealed class GurobiLinearDiagnosticRun : GurobiSolver {
    private readonly GurobiLinearSolver _owner;
    private readonly IisConfig _iisConfig;

    internal GurobiLinearDiagnosticRun(GurobiLinearSolver owner, IisConfig iisConfig) {
        _owner = owner;
        _iisConfig = iisConfig;
    }

    internal Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ILinearTriadModelView model,
        Stopwatch stopwatch,
        CancellationToken cancellationToken) => RunAsync(model, stopwatch, cancellationToken, nativeIis: false);

    internal Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeIisAsync(
        ILinearTriadModelView model,
        Stopwatch stopwatch,
        CancellationToken cancellationToken) => RunAsync(model, stopwatch, cancellationToken, nativeIis: true);

    private async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> RunAsync(
        ILinearTriadModelView model,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        bool nativeIis) {
        Result<Success, ErrorCode, Error<ErrorCode>> init = await InitDiagnosticAsync(model.Name);
        if (init is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<InfeasibilityEvidence, Success>(init);
        }
        try {
            var variables = new List<GRBVar>(model.Variables.Count);
            var constraints = new List<GRBConstr>(model.Constraints.Size);
            Result<Success, ErrorCode, Error<ErrorCode>> dumped = Dump(model, variables, constraints);
            if (dumped is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<InfeasibilityEvidence, Success>(dumped);
            }
            return nativeIis
                ? SolveIis(model, variables, stopwatch)
                : Solve(model, variables, constraints, stopwatch, cancellationToken);
        } catch (GRBException error) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"Gurobi Farkas 诊断失败：{error.Message} / Gurobi Farkas diagnosis failed: {error.Message}"));
        } catch (Exception error) when (error is not OutOfMemoryException) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"Gurobi Farkas 诊断失败：{error.Message} / Gurobi Farkas diagnosis failed: {error.Message}"));
        }
    }

    private async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitDiagnosticAsync(string name) {
        // 诊断沿用调用方配置的连接信息，但**不继承求解器实例**：证书必须来自一次干净的 LP。
        // The diagnosis reuses the caller's connection settings but **does not inherit the solver instance**: the certificate must come from a clean LP.
        var connection = _owner.Config.BackendConfiguration as GurobiSolverConfig;
        if (connection?.Server is { } server
            && connection.Password is { } password
            && connection.ConnectionTime is { } connectionTime) {
            return await InitAsync(server, password, connectionTime, $"{name}:farkas-diagnostic");
        }
        return await InitAsync($"{name}:farkas-diagnostic");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> Dump(
        ILinearTriadModelView model,
        List<GRBVar> variables,
        List<GRBConstr> constraints) {
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
                variables.Add(GrbModel.AddVar(
                    lower,
                    upper,
                    0.0,
                    GRB.CONTINUOUS,
                    $"diagnostic-var-{variable.Index}"));
            }

            ModelConstraint<LinearConstraintCell> rows = model.Constraints;
            for (int row = 0; row < rows.Size; row++) {
                var lhs = new GRBLinExpr();
                foreach (LinearConstraintCell cell in rows.Lhs[row]) {
                    lhs.AddTerm(cell.Coefficient.ToDouble(), variables[cell.ColIndex]);
                }
                constraints.Add(GrbModel.AddConstr(
                    lhs,
                    GurobiConstraintSignExtensions.From(rows.Signs[row]).ToGurobiChar(),
                    rows.Rhs[row].ToDouble(),
                    $"diagnostic-row-{row}"));
            }
            GrbModel.Update();
            GrbModel.Set(GRB.DoubleParam.TimeLimit, _iisConfig.Time.TotalSeconds);
            GrbModel.Set(GRB.IntParam.Threads, (int)_iisConfig.ThreadCount);
            // **关掉对偶归约并打开无界/不可行信息**：没有这两项，Gurobi 可能给出
            // "infeasible or unbounded" 这种无法支撑证书的结论。
            //
            // **Dual reductions off and infeasibility info on**: without these Gurobi may return "infeasible or unbounded", which cannot support a certificate.
            GrbModel.Set(GRB.IntParam.DualReductions, 0);
            GrbModel.Set(GRB.IntParam.InfUnbdInfo, 1);
            return Results.OkInstance;
        } catch (GRBException error) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, error.Message));
        }
    }

    private Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> Solve(
        ILinearTriadModelView model,
        List<GRBVar> variables,
        List<GRBConstr> constraints,
        Stopwatch stopwatch,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "Gurobi Farkas 诊断已取消 / The Gurobi Farkas diagnosis was cancelled"));
        }
        GrbModel.Optimize();
        if ((int)GrbModel.Get(GRB.IntAttr.Status) != GRB.Status.INFEASIBLE) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.ORModelInfeasible,
                "Gurobi Farkas 要求模型状态为不可行 / Gurobi Farkas requires an infeasible model"));
        }
        // **`FarkasProof` 是"证书确实存在"的信号**：它为零时逐行对偶值仍然读得出来，
        // 但那些数字不构成证书，据此报一条"已证明不可行"是编造。
        //
        // **`FarkasProof` is the "a certificate exists" signal**: at zero the per-row duals are still readable, but they do not form a certificate, and reporting "proven
        // infeasible" from them would be fabrication.
        if (GrbModel.Get(GRB.DoubleAttr.FarkasProof) <= GurobiFarkasInfeasibilityAnalyzer.FarkasTolerance) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Gurobi 未返回有效 FarkasProof / Gurobi did not return a valid FarkasProof"));
        }

        var rowIds = new HashSet<ConstraintId>();
        var members = new HashSet<InfeasibilityMember>();
        double[] multipliers = new double[constraints.Count];
        for (int row = 0; row < constraints.Count; row++) {
            double multiplier = constraints[row].Get(GRB.DoubleAttr.FarkasDual);
            if (!double.IsFinite(multiplier)) {
                return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    $"第 {row} 行 Farkas 乘子无效 / The Farkas multiplier for row {row} is invalid"));
            }
            multipliers[row] = multiplier;
            if (global::System.Math.Abs(multiplier) > GurobiFarkasInfeasibilityAnalyzer.FarkasTolerance) {
                ConstraintId id = model.DiagnosticConstraintId(row);
                rowIds.Add(id);
                members.Add(new InfeasibilityMember.ConstraintMember(id));
            }
        }

        Result<HashSet<VariableBoundRef>, ErrorCode, Error<ErrorCode>> boundsResult =
            SelectedFarkasBounds(model, multipliers);
        if (boundsResult is not Ok<HashSet<VariableBoundRef>, ErrorCode, Error<ErrorCode>> bounds) {
            return Propagate<InfeasibilityEvidence, HashSet<VariableBoundRef>>(boundsResult);
        }
        foreach (VariableBoundRef bound in bounds.Value) {
            members.Add(new InfeasibilityMember.VariableBoundMember(bound));
        }
        if (members.Count == 0) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Gurobi 未返回非零 Farkas 成分 / Gurobi returned no nonzero Farkas component"));
        }

        stopwatch.Stop();
        return Results.Ok(new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.Farkas,
            Exactness = EvidenceExactness.Exact,
            Completeness = EvidenceCompleteness.Complete,
            ConstraintIds = rowIds,
            VariableBoundIds = new HashSet<VariableId>(bounds.Value.Select(bound => bound.VariableId)),
            Elapsed = stopwatch.Elapsed,
            Validity = EvidenceValidity.Verified,
            Minimality = EvidenceMinimality.NotChecked,
            VariableBoundRefs = bounds.Value,
            Members = members,
            Reference = "gurobi.farkas",
        });
    }

    /// <summary>
    /// 原生 IIS：让 Gurobi 自己归约出不可行子系统 / Native IIS: let Gurobi reduce the infeasible subsystem itself.
    /// </summary>
    /// <remarks>
    /// **`IISMinimal` 决定 `Exactness` 与 `Minimality`，两者必须同源**：一个标了 `Irreducible` 却给了 `Heuristic` 精确度的证据，
    /// 会让下游既以为它最小、又以为它只是启发式。
    ///
    /// **`IISMinimal` drives both `Exactness` and `Minimality`, and they must agree**: an evidence marked `Irreducible` while claiming `Heuristic` exactness would leave
    /// downstream believing it is both minimal and merely a heuristic.
    /// </remarks>
    private Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> SolveIis(
        ILinearTriadModelView model,
        List<GRBVar> variables,
        Stopwatch stopwatch) {
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
        var constraints = new List<GRBConstr>(GrbModel.GetConstrs());
        for (int row = 0; row < constraints.Count; row++) {
            if ((int)constraints[row].Get(GRB.IntAttr.IISConstr) != 0) {
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
        // **IIS 为空就是失败**：报一条"已证明不可行但没有任何成员"的证据，
        // 下游会把它当成"模型自己不可行，删什么也没用"——**而那正是没归约出来的样子。**
        //
        // **An empty IIS is a failure**: evidence saying "proven infeasible with no members" would be read downstream as "the model is infeasible on its own",
        // **which is exactly what a failed reduction looks like.**
        if (members.Count == 0) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Gurobi 原生 IIS 未归约出任何成员 / Gurobi native IIS reduced to no members"));
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

    private static Result<HashSet<VariableBoundRef>, ErrorCode, Error<ErrorCode>> SelectedFarkasBounds(
        ILinearTriadModelView model,
        double[] multipliers) {
        // 按列聚合证书系数，据此判定不可行性用到了哪一侧的变量界。 / Aggregate the certificate coefficients per column to learn which bound side the proof used.
        double[] aggregated = new double[model.Variables.Count];
        ModelConstraint<LinearConstraintCell> rows = model.Constraints;
        for (int row = 0; row < multipliers.Length; row++) {
            if (global::System.Math.Abs(multipliers[row]) <= GurobiFarkasInfeasibilityAnalyzer.FarkasTolerance) {
                continue;
            }
            foreach (LinearConstraintCell cell in rows.Lhs[row]) {
                aggregated[cell.ColIndex] += multipliers[row] * cell.Coefficient.ToDouble();
            }
        }

        var bounds = new HashSet<VariableBoundRef>();
        for (int column = 0; column < aggregated.Length; column++) {
            ModelViewVariable variable = model.Variables[column];
            if (aggregated[column] > GurobiFarkasInfeasibilityAnalyzer.FarkasTolerance) {
                if (double.IsNegativeInfinity(variable.LowerBound.ToDouble()) || variable.LowerBound.ToDouble() <= -1e100) {
                    return Results.Failed<HashSet<VariableBoundRef>>(new Err<ErrorCode>(
                        ErrorCode.OREngineSolvingException,
                        "Gurobi Farkas 证书需要有限下界但模型变量无下界 / "
                        + "Gurobi Farkas certificate requires a finite lower bound"));
                }
                bounds.Add(new VariableBoundRef(variable.DiagnosticVariableId(), BoundSide.Lower));
            } else if (aggregated[column] < -GurobiFarkasInfeasibilityAnalyzer.FarkasTolerance) {
                if (double.IsPositiveInfinity(variable.UpperBound.ToDouble()) || variable.UpperBound.ToDouble() >= 1e100) {
                    return Results.Failed<HashSet<VariableBoundRef>>(new Err<ErrorCode>(
                        ErrorCode.OREngineSolvingException,
                        "Gurobi Farkas 证书需要有限上界但模型变量无上界 / "
                        + "Gurobi Farkas certificate requires a finite upper bound"));
                }
                bounds.Add(new VariableBoundRef(variable.DiagnosticVariableId(), BoundSide.Upper));
            }
        }
        return Results.Ok(bounds);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result) => result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "Gurobi Farkas 诊断返回了无效状态 / The Gurobi Farkas diagnosis returned an invalid state")),
        };
}

/// <summary>
/// Gurobi 原生 IIS 分析器（线性模型）/ Gurobi native IIS analyzer for linear models.
/// </summary>
/// <remarks>
/// **整数列不拒**：原生 IIS 对 LP 与 MIP 都成立，这正是它与 Farkas 分析器的分工。
/// **Integer columns are not refused here**: a native IIS holds for both LP and MIP, which is exactly how it differs from the Farkas analyzer.
/// </remarks>
internal sealed class GurobiNativeIisInfeasibilityAnalyzer : INativeIisAnalyzer<ILinearTriadModelView> {
    private readonly GurobiLinearSolver _solver;
    private readonly IisConfig _iisConfig;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.NativeIis,
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip },
        Exact: true);

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.NativeIis;

    /// <summary>创建 Gurobi 原生 IIS 分析器 / Create a Gurobi native IIS analyzer.</summary>
    /// <param name="solver">发起诊断的 Gurobi 线性求解器 / Gurobi linear solver that owns the diagnostic run.</param>
    /// <param name="iisConfig">诊断配置 / Diagnostic configuration.</param>
    internal GurobiNativeIisInfeasibilityAnalyzer(GurobiLinearSolver solver, IisConfig iisConfig) {
        _solver = solver;
        _iisConfig = iisConfig;
    }

    /// <inheritdoc/>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(model);

        var stopwatch = Stopwatch.StartNew();
        using var run = new GurobiLinearDiagnosticRun(_solver, _iisConfig);
        return await run.AnalyzeIisAsync(model, stopwatch, cancellationToken);
    }
}
