#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using CoptSdk = Copt;

namespace Fuookami.Ospf.Core.Plugin.Copt;

internal static class CoptNativeSolver {
    private const double Infinity = 1e30;
    private static readonly double FreeBoundThreshold =
        (Flt64.One / NumericConstantsRegistry.For<Flt64>().DecimalPrecision!.Value).ToDouble();
    // MIPSOL | MIP_NODE，兼顾 incumbent 通知与节点进度。 / MIPSOL | MIP_NODE for incumbent and node progress.
    private const int CallbackContext = 2 | 4;

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> SolveLinear(
        CoptLinearSnapshot snapshot,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        string name,
        CoptLinearDualCapture? dualCapture = null) {
        if (!Fits(snapshot)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "数值超出 COPT 可表示范围 / Value exceeds the COPT representable range");
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
        CoptQuadraticSnapshot snapshot,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        string name) {
        if (!Fits(snapshot)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "数值超出 COPT 可表示范围 / Value exceeds the COPT representable range");
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
        Func<CoptSdk.Model, Result<CoptNativeDump, ErrorCode, Error<ErrorCode>>> dump,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        CoptLinearDualCapture? dualCapture = null) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(CoptReportSupport.Cancelled());
        }
        CoptSdk.Envr? environment = null;
        CoptSdk.Model? model = null;
        string stage = "environment";
        try {
            Result<(CoptSdk.Envr Env, CoptSdk.Model Model), ErrorCode, Error<ErrorCode>> created =
                CreateEnvironment(backend: config.BackendConfiguration, name: name);
            if (created is not Ok<(CoptSdk.Envr Env, CoptSdk.Model Model), ErrorCode, Error<ErrorCode>> success) {
                return created.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineEnvironmentLost,
                    "COPT 环境初始化失败 / COPT environment initialization failed"));
            }
            environment = success.Value.Env;
            model = success.Value.Model;
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(CoptReportSupport.Cancelled());
            }

            stage = "modeling";
            Result<CoptNativeDump, ErrorCode, Error<ErrorCode>> dumped = dump(model);
            if (dumped is not Ok<CoptNativeDump, ErrorCode, Error<ErrorCode>> native) {
                return dumped.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "COPT 模型写入失败 / COPT model dump failed"));
            }
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(CoptReportSupport.Cancelled());
            }

            stage = "configuration";
            Result<Success, ErrorCode, Error<ErrorCode>> configured = Configure(
                model: model,
                config: config,
                requireFarkas: dualCapture?.CaptureFarkas == true);
            if (!configured.IsOk) {
                return configured.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "COPT 参数配置失败 / COPT parameter configuration failed"));
            }
            CoptSolveCallback? callback = null;
            bool callbackActive = cancellationToken.CanBeCanceled
                || statusCallBack is not null
                || config.NotImprovementTime is not null;
            if (callbackActive) {
                callback = new CoptSolveCallback(
                    model: model,
                    variables: native.Value.Variables,
                    sourceModel: sourceModel,
                    config: config,
                    statusCallBack: statusCallBack,
                    cancellationToken: cancellationToken);
                model.SetCallback(callback, CallbackContext);
            }

            stage = "solving";
            if (cancellationToken.IsCancellationRequested) {
                return Results.Ok(CoptReportSupport.Cancelled());
            }
            bool isMip = model.GetIntAttr("IsMIP") != 0;
            if (isMip) {
                model.Solve();
            }
            else {
                model.SolveLp();
            }
            if (callback?.CallbackError is { } callbackError) {
                return Failure(
                    ErrorCode.OREngineSolvingException,
                    $"COPT 回调失败：{callbackError.Message} / COPT callback failed: {callbackError.Message}");
            }

            stage = "analysis";
            int nativeStatus = model.GetIntAttr(isMip ? "MipStatus" : "LpStatus");
            Result<Success, ErrorCode, Error<ErrorCode>> captured = CaptureDuals(
                model: model,
                constraints: native.Value.Constraints,
                isMip: isMip,
                nativeStatus: nativeStatus,
                capture: dualCapture);
            if (!captured.IsOk) {
                return captured.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineSolvingException,
                    "COPT 对偶值读取失败 / COPT dual readback failed"));
            }
            Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> solution = ReadSolution(
                model: model,
                variables: native.Value.Variables,
                nativeStatus: nativeStatus,
                isMip: isMip,
                solutionAmount: solutionAmount);
            if (solution is not Ok<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> readable) {
                return solution.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineSolvingException,
                    "COPT 解读取失败 / COPT solution readback failed"));
            }
            SolveStatistics statistics = ReadStatistics(
                model: model,
                isMip: isMip,
                nativeStatus: nativeStatus,
                solution: readable.Value);
            return CoptReportSupport.Create(
                nativeStatus: nativeStatus,
                solution: readable.Value,
                statistics: statistics,
                cancelledDuringSolve: callback?.Cancelled == true,
                interruptedDuringSolve: callback?.Interrupted == true,
                config: config,
                quadratic: quadratic);
        }
        catch (CoptSdk.CoptException error) {
            ErrorCode code = stage switch {
                "environment" => ErrorCode.OREngineEnvironmentLost,
                "modeling" or "configuration" => ErrorCode.OREngineModelingException,
                _ => ErrorCode.OREngineSolvingException,
            };
            return Failure(
                code,
                $"COPT 调用失败（{stage}，{error.GetCode()}）：{error.Message} / COPT call failed ({stage}, {error.GetCode()}): {error.Message}");
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                ErrorCode.OREngineSolvingException,
                $"COPT 调用失败（{stage}）：{error.Message} / COPT call failed ({stage}): {error.Message}");
        }
        finally {
            try { model?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            try { environment?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
        }
    }

    private static Result<(CoptSdk.Envr Env, CoptSdk.Model Model), ErrorCode, Error<ErrorCode>>
        CreateEnvironment(IBackendConfiguration? backend, string name) {
        try {
            if (backend is CoptSolverConfig {
                Server: { } server,
                Port: { } port,
                Password: { } password,
                ConnectionTime: { } connectionTime,
            }) {
                using var client = new CoptSdk.EnvrConfig();
                client.Set("Cluster", server);
                client.Set("Port", port.ToString(CultureInfo.InvariantCulture));
                client.Set("Password", password);
                client.Set(
                    "WaitTime",
                    ((long)connectionTime.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                var remoteEnvironment = new CoptSdk.Envr(client);
                return Results.Ok((remoteEnvironment, remoteEnvironment.CreateModel(name)));
            }
            var environment = new CoptSdk.Envr();
            return Results.Ok((environment, environment.CreateModel(name)));
        }
        catch (CoptSdk.CoptException error) {
            return Results.Failed<(CoptSdk.Envr Env, CoptSdk.Model Model)>(new Err<ErrorCode>(
                ErrorCode.OREngineEnvironmentLost,
                $"COPT 环境错误：{error.Message} / COPT environment error: {error.Message}"));
        }
    }

    internal static Result<CoptNativeIis, ErrorCode, Error<ErrorCode>> ComputeIis(
        ILinearTriadModelView model,
        IisConfig config,
        CancellationToken cancellationToken) {
        Result<Success, ErrorCode, Error<ErrorCode>> validation = config.ValidateRuntime();
        if (!validation.IsOk) {
            return validation.Bind<CoptNativeIis>(_ => Failure<CoptNativeIis>(
                ErrorCode.IllegalArgument,
                "COPT IIS 配置无效 / The COPT IIS configuration is invalid"));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> backend = ValidateBackend(config.BackendConfiguration);
        if (!backend.IsOk) {
            return backend.Bind<CoptNativeIis>(_ => Failure<CoptNativeIis>(
                ErrorCode.IllegalArgument,
                "COPT IIS 后端配置无效 / The COPT IIS backend configuration is invalid"));
        }
        if (cancellationToken.IsCancellationRequested) {
            return Failure<CoptNativeIis>(
                ErrorCode.ApplicationStopped,
                "COPT IIS 已取消 / COPT IIS was cancelled");
        }
        Result<CoptLinearSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            CoptLinearSnapshot.Capture(model, cancellationToken);
        if (snapshot is not Ok<CoptLinearSnapshot, ErrorCode, Error<ErrorCode>> captured) {
            return CoptDualSupport.Propagate<CoptNativeIis, CoptLinearSnapshot>(
                snapshot,
                "COPT IIS 模型快照失败 / The COPT IIS model snapshot failed");
        }

        var stopwatch = Stopwatch.StartNew();
        CoptSdk.Envr? environment = null;
        CoptSdk.Model? nativeModel = null;
        string stage = "environment";
        try {
            Result<(CoptSdk.Envr Env, CoptSdk.Model Model), ErrorCode, Error<ErrorCode>> created =
                CreateEnvironment(config.BackendConfiguration, model.Name);
            if (created is not Ok<(CoptSdk.Envr Env, CoptSdk.Model Model), ErrorCode, Error<ErrorCode>> ready) {
                return Failure<CoptNativeIis>(
                    ErrorCode.OREngineEnvironmentLost,
                    "COPT IIS 环境初始化失败 / COPT IIS environment initialization failed");
            }
            environment = ready.Value.Env;
            nativeModel = ready.Value.Model;

            stage = "modeling";
            Result<CoptNativeDump, ErrorCode, Error<ErrorCode>> dumped =
                DumpLinear(nativeModel, captured.Value);
            if (dumped is not Ok<CoptNativeDump, ErrorCode, Error<ErrorCode>> native) {
                return Failure<CoptNativeIis>(
                    ErrorCode.OREngineModelingException,
                    "COPT IIS 模型写入失败 / COPT IIS model dump failed");
            }
            nativeModel.SetDblParam("TimeLimit", config.Time.TotalSeconds);
            nativeModel.SetIntParam("Threads", config.NativeThreadCount!.Value);
            nativeModel.SetIntParam("Logging", 0);

            stage = "solving";
            if (cancellationToken.IsCancellationRequested) {
                return Failure<CoptNativeIis>(
                    ErrorCode.ApplicationStopped,
                    "COPT IIS 已取消 / COPT IIS was cancelled");
            }
            bool isMip = nativeModel.GetIntAttr("IsMIP") != 0;
            if (isMip) {
                nativeModel.Solve();
            }
            else {
                nativeModel.SolveLp();
            }
            int status = nativeModel.GetIntAttr(isMip ? "MipStatus" : "LpStatus");
            if (status is not 2 and not 4) {
                return Failure<CoptNativeIis>(
                    ErrorCode.ORModelInfeasible,
                    $"COPT IIS 要求不可行模型，当前状态为 {status} / COPT IIS requires an infeasible model, current status is {status}");
            }
            if (cancellationToken.IsCancellationRequested) {
                return Failure<CoptNativeIis>(
                    ErrorCode.ApplicationStopped,
                    "COPT IIS 已取消 / COPT IIS was cancelled");
            }

            stage = "analysis";
            nativeModel.ComputeIIS();
            if (nativeModel.GetIntAttr("HasIIS") == 0) {
                return Failure<CoptNativeIis>(
                    ErrorCode.OREngineSolvingException,
                    "COPT 未生成 IIS / COPT did not produce an IIS");
            }
            var rows = new List<int>();
            for (int row = 0; row < native.Value.Constraints.Count; row++) {
                CoptSdk.Constraint constraint = native.Value.Constraints[row];
                if (constraint.GetLowerIIS() != 0 || constraint.GetUpperIIS() != 0) {
                    rows.Add(row);
                }
            }
            var bounds = new List<(int Column, BoundSide Side)>();
            for (int column = 0; column < native.Value.Variables.Count; column++) {
                CoptSdk.Var variable = native.Value.Variables[column];
                if (variable.GetLowerIIS() != 0) {
                    bounds.Add((column, BoundSide.Lower));
                }
                if (variable.GetUpperIIS() != 0) {
                    bounds.Add((column, BoundSide.Upper));
                }
            }
            if (rows.Count == 0 && bounds.Count == 0) {
                return Failure<CoptNativeIis>(
                    ErrorCode.OREngineSolvingException,
                    "COPT IIS 不包含约束或变量界 / The COPT IIS contains neither constraints nor variable bounds");
            }
            stopwatch.Stop();
            return Results.Ok(new CoptNativeIis(
                Rows: rows,
                Bounds: bounds,
                Minimal: nativeModel.GetIntAttr("IsMinIIS") != 0,
                Elapsed: stopwatch.Elapsed));
        }
        catch (CoptSdk.CoptException error) {
            return Failure<CoptNativeIis>(
                stage == "environment" ? ErrorCode.OREngineEnvironmentLost : ErrorCode.OREngineSolvingException,
                $"COPT IIS 调用失败（{stage}，{error.GetCode()}）：{error.Message} / COPT IIS call failed ({stage}, {error.GetCode()}): {error.Message}");
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure<CoptNativeIis>(
                ErrorCode.OREngineSolvingException,
                $"COPT IIS 调用失败（{stage}）：{error.Message} / COPT IIS call failed ({stage}): {error.Message}");
        }
        finally {
            try { nativeModel?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
            try { environment?.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
        }
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateBackend(
        IBackendConfiguration? backend) {
        if (backend is not null and not CoptSolverConfig) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"COPT 不能使用 {backend.Type} backend 配置 / COPT cannot use backend configuration {backend.Type}"));
        }
        if (backend is CoptSolverConfig copt
            && (copt.Server is not null
                || copt.Port is not null
                || copt.Password is not null
                || copt.ConnectionTime is not null)
            && (copt.Server is null
                || copt.Port is null
                || copt.Password is null
                || copt.ConnectionTime is null)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "COPT 远程连接必须同时提供 server、port、password 和 connectionTime / " +
                "COPT remote connections require server, port, password and connectionTime together"));
        }
        return Results.OkInstance;
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Configure(
        CoptSdk.Model model,
        SolverConfig config,
        bool requireFarkas) {
        model.SetDblParam("TimeLimit", config.TimeLimit.TotalSeconds);
        model.SetDblParam("AbsGap", config.Gap.ToDouble());
        model.SetIntParam("Threads", config.NativeThreadCount!.Value);
        model.SetIntParam("Logging", 0);
        if (requireFarkas) {
            model.SetIntParam("ReqFarkasRay", 1);
        }
        return Results.OkInstance;
    }

    private static Result<CoptNativeDump, ErrorCode, Error<ErrorCode>> DumpLinear(
        CoptSdk.Model model,
        CoptLinearSnapshot snapshot) {
        var variables = new List<CoptSdk.Var>(snapshot.Columns.Length);
        var constraints = new List<CoptSdk.Constraint>(snapshot.Rows.Length);
        for (int index = 0; index < snapshot.Columns.Length; index++) {
            CoptColumn column = snapshot.Columns[index];
            CoptSdk.Var variable = model.AddVar(
                Lower(column.Lower),
                Upper(column.Upper),
                0.0,
                column.Type,
                $"x{index}");
            variables.Add(variable);
            if (column.Initial is { } initial) {
                model.SetMipStart(variable, initial);
            }
        }
        for (int row = 0; row < snapshot.Rows.Length; row++) {
            CoptRow source = snapshot.Rows[row];
            using var expression = new CoptSdk.Expr();
            for (int index = 0; index < source.Columns.Length; index++) {
                expression.AddTerm(
                    variables[source.Columns[index]],
                    source.Coefficients[index]);
            }
            constraints.Add(model.AddConstr(
                expression,
                Sign(source.Relation),
                source.Right,
                $"r{row}"));
        }
        using var objective = new CoptSdk.Expr();
        for (int index = 0; index < snapshot.Objective.Length; index++) {
            if (snapshot.Objective[index] != 0) {
                objective.AddTerm(variables[index], snapshot.Objective[index]);
            }
        }
        objective.AddConstant(snapshot.Constant);
        model.SetObjective(objective, snapshot.Category == ObjectCategory.Minimum ? 1 : -1);
        return Results.Ok(new CoptNativeDump(
            Variables: variables,
            Constraints: constraints));
    }

    private static Result<CoptNativeDump, ErrorCode, Error<ErrorCode>> DumpQuadratic(
        CoptSdk.Model model,
        CoptQuadraticSnapshot snapshot) {
        var variables = new List<CoptSdk.Var>(snapshot.Columns.Length);
        for (int index = 0; index < snapshot.Columns.Length; index++) {
            CoptColumn column = snapshot.Columns[index];
            CoptSdk.Var variable = model.AddVar(
                Lower(column.Lower),
                Upper(column.Upper),
                0.0,
                column.Type,
                $"x{index}");
            variables.Add(variable);
            if (column.Initial is { } initial) {
                model.SetMipStart(variable, initial);
            }
        }
        for (int row = 0; row < snapshot.Rows.Length; row++) {
            CoptQuadraticRow source = snapshot.Rows[row];
            if (source.QuadraticTerms.Length == 0) {
                using var expression = new CoptSdk.Expr();
                for (int index = 0; index < source.LinearColumns.Length; index++) {
                    expression.AddTerm(
                        variables[source.LinearColumns[index]],
                        source.LinearCoefficients[index]);
                }
                model.AddConstr(
                    expression,
                    Sign(source.Relation),
                    source.Right,
                    $"r{row}");
            }
            else {
                using var expression = new CoptSdk.QuadExpr();
                for (int index = 0; index < source.LinearColumns.Length; index++) {
                    expression.AddTerm(
                        variables[source.LinearColumns[index]],
                        source.LinearCoefficients[index]);
                }
                foreach (CoptQuadraticTerm term in source.QuadraticTerms) {
                    expression.AddTerm(
                        variables[term.Col1],
                        variables[term.Col2],
                        term.Coefficient);
                }
                model.AddQConstr(
                    expression,
                    Sign(source.Relation),
                    source.Right,
                    $"r{row}");
            }
        }
        using var objective = new CoptSdk.QuadExpr();
        for (int index = 0; index < snapshot.Objective.Length; index++) {
            if (snapshot.Objective[index] != 0) {
                objective.AddTerm(variables[index], snapshot.Objective[index]);
            }
        }
        foreach (CoptQuadraticTerm term in snapshot.QuadraticObjective) {
            objective.AddTerm(
                variables[term.Col1],
                variables[term.Col2],
                term.Coefficient);
        }
        objective.AddConstant(snapshot.Constant);
        model.SetQuadObjective(
            objective,
            snapshot.Category == ObjectCategory.Minimum ? 1 : -1);
        return Results.Ok(new CoptNativeDump(
            Variables: variables,
            Constraints: []));
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> CaptureDuals(
        CoptSdk.Model model,
        IReadOnlyList<CoptSdk.Constraint> constraints,
        bool isMip,
        int nativeStatus,
        CoptLinearDualCapture? capture) {
        if (capture is null || nativeStatus is not 1 and not 2) {
            return Results.OkInstance;
        }
        if (isMip) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "COPT 对偶值只支持连续松驰 / COPT dual values require a continuous relaxation"));
        }
        bool farkas = nativeStatus == 2;
        if (farkas && !capture.CaptureFarkas) {
            return Results.OkInstance;
        }
        if (farkas && model.GetIntAttr("HasDualFarkas") == 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "COPT 未提供 Farkas 对偶值 / COPT did not provide Farkas dual values"));
        }
        string key = farkas ? "DualFarkas" : "Dual";
        var values = new Flt64[constraints.Count];
        for (int row = 0; row < constraints.Count; row++) {
            double value = constraints[row].Get(key);
            if (!double.IsFinite(value)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    $"第 {row} 行 COPT 对偶值无效 / The COPT dual value for row {row} is invalid"));
            }
            values[row] = new Flt64(value);
        }
        IReadOnlyList<Flt64> result = Array.AsReadOnly(values);
        if (farkas) {
            capture.FarkasDuals = result;
        }
        else {
            capture.RowDuals = result;
        }
        return Results.OkInstance;
    }

    private static Result<SolveSolution<Flt64>?, ErrorCode, Error<ErrorCode>> ReadSolution(
        CoptSdk.Model model,
        IReadOnlyList<CoptSdk.Var> variables,
        int nativeStatus,
        bool isMip,
        int solutionAmount) {
        bool hasSolution = nativeStatus == 1
            || (nativeStatus is not 2 and not 3 and not 4
                && (isMip
                    ? model.GetIntAttr("HasMipSol") != 0
                    : model.GetIntAttr("HasLpSol") != 0));
        if (!hasSolution) {
            return Results.Ok<SolveSolution<Flt64>?>(null);
        }
        var values = new List<Flt64>(variables.Count);
        foreach (CoptSdk.Var variable in variables) {
            double value = variable.Get("Value");
            if (!double.IsFinite(value)) {
                return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "COPT 解包含非有限值 / COPT solution contains a nonfinite value"));
            }
            values.Add(new Flt64(value));
        }
        double objective = model.GetDblAttr(isMip ? "BestObj" : "LpObjval");
        if (!double.IsFinite(objective)) {
            return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "COPT 目标值不是有限数 / COPT objective is not finite"));
        }
        var solution = new SolveSolution<Flt64>(
            Values: new Solution<Flt64>(Array.AsReadOnly(values.ToArray())),
            Objective: new Flt64(objective));
        if (!isMip || solutionAmount <= 1) {
            return Results.Ok<SolveSolution<Flt64>?>(solution);
        }

        int poolCount = System.Math.Min(
            solutionAmount,
            System.Math.Max(0, model.GetIntAttr("PoolSols")));
        CoptSdk.Var[] nativeVariables = variables.ToArray();
        var pool = new List<Solution<Flt64>>(poolCount);
        for (int index = 0; index < poolCount; index++) {
            double[] poolValues = model.GetPoolSolution(index, nativeVariables);
            if (poolValues.Any(value => !double.IsFinite(value))) {
                return Results.Failed<SolveSolution<Flt64>?>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "COPT 解池包含非有限值 / COPT solution pool contains a nonfinite value"));
            }
            Flt64[] poolSolution = poolValues.Select(value => new Flt64(value)).ToArray();
            if (!solution.Values.Values.SequenceEqual(poolSolution)
                && !pool.Exists(existing => existing.Values.SequenceEqual(poolSolution))) {
                pool.Add(new Solution<Flt64>(Array.AsReadOnly(
                    poolSolution)));
            }
        }
        return Results.Ok<SolveSolution<Flt64>?>(solution with {
            Pool = Array.AsReadOnly(pool.ToArray()),
        });
    }

    private static SolveStatistics ReadStatistics(
        CoptSdk.Model model,
        bool isMip,
        int nativeStatus,
        SolveSolution<Flt64>? solution) {
        double seconds = TryDouble(model, "SolvingTime") ?? double.NaN;
        double? bound = isMip
            ? TryDouble(model, "BestBnd")
            : nativeStatus == 1 ? solution?.Objective?.ToDouble() : null;
        double? gap = isMip && solution is not null ? TryDouble(model, "BestGap")
            : !isMip && nativeStatus == 1 ? 0.0
            : null;
        ulong? iterations = Sum(
            left: TryCount(model, "SimplexIter"),
            right: TryCount(model, "BarrierIter"));
        return new SolveStatistics(
            SolveTime: double.IsFinite(seconds) && seconds >= 0
                ? TimeSpan.FromSeconds(seconds)
                : null,
            Iterations: iterations,
            Nodes: isMip ? TryCount(model, "NodeCnt") : null,
            BestBound: Finite(bound),
            Gap: Finite(gap));
    }

    private static double? TryDouble(CoptSdk.Model model, string name) {
        try {
            double value = model.GetDblAttr(name);
            return double.IsFinite(value) ? value : null;
        }
        catch (CoptSdk.CoptException) {
            return null;
        }
    }

    private static ulong? TryCount(CoptSdk.Model model, string name) {
        try {
            int value = model.GetIntAttr(name);
            return value >= 0 ? (ulong)value : null;
        }
        catch (CoptSdk.CoptException) {
            return null;
        }
    }

    private static ulong? Sum(ulong? left, ulong? right) =>
        left is null && right is null
            ? null
            : (left ?? 0) + (right ?? 0);

    private static Flt64? Finite(double? value) =>
        value is { } number && double.IsFinite(number) && System.Math.Abs(number) < Infinity
            ? new Flt64(number)
            : null;

    private static char Sign(ConstraintRelation relation) =>
        relation == ConstraintRelation.LessEqual
            ? 'L'
            : relation == ConstraintRelation.GreaterEqual
                ? 'G'
                : 'E';

    private static double Lower(double value) =>
        double.IsNegativeInfinity(value) || value <= -FreeBoundThreshold ? -Infinity : value;

    private static double Upper(double value) =>
        double.IsPositiveInfinity(value) || value >= FreeBoundThreshold ? Infinity : value;

    private static bool Fits(CoptLinearSnapshot snapshot) =>
        System.Math.Abs(snapshot.Constant) < Infinity
        && snapshot.Objective.All(value => System.Math.Abs(value) < Infinity)
        && snapshot.Rows.All(row => System.Math.Abs(row.Right) < Infinity
            && row.Coefficients.All(value => System.Math.Abs(value) < Infinity))
        && snapshot.Columns.All(column => BoundFits(column.Lower, lower: true)
            && BoundFits(column.Upper, lower: false)
            && (column.Initial is null || System.Math.Abs(column.Initial.Value) < Infinity));

    private static bool Fits(CoptQuadraticSnapshot snapshot) =>
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
        if (lower ? double.IsNegativeInfinity(value) || value <= -FreeBoundThreshold
            : double.IsPositiveInfinity(value) || value >= FreeBoundThreshold) {
            return true;
        }
        return double.IsFinite(value) && System.Math.Abs(value) < Infinity;
    }

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Failure(
        ErrorCode code,
        string message) =>
        Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(code, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}

