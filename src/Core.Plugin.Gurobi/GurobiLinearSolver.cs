using System.Linq;
#nullable enable

using Fuookami.Ospf.Core.Solver.Iis;
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
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using SolverConfigGurobi = Fuookami.Ospf.Core.Solver.Config.GurobiSolverConfig;
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
    private readonly GurobiSolverConfig? _connectionConfig;

    public GurobiLinearSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null,
        GurobiSolverConfig? connectionConfig = null) {
        Config = config ?? new SolverConfigGurobi();
        _callBack = callBack;
        _connectionConfig = connectionConfig;
    }

    /// <summary>
    /// 求解线性模型 / Solve linear model.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        using var impl = new GurobiLinearSolverImpl(Config, _callBack, _connectionConfig, solvingStatusCallBack);
        return await impl.InvokeAsync(model);
    }

    /// <summary>
    /// 求解线性模型并启用 IIS 诊断 / Solve linear model with IIS diagnostics.
    /// </summary>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await InvokeAsync((ILinearTriadModelView)model, solvingStatusCallBack, cancellationToken);
        return result switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok => Results.Ok<SolverOutput>(ok.Value),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f => Results.Failed<SolverOutput>(f.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat => new Fatal<SolverOutput, ErrorCode, Error<ErrorCode>>(fat.Errors),
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>
    /// 求解线性模型获取多个解 / Solve linear model for multiple solutions.
    /// </summary>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        if (solutionAmount <= 1) {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> single = await InvokeAsync(model, solvingStatusCallBack, cancellationToken);
            return single switch {
                Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                    Results.Ok((ok.Value, new List<List<Flt64>>())),
                Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f =>
                    Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(f.Error),
                Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat =>
                    new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fat.Errors),
                _ => throw new InvalidOperationException()
            };
        }

        var results = new List<List<Flt64>>();
        GurobiLinearSolverCallBack callBack = (_callBack?.Copy() ?? new GurobiLinearSolverCallBack())
            .Configuration(async (_, grbModel, _, _) => {
                grbModel.Set(GRB.DoubleParam.PoolGap, 1.0);
                grbModel.Set(GRB.IntParam.PoolSearchMode, 2);
                grbModel.Set(GRB.IntParam.PoolSolutions, (int)solutionAmount);
                return Results.OkInstance;
            })
            .AnalyzingSolution(async (_, grbModel, variables, _) => {
                int count = System.Math.Min((int)solutionAmount, grbModel.Get(GRB.IntAttr.SolCount));
                for (int i = 0; i < count; i++) {
                    grbModel.Set(GRB.IntParam.SolutionNumber, i);
                    var thisResults = new List<Flt64>(variables.Count);
                    foreach (GRBVar v in variables) { thisResults.Add(new Flt64(v.Get(GRB.DoubleAttr.Xn))); }
                    if (!results.Exists(r => r.SequenceEqual(thisResults))) { results.Add(thisResults); }
                }
                return Results.OkInstance;
            });

        using var impl = new GurobiLinearSolverImpl(Config, callBack, _connectionConfig, solvingStatusCallBack);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await impl.InvokeAsync(model);
        return result switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok((ok.Value, results)),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f =>
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(f.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat =>
                new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fat.Errors),
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>
    /// 转储线性机制模型为三元组模型 / Dump linear mechanism model to triad model.
    /// </summary>
    public Task<LinearTriadModel> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default) {
        IReadOnlyList<Token<Flt64>> tokens = model.Tokens.TokensInSolver;
        var variables = new List<ModelViewVariable>(tokens.Count);
        for (int i = 0; i < tokens.Count; i++) {
            IVariableItem v = tokens[i].Variable;
            variables.Add(new ModelViewVariable(
                i,
                v.LowerBound?.Value is Fuookami.Ospf.Math.Algebra.ValueRange.ValueWrapper<Flt64>.Value val1 ? val1.Number : new Flt64(double.NegativeInfinity),
                v.UpperBound?.Value is Fuookami.Ospf.Math.Algebra.ValueRange.ValueWrapper<Flt64>.Value val2 ? val2.Number : new Flt64(double.PositiveInfinity),
                v.TypeKind,
                v.Name));
        }

        IReadOnlyList<IConstraint<Flt64, LinearCategory>> constraints = ((IMechanismModel<Flt64>)model).Constraints;
        int rowCount = constraints.Count;
        var sparseRows = new List<SparseVector>(rowCount);
        var signs = new List<ConstraintRelation>(rowCount);
        var rhs = new List<Flt64>(rowCount);
        var names = new List<string>(rowCount);
        var sources = new List<ConstraintSource>(rowCount);

        for (int row = 0; row < rowCount; row++) {
            IConstraint<Flt64, LinearCategory> constraint = constraints[row];
            var entries = new List<SparseVectorEntry>();
            foreach (ICell<Flt64> cell in constraint.Lhs) {
                if (cell is ILinearCell<Flt64> linearCell) {
                    int? colIndex = model.Tokens.IndexOf(linearCell.Token);
                    if (colIndex is { } ci && linearCell.Coefficient != Flt64.Zero) {
                        entries.Add(new SparseVectorEntry(ci, linearCell.Coefficient));
                    }
                }
            }
            sparseRows.Add(new SparseVector(entries));
            signs.Add(constraint.Sign);
            rhs.Add(constraint.Rhs);
            names.Add(constraint.Name);
            sources.Add(ConstraintSource.Origin);
        }

        var constraintBatch = new LinearConstraintBatch(
            new SparseMatrix(sparseRows), signs, rhs, names, sources);

        var singleObj = (SingleObject)model.ObjectFunction;
        IReadOnlyList<SubObject<Flt64>> subObjects = singleObj.GetSubObjects<Flt64>();
        var objCells = new List<LinearObjectiveCell>();
        Flt64 objConstant = Flt64.Zero;
        foreach (SubObject<Flt64> sub in subObjects) {
            if (sub is LinearSubObject<Flt64> linearSub) {
                foreach (LinearMonomial<Flt64> mono in linearSub.LinearTerms()) {
                    if (mono.Symbol is Core.Variable.IVariableItem vi) {
                        int? colIndex = model.Tokens.IndexOf(vi);
                        if (colIndex is { } ci && mono.Coefficient != Flt64.Zero) {
                            objCells.Add(new LinearObjectiveCell(ci, mono.Coefficient));
                        }
                    }
                }
                objConstant = objConstant.Plus(sub.Constant);
            }
        }

        var objective = new Objective<LinearObjectiveCell>(
            singleObj.Category, objCells, objConstant);

        var triadModel = new LinearTriadModel(variables, constraintBatch, objective, model.Name);
        return Task.FromResult(triadModel);
    }

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
        private readonly GurobiSolverConfig? _connectionConfig;
        private readonly SolvingStatusCallBack? _statusCallBack;

        private List<GRBVar> _grbVars = new();
        private List<GRBConstr> _grbConstraints = new();
        private FeasibleSolverOutput<Flt64>? _output;

        public GurobiLinearSolverImpl(
            SolverConfig config,
            GurobiLinearSolverCallBack? callBack,
            GurobiSolverConfig? connectionConfig,
            SolvingStatusCallBack? statusCallBack) {
            _config = config;
            _callBack = callBack;
            _connectionConfig = connectionConfig;
            _statusCallBack = statusCallBack;
        }

        /// <summary>
        /// 执行求解流程 / Execute solving process.
        /// </summary>
        public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(ILinearTriadModelView model) {
            string? server = _connectionConfig?.Server;
            string? password = _connectionConfig?.Password;
            TimeSpan? connectionTime = _connectionConfig?.ConnectionTime;

            // Step 1: Init
            Result<Success, ErrorCode, Error<ErrorCode>> initResult;
            if (server is not null && password is not null && connectionTime is not null) {
                initResult = await InitAsync(server, password, connectionTime.Value, model.Name, _callBack?.CreatingEnvironmentFunction);
            }
            else {
                initResult = await InitAsync(model.Name, _callBack?.CreatingEnvironmentFunction);
            }
            if (initResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(initResult);
            }

            // Step 2: Dump
            Result<Success, ErrorCode, Error<ErrorCode>> dumpResult = Dump(model);
            if (dumpResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(dumpResult);
            }

            // Step 3: Configure
            Result<Success, ErrorCode, Error<ErrorCode>> configResult = Configure();
            if (configResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(configResult);
            }

            // Step 4: Solve
            Result<Success, ErrorCode, Error<ErrorCode>> solveResult = await SolveAsync();
            if (solveResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(solveResult);
            }

            // Step 5: Analyze status
            Result<Success, ErrorCode, Error<ErrorCode>> statusResult = await AnalyzeStatusAsync();
            if (statusResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(statusResult);
            }

            // Step 6: Analyze solution
            return await AnalyzeSolution();
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Dump(ILinearTriadModelView model) {
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

        private Result<Success, ErrorCode, Error<ErrorCode>> Configure() {
            try {
                var gurobiConfig = _config as SolverConfigGurobi;
                if (gurobiConfig is not null) {
                    GrbModel.Set(GRB.DoubleParam.TimeLimit, gurobiConfig.TimeLimit);
                    GrbModel.Set(GRB.DoubleParam.MIPGap, gurobiConfig.MipGap);
                    GrbModel.Set(GRB.IntParam.Threads, gurobiConfig.Threads);
                }

                if (_callBack?.NativeCallback is not null || _statusCallBack is not null) {
                    GrbModel.SetCallback(new GurobiLinearCallback(
                        _callBack?.NativeCallback, _statusCallBack, _grbVars));
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

        private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> AnalyzeSolution() {
            try {
                if (Status.Succeeded()) {
                    var results = new List<Flt64>(_grbVars.Count);
                    foreach (GRBVar grbVar in _grbVars) {
                        results.Add(new Flt64(grbVar.Get(GRB.DoubleAttr.X)));
                    }

                    bool isMip = GrbModel.Get(GRB.IntAttr.IsMIP) != 0;
                    _output = new FeasibleSolverOutput<Flt64>(
                        Obj: new Flt64(GrbModel.Get(GRB.DoubleAttr.ObjVal)),
                        Solution: new Solution<Flt64>(results),
                        Time: TimeSpan.FromSeconds(GrbModel.Get(GRB.DoubleAttr.Runtime)),
                        PossibleBestObj: new Flt64(isMip ? GrbModel.Get(GRB.DoubleAttr.ObjBound) : GrbModel.Get(GRB.DoubleAttr.ObjVal)),
                        Gap: new Flt64(isMip ? GrbModel.Get(GRB.DoubleAttr.MIPGap) : 0.0));

                    // AnalyzingSolution callback
                    Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = await (_callBack?.ExecIfContainAsync(
                        CallBackPoint.AnalyzingSolution, Status, GrbModel, _grbVars, _grbConstraints) ?? Task.FromResult<Result<Success, ErrorCode, Error<ErrorCode>>?>(null));
                    if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                        return PropagateError<FeasibleSolverOutput<Flt64>>(cbResult!);
                    }

                    return Results.Ok(_output);
                }
                else {
                    // AfterFailure callback
                    Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = await (_callBack?.ExecIfContainAsync(
                        CallBackPoint.AfterFailure, Status, GrbModel, _grbVars, _grbConstraints) ?? Task.FromResult<Result<Success, ErrorCode, Error<ErrorCode>>?>(null));

                    return FailByStatus<FeasibleSolverOutput<Flt64>>(Status);
                }
            }
            catch (GRBException e) { return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
            catch { return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)); }
        }
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> PropagateError<T>(Result<Success, ErrorCode, Error<ErrorCode>> r) => r switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> f => Results.Failed<T>(f.Error),
        Fatal<Success, ErrorCode, Error<ErrorCode>> fat => new Fatal<T, ErrorCode, Error<ErrorCode>>(fat.Errors),
        _ => throw new InvalidOperationException()
    };

    private static Result<T, ErrorCode, Error<ErrorCode>> FailByStatus<T>(SolverStatus status) => status switch {
        SolverStatus.Infeasible => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORModelInfeasible)),
        SolverStatus.Unbounded => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORModelUnbounded)),
        SolverStatus.InfeasibleOrUnbounded => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ORModelInfeasibleOrUnbounded)),
        _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, $"Solver status: {status}"))
    };
}

