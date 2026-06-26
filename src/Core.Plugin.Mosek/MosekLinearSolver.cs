#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using mosek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Mosek;

/// <summary>
/// MOSEK 线性求解器 / MOSEK linear solver.
/// </summary>
public sealed class MosekLinearSolver : ILinearSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "mosek";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly MosekSolverCallBack? _callBack;

    public MosekLinearSolver(
        SolverConfig? config = null,
        MosekSolverCallBack? callBack = null) {
        Config = config ?? new MosekSolverConfig();
        _callBack = callBack;
    }

    /// <summary>
    /// 求解线性模型 / Solve linear model.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        using var impl = new MosekLinearSolverImpl(Config, _callBack, solvingStatusCallBack);
        return await impl.InvokeAsync(model);
    }

    /// <summary>
    /// 求解线性模型（含 IIS） / Solve linear model with IIS diagnostics.
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

        // Multi-solution not supported for Mosek adapter; fall back to single
        return await InvokeAsync(model, 1, solvingStatusCallBack, cancellationToken);
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
        return System.Threading.Tasks.Task.FromResult(triadModel);
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
    /// MOSEK 线性求解器内部实现 / MOSEK linear solver internal implementation.
    /// </summary>
    private sealed class MosekLinearSolverImpl : MosekSolver {
        private readonly SolverConfig _config;
        private readonly MosekSolverCallBack? _callBack;
        private readonly SolvingStatusCallBack? _statusCallBack;

        private FeasibleSolverOutput<Flt64>? _output;

        public MosekLinearSolverImpl(
            SolverConfig config,
            MosekSolverCallBack? callBack,
            SolvingStatusCallBack? statusCallBack) {
            _config = config;
            _callBack = callBack;
            _statusCallBack = statusCallBack;
        }

        /// <summary>
        /// 执行求解流程 / Execute solving process.
        /// </summary>
        public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(ILinearTriadModelView model) {
            // Step 1: Init
            Result<Success, ErrorCode, Error<ErrorCode>> initResult = Init(model.Name, _callBack?.CreatingEnvironmentFunction);
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
            Result<Success, ErrorCode, Error<ErrorCode>> solveResult = Solve();
            if (solveResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(solveResult);
            }

            // Step 5: Analyze status
            Result<Success, ErrorCode, Error<ErrorCode>> statusResult = AnalyzeStatus();
            if (statusResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                return PropagateError<FeasibleSolverOutput<Flt64>>(statusResult);
            }

            // Step 6: Analyze solution
            return AnalyzeSolution();
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Dump(ILinearTriadModelView model) {
            try {
                // Variables
                int numVars = model.Variables.Count;
                MosekModel.appendvars(numVars);
                foreach (ModelViewVariable v in model.Variables) {
                    int col = v.Index;
                    MosekModel.putvarname(col, v.Name);
                    if (v.Type.IsContinuousType) {
                        MosekModel.putvartype(col, variabletype.type_cont);
                    }
                    else {
                        MosekModel.putvartype(col, variabletype.type_int);
                    }

                    Flt64 lb = v.LowerBound;
                    Flt64 ub = v.UpperBound;
                    double lbVal = lb.ToDouble();
                    double ubVal = ub.ToDouble();

                    if (double.IsNegativeInfinity(lbVal) && double.IsPositiveInfinity(ubVal)) {
                        MosekModel.putvarbound(col, boundkey.fr, 0.0, 0.0);
                    }
                    else if (double.IsNegativeInfinity(lbVal)) {
                        MosekModel.putvarbound(col, boundkey.up, 0.0, ubVal);
                    }
                    else if (double.IsPositiveInfinity(ubVal)) {
                        MosekModel.putvarbound(col, boundkey.lo, lbVal, 0.0);
                    }
                    else if (System.Math.Abs(lbVal - ubVal) < 1e-15) {
                        MosekModel.putvarbound(col, boundkey.fx, lbVal, ubVal);
                    }
                    else {
                        MosekModel.putvarbound(col, boundkey.ra, lbVal, ubVal);
                    }
                }

                // Set initial values
                foreach (ModelViewVariable v in model.Variables) {
                    if (v.InitialResult is { } init) {
                        MosekModel.putxxslice(soltype.bas, v.Index, v.Index + 1, new double[] { init.ToDouble() });
                    }
                }

                // Constraints
                ModelConstraint<LinearConstraintCell> constraints = model.Constraints;
                int numRows = constraints.Size;
                MosekModel.appendcons(numRows);
                for (int i = 0; i < numRows; i++) {
                    MosekModel.putconname(i, constraints.Names[i]);

                    var cols = new List<int>();
                    var coefficients = new List<double>();
                    foreach (LinearConstraintCell cell in constraints.Lhs[i]) {
                        cols.Add(cell.ColIndex);
                        coefficients.Add(cell.Coefficient.ToDouble());
                    }

                    if (constraints.Signs[i] == ConstraintRelation.LessEqual) {
                        MosekModel.putconbound(i, boundkey.up, 0.0, constraints.Rhs[i].ToDouble());
                    }
                    else if (constraints.Signs[i] == ConstraintRelation.GreaterEqual) {
                        MosekModel.putconbound(i, boundkey.lo, constraints.Rhs[i].ToDouble(), 0.0);
                    }
                    else if (constraints.Signs[i] == ConstraintRelation.Equal) {
                        MosekModel.putconbound(i, boundkey.fx, constraints.Rhs[i].ToDouble(), constraints.Rhs[i].ToDouble());
                    }

                    MosekModel.putarow(i, cols.ToArray(), coefficients.ToArray());
                }

                // Objective
                foreach (LinearObjectiveCell cell in model.Objective.Cells) {
                    MosekModel.putcj(cell.ColIndex, cell.Coefficient.ToDouble());
                }
                MosekModel.putcfix(model.Objective.Constant.ToDouble());
                MosekModel.putobjsense(
                    model.Objective.Category == ObjectCategory.Minimum ? objsense.minimize : objsense.maximize);

                // AfterModeling callback
                Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                    CallBackPoint.AfterModeling, null, MosekModel);
                if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return cbResult!;
                }

                return Results.OkInstance;
            }
            catch (mosek.Exception e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException, e.Message)); }
            catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException)); }
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Configure() {
            try {
                MosekModel.putintparam(mosek.iparam.optimizer, mosek.optimizertype.primal_simplex);

                // Configuration callback
                Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                    CallBackPoint.Configuration, null, MosekModel);
                if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return cbResult!;
                }

                return Results.OkInstance;
            }
            catch (mosek.Exception e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException, e.Message)); }
            catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException)); }
        }

        private Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> AnalyzeSolution() {
            try {
                if (Status.Succeeded()) {
                    int numVars = MosekModel.getnumvar();
                    double[] xx = new double[numVars];
                    MosekModel.getxxslice(soltype.bas, 0, numVars, xx);

                    var results = new List<Flt64>(numVars);
                    for (int i = 0; i < numVars; i++) {
                        results.Add(new Flt64(xx[i]));
                    }

                    double objVal = MosekModel.getprimalobj(soltype.bas);
                    _output = new FeasibleSolverOutput<Flt64>(
                        Obj: new Flt64(objVal),
                        Solution: new Solution<Flt64>(results),
                        Time: TimeSpan.FromSeconds(MosekModel.getdouinf(dinfitem.optimizer_time)),
                        PossibleBestObj: new Flt64(objVal),
                        Gap: Flt64.Zero);

                    // AnalyzingSolution callback
                    Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                        CallBackPoint.AnalyzingSolution, Status, MosekModel);
                    if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                        return PropagateError<FeasibleSolverOutput<Flt64>>(cbResult!);
                    }

                    return Results.Ok(_output);
                }
                else {
                    // AfterFailure callback
                    Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                        CallBackPoint.AfterFailure, Status, MosekModel);

                    return FailByStatus<FeasibleSolverOutput<Flt64>>(Status);
                }
            }
            catch (mosek.Exception e) { return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
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
