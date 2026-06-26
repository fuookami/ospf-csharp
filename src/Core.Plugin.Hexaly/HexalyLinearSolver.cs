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
using Hexaly.Optimizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 线性求解器 / Hexaly linear solver.
/// </summary>
public sealed class HexalyLinearSolver : ILinearSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "hexaly";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly HexalySolverCallBack? _callBack;

    public HexalyLinearSolver(
        SolverConfig? config = null,
        HexalySolverCallBack? callBack = null) {
        Config = config ?? new HexalySolverConfig();
        _callBack = callBack;
    }

    /// <summary>
    /// 求解线性模型 / Solve linear model.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        using var impl = new HexalyLinearSolverImpl(Config, _callBack, solvingStatusCallBack);
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

        // Multi-solution not supported for Hexaly adapter; fall back to single
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
    /// Hexaly 线性求解器内部实现 / Hexaly linear solver internal implementation.
    /// </summary>
    private sealed class HexalyLinearSolverImpl : HexalySolver {
        private readonly SolverConfig _config;
        private readonly HexalySolverCallBack? _callBack;
        private readonly SolvingStatusCallBack? _statusCallBack;

        private List<HxExpression> _hexalyVars = new();
        private List<HxExpression> _hexalyConstraints = new();
        private HxExpression? _hexalyObjective;
        private FeasibleSolverOutput<Flt64>? _output;

        private TimeSpan _bestTime = TimeSpan.Zero;

        public HexalyLinearSolverImpl(
            SolverConfig config,
            HexalySolverCallBack? callBack,
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
            Result<Success, ErrorCode, Error<ErrorCode>> configResult = Configure(model);
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
                var vars = new List<HxExpression>(model.Variables.Count);
                foreach (ModelViewVariable v in model.Variables) {
                    HexalyVariableType vtype = HexalyVariableExtensions.From(v.Type);
                    HxExpression hxVar = vtype.CreateVariable(HexalyModel, v.LowerBound, v.UpperBound);
                    vars.Add(hxVar);
                }
                _hexalyVars = vars;

                // Set initial values
                foreach (ModelViewVariable v in model.Variables) {
                    if (v.InitialResult is { } init) {
                        _hexalyVars[v.Index].SetValue(init.ToDouble());
                    }
                }

                // Constraints
                ModelConstraint<LinearConstraintCell> constraints = model.Constraints;
                var hexalyConstraints = new List<HxExpression>(constraints.Size);
                for (int i = 0; i < constraints.Size; i++) {
                    HxExpression lhs = HexalyModel.Sum();
                    foreach (LinearConstraintCell cell in constraints.Lhs[i]) {
                        lhs.AddOperand(HexalyModel.Prod(cell.Coefficient.ToDouble(), _hexalyVars[cell.ColIndex]));
                    }

                    HxExpression constraint;
                    if (constraints.Signs[i] == ConstraintRelation.LessEqual) {
                        constraint = HexalyModel.Leq(lhs, constraints.Rhs[i].ToDouble());
                    }
                    else if (constraints.Signs[i] == ConstraintRelation.Equal) {
                        constraint = HexalyModel.Eq(lhs, constraints.Rhs[i].ToDouble());
                    }
                    else if (constraints.Signs[i] == ConstraintRelation.GreaterEqual) {
                        constraint = HexalyModel.Geq(lhs, constraints.Rhs[i].ToDouble());
                    }
                    else {
                        constraint = HexalyModel.Leq(lhs, constraints.Rhs[i].ToDouble());
                    }
                    HexalyModel.Constraint(constraint);
                    hexalyConstraints.Add(constraint);
                }
                _hexalyConstraints = hexalyConstraints;

                // Objective
                HxExpression obj = HexalyModel.Sum();
                foreach (LinearObjectiveCell cell in model.Objective.Cells) {
                    obj.AddOperand(HexalyModel.Prod(cell.Coefficient.ToDouble(), _hexalyVars[cell.ColIndex]));
                }
                obj.AddOperand(model.Objective.Constant.ToDouble());
                if (model.Objective.Category == ObjectCategory.Maximum) {
                    HexalyModel.Maximize(obj);
                }
                else {
                    HexalyModel.Minimize(obj);
                }
                _hexalyObjective = obj;

                // AfterModeling callback
                Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                    CallBackPoint.AfterModeling, null, Optimizer, _hexalyVars, _hexalyConstraints);
                if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return cbResult!;
                }

                return Results.OkInstance;
            }
            catch (HxException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException, e.Message)); }
            catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException)); }
        }

        private Result<Success, ErrorCode, Error<ErrorCode>> Configure(ILinearTriadModelView model) {
            try {
                // Time limit
                Optimizer.GetParam().SetTimeLimit((int)_config.DumpMechanismModelConcurrent.GetHashCode()); // placeholder

                // Configuration callback
                Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                    CallBackPoint.Configuration, null, Optimizer, _hexalyVars, _hexalyConstraints);
                if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return cbResult!;
                }

                return Results.OkInstance;
            }
            catch (HxException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException, e.Message)); }
            catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineModelingException)); }
        }

        private Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> AnalyzeSolution() {
            try {
                if (Status.Succeeded()) {
                    var results = new List<Flt64>(_hexalyVars.Count);
                    foreach (HxExpression hexalyVar in _hexalyVars) {
                        results.Add(new Flt64(hexalyVar.GetDoubleValue()));
                    }

                    _output = new FeasibleSolverOutput<Flt64>(
                        Obj: new Flt64(_hexalyObjective!.GetDoubleValue()),
                        Solution: new Solution<Flt64>(results),
                        Time: SolvingTime!.Value,
                        PossibleBestObj: new Flt64(HexalySolution.GetDoubleObjectiveBound(0)),
                        Gap: new Flt64(HexalySolution.GetObjectiveGap(0)));

                    // AnalyzingSolution callback
                    Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                        CallBackPoint.AnalyzingSolution, Status, Optimizer, _hexalyVars, _hexalyConstraints);
                    if (cbResult is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                        return PropagateError<FeasibleSolverOutput<Flt64>>(cbResult!);
                    }

                    return Results.Ok(_output);
                }
                else {
                    // AfterFailure callback
                    Result<Success, ErrorCode, Error<ErrorCode>>? cbResult = _callBack?.ExecIfContain(
                        CallBackPoint.AfterFailure, Status, Optimizer, _hexalyVars, _hexalyConstraints);

                    return FailByStatus<FeasibleSolverOutput<Flt64>>(Status);
                }
            }
            catch (HxException e) { return new Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
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