/// <summary>
/// Gurobi 线性回调包装器 / Gurobi linear callback wrapper.
/// </summary>
internal sealed class GurobiLinearCallback : GRBCallback {
    private readonly NativeCallBack? _nativeCallBack;
    private readonly SolvingStatusCallBack? _statusCallBack;
    private readonly IReadOnlyList<GRBVar> _vars;

    public GurobiLinearCallback(
        NativeCallBack? nativeCallBack,
        SolvingStatusCallBack? statusCallBack,
        IReadOnlyList<GRBVar> vars) {
        _nativeCallBack = nativeCallBack;
        _statusCallBack = statusCallBack;
        _vars = vars;
    }

    protected override void Callback() {
        try {
            _nativeCallBack?.Invoke(this);

            if (where == GRB.Callback.MIP || where == GRB.Callback.MIPSOL) {
                var currentObj = new Flt64(GetDoubleInfo(GRB.Callback.MIP_OBJBST));
                var currentBound = new Flt64(GetDoubleInfo(GRB.Callback.MIP_OBJBND));
                var epsilon = new Flt64(1e-10);
                Flt64 gap = currentObj == Flt64.Zero
                    ? Flt64.Zero
                    : (currentObj.Minus(currentBound)).Div(currentObj.Plus(epsilon)).Abs();

                _statusCallBack?.Invoke(new SolvingStatus(
                    Obj: currentObj,
                    PossibleBestObj: currentBound,
                    Gap: gap,
                    Solver: "gurobi",
                    SolverIndex: 0,
                    Status: SolverStatus.Feasible));
            }
        }
        catch (GRBException) {
            // Swallow Gurobi callback exceptions to avoid crashing the solve
        }
        catch {
            // Swallow unexpected callback exceptions
        }
    }
}
