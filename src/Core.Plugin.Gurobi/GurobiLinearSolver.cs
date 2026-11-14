#nullable enable

using Fuookami.Ospf.Core.Solver.Iis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gurobi;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 线性求解器 / Gurobi linear solver.
/// </summary>
public sealed class GurobiLinearSolver : ILinearSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiLinearSolverCallBack? _callBack;
    private readonly GurobiBackendIdentity _identity;

    /// <summary>
    /// **是否在主求解中捕获 RHS 敏感性范围** / Whether to capture RHS sensitivity ranges during the primary solve.
    /// </summary>
    /// <remarks>
    /// **必须&lt;b&gt;在主求解中**捕获，不能事后重解。** **基线记了实测：对同一个 triad 二次求解时 presolve 配置不再生效，
    /// 读回的 `SARHSLow` 会退化成哨兵值（c1 得到 4 而不是 0）。**
    ///
    /// **打开这个开关会把 presolve 关掉**——**因为 `SARHSLow`／`SARHSUp` 在 presolve 打开时是哨兵而不是区间。**
    /// **代价是求解变慢，所以它是个显式开关而不是默认行为。**
    ///
    /// **Ranges must be captured during the &lt;b&gt;primary&lt;/b&gt; solve, never by re-solving: the baseline measured that a second solve of
    /// the same triad no longer honours the presolve configuration and returns sentinel `SARHSLow` values.** **Turning this on
    /// disables presolve, because `SARHSLow`/`SARHSUp` are sentinels rather than ranges when presolve is on — at the cost of a
    /// slower solve, which is why it is an explicit switch.**
    /// </remarks>
    public bool CaptureSensitivityRanges { get; set; }

    /// <summary>
    /// **上次求解捕获到的 RHS 范围，按约束名（即 `ConstraintId`）索引** /
    /// The RHS ranges captured by the last solve, keyed by constraint name (that is, by `ConstraintId`).
    /// </summary>
    /// <remarks>
    /// **键是&lt;b&gt;行号**，不是约束名。** **实测发现：这条 triad 路径建出来的 `GRBConstr` 名字是空的，
    /// 所以"名字就是 `ConstraintId`"这个假设不成立**——**按名字索引会得到空字典，而它看起来像"没有范围"。**
    /// **行号与报告里 `ConstraintEvaluations` 的顺序一致，所以配对交给上层做。**
    ///
    /// **The key is a &lt;b&gt;row index&lt;/b&gt;, not a constraint name: measurement showed the `GRBConstr`s built on this triad path carry
    /// empty names, so "the name is the `ConstraintId`" does not hold — indexing by name yields an empty dictionary that looks like
    /// "no ranges".** **Row order matches `ConstraintEvaluations` in the report, so the pairing belongs upstairs.**
    /// </remarks>
    public IReadOnlyDictionary<int, (double? Lower, double? Upper)> SensitivityRangesByRowIndex { get; private set; }
        = new Dictionary<int, (double? Lower, double? Upper)>();

    /// <summary>
    /// **上一行为什么没读到区间** / Why the last row yielded no range.
    /// </summary>
    /// <remarks>
    /// **这条不是调试残留，是这套 API 的必需品：** **`SARHSLow`／`SARHSUp` 读失败时只抛一句
    /// "Error at GRBConstr.Get."，而&lt;b&gt;原因可能有四种**——模型是 MIP、没有变量、没有最优基、或有二次项。**
    /// **一句话的异常让这四种长得完全一样，于是"没有区间"会变成一个查不下去的现象。**
    ///
    /// **它记录下来的是行数、变量数、`IsMIP` 与状态**——**B1282 正是靠它发现夹具退化成 `vars=0` 的。**
    ///
    /// **Not debug residue but a necessity of this API: a failed `SARHSLow`/`SARHSUp` read throws only "Error at
    /// GRBConstr.Get." while at least four causes look identical — the model is a MIP, it has no variables, it has no optimal basis,
    /// or it is quadratic.** **Recording row count, variable count, `IsMIP` and status is what let B1282 find that its own fixture had
    /// degenerated to `vars=0`.**
    /// </remarks>
    public string? SensitivityRangeDiagnostic { get; private set; }

    /// <summary>创建 Gurobi 线性求解器 / Create a Gurobi linear solver.</summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">求解回调 / Solver callback.</param>
    public GurobiLinearSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiLinearSolver(
        SolverConfig? config,
        GurobiLinearSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
        _identity = identity;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Gurobi 现在同时提供连续 LP 的 Farkas 分析器与原生 IIS 分析器。**顺序有讲究**：原生 IIS 在前，
    /// 因为它是后端自己归约出来的子系统；Farkas 在后，用于只要一份对偶证书就够的场合。
    /// **归约后的 IIS 模型仍由 Core 的 `LinearIisCompute` 统一物化**，这里只负责给出证据。
    ///
    /// Gurobi now offers both the continuous-LP Farkas analyzer and a native IIS analyzer. **The order matters**: native IIS comes first because it is the subsystem the
    /// backend reduced itself; Farkas follows, for callers that only need a dual certificate. **The reduced IIS model is still materialized by Core's `LinearIisCompute`**;
    /// this list only supplies the evidence.
    /// </remarks>
    public IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(IisConfig config) =>
        new IInfeasibilityAnalyzer<ILinearTriadModelView>[] {
            new GurobiNativeIisInfeasibilityAnalyzer(this, config),
            new GurobiFarkasInfeasibilityAnalyzer(this, config),
        };

    /// <summary>兼容旧求解入口 / Legacy solve entry.</summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) => GurobiReportSupport.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(GurobiReportSupport.Cancelled(
                Config,
                quadratic: false,
                _identity,
                GurobiSolver.NativeVersion));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> shape = model.ValidateIdentity();
        if (!shape.IsOk) {
            return PropagateError<SolveReport<Flt64>>(shape);
        }
        using var impl = new GurobiLinearSolverImpl(
            config: Config,
            callBack: _callBack,
            statusCallBack: solvingStatusCallBack,
            identity: _identity,
            captureSensitivityRanges: CaptureSensitivityRanges,
            publishRanges: (ranges, diagnostic) => {
                SensitivityRangesByRowIndex = ranges;
                SensitivityRangeDiagnostic = diagnostic;
            });
        return await impl.InvokeReportAsync(model, cancellationToken);
    }

    /// <summary>兼容旧 IIS 入口 / Legacy IIS entry.</summary>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await InvokeAsync(
            model: (ILinearTriadModelView)model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        return result switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok => Results.Ok<SolverOutput>(ok.Value),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed => Results.Failed<SolverOutput>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal => new Fatal<SolverOutput, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<SolverOutput>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)),
        };
    }

    /// <summary>兼容旧解池入口 / Legacy solution-pool entry.</summary>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = GurobiReportSupport.ToLegacy(report);
        return legacy switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok => Results.Ok((
                ok.Value,
                report.Value.Solution?.Pool.Select(solution => solution.Values.ToList()).ToList() ?? new List<List<Flt64>>())),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal => new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(GurobiReportSupport.Cancelled(
                Config,
                quadratic: false,
                _identity,
                GurobiSolver.NativeVersion));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> shape = model.ValidateIdentity();
        if (!shape.IsOk) {
            return PropagateError<SolveReport<Flt64>>(shape);
        }
        if (solutionAmount <= 1) {
            return await InvokeReportAsync(
                model: model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken);
        }
        if (solutionAmount > int.MaxValue) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "解池容量超出 Gurobi 整数范围 / Solution pool capacity exceeds the Gurobi integer range"));
        }

        var results = new List<List<Flt64>>();
        GurobiLinearSolverCallBack callBack = (_callBack?.Copy() ?? new GurobiLinearSolverCallBack())
            .Configuration((_, grbModel, _, _) => {
                grbModel.Set(GRB.DoubleParam.PoolGap, 1.0);
                grbModel.Set(GRB.IntParam.PoolSearchMode, 2);
                grbModel.Set(GRB.IntParam.PoolSolutions, (int)solutionAmount);
                return Task.FromResult(Results.OkInstance);
            })
            .AnalyzingSolution((_, grbModel, variables, _) => {
                int count = System.Math.Min((int)solutionAmount, grbModel.Get(GRB.IntAttr.SolCount));
                for (int i = 0; i < count; i++) {
                    grbModel.Set(GRB.IntParam.SolutionNumber, i);
                    var values = new List<Flt64>(variables.Count);
                    foreach (GRBVar variable in variables) {
                        double value = variable.Get(GRB.DoubleAttr.Xn);
                        if (!double.IsFinite(value)) {
                            return Task.FromResult(Results.Failed<Success>(new Err<ErrorCode>(
                                ErrorCode.ORSolutionInvalid,
                                "Gurobi 解池含非有限值 / Gurobi solution pool contains a nonfinite value")));
                        }
                        values.Add(new Flt64(value));
                    }
                    if (!results.Exists(existing => existing.SequenceEqual(values))) { results.Add(values); }
                }
                return Task.FromResult(Results.OkInstance);
            });
        using var impl = new GurobiLinearSolverImpl(
            config: Config,
            callBack: callBack,
            statusCallBack: solvingStatusCallBack,
            identity: _identity,
            captureSensitivityRanges: CaptureSensitivityRanges,
            publishRanges: (ranges, diagnostic) => {
                SensitivityRangesByRowIndex = ranges;
                SensitivityRangeDiagnostic = diagnostic;
            });
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await impl.InvokeReportAsync(model, cancellationToken);
        if (report is Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok && ok.Value.Solution is { } solution) {
            return Results.Ok(ok.Value with {
                Solution = solution with {
                    Pool = Array.AsReadOnly(results.Select(values => new Solution<Flt64>(Array.AsReadOnly(values.ToArray()))).ToArray()),
                },
            });
        }
        return report;
    }

    /// <summary>
    /// 转储线性机制模型为三元组模型 / Dump linear mechanism model to triad model.
    /// </summary>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToLinearTriadModel(
            cancellationToken: cancellationToken,
            configPolicy: Config.FunctionExpansionPolicy));

    /// <summary>
    /// 转储线性元模型为机制模型 / Dump linear meta model to mechanism model.
    /// </summary>
    public async Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        return await LinearMechanismModel<Flt64>.InvokeAsync(
            model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
    }

    /// <summary>
    /// Gurobi 线性求解器内部实现 / Gurobi linear solver internal implementation.
    /// </summary>
    private sealed class GurobiLinearSolverImpl : GurobiSolver {
        private readonly SolverConfig _config;
        private readonly GurobiLinearSolverCallBack? _callBack;
        private readonly SolvingStatusCallBack? _statusCallBack;

        private List<GRBVar> _grbVars = new();
        private List<GRBConstr> _grbConstraints = new();

        private readonly bool _captureSensitivityRanges;
        private readonly Action<IReadOnlyDictionary<int, (double? Lower, double? Upper)>, string?>? _publishRanges;

        public GurobiLinearSolverImpl(
            SolverConfig config,
            GurobiLinearSolverCallBack? callBack,
            SolvingStatusCallBack? statusCallBack,
            GurobiBackendIdentity identity,
            bool captureSensitivityRanges = false,
            Action<IReadOnlyDictionary<int, (double? Lower, double? Upper)>, string?>? publishRanges = null)
            : base(identity) {
            _config = config;
            _callBack = callBack;
            _statusCallBack = statusCallBack;
            _captureSensitivityRanges = captureSensitivityRanges;
            _publishRanges = publishRanges;
        }

        /// <summary>
        /// 执行求解流程 / Execute solving process.
        /// </summary>
        public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            ILinearTriadModelView model,
            CancellationToken cancellationToken) {
            var connection = _config.BackendConfiguration as GurobiSolverConfig;
            string? server = connection?.Server;
            string? password = connection?.Password;
            TimeSpan? connectionTime = connection?.ConnectionTime;

            // Step 1: Init
            Result<Success, ErrorCode, Error<ErrorCode>> initResult;
            if (server is not null && password is not null && connectionTime is not null) {
                initResult = await InitAsync(server, password, connectionTime.Value, model.Name, _callBack?.CreatingEnvironmentFunction);
            }
            else {
                initResult = await InitAsync(model.Name, _callBack?.CreatingEnvironmentFunction);
            }
            if (initResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<SolveReport<Flt64>>(initResult);
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(GurobiReportSupport.Cancelled(
                    _config,
                    quadratic: false,
                    Identity,
                    NativeVersion));
            }

            // Step 2: Dump
            Result<Success, ErrorCode, Error<ErrorCode>> dumpResult = Dump(model);
            if (dumpResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<SolveReport<Flt64>>(dumpResult);
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(GurobiReportSupport.Cancelled(
                    _config,
                    quadratic: false,
                    Identity,
                    NativeVersion));
            }

            // Step 3: Configure
            Result<Success, ErrorCode, Error<ErrorCode>> configResult = Configure(
                model: model,
                cancellationToken: cancellationToken);
            if (configResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<SolveReport<Flt64>>(configResult);
            }

            // Step 4: Solve
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(GurobiReportSupport.Cancelled(
                    _config,
                    quadratic: false,
                    Identity,
                    NativeVersion));
            }
            // **关掉 presolve 才读得到真区间。**
            //
            // **Gurobi 在 presolve 打开时返回的是哨兵值而不是区间**——**基线的实测结论是
            // "`SARHSLow` 会退化成 presolve 打开时的哨兵值（c1 得到 4 而不是 0）"。**
            // **所以只有真的要从这次求解里取范围时，才付"关 presolve、求解变慢"这个代价。**
            //
            // **Presolve must be off for real ranges: with it on, Gurobi returns sentinels rather than ranges** — **the baseline's
            // measurement was that `SARHSLow` degrades to the presolve-on sentinel (c1 yields 4 instead of 0).** **So the cost of a
            // slower solve is paid only when ranges are actually wanted from this solve.**
            if (_captureSensitivityRanges) {
                // **两个参数缺一不可。**
                //
                // **`Presolve = 0` 是基线记下的那个：presolve 打开时读回的是哨兵。**
                // **`Method = 1`（对偶单纯形）是实测补上的：** **ranging 属性来自<b>最优基**，
                // 而 barrier／并发求解不产生基——**那时读 `SARHSLow` 会抛 `GRBException`，
                // 于是"没有范围"和"没有基"分不清。**
                //
                // **Two parameters, neither optional: `Presolve = 0` is the baseline's finding (presolve-on returns sentinels), and
                // `Method = 1` (dual simplex) was added from measurement — ranging comes from an <b>optimal basis</b>, which barrier
                // and concurrent runs do not produce, so the read throws and "no ranges" becomes indistinguishable from "no basis".**
                GrbModel.Parameters.Presolve = 0;
                GrbModel.Parameters.Method = 1;
            }

            Result<bool, ErrorCode, Error<ErrorCode>> solveResult = Solve(cancellationToken);
            if (solveResult is Failed<bool, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<SolveReport<Flt64>>(failed.Error);
            }
            if (solveResult is Fatal<bool, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            if (!solveResult.Value) {
                return Results.Ok(GurobiReportSupport.Cancelled(
                    _config,
                    quadratic: false,
                    Identity,
                    NativeVersion));
            }

            // **在主求解之后、同一份 `GRBModel` 上读范围。**
            //
            // **每一行都单独 try：** **`SARHSLow`／`SARHSUp` 只在"是 LP、已证最优、presolve 关闭"时才可读；
            // 无解／无界的模型上读它会抛 `GRBException`。** **一行读不到不该让整次求解失败，
            // 但也不该被静默补上一个数——所以<b>读不到就不进字典**。
            //
            // **Read the ranges on the same `GRBModel` right after the primary solve.** **Each row is guarded separately:
            // `SARHSLow`/`SARHSUp` are only readable for an LP that proved optimal with presolve off; on an infeasible or unbounded
            // model the read throws `GRBException`.** **One unreadable row must not fail the whole solve, and must not be silently
            // filled either — so <b>unreadable rows simply do not enter the dictionary</b>.**
            if (_captureSensitivityRanges && _publishRanges is { } publish) {
                var captured = new Dictionary<int, (double? Lower, double? Upper)>();
                string? diagnostic = null;
                for (int row = 0; row < _grbConstraints.Count; ++row) {
                    GRBConstr constraint = _grbConstraints[row];
                    try {
                        captured[row] = (
                            constraint.Get(GRB.DoubleAttr.SARHSLow),
                            constraint.Get(GRB.DoubleAttr.SARHSUp));
                    }
                    catch (GRBException exception) {
                        diagnostic ??= $"rows={_grbConstraints.Count} vars={_grbVars.Count} isMip={GrbModel.Get(GRB.IntAttr.IsMIP)} status={GrbModel.Get(GRB.IntAttr.Status)} row={row}: {exception.Message}";
                    }
                }
                publish(captured, diagnostic);
            }

            // Step 5: Analyze status
            Result<Success, ErrorCode, Error<ErrorCode>> statusResult = await AnalyzeStatusAsync();
            if (statusResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<SolveReport<Flt64>>(statusResult);
            }

            // Step 6: Analyze solution
            return await AnalyzeSolution();
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Dump(ILinearTriadModelView model) {
            Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
            if (!identity.IsOk) {
                return identity;
            }
            try {
                // Variables
                var vars = new List<GRBVar>(model.Variables.Count);
                foreach (ModelViewVariable v in model.Variables) {
                    Flt64 lb = v.LowerBound;
                    Flt64 ub = v.UpperBound;
                    // Replace infinity with GRB.UNDEFINED
                    if (lb.ToDouble() <= -1e100) {
                        lb = new Flt64(-GRB.INFINITY);
                    }

                    if (ub.ToDouble() >= 1e100) {
                        ub = new Flt64(GRB.INFINITY);
                    }

                    vars.Add(GrbModel.AddVar(
                        lb.ToDouble(), ub.ToDouble(), 0.0,
                        GurobiVariableExtensions.From(v.Type).ToGurobiChar(),
                        v.Name));
                }
                _grbVars = vars;

                // Set initial values
                foreach (ModelViewVariable v in model.Variables) {
                    if (v.InitialResult is { } init) {
                        _grbVars[v.Index].Set(GRB.DoubleAttr.Start, init.ToDouble());
                    }
                }

                // Constraints
                ModelConstraint<LinearConstraintCell> constraints = model.Constraints;
                Result<Success, ErrorCode, Error<ErrorCode>> shape = model.ValidateIdentity();
                if (!shape.IsOk) {
                    return shape;
                }
                var grbConstrs = new List<GRBConstr>(constraints.Size);
                for (int i = 0; i < constraints.Size; i++) {
                    var lhs = new GRBLinExpr();
                    foreach (LinearConstraintCell cell in constraints.Lhs[i]) {
                        lhs.AddTerm(cell.Coefficient.ToDouble(), _grbVars[cell.ColIndex]);
                    }
                    grbConstrs.Add(GrbModel.AddConstr(
                        lhs,
                        GurobiConstraintSignExtensions.From(constraints.Signs[i]).ToGurobiChar(),
                        constraints.Rhs[i].ToDouble(),
                        constraints.Names[i]));
                }
                _grbConstraints = grbConstrs;

                // Objective
                var obj = new GRBLinExpr();
                foreach (LinearObjectiveCell cell in model.Objective.Cells) {
                    obj.AddTerm(cell.Coefficient.ToDouble(), _grbVars[cell.ColIndex]);
                }
                obj.AddConstant(model.Objective.Constant.ToDouble());
                GrbModel.SetObjective(obj,
                    model.Objective.Category == ObjectCategory.Minimum ? GRB.MINIMIZE : GRB.MAXIMIZE);

                // AfterModeling callback
                Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContainAsync(
                    CallBackPoint.AfterModeling, null, GrbModel, _grbVars, _grbConstraints).GetAwaiter().GetResult();
                if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return cbResult!;
                }

                return Results.OkInstance;
            }
            catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException, e.Message)); }
            catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException)); }
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Configure(
            ILinearTriadModelView model,
            CancellationToken cancellationToken) {
            try {
                Result<Success, ErrorCode, Error<ErrorCode>> validation = _config.ValidateRuntime();
                if (!validation.IsOk) {
                    return validation;
                }
                int threads = _config.NativeThreadCount!.Value;
                GrbModel.Set(GRB.DoubleParam.TimeLimit, _config.TimeLimit.TotalSeconds);
                GrbModel.Set(GRB.DoubleParam.MIPGap, _config.Gap.ToSolverDouble());
                GrbModel.Set(GRB.IntParam.Threads, threads);

                if (_config.NotImprovementTime is not null || _callBack?.NativeCallback is not null ||
                    _statusCallBack is not null || cancellationToken.CanBeCanceled) {
                    GrbModel.SetCallback(new GurobiSolveCallback(
                        config: _config,
                        model: GrbModel,
                        vars: _grbVars,
                        intermediateModel: model,
                        nativeCallBack: _callBack?.NativeCallback,
                        statusCallBack: _statusCallBack,
                        cancellationToken: cancellationToken));
                }

                // Configuration callback
                Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContainAsync(
                    CallBackPoint.Configuration, null, GrbModel, _grbVars, _grbConstraints).GetAwaiter().GetResult();
                if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return cbResult!;
                }

                return Results.OkInstance;
            }
            catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException, e.Message)); }
            catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException)); }
        }

        private async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> AnalyzeSolution() {
            try {
                SolveSolution<Flt64>? solution = null;
                if (HasSolution) {
                    var values = new List<Flt64>(_grbVars.Count);
                    foreach (GRBVar variable in _grbVars) {
                        values.Add(new Flt64(variable.Get(GRB.DoubleAttr.X)));
                    }
                    solution = new SolveSolution<Flt64>(
                        Values: new Solution<Flt64>(Array.AsReadOnly(values.ToArray())),
                        Objective: new Flt64(GrbModel.Get(GRB.DoubleAttr.ObjVal)));
                }
                SolveStatistics statistics = Statistics(solution?.Objective);
                Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = Report(
                    solution: solution,
                    statistics: statistics,
                    quadratic: false,
                    config: _config);
                if (!report.IsOk) {
                    return report;
                }
                CallBackPoint point = HasSolution && Status.Succeeded()
                    ? CallBackPoint.AnalyzingSolution : CallBackPoint.AfterFailure;
                Result<Success, ErrorCode, Error<ErrorCode>>? callbackResult = await (_callBack?.ExecIfContainAsync(
                    point: point,
                    status: Status,
                    grbModel: GrbModel,
                    variables: _grbVars,
                    constraints: _grbConstraints)
                    ?? Task.FromResult<Result<Success, ErrorCode, Error<ErrorCode>>?>(null));
                if (callbackResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return PropagateError<SolveReport<Flt64>>(callbackResult);
                }
                return report;
            }
            catch (GRBException error) {
                return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, error.Message));
            }
            catch {
                return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
            }
        }
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> PropagateError<T>(Result<Success, ErrorCode, Error<ErrorCode>> r) => r switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> f => Results.Failed<T>(f.Error),
        Fatal<Success, ErrorCode, Error<ErrorCode>> fat => new Fatal<T, ErrorCode, Error<ErrorCode>>(fat.Errors),
        _ => throw new InvalidOperationException()
    };


}
