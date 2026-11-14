#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using ILOG.Concert;
using CplexSdk = ILOG.CPLEX.Cplex;
using Exception = System.Exception;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

internal static class CplexNativeSolver {
    private const double Infinity = 1e20;
    private static readonly double FreeBoundThreshold =
        (Flt64.One / NumericConstantsRegistry.For<Flt64>().DecimalPrecision!.Value).ToDouble();

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> SolveLinear(
        CplexLinearSnapshot snapshot,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        string name,
        CplexLinearDualCapture? dualCapture = null) {
        if (!Fits(snapshot)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "数值超出 CPLEX 可表示范围 / Value exceeds the CPLEX representable range");
        }
        return Run(
            config: config,
            name: name,
            quadratic: false,
            dump: model => DumpLinear(
                model: model,
                snapshot: snapshot),
            sourceModel: sourceModel,
            statusCallBack: statusCallBack,
            solutionAmount: solutionAmount,
            cancellationToken: cancellationToken,
            dualCapture: dualCapture);
    }

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> SolveQuadratic(
        CplexQuadraticSnapshot snapshot,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        string name) {
        if (!Fits(snapshot)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "数值超出 CPLEX 可表示范围 / Value exceeds the CPLEX representable range");
        }
        return Run(
            config: config,
            name: name,
            quadratic: true,
            dump: model => DumpQuadratic(
                model: model,
                snapshot: snapshot),
            sourceModel: sourceModel,
            statusCallBack: statusCallBack,
            solutionAmount: solutionAmount,
            cancellationToken: cancellationToken);
    }

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Run(
        SolverConfig config,
        string name,
        bool quadratic,
        Func<CplexSdk, Result<CplexNativeDump, ErrorCode, Error<ErrorCode>>> dump,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        CplexLinearDualCapture? dualCapture = null) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(CplexReportSupport.Cancelled());
        }
        CplexSdk? model = null;
        string stage = "environment";
        try {
            model = new CplexSdk {
                Name = name,
            };
            model.SetOut(TextWriter.Null);
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(CplexReportSupport.Cancelled());
            }

            stage = "modeling";
            Result<CplexNativeDump, ErrorCode, Error<ErrorCode>> dumped = dump(model);
            if (dumped is not Ok<CplexNativeDump, ErrorCode, Error<ErrorCode>> native) {
                return dumped.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "CPLEX 模型写入失败 / CPLEX model dump failed"));
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(CplexReportSupport.Cancelled());
            }

            stage = "configuration";
            bool isMip = native.Value.IsMip;
            bool callbackActive = statusCallBack is not null
                || config.NotImprovementTime is not null;
            Result<Success, ErrorCode, Error<ErrorCode>> configured = Configure(
                model: model,
                config: config,
                isMip: isMip,
                solutionAmount: solutionAmount,
                requireFarkas: dualCapture?.CaptureFarkas == true,
                callbackActive: callbackActive);
            if (!configured.IsOk) {
                return configured.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "CPLEX 参数配置失败 / CPLEX parameter configuration failed"));
            }

            using CplexSdk.Aborter aborter = model.Use(new CplexSdk.Aborter());
            using CancellationTokenRegistration registration =
                cancellationToken.Register(aborter.Abort);
            CplexSolveCallback? mipCallback = null;
            CplexContinuousCallback? continuousCallback = null;
            if (callbackActive) {
                if (isMip) {
                    mipCallback = new CplexSolveCallback(
                        solverModel: model,
                        variables: native.Value.Variables.ToArray(),
                        sourceModel: sourceModel,
                        config: config,
                        statusCallBack: statusCallBack,
                        cancellationToken: cancellationToken,
                        category: native.Value.Category);
                    model.Use(mipCallback);
                }
                else {
                    continuousCallback = new CplexContinuousCallback(
                        solverModel: model,
                        sourceModel: sourceModel,
                        config: config,
                        statusCallBack: statusCallBack,
                        cancellationToken: cancellationToken,
                        category: native.Value.Category);
                    model.Use(continuousCallback);
                }
            }

            stage = "solving";
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(CplexReportSupport.Cancelled());
            }
            var stopwatch = Stopwatch.StartNew();
            if (isMip && solutionAmount > 1) {
                model.Populate();
            }
            else {
                model.Solve();
            }
            stopwatch.Stop();

            Exception? callbackError = mipCallback?.CallbackError
                ?? continuousCallback?.CallbackError;
            if (callbackError is not null) {
                return Failure(
                    ErrorCode.OREngineSolvingException,
                    $"CPLEX 回调失败：{callbackError.Message} / " +
                    $"CPLEX callback failed: {callbackError.Message}");
            }

            stage = "analysis";
            string status = model.GetStatus().ToString() ?? "Unknown";
            string detailedStatus = model.GetCplexStatus().ToString() ?? "Unknown";
            Result<Success, ErrorCode, Error<ErrorCode>> captured = CaptureDuals(
                model: model,
                constraints: native.Value.Constraints,
                status: status,
                detailedStatus: detailedStatus,
                isMip: isMip,
                capture: dualCapture);
            if (!captured.IsOk) {
                return captured.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineSolvingException,
                    "CPLEX 对偶值读取失败 / CPLEX dual readback failed"));
            }
            Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> solution = ReadSolution(
                model: model,
                variables: native.Value.Variables.ToArray(),
                status: status,
                detailedStatus: detailedStatus,
                isMip: isMip,
                solutionAmount: solutionAmount);
            if (solution is not Ok<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> readable) {
                return solution.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineSolvingException,
                    "CPLEX 解读取失败 / CPLEX solution readback failed"));
            }
            SolveStatistics statistics = ReadStatistics(
                model: model,
                isMip: isMip,
                solution: readable.Value,
                elapsed: stopwatch.Elapsed);
            bool cancelled = cancellationToken.IsCancellationRequested
                || mipCallback?.Cancelled == true
                || continuousCallback?.Cancelled == true;
            bool interrupted = mipCallback?.Interrupted == true
                || continuousCallback?.Interrupted == true;
            return CplexReportSupport.Create(
                status: status,
                detailedStatus: detailedStatus,
                solution: readable.Value,
                statistics: statistics,
                cancelledDuringSolve: cancelled,
                interruptedDuringSolve: interrupted,
                config: config,
                quadratic: quadratic,
                nativeVersion: model.Version);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            ErrorCode code = stage switch {
                "environment" => ErrorCode.OREngineEnvironmentLost,
                "modeling" or "configuration" => ErrorCode.OREngineModelingException,
                _ => ErrorCode.OREngineSolvingException,
            };
            return Failure(
                code,
                $"CPLEX 调用失败（{stage}）：{error.Message} / " +
                $"CPLEX call failed ({stage}): {error.Message}");
        }
        finally {
            try { model?.End(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            try { model?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Configure(
        CplexSdk model,
        SolverConfig config,
        bool isMip,
        int solutionAmount,
        bool requireFarkas,
        bool callbackActive) {
        model.SetParam(CplexSdk.Param.TimeLimit, config.TimeLimit.TotalSeconds);
        model.SetParam(CplexSdk.Param.Threads, config.NativeThreadCount!.Value);
        model.SetParam(CplexSdk.Param.Output.WriteLevel, 0);
        if (isMip) {
            model.SetParam(
                CplexSdk.Param.MIP.Tolerances.MIPGap,
                config.Gap.ToDouble());
        }
        if (isMip && solutionAmount > 1) {
            model.SetParam(CplexSdk.Param.MIP.Pool.Capacity, solutionAmount);
            model.SetParam(CplexSdk.Param.MIP.Pool.Intensity, 4);
        }
        if (requireFarkas) {
            // CPLEX only exposes a Farkas ray without presolve and with dual preprocessing enabled.
            // 仅在关闭预求解并启用对偶预处理时，CPLEX 才会给出 Farkas 射线。
            model.SetParam(CplexSdk.Param.Preprocessing.Presolve, false);
            model.SetParam(CplexSdk.Param.Preprocessing.Dual, 1);
        }
        else if (callbackActive) {
            model.SetParam(CplexSdk.Param.Preprocessing.Presolve, false);
        }
        return Results.OkInstance;
    }

    private static Result<CplexNativeDump, ErrorCode, Error<ErrorCode>> DumpLinear(
        CplexSdk model,
        CplexLinearSnapshot snapshot) {
        var variables = new List<INumVar>(snapshot.Columns.Length);
        for (int index = 0; index < snapshot.Columns.Length; index++) {
            CplexColumn column = snapshot.Columns[index];
            variables.Add(model.NumVar(
                Lower(column.Lower),
                Upper(column.Upper),
                VariableType(column.Type),
                $"x{index}"));
        }
        var constraints = new List<IRange>(snapshot.Rows.Length);
        for (int row = 0; row < snapshot.Rows.Length; row++) {
            CplexRow source = snapshot.Rows[row];
            ILinearNumExpr expression = model.LinearNumExpr();
            for (int index = 0; index < source.Columns.Length; index++) {
                expression.AddTerm(
                    source.Coefficients[index],
                    variables[source.Columns[index]]);
            }
            constraints.Add(AddConstraint(
                model: model,
                expression: expression,
                relation: source.Relation,
                right: source.Right,
                name: $"r{row}"));
        }
        ILinearNumExpr objective = model.LinearNumExpr(snapshot.Constant);
        objective.AddTerms(snapshot.Objective, variables.ToArray());
        AddObjective(
            model: model,
            expression: objective,
            category: snapshot.Category,
            name: "objective");

        bool isMip = snapshot.Columns.Any(column => column.Type is 'I' or 'B');
        if (isMip) {
            AddInitialSolution(
                model: model,
                columns: snapshot.Columns,
                variables: variables);
        }
        return Results.Ok(new CplexNativeDump(
            Variables: Array.AsReadOnly(variables.ToArray()),
            Constraints: Array.AsReadOnly(constraints.ToArray()),
            Category: snapshot.Category,
            IsMip: isMip));
    }

    private static Result<CplexNativeDump, ErrorCode, Error<ErrorCode>> DumpQuadratic(
        CplexSdk model,
        CplexQuadraticSnapshot snapshot) {
        var variables = new List<INumVar>(snapshot.Columns.Length);
        for (int index = 0; index < snapshot.Columns.Length; index++) {
            CplexColumn column = snapshot.Columns[index];
            variables.Add(model.NumVar(
                Lower(column.Lower),
                Upper(column.Upper),
                VariableType(column.Type),
                $"x{index}"));
        }
        var constraints = new List<IRange>(snapshot.Rows.Length);
        for (int row = 0; row < snapshot.Rows.Length; row++) {
            CplexQuadraticRow source = snapshot.Rows[row];
            ILinearNumExpr linear = model.LinearNumExpr();
            for (int index = 0; index < source.LinearColumns.Length; index++) {
                linear.AddTerm(
                    source.LinearCoefficients[index],
                    variables[source.LinearColumns[index]]);
            }
            INumExpr expression = linear;
            if (source.QuadraticTerms.Length > 0) {
                IQuadNumExpr quadratic = model.QuadNumExpr();
                foreach (CplexQuadraticTerm term in source.QuadraticTerms) {
                    quadratic.AddTerm(
                        term.Coefficient,
                        variables[term.Col1],
                        variables[term.Col2]);
                }
                expression = model.Sum(linear, quadratic);
            }
            constraints.Add(AddConstraint(
                model: model,
                expression: expression,
                relation: source.Relation,
                right: source.Right,
                name: $"r{row}"));
        }
        ILinearNumExpr objectiveLinear = model.LinearNumExpr(snapshot.Constant);
        objectiveLinear.AddTerms(snapshot.Objective, variables.ToArray());
        INumExpr objective = objectiveLinear;
        if (snapshot.QuadraticObjective.Length > 0) {
            IQuadNumExpr quadratic = model.QuadNumExpr();
            foreach (CplexQuadraticTerm term in snapshot.QuadraticObjective) {
                quadratic.AddTerm(
                    term.Coefficient,
                    variables[term.Col1],
                    variables[term.Col2]);
            }
            objective = model.Sum(objectiveLinear, quadratic);
        }
        AddObjective(
            model: model,
            expression: objective,
            category: snapshot.Category,
            name: "objective");

        bool isMip = snapshot.Columns.Any(column => column.Type is 'I' or 'B');
        if (isMip) {
            AddInitialSolution(
                model: model,
                columns: snapshot.Columns,
                variables: variables);
        }
        return Results.Ok(new CplexNativeDump(
            Variables: Array.AsReadOnly(variables.ToArray()),
            Constraints: Array.AsReadOnly(constraints.ToArray()),
            Category: snapshot.Category,
            IsMip: isMip));
    }

    private static void AddInitialSolution(
        CplexSdk model,
        IReadOnlyList<CplexColumn> columns,
        IReadOnlyList<INumVar> variables) {
        KeyValuePair<int, CplexColumn>[] initial = columns
            .Select((column, index) => new KeyValuePair<int, CplexColumn>(index, column))
            .Where(entry => entry.Value.Initial is not null)
            .ToArray();
        if (initial.Length == 0) {
            return;
        }
        model.AddMIPStart(
            initial.Select(entry => variables[entry.Key]).ToArray(),
            initial.Select(entry => entry.Value.Initial!.Value).ToArray(),
            CplexSdk.MIPStartEffort.CheckFeas,
            "ospf-start");
    }

    private static IRange AddConstraint(
        CplexSdk model,
        INumExpr expression,
        ConstraintRelation relation,
        double right,
        string name) {
        if (relation == ConstraintRelation.LessEqual) {
            return model.AddLe(expression, right, name);
        }
        if (relation == ConstraintRelation.GreaterEqual) {
            return model.AddGe(expression, right, name);
        }
        return model.AddEq(expression, right, name);
    }

    private static void AddObjective(
        CplexSdk model,
        INumExpr expression,
        ObjectCategory category,
        string name) {
        if (category == ObjectCategory.Minimum) {
            model.AddMinimize(expression, name);
        }
        else {
            model.AddMaximize(expression, name);
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> CaptureDuals(
        CplexSdk model,
        IReadOnlyList<IRange> constraints,
        string status,
        string detailedStatus,
        bool isMip,
        CplexLinearDualCapture? capture) {
        if (capture is null) {
            return Results.OkInstance;
        }
        if (isMip) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "CPLEX 对偶值只支持连续松弛 / CPLEX dual values require a continuous relaxation"));
        }
        bool infeasible = status == "Infeasible"
            || detailedStatus is "Infeasible" or "OptimalInfeas";
        if (infeasible) {
            if (!capture.CaptureFarkas) {
                return Results.OkInstance;
            }
            IConstraint[] nativeConstraints = constraints.Cast<IConstraint>().ToArray();
            double[] values = new double[nativeConstraints.Length];
            double norm = model.DualFarkas(nativeConstraints, values);
            if (!double.IsFinite(norm)
                || values.Any(value => !double.IsFinite(value))
                || values.All(value => value == 0)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "CPLEX 未提供可用的 Farkas 证书 / CPLEX did not provide a usable Farkas certificate"));
            }
            capture.FarkasDuals = Array.AsReadOnly(
                values.Select(value => new Flt64(value)).ToArray());
            return Results.OkInstance;
        }
        if (status is not "Optimal" and not "Feasible"
            && detailedStatus is not "Optimal" and not "OptimalTol") {
            return Results.OkInstance;
        }
        IRange[] nativeRanges = constraints.ToArray();
        double[] duals = model.GetDuals(nativeRanges);
        if (duals.Any(value => !double.IsFinite(value))) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "CPLEX 行对偶值包含非有限数 / CPLEX row duals contain a nonfinite value"));
        }
        capture.RowDuals = Array.AsReadOnly(
            duals.Select(value => new Flt64(value)).ToArray());
        return Results.OkInstance;
    }

    private static Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> ReadSolution(
        CplexSdk model,
        INumVar[] variables,
        string status,
        string detailedStatus,
        bool isMip,
        int solutionAmount) {
        if (!CplexReportSupport.HasSolution(status, detailedStatus)) {
            return Results.Ok<SolveSolution<Flt64>?>(null);
        }
        double[] values = model.GetValues(variables);
        if (values.Any(value => !double.IsFinite(value))) {
            return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "CPLEX 解包含非有限值 / CPLEX solution contains a nonfinite value"));
        }
        double objective = model.GetObjValue();
        if (!double.IsFinite(objective)) {
            return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "CPLEX 目标值不是有限数 / CPLEX objective is not finite"));
        }
        SolveSolution<Flt64> solution = new(
            Values: new Solution<Flt64>(Array.AsReadOnly(
                values.Select(value => new Flt64(value)).ToArray())),
            Objective: new Flt64(objective));
        if (!isMip || solutionAmount <= 1) {
            return Results.Ok<SolveSolution<Flt64>?>(solution);
        }

        int poolCount = System.Math.Min(
            solutionAmount,
            System.Math.Max(0, model.GetSolnPoolNsolns()));
        var pool = new List<Solution<Flt64>>(poolCount);
        for (int index = 0; index < poolCount; index++) {
            double[] poolValues = model.GetValues(variables, index);
            if (poolValues.Any(value => !double.IsFinite(value))) {
                return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "CPLEX 解池包含非有限值 / CPLEX solution pool contains a nonfinite value"));
            }
            Flt64[] poolSolution = poolValues
                .Select(value => new Flt64(value))
                .ToArray();
            if (!solution.Values.Values.SequenceEqual(poolSolution)
                && !pool.Exists(existing => existing.Values.SequenceEqual(poolSolution))) {
                pool.Add(new Solution<Flt64>(Array.AsReadOnly(poolSolution)));
            }
        }
        return Results.Ok<SolveSolution<Flt64>?>(solution with {
            Pool = Array.AsReadOnly(pool.ToArray()),
        });
    }

    private static SolveStatistics ReadStatistics(
        CplexSdk model,
        bool isMip,
        SolveSolution<Flt64>? solution,
        TimeSpan elapsed) {
        double? bound = isMip
            ? TryDouble(model.GetBestObjValue)
            : solution?.Objective?.ToDouble();
        double? gap = isMip && solution is not null
            ? TryDouble(model.GetMIPRelativeGap)
            : !isMip && solution is not null
                ? 0.0
                : null;
        return new SolveStatistics(
            SolveTime: elapsed >= TimeSpan.Zero ? elapsed : null,
            Iterations: Count(model.Niterations64),
            Nodes: isMip ? Count(model.Nnodes64) : null,
            BestBound: Finite(bound),
            Gap: Finite(gap));
    }

    private static double? TryDouble(Func<double> read) {
        try {
            double value = read();
            return double.IsFinite(value) ? value : null;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return null;
        }
    }

    private static ulong? Count(long value) => value >= 0 ? (ulong)value : null;

    private static Flt64? Finite(double? value) =>
        value is { } number && double.IsFinite(number) && System.Math.Abs(number) < Infinity
            ? new Flt64(number)
            : null;

    private static NumVarType VariableType(char type) =>
        type switch {
            'I' => NumVarType.Int,
            'B' => NumVarType.Bool,
            _ => NumVarType.Float,
        };

    private static double Lower(double value) =>
        double.IsNegativeInfinity(value) || value <= -FreeBoundThreshold
            ? -Infinity
            : value;

    private static double Upper(double value) =>
        double.IsPositiveInfinity(value) || value >= FreeBoundThreshold
            ? Infinity
            : value;

    private static bool Fits(CplexLinearSnapshot snapshot) =>
        System.Math.Abs(snapshot.Constant) < Infinity
        && snapshot.Objective.All(value => System.Math.Abs(value) < Infinity)
        && snapshot.Rows.All(row => System.Math.Abs(row.Right) < Infinity
            && row.Coefficients.All(value => System.Math.Abs(value) < Infinity))
        && snapshot.Columns.All(column => BoundFits(column.Lower, lower: true)
            && BoundFits(column.Upper, lower: false)
            && (column.Initial is null || System.Math.Abs(column.Initial.Value) < Infinity));

    private static bool Fits(CplexQuadraticSnapshot snapshot) =>
        System.Math.Abs(snapshot.Constant) < Infinity
        && snapshot.Objective.All(value => System.Math.Abs(value) < Infinity)
        && snapshot.QuadraticObjective.All(term => System.Math.Abs(term.Coefficient) < Infinity)
        && snapshot.Rows.All(row => System.Math.Abs(row.Right) < Infinity
            && row.LinearCoefficients.All(value => System.Math.Abs(value) < Infinity)
            && row.QuadraticTerms.All(term => System.Math.Abs(term.Coefficient) < Infinity))
        && snapshot.Columns.All(column => BoundFits(column.Lower, lower: true)
            && BoundFits(column.Upper, lower: false)
            && (column.Initial is null || System.Math.Abs(column.Initial.Value) < Infinity));

    private static bool BoundFits(double value, bool lower) {
        if (double.IsNaN(value)) {
            return false;
        }
        if (lower
            ? double.IsNegativeInfinity(value) || value <= -FreeBoundThreshold
            : double.IsPositiveInfinity(value) || value >= FreeBoundThreshold) {
            return true;
        }
        return double.IsFinite(value) && System.Math.Abs(value) < Infinity;
    }

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(code, message));
}

internal sealed record CplexNativeDump(
    IReadOnlyList<INumVar> Variables,
    IReadOnlyList<IRange> Constraints,
    ObjectCategory Category,
    bool IsMip);
