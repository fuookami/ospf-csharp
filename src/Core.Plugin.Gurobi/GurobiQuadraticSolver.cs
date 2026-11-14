#nullable enable

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
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 二次求解器 / Gurobi quadratic solver.
/// </summary>
public sealed class GurobiQuadraticSolver : IQuadraticSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    /// <inheritdoc/>
    /// <remarks>
    /// 二次模型只提供原生 IIS 分析器：二次约束的 Farkas 证书不是这个插件给得出的东西，
    /// **不声明比声明一个给不出的能力好。**
    ///
    /// The quadratic model offers only the native IIS analyzer: a Farkas certificate for quadratic constraints is not something this plugin produces, and **declaring nothing
    /// beats declaring a capability that cannot be delivered.**
    /// </remarks>
    public IReadOnlyList<IInfeasibilityAnalyzer<IQuadraticTetradModelView>> DiagnosticAnalyzers(IisConfig config) =>
        new IInfeasibilityAnalyzer<IQuadraticTetradModelView>[] {
            new GurobiQuadraticNativeIisInfeasibilityAnalyzer(this, config),
        };

    public SolverConfig Config { get; }

    private readonly GurobiQuadraticSolverCallBack? _callBack;
    private readonly GurobiBackendIdentity _identity;

    /// <summary>创建 Gurobi 二次求解器 / Create a Gurobi quadratic solver.</summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">求解回调 / Solver callback.</param>
    public GurobiQuadraticSolver(
        SolverConfig? config = null,
        GurobiQuadraticSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiQuadraticSolver(
        SolverConfig? config,
        GurobiQuadraticSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
        _identity = identity;
    }

    /// <summary>兼容旧求解入口 / Legacy solve entry.</summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) => GurobiReportSupport.ToLegacy(await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(GurobiReportSupport.Cancelled(
                Config,
                quadratic: true,
                _identity,
                GurobiSolver.NativeVersion));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> shape = model.ValidateIdentity();
        if (!shape.IsOk) {
            return PropagateError<SolveReport<Flt64>>(shape);
        }
        using var impl = new GurobiQuadraticSolverImpl(
            config: Config,
            callBack: _callBack,
            statusCallBack: solvingStatusCallBack,
            identity: _identity);
        return await impl.InvokeReportAsync(model, cancellationToken);
    }

    /// <summary>兼容旧 IIS 入口 / Legacy IIS entry.</summary>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await InvokeAsync(
            model: (IQuadraticTetradModelView)model,
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
        IQuadraticTetradModelView model,
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
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(GurobiReportSupport.Cancelled(
                Config,
                quadratic: true,
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
        GurobiQuadraticSolverCallBack callBack = (_callBack?.Copy() ?? new GurobiQuadraticSolverCallBack())
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
        using var impl = new GurobiQuadraticSolverImpl(
            config: Config,
            callBack: callBack,
            statusCallBack: solvingStatusCallBack,
            identity: _identity);
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
    /// 转储二次机制模型为四元组模型 / Dump quadratic mechanism model to tetrad model.
    /// </summary>
    public Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(model.ToQuadraticTetradModel(cancellationToken: cancellationToken));

    /// <summary>
    /// 转储二次元模型为机制模型 / Dump quadratic meta model to mechanism model.
    /// </summary>
    public async Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default) {
        return await QuadraticMechanismModel<Flt64>.InvokeAsync(
            model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack,
            dumpingStatusCallBack: dumpingStatusCallBack);
    }

    /// <summary>
    /// Gurobi 二次求解器内部实现 / Gurobi quadratic solver internal implementation.
    /// </summary>
    private sealed class GurobiQuadraticSolverImpl : GurobiSolver {
        private readonly SolverConfig _config;
        private readonly GurobiQuadraticSolverCallBack? _callBack;
        private readonly SolvingStatusCallBack? _statusCallBack;

        private List<GRBVar> _grbVars = new();
        private List<GRBQConstr> _grbConstraints = new();

        public GurobiQuadraticSolverImpl(
            SolverConfig config,
            GurobiQuadraticSolverCallBack? callBack,
            SolvingStatusCallBack? statusCallBack,
            GurobiBackendIdentity identity)
            : base(identity) {
            _config = config;
            _callBack = callBack;
            _statusCallBack = statusCallBack;
        }

        /// <summary>
        /// 执行求解流程 / Execute solving process.
        /// </summary>
        public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            IQuadraticTetradModelView model,
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
                    quadratic: true,
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
                    quadratic: true,
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
                    quadratic: true,
                    Identity,
                    NativeVersion));
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
                    quadratic: true,
                    Identity,
                    NativeVersion));
            }

            // Step 5: Analyze status
            Result<Success, ErrorCode, Error<ErrorCode>> statusResult = await AnalyzeStatusAsync();
            if (statusResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<SolveReport<Flt64>>(statusResult);
            }

            // Step 6: Analyze solution
            return await AnalyzeSolution();
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Dump(IQuadraticTetradModelView model) {
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

                // Constraints (quadratic)
                ModelConstraint<QuadraticConstraintCell> constraints = model.Constraints;
                Result<Success, ErrorCode, Error<ErrorCode>> shape = model.ValidateIdentity();
                if (!shape.IsOk) {
                    return shape;
                }
                var grbQConstrs = new List<GRBQConstr>(constraints.Size);
                for (int i = 0; i < constraints.Size; i++) {
                    var lhs = new GRBQuadExpr();
                    foreach (QuadraticConstraintCell cell in constraints.Lhs[i]) {
                        if (cell.Col2 < 0) {
                            // Linear term (Col2 = -1 sentinel)
                            lhs.AddTerm(cell.Coefficient.ToDouble(), _grbVars[cell.Col1]);
                        }
                        else {
                            // Quadratic term
                            lhs.AddTerm(cell.Coefficient.ToDouble(), _grbVars[cell.Col1], _grbVars[cell.Col2]);
                        }
                    }
                    grbQConstrs.Add(GrbModel.AddQConstr(
                        lhs,
                        GurobiConstraintSignExtensions.From(constraints.Signs[i]).ToGurobiChar(),
                        constraints.Rhs[i].ToDouble(),
                        constraints.Names[i]));
                }
                _grbConstraints = grbQConstrs;

                // Objective (quadratic)
                var obj = new GRBQuadExpr();
                foreach (QuadraticObjectiveCell cell in model.Objective.Cells) {
                    if (cell.Col2 < 0) {
                        // Linear term (Col2 = -1 sentinel)
                        obj.AddTerm(cell.Coefficient.ToDouble(), _grbVars[cell.Col1]);
                    }
                    else {
                        // Quadratic term
                        obj.AddTerm(cell.Coefficient.ToDouble(), _grbVars[cell.Col1], _grbVars[cell.Col2]);
                    }
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
            IQuadraticTetradModelView model,
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
                    quadratic: true,
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
