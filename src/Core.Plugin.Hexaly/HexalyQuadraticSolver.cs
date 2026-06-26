#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Hexaly.Optimizer;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 二次求解器 / Hexaly quadratic solver.
/// </summary>
public sealed class HexalyQuadraticSolver : IQuadraticSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "hexaly";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly HexalySolverCallBack? _callBack;

    public HexalyQuadraticSolver(
        SolverConfig? config = null,
        HexalySolverCallBack? callBack = null) {
        Config = config ?? new HexalySolverConfig();
        _callBack = callBack;
    }

    /// <summary>
    /// 求解二次模型 / Solve quadratic model.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        using var impl = new HexalyQuadraticSolverImpl(Config, _callBack, solvingStatusCallBack);
        return await Task.FromResult(impl.Invoke(model));
    }

    /// <summary>
    /// 求解二次模型（含 IIS） / Solve quadratic model with IIS diagnostics.
    /// </summary>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default) {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await InvokeAsync(model, solvingStatusCallBack, cancellationToken);
        return result switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok => Results.Ok<SolverOutput>(ok.Value),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f => Results.Failed<SolverOutput>(f.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat => new Fatal<SolverOutput, ErrorCode, Error<ErrorCode>>(fat.Errors),
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>
    /// 求解二次模型获取多个解 / Solve quadratic model for multiple solutions.
    /// </summary>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
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

        return await InvokeAsync(model, 1, solvingStatusCallBack, cancellationToken);
    }

    /// <summary>
    /// 转储二次机制模型为四元组模型 / Dump quadratic mechanism model to tetrad model.
    /// </summary>
    public Task<QuadraticTetradModel> DumpAsync(QuadraticMechanismModel<Flt64> model, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Quadratic model dumping for Hexaly is not yet implemented.");

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
    /// Hexaly 二次求解器内部实现 / Hexaly quadratic solver internal implementation.
    /// </summary>
    private sealed class HexalyQuadraticSolverImpl : HexalySolver {
        private readonly SolverConfig _config;
        private readonly HexalySolverCallBack? _callBack;
        private readonly SolvingStatusCallBack? _statusCallBack;

        private List<HxExpression> _hexalyVars = new();
        private List<HxExpression> _hexalyConstraints = new();
        private HxExpression? _hexalyObjective;
        private FeasibleSolverOutput<Flt64>? _output;

        public HexalyQuadraticSolverImpl(
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
        public Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> Invoke(IQuadraticTetradModelView model) {
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

        private Result<Success, ErrorCode, Error<ErrorCode>> Dump(IQuadraticTetradModelView model) {
            try {
                // Variables
                var vars = new List<HxExpression>(model.Variables.Count);
                foreach (ModelViewVariable v in model.Variables) {
                    HexalyVariableType vtype = HexalyVariableExtensions.From(v.Type);
                    HxExpression var = vtype.CreateVariable(HexalyModel, v.LowerBound, v.UpperBound);
                    vars.Add(var);
                }
                _hexalyVars = vars;

                // Constraints (quadratic)
                ModelConstraint<QuadraticConstraintCell> constraints = model.Constraints;
                var hexalyConstraints = new List<HxExpression>(constraints.Size);
                for (int i = 0; i < constraints.Size; i++) {
                    HxExpression lhs = HexalyModel.Sum();
                    foreach (QuadraticConstraintCell cell in constraints.Lhs[i]) {
                        if (cell.Col2 < 0) {
                            // Linear term (Col2 = -1 sentinel)
                            lhs.AddOperand(HexalyModel.Prod(cell.Coefficient.ToDouble(), _hexalyVars[cell.Col1]));
                        }
                        else {
                            // Quadratic term
                            lhs.AddOperand(
                                HexalyModel.Prod(
                                    HexalyModel.Prod(cell.Coefficient.ToDouble(), _hexalyVars[cell.Col1]),
                                    _hexalyVars[cell.Col2]));
                        }
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

                // Objective (quadratic)
                HxExpression obj = HexalyModel.Sum();
                foreach (QuadraticObjectiveCell cell in model.Objective.Cells) {
                    if (cell.Col2 < 0) {
                        // Linear term (Col2 = -1 sentinel)
                        obj.AddOperand(HexalyModel.Prod(cell.Coefficient.ToDouble(), _hexalyVars[cell.Col1]));
                    }
                    else {
                        // Quadratic term
                        obj.AddOperand(
                            HexalyModel.Prod(
                                HexalyModel.Prod(cell.Coefficient.ToDouble(), _hexalyVars[cell.Col1]),
                                _hexalyVars[cell.Col2]));
                    }
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

        private Result<Success, ErrorCode, Error<ErrorCode>> Configure() {
            try {
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