internal sealed record CoptNativeDump(
    IReadOnlyList<CoptSdk.Var> Variables,
    IReadOnlyList<CoptSdk.Constraint> Constraints);

internal sealed record CoptNativeIis(
    IReadOnlyList<int> Rows,
    IReadOnlyList<(int Column, BoundSide Side)> Bounds,
    bool Minimal,
    TimeSpan Elapsed);

internal sealed class CoptInfeasibilityAnalyzer : INativeIisAnalyzer<ILinearTriadModelView> {
    private readonly IisConfig _config;

    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.NativeIis,
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip },
        Exact: true);

    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.NativeIis;

    internal CoptInfeasibilityAnalyzer(IisConfig config) {
        _config = config;
    }

    public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        Result<CoptNativeIis, ErrorCode, Error<ErrorCode>> result =
            CoptNativeSolver.ComputeIis(model, _config, cancellationToken);
        if (result is not Ok<CoptNativeIis, ErrorCode, Error<ErrorCode>> ok) {
            return Task.FromResult(CoptDualSupport.Propagate<InfeasibilityEvidence, CoptNativeIis>(
                result,
                "COPT IIS 分析失败 / The COPT IIS analysis failed"));
        }

        CoptNativeIis native = ok.Value;
        var constraintIds = new HashSet<ConstraintId>(
            native.Rows.Select(model.DiagnosticConstraintId));
        var boundRefs = new HashSet<VariableBoundRef>(
            native.Bounds.Select(bound => new VariableBoundRef(
                model.Variables[bound.Column].DiagnosticVariableId(),
                bound.Side)));
        var members = new HashSet<InfeasibilityMember>(
            constraintIds.Select(id => (InfeasibilityMember)new InfeasibilityMember.ConstraintMember(id)));
        members.UnionWith(boundRefs.Select(reference =>
            (InfeasibilityMember)new InfeasibilityMember.VariableBoundMember(reference)));

        return Task.FromResult(Results.Ok(new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.NativeIis,
            Exactness = native.Minimal ? EvidenceExactness.Irreducible : EvidenceExactness.Exact,
            Completeness = EvidenceCompleteness.Complete,
            ConstraintIds = constraintIds,
            VariableBoundIds = new HashSet<VariableId>(boundRefs.Select(reference => reference.VariableId)),
            Elapsed = native.Elapsed,
            Validity = EvidenceValidity.Verified,
            Minimality = native.Minimal ? EvidenceMinimality.Irreducible : EvidenceMinimality.NotChecked,
            VariableBoundRefs = boundRefs,
            Members = members,
            Reference = "copt.computeIIS",
        }));
    }
}
