#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Scip;

internal static class ScipQuadraticNativeSolver {
    private const int Okay = 1;

    internal static readonly SolverCapabilities QuadraticCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Qp, SolverModelType.Qcp },
        Callback: true,
        Interrupt: true);

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Solve(
        ScipQuadraticSnapshot model,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(ScipLinearNativeSolver.Cancelled());
        }
        Result<ScipNative, ErrorCode, Error<ErrorCode>> loaded = ScipNative.Load();
        if (loaded is not Ok<ScipNative, ErrorCode, Error<ErrorCode>> ok) {
            return ScipLinearNativeSolver.Failure(
                ErrorCode.OREngineEnvironmentLost,
                "无法加载 SCIP / Cannot load SCIP");
        }
        using ScipNative native = ok.Value;
        IntPtr scip = IntPtr.Zero;
        IntPtr initialSolution = IntPtr.Zero;
        IntPtr[] constraints = new IntPtr[model.Rows.Length + model.QuadraticObjective.Length];
        IntPtr[] variables = new IntPtr[model.Columns.Length + model.QuadraticObjective.Length];
        var events = new ScipSolveEvents(
            native: native,
            token: cancellationToken,
            config: config,
            category: model.Category,
            variables: variables,
            variableCount: model.Columns.Length,
            statusCallBack: statusCallBack,
            sourceModel: sourceModel);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        string? cleanupFailure = null;
        try {
            result = Run(
                model: model,
                config: config,
                native: native,
                scip: ref scip,
                constraints: constraints,
                initialSolution: ref initialSolution,
                variables: variables,
                solutionAmount: solutionAmount,
                cancellationToken: cancellationToken,
                events: events.IsActive ? events : null);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            result = ScipLinearNativeSolver.Failure(
                ErrorCode.OREngineSolvingException,
                $"SCIP 调用失败：{error.Message} / SCIP call failed: {error.Message}");
        }
        finally {
            if (scip != IntPtr.Zero) {
                try {
                    if (initialSolution != IntPtr.Zero && native.FreeSolution(scip, ref initialSolution) != Okay) {
                        cleanupFailure = "SCIPfreeSol";
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                    cleanupFailure = $"SCIPfreeSol: {error.Message}";
                }
                for (int i = 0; i < constraints.Length; i++) {
                    try {
                        if (constraints[i] != IntPtr.Zero && native.ReleaseConstraint(scip, ref constraints[i]) != Okay) {
                            cleanupFailure ??= "SCIPreleaseCons";
                        }
                    }
                    catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                        cleanupFailure ??= $"SCIPreleaseCons: {error.Message}";
                    }
                }
                for (int i = 0; i < variables.Length; i++) {
                    try {
                        if (variables[i] != IntPtr.Zero && native.ReleaseVariable(scip, ref variables[i]) != Okay) {
                            cleanupFailure ??= "SCIPreleaseVar";
                        }
                    }
                    catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                        cleanupFailure ??= $"SCIPreleaseVar: {error.Message}";
                    }
                }
                try {
                    if (native.FreeScip(ref scip) != Okay) {
                        cleanupFailure ??= "SCIPfree";
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                    cleanupFailure ??= $"SCIPfree: {error.Message}";
                }
            }
            GC.KeepAlive(events);
        }
        return cleanupFailure is null ? result : ScipLinearNativeSolver.Failure(
            ErrorCode.OREngineSolvingException,
            $"SCIP 资源释放失败：{cleanupFailure} / SCIP resource release failed: {cleanupFailure}");
    }

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Run(
        ScipQuadraticSnapshot model,
        SolverConfig config,
        ScipNative native,
        ref IntPtr scip,
        IntPtr[] constraints,
        ref IntPtr initialSolution,
        IntPtr[] variables,
        int solutionAmount,
        CancellationToken cancellationToken,
        ScipSolveEvents? events) {
        if (cancellationToken.IsCancellationRequested) { return Results.Ok(ScipLinearNativeSolver.Cancelled()); }
        int code = native.CreateScip(ref scip);
        if (code != Okay || scip == IntPtr.Zero) {
            return ScipLinearNativeSolver.NativeFailure("SCIPcreate", code);
        }
        native.SetQuiet(scip, 1);
        code = native.IncludePlugins(scip);
        if (code != Okay) {
            return ScipLinearNativeSolver.NativeFailure("SCIPincludeDefaultPlugins", code);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> parameters = ScipLinearNativeSolver.ConfigureParameters(
            native: native,
            scip: scip,
            config: config,
            solutionAmount: solutionAmount);
        if (!parameters.IsOk) {
            return parameters.Bind<SolveReport<Flt64>>(_ => ScipLinearNativeSolver.Failure(
                ErrorCode.OREngineModelingException,
                "SCIP 原生参数设置失败 / SCIP native parameter setup failed"));
        }
        code = native.CreateProblem(scip, "ospf-quadratic");
        if (code != Okay) {
            return ScipLinearNativeSolver.NativeFailure("SCIPcreateProbBasic", code);
        }
        double infinity = native.Infinity(scip);
        if (!model.Fits(infinity)) {
            return ScipLinearNativeSolver.Failure(
                ErrorCode.IllegalArgument,
                "数值超出 SCIP 可表示范围 / Value exceeds the SCIP representable range");
        }
        code = native.SetObjectiveSense(scip, model.Category == ObjectCategory.Minimum ? 1 : -1);
        if (code != Okay) {
            return ScipLinearNativeSolver.NativeFailure("SCIPsetObjsense", code);
        }
        code = native.AddObjectiveOffset(scip, model.Constant);
        if (code != Okay) {
            return ScipLinearNativeSolver.NativeFailure("SCIPaddOrigObjoffset", code);
        }
        for (int i = 0; i < model.Columns.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(ScipLinearNativeSolver.Cancelled());
            }
            ScipColumn column = model.Columns[i];
            code = native.CreateVariable(
                scip: scip,
                variable: ref variables[i],
                name: $"x{i}",
                lower: double.IsNegativeInfinity(column.Lower) ? -infinity : column.Lower,
                upper: double.IsPositiveInfinity(column.Upper) ? infinity : column.Upper,
                objective: model.Objective[i],
                type: column.Type);
            if (code != Okay || variables[i] == IntPtr.Zero) {
                return ScipLinearNativeSolver.NativeFailure("SCIPcreateVarBasic", code);
            }
            code = native.AddVariable(scip, variables[i]);
            if (code != Okay) {
                return ScipLinearNativeSolver.NativeFailure("SCIPaddVar", code);
            }
        }
        for (int i = 0; i < model.Rows.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(ScipLinearNativeSolver.Cancelled());
            }
            ScipQuadraticRow row = model.Rows[i];
            double lower = row.Relation == ConstraintRelation.LessEqual ? -infinity : row.Right;
            double upper = row.Relation == ConstraintRelation.GreaterEqual ? infinity : row.Right;
            IntPtr[] linearVariables = row.LinearColumns.Select(index => variables[index]).ToArray();
            if (row.QuadraticTerms.Length == 0) {
                code = native.CreateLinearConstraint(
                    scip: scip,
                    constraint: ref constraints[i],
                    name: $"r{i}",
                    count: linearVariables.Length,
                    variables: linearVariables,
                    coefficients: row.LinearCoefficients,
                    lower: lower,
                    upper: upper);
            }
            else {
                code = native.CreateQuadraticConstraint(
                    scip: scip,
                    constraint: ref constraints[i],
                    name: $"r{i}",
                    linearCount: linearVariables.Length,
                    linearVariables: linearVariables,
                    linearCoefficients: row.LinearCoefficients,
                    quadraticCount: row.QuadraticTerms.Length,
                    quadraticVariables1: row.QuadraticTerms.Select(term => variables[term.Col1]).ToArray(),
                    quadraticVariables2: row.QuadraticTerms.Select(term => variables[term.Col2]).ToArray(),
                    quadraticCoefficients: row.QuadraticTerms.Select(term => term.Coefficient).ToArray(),
                    lower: lower,
                    upper: upper);
            }
            if (code != Okay || constraints[i] == IntPtr.Zero) {
                return ScipLinearNativeSolver.NativeFailure("SCIPcreateConsBasicQuadratic", code);
            }
            code = native.AddConstraint(scip, constraints[i]);
            if (code != Okay) {
                return ScipLinearNativeSolver.NativeFailure("SCIPaddCons", code);
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> start = ScipLinearNativeSolver.SubmitInitialSolution(
            native: native,
            scip: scip,
            columns: model.Columns,
            variables: variables[..model.Columns.Length],
            solution: ref initialSolution,
            cancellationToken: cancellationToken,
            stored: out _);
        if (start is Failed<Success, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.ApplicationStopped }
            && cancellationToken.IsCancellationRequested) {
            return Results.Ok(ScipLinearNativeSolver.Cancelled());
        }
        if (!start.IsOk) {
            return start.Bind<SolveReport<Flt64>>(_ => ScipLinearNativeSolver.Failure(
                ErrorCode.OREngineSolvingException,
                "SCIP 初始解提交失败 / SCIP initial solution submission failed"));
        }
        for (int i = 0; i < model.QuadraticObjective.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(ScipLinearNativeSolver.Cancelled());
            }
            ScipQuadraticTerm term = model.QuadraticObjective[i];
            int variableIndex = model.Columns.Length + i;
            int constraintIndex = model.Rows.Length + i;
            code = native.CreateVariable(
                scip: scip,
                variable: ref variables[variableIndex],
                name: $"q{i}",
                lower: -infinity,
                upper: infinity,
                objective: term.Coefficient,
                type: 3);
            if (code != Okay || variables[variableIndex] == IntPtr.Zero) {
                return ScipLinearNativeSolver.NativeFailure("SCIPcreateVarBasic", code);
            }
            code = native.AddVariable(scip, variables[variableIndex]);
            if (code != Okay) {
                return ScipLinearNativeSolver.NativeFailure("SCIPaddVar", code);
            }
            code = native.CreateQuadraticConstraint(
                scip: scip,
                constraint: ref constraints[constraintIndex],
                name: $"q{i}",
                linearCount: 1,
                linearVariables: [variables[variableIndex]],
                linearCoefficients: [-1.0],
                quadraticCount: 1,
                quadraticVariables1: [variables[term.Col1]],
                quadraticVariables2: [variables[term.Col2]],
                quadraticCoefficients: [1.0],
                lower: 0,
                upper: 0);
            if (code != Okay || constraints[constraintIndex] == IntPtr.Zero) {
                return ScipLinearNativeSolver.NativeFailure("SCIPcreateConsBasicQuadratic", code);
            }
            code = native.AddConstraint(scip, constraints[constraintIndex]);
            if (code != Okay) {
                return ScipLinearNativeSolver.NativeFailure("SCIPaddCons", code);
            }
        }
        if (events is not null) {
            code = events.Install(scip);
            if (code != Okay) {
                return ScipLinearNativeSolver.NativeFailure("SCIP cancellation event setup", code);
            }
        }
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(ScipLinearNativeSolver.Cancelled());
        }
        code = native.Solve(scip);
        if (events?.CallbackError is { } callbackError) {
            return ScipLinearNativeSolver.Failure(
                ErrorCode.OREngineSolvingException,
                $"SCIP 取消回调失败：{callbackError.Message} / SCIP cancellation callback failed: {callbackError.Message}");
        }
        if (code != Okay) {
            return ScipLinearNativeSolver.NativeFailure("SCIPsolve", code);
        }
        return ScipLinearNativeSolver.ReadReport(
            native: native,
            scip: scip,
            variables: variables[..model.Columns.Length],
            infinity: infinity,
            interruptedByCancellation: events?.Cancelled == true,
            config: config,
            solutionAmount: solutionAmount,
            capabilities: QuadraticCapabilities);
    }
}
