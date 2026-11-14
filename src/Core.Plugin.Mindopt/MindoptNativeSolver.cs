#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Mindopt;

namespace Fuookami.Ospf.Core.Plugin.Mindopt;

internal sealed record MindoptNativeDump(
    MDOVar[] Variables,
    MDOConstr[] Constraints,
    MDOQConstr[] QuadraticConstraints,
    bool IsMip,
    ObjectCategory Category);

internal static class MindoptNativeSolver {
    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> SolveLinear(
        MindoptLinearSnapshot snapshot,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        string name,
        MindoptLinearDualCapture? dualCapture = null) {
        if (!Fits(snapshot)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "数值超出 MindOPT 可表示范围 / Value exceeds the MindOPT representable range");
        }
        return Run(
            config: config,
            name: name,
            quadratic: false,
            dump: model => DumpLinear(model: model, snapshot: snapshot),
            sourceModel: sourceModel,
            statusCallBack: statusCallBack,
            solutionAmount: solutionAmount,
            cancellationToken: cancellationToken,
            dualCapture: dualCapture);
    }

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> SolveQuadratic(
        MindoptQuadraticSnapshot snapshot,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        string name) {
        if (!Fits(snapshot)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "数值超出 MindOPT 可表示范围 / Value exceeds the MindOPT representable range");
        }
        return Run(
            config: config,
            name: name,
            quadratic: true,
            dump: model => DumpQuadratic(model: model, snapshot: snapshot),
            sourceModel: sourceModel,
            statusCallBack: statusCallBack,
            solutionAmount: solutionAmount,
            cancellationToken: cancellationToken);
    }

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Run(
        SolverConfig config,
        string name,
        bool quadratic,
        Func<MDOModel, Result<MindoptNativeDump, ErrorCode, Error<ErrorCode>>> dump,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        MindoptLinearDualCapture? dualCapture = null) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(MindoptReportSupport.Cancelled(config, quadratic));
        }
        string stage = "environment";
        MDOEnv? env = null;
        MDOModel? model = null;
        try {
            env = new MDOEnv();
            model = new MDOModel(env, name);
            model.Set(MDO.IntParam.OutputFlag, 0);
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(MindoptReportSupport.Cancelled(config, quadratic));
            }

            stage = "modeling";
            Result<MindoptNativeDump, ErrorCode, Error<ErrorCode>> dumped = dump(model);
            if (dumped is not Ok<MindoptNativeDump, ErrorCode, Error<ErrorCode>> native) {
                return dumped.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "MindOPT 模型写入失败 / MindOPT model dump failed"));
            }

            stage = "configuration";
            MindoptNativeDump dumpResult = native.Value;
            Result<Success, ErrorCode, Error<ErrorCode>> configured = Configure(
                model: model,
                config: config,
                isMip: dumpResult.IsMip,
                solutionAmount: solutionAmount,
                requireFarkas: dualCapture?.CaptureFarkas == true);
            if (!configured.IsOk) {
                return configured.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "MindOPT 参数配置失败 / MindOPT parameter configuration failed"));
            }

            MindoptSolveCallback? callback = null;
            CancellationTokenRegistration registration = default;
            if (dumpResult.IsMip
                && (statusCallBack is not null
                    || config.NotImprovementTime is not null
                    || cancellationToken.CanBeCanceled)) {
                callback = new MindoptSolveCallback(
                    model: model,
                    variables: dumpResult.Variables,
                    sourceModel: sourceModel,
                    config: config,
                    statusCallBack: statusCallBack,
                    category: dumpResult.Category);
                model.SetCallback(callback);
                registration = cancellationToken.Register(callback.Cancel);
            }

            try {
                stage = "solving";
                if (cancellationToken.IsCancellationRequested) {
                    return Results.Ok(MindoptReportSupport.Cancelled(config, quadratic));
                }
                var stopwatch = Stopwatch.StartNew();
                model.Optimize();
                stopwatch.Stop();

                if (callback?.CallbackError is { } callbackError) {
                    return Failure(
                        ErrorCode.OREngineSolvingException,
                        $"MindOPT 回调失败：{callbackError.Message} / " +
                        $"MindOPT callback failed: {callbackError.Message}");
                }

                stage = "analysis";
                int status = model.Get(MDO.IntAttr.Status);
                int solutionCount = TryGet(model, MDO.IntAttr.SolCount) ?? 0;
                bool hasSolution = (TryGet(model, MDO.IntAttr.HasSolution) ?? 0) > 0;
                Result<Success, ErrorCode, Error<ErrorCode>> captured = CaptureDuals(
                    model: model,
                    constraints: dumpResult.Constraints,
                    status: status,
                    capture: dualCapture);
                if (!captured.IsOk) {
                    return captured.Bind<SolveReport<Flt64>>(_ => Failure(
                        ErrorCode.OREngineSolvingException,
                        "MindOPT 对偶值读取失败 / MindOPT dual readback failed"));
                }
                Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> solution = ReadSolution(
                    model: model,
                    variables: dumpResult.Variables,
                    status: status,
                    solutionCount: solutionCount,
                    hasSolution: hasSolution,
                    solutionAmount: solutionAmount,
                    isMip: dumpResult.IsMip);
                if (solution is not Ok<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> readable) {
                    return solution.Bind<SolveReport<Flt64>>(_ => Failure(
                        ErrorCode.OREngineSolvingException,
                        "MindOPT 解读取失败 / MindOPT solution readback failed"));
                }
                SolveStatistics statistics = ReadStatistics(
                    model: model,
                    isMip: dumpResult.IsMip,
                    solution: readable.Value,
                    elapsed: stopwatch.Elapsed,
                    nodeCount: callback?.NodeCount);
                bool cancelled = cancellationToken.IsCancellationRequested
                    || callback?.Cancelled == true;
                bool interrupted = callback?.Interrupted == true;
                return MindoptReportSupport.Create(
                    status: status,
                    solution: readable.Value,
                    statistics: statistics,
                    cancelledDuringSolve: cancelled,
                    interruptedDuringSolve: interrupted,
                    config: config,
                    quadratic: quadratic);
            }
            finally {
                registration.Dispose();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            ErrorCode code = stage switch {
                "environment" => ErrorCode.OREngineEnvironmentLost,
                "modeling" or "configuration" => ErrorCode.OREngineModelingException,
                _ => ErrorCode.OREngineSolvingException,
            };
            return Failure(
                code,
                $"MindOPT 调用失败（{stage}）：{error.Message} / " +
                $"MindOPT call failed ({stage}): {error.Message}");
        }
        finally {
            try { model?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            try { env?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Configure(
        MDOModel model,
        SolverConfig config,
        bool isMip,
        int solutionAmount,
        bool requireFarkas) {
        model.Set(MDO.DoubleParam.MaxTime, config.TimeLimit.TotalSeconds);
        model.Set(MDO.DoubleParam.MIP_GapAbs, config.Gap.ToDouble());
        model.Set(MDO.IntParam.NumThreads, config.NativeThreadCount!.Value);
        if (isMip && solutionAmount > 1) {
            model.Set(MDO.IntParam.MIP_SolutionPoolSize, solutionAmount);
        }
        if (requireFarkas) {
            // MindOPT exposes a dual ray before presolve reductions are applied.
            // MindOPT 在预求解前提供对偶射线，因此证书路径关闭预求解。
            model.Set(MDO.IntParam.Presolve, 0);
        }
        return Results.OkInstance;
    }

    private static Result<MindoptNativeDump, ErrorCode, Error<ErrorCode>> DumpLinear(
        MDOModel model,
        MindoptLinearSnapshot snapshot) {
        MDOVar[] variables = AddVariables(model, snapshot.Columns);
        var constraints = new MDOConstr[snapshot.Rows.Length];
        for (int row = 0; row < snapshot.Rows.Length; row++) {
            MindoptRow source = snapshot.Rows[row];
            var expression = new MDOLinExpr();
            for (int index = 0; index < source.Columns.Length; index++) {
                expression.AddTerm(
                    source.Coefficients[index],
                    variables[source.Columns[index]]);
            }
            constraints[row] = model.AddConstr(
                expression,
                Sign(source.Relation),
                source.Right,
                $"r{row}");
        }
        var objective = new MDOLinExpr(snapshot.Constant);
        for (int column = 0; column < snapshot.Objective.Length; column++) {
            objective.AddTerm(snapshot.Objective[column], variables[column]);
        }
        model.SetObjective(objective, Sense(snapshot.Category));
        return Results.Ok(new MindoptNativeDump(
            Variables: variables,
            Constraints: constraints,
            QuadraticConstraints: [],
            IsMip: snapshot.Columns.Any(column => column.Type is 'I' or 'B'),
            Category: snapshot.Category));
    }

    private static Result<MindoptNativeDump, ErrorCode, Error<ErrorCode>> DumpQuadratic(
        MDOModel model,
        MindoptQuadraticSnapshot snapshot) {
        MDOVar[] variables = AddVariables(model, snapshot.Columns);
        var constraints = new MDOQConstr[snapshot.Rows.Length];
        for (int row = 0; row < snapshot.Rows.Length; row++) {
            MindoptQuadraticRow source = snapshot.Rows[row];
            var expression = new MDOQuadExpr();
            for (int index = 0; index < source.LinearColumns.Length; index++) {
                expression.AddTerm(
                    source.LinearCoefficients[index],
                    variables[source.LinearColumns[index]]);
            }
            foreach (MindoptQuadraticTerm term in source.QuadraticTerms) {
                expression.AddTerm(
                    term.Coefficient,
                    variables[term.Col1],
                    variables[term.Col2]);
            }
            constraints[row] = model.AddQConstr(
                expression,
                Sign(source.Relation),
                source.Right,
                $"r{row}");
        }
        var objective = new MDOQuadExpr(snapshot.Constant);
        for (int column = 0; column < snapshot.Objective.Length; column++) {
            objective.AddTerm(snapshot.Objective[column], variables[column]);
        }
        foreach (MindoptQuadraticTerm term in snapshot.QuadraticObjective) {
            objective.AddTerm(
                term.Coefficient,
                variables[term.Col1],
                variables[term.Col2]);
        }
        model.SetObjective(objective, Sense(snapshot.Category));
        return Results.Ok(new MindoptNativeDump(
            Variables: variables,
            Constraints: [],
            QuadraticConstraints: constraints,
            IsMip: snapshot.Columns.Any(column => column.Type is 'I' or 'B'),
            Category: snapshot.Category));
    }

    private static MDOVar[] AddVariables(
        MDOModel model,
        IReadOnlyList<MindoptColumn> columns) {
        var variables = new MDOVar[columns.Count];
        for (int index = 0; index < columns.Count; index++) {
            MindoptColumn column = columns[index];
            variables[index] = model.AddVar(
                Bound(column.Lower),
                Bound(column.Upper),
                0,
                column.Type,
                $"x{index}");
            if (column.Initial is { } initial) {
                variables[index].Set(MDO.DoubleAttr.Start, initial);
            }
        }
        return variables;
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> CaptureDuals(
        MDOModel model,
        IReadOnlyList<MDOConstr> constraints,
        int status,
        MindoptLinearDualCapture? capture) {
        if (capture is null || constraints.Count == 0) {
            return Results.OkInstance;
        }
        if (status == MindoptStatus.Infeasible) {
            if (!capture.CaptureFarkas) {
                return Results.OkInstance;
            }
            if ((TryGet(model, MDO.IntAttr.HasDualRay) ?? 0) <= 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "MindOPT 未提供对偶射线 / MindOPT did not expose a dual ray"));
            }
            Flt64[] farkas = ReadDuals(constraints);
            if (farkas.All(value => value == Flt64.Zero)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "MindOPT 未提供可用的 Farkas 证书 / MindOPT did not provide a usable Farkas certificate"));
            }
            capture.FarkasDuals = farkas;
            return Results.OkInstance;
        }
        if (status is MindoptStatus.Optimal or MindoptStatus.SubOptimal) {
            capture.RowDuals = ReadDuals(constraints);
        }
        return Results.OkInstance;
    }

    private static Flt64[] ReadDuals(IReadOnlyList<MDOConstr> constraints) {
        var duals = new Flt64[constraints.Count];
        for (int row = 0; row < constraints.Count; row++) {
            double value = constraints[row].Get(MDO.DoubleAttr.DualSoln);
            if (!double.IsFinite(value)) {
                throw new InvalidOperationException(
                    $"MindOPT returned a nonfinite dual at row {row}");
            }
            duals[row] = new Flt64(value);
        }
        return duals;
    }

    private static Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> ReadSolution(
        MDOModel model,
        IReadOnlyList<MDOVar> variables,
        int status,
        int solutionCount,
        bool hasSolution,
        int solutionAmount,
        bool isMip) {
        if (status is MindoptStatus.Infeasible
            or MindoptStatus.Unbounded
            or MindoptStatus.InfeasibleOrUnbounded) {
            return Results.Ok<SolveSolution<Flt64>?>(null);
        }
        if (!hasSolution && solutionCount <= 0 && status != MindoptStatus.Optimal) {
            return Results.Ok<SolveSolution<Flt64>?>(null);
        }
        try {
            Flt64[] values = variables
                .Select(variable => new Flt64(variable.Get(MDO.DoubleAttr.X)))
                .ToArray();
            var objective = new Flt64(model.Get(MDO.DoubleAttr.ObjVal));
            IReadOnlyList<Solution<Flt64>> pool = isMip && solutionAmount > 1
                ? ReadPool(
                    model: model,
                    variables: variables,
                    solutionCount: solutionCount,
                    solutionAmount: solutionAmount,
                    main: values)
                : [];
            return Results.Ok<SolveSolution<Flt64>?>(new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(values),
                Objective: objective,
                Pool: pool));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                $"无法读取 MindOPT 解：{error.Message} / " +
                $"Cannot read the MindOPT solution: {error.Message}"));
        }
    }

    private static IReadOnlyList<Solution<Flt64>> ReadPool(
        MDOModel model,
        IReadOnlyList<MDOVar> variables,
        int solutionCount,
        int solutionAmount,
        IReadOnlyList<Flt64> main) {
        var pool = new List<Solution<Flt64>>();
        var seen = new HashSet<string>(StringComparer.Ordinal) {
            Key(main),
        };
        int count = System.Math.Min(solutionCount, solutionAmount);
        for (int index = 0; index < count; index++) {
            model.Set(MDO.IntParam.MIP_SolutionNumber, index);
            Flt64[] values = variables
                .Select(variable => new Flt64(variable.Get(MDO.DoubleAttr.Xn)))
                .ToArray();
            if (seen.Add(Key(values))) {
                pool.Add(new Solution<Flt64>(values));
            }
        }
        return pool;
    }

    private static SolveStatistics ReadStatistics(
        MDOModel model,
        bool isMip,
        SolveSolution<Flt64>? solution,
        TimeSpan elapsed,
        ulong? nodeCount) {
        double nativeTime = TryGet(model, MDO.DoubleAttr.SolverTime) ?? double.NaN;
        TimeSpan solveTime = double.IsFinite(nativeTime) && nativeTime >= 0
            ? TimeSpan.FromSeconds(nativeTime)
            : elapsed;
        int simplex = TryGet(model, MDO.IntAttr.SPX_NumIters) ?? 0;
        int interior = TryGet(model, MDO.IntAttr.IPM_NumIters) ?? 0;
        ulong? iterations = simplex > 0 || interior > 0
            ? (ulong)System.Math.Max(simplex, interior)
            : null;
        Flt64? bestBound = null;
        Flt64? gap = null;
        if (solution is not null) {
            double bound = isMip
                ? TryGet(model, MDO.DoubleAttr.DualObjVal) ?? double.NaN
                : TryGet(model, MDO.DoubleAttr.ObjVal) ?? double.NaN;
            if (double.IsFinite(bound)) {
                bestBound = new Flt64(bound);
            }
            double nativeGap = isMip ? TryGet(model, MDO.DoubleAttr.MIP_GapAbs) ?? 0 : 0;
            if (double.IsFinite(nativeGap)) {
                gap = new Flt64(nativeGap);
            }
        }
        return new SolveStatistics(
            SolveTime: solveTime,
            Iterations: iterations,
            Nodes: nodeCount,
            BestBound: bestBound,
            Gap: gap);
    }

    private static int? TryGet(MDOModel model, MDO.IntAttr attribute) {
        try {
            return model.Get(attribute);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return null;
        }
    }

    private static double? TryGet(MDOModel model, MDO.DoubleAttr attribute) {
        try {
            return model.Get(attribute);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return null;
        }
    }

    private static char Sign(ConstraintRelation relation) =>
        relation == ConstraintRelation.LessEqual
            ? MDO.LESS_EQUAL
            : relation == ConstraintRelation.GreaterEqual
                ? MDO.GREATER_EQUAL
                : MDO.EQUAL;

    private static int Sense(ObjectCategory category) =>
        category == ObjectCategory.Minimum ? MDO.MINIMIZE : MDO.MAXIMIZE;

    private static double Bound(double value) =>
        value switch {
            double.PositiveInfinity => MDO.INFINITY,
            double.NegativeInfinity => -MDO.INFINITY,
            _ => value,
        };

    private static bool Fits(MindoptLinearSnapshot snapshot) =>
        snapshot.Columns.All(Fits)
        && snapshot.Rows.All(row =>
            !double.IsNaN(row.Right)
            && System.Math.Abs(row.Right) <= MDO.INFINITY
            && row.Coefficients.All(value => System.Math.Abs(value) <= MDO.INFINITY))
        && snapshot.Objective.All(value => System.Math.Abs(value) <= MDO.INFINITY)
        && System.Math.Abs(snapshot.Constant) <= MDO.INFINITY;

    private static bool Fits(MindoptQuadraticSnapshot snapshot) =>
        snapshot.Columns.All(Fits)
        && snapshot.Rows.All(row =>
            !double.IsNaN(row.Right)
            && System.Math.Abs(row.Right) <= MDO.INFINITY
            && row.LinearCoefficients.All(value => System.Math.Abs(value) <= MDO.INFINITY)
            && row.QuadraticTerms.All(term => System.Math.Abs(term.Coefficient) <= MDO.INFINITY))
        && snapshot.Objective.All(value => System.Math.Abs(value) <= MDO.INFINITY)
        && snapshot.QuadraticObjective.All(term => System.Math.Abs(term.Coefficient) <= MDO.INFINITY)
        && System.Math.Abs(snapshot.Constant) <= MDO.INFINITY;

    private static bool Fits(MindoptColumn column) =>
        (double.IsNegativeInfinity(column.Lower)
            || double.IsFinite(column.Lower) && System.Math.Abs(column.Lower) <= MDO.INFINITY)
        && (double.IsPositiveInfinity(column.Upper)
            || double.IsFinite(column.Upper) && System.Math.Abs(column.Upper) <= MDO.INFINITY)
        && (column.Initial is not { } initial
            || double.IsFinite(initial) && System.Math.Abs(initial) <= MDO.INFINITY);

    private static string Key(IReadOnlyList<Flt64> values) =>
        string.Join(
            separator: ",",
            values.Select(value => value.ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(code, message));
}
