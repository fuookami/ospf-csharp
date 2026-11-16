#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Value;
namespace Fuookami.Ospf.Core.Plugin.Scip;

internal static class ScipLinearNativeSolver {
    private const int Okay = 1;
    // SCIP_PARAMSETTING：0 = DEFAULT、3 = OFF / SCIP_PARAMSETTING: 0 = DEFAULT, 3 = OFF.
    private const int PresolvingDefault = 0;
    private const int PresolvingOff = 3;

    /// <summary>
    /// SCIP 线性后端能力：LP/MIP、对偶、Farkas、热启动、解池、回调与中断；不具备原生 IIS。
    /// SCIP linear backend capabilities: LP/MIP, duals, Farkas, warm start, solution pool, callbacks and interrupt; no native IIS.
    /// </summary>
    internal static readonly SolverCapabilities LinearCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip },
        Dual: true,
        Farkas: true,
        WarmStart: true,
        SolutionPool: true,
        Callback: true,
        Interrupt: true);

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Solve(
        ScipLinearSnapshot model,
        SolverConfig config,
        object? sourceModel,
        SolvingStatusCallBack? statusCallBack,
        int solutionAmount,
        CancellationToken cancellationToken,
        ScipDualTargets? duals = null) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Ok(Cancelled());
        }
        Result<ScipNative, ErrorCode, Error<ErrorCode>> loaded = ScipNative.Load();
        if (loaded is not Ok<ScipNative, ErrorCode, Error<ErrorCode>> ok) {
            return loaded.Bind<SolveReport<Flt64>>(_ => Failure(ErrorCode.OREngineEnvironmentLost, "无法加载 SCIP / Cannot load SCIP"));
        }
        using ScipNative native = ok.Value;
        IntPtr scip = IntPtr.Zero;
        IntPtr[] constraints = new IntPtr[model.Rows.Length];
        IntPtr initialSolution = IntPtr.Zero;
        IntPtr[] variables = new IntPtr[model.Columns.Length];
        var events = new ScipSolveEvents(
            native: native,
            token: cancellationToken,
            config: config,
            category: model.Category,
            variables: variables,
            variableCount: variables.Length,
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
                duals: duals,
                constraints: constraints,
                initialSolution: ref initialSolution,
                variables: variables,
                solutionAmount: solutionAmount,
                cancellationToken: cancellationToken,
                events: events.IsActive ? events : null);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            result = Failure(ErrorCode.OREngineSolvingException, $"SCIP 调用失败：{error.Message} / SCIP call failed: {error.Message}");
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
                try {
                    for (int i = 0; i < constraints.Length; i++) {
                        if (constraints[i] != IntPtr.Zero && native.ReleaseConstraint(scip, ref constraints[i]) != Okay) {
                            cleanupFailure ??= "SCIPreleaseCons";
                        }
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                    cleanupFailure ??= $"SCIPreleaseCons: {error.Message}";
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
        return cleanupFailure is null ? result : Failure(
            ErrorCode.OREngineSolvingException,
            $"SCIP 资源释放失败：{cleanupFailure} / SCIP resource release failed: {cleanupFailure}");
    }

    private static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Run(
        ScipLinearSnapshot model,
        SolverConfig config,
        ScipNative native,
        ref IntPtr scip,
        ScipDualTargets? duals,
        IntPtr[] constraints,
        ref IntPtr initialSolution,
        IntPtr[] variables,
        int solutionAmount,
        CancellationToken cancellationToken,
        ScipSolveEvents? events) {
        if (cancellationToken.IsCancellationRequested) { return Results.Ok(Cancelled()); }
        int code = native.CreateScip(ref scip);
        if (code != Okay || scip == IntPtr.Zero) { return NativeFailure("SCIPcreate", code); }
        native.SetQuiet(scip, 1);
        code = native.IncludePlugins(scip);
        if (code != Okay) { return NativeFailure("SCIPincludeDefaultPlugins", code); }
        Result<Success, ErrorCode, Error<ErrorCode>> parameters = ConfigureParameters(
            native: native,
            scip: scip,
            config: config,
            solutionAmount: solutionAmount);
        if (!parameters.IsOk) {
            return parameters.Bind<SolveReport<Flt64>>(_ => Failure(
                ErrorCode.OREngineModelingException,
                "SCIP 原生参数设置失败 / SCIP native parameter setup failed"));
        }
        if (duals is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> lpMode = ConfigureLpMode(native: native, scip: scip);
            if (!lpMode.IsOk) {
                return lpMode.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineModelingException,
                    "SCIP 对偶提取模式设置失败 / SCIP dual extraction mode setup failed"));
            }
        }
        code = native.CreateProblem(scip, "ospf");
        if (code != Okay) { return NativeFailure("SCIPcreateProbBasic", code); }
        double infinity = native.Infinity(scip);
        if (!model.Fits(infinity)) {
            return Failure(ErrorCode.IllegalArgument, "数值超出 SCIP 可表示范围 / Value exceeds the SCIP representable range");
        }
        code = native.SetObjectiveSense(scip, model.Category == ObjectCategory.Minimum ? 1 : -1);
        if (code != Okay) { return NativeFailure("SCIPsetObjsense", code); }
        code = native.AddObjectiveOffset(scip, model.Constant);
        if (code != Okay) { return NativeFailure("SCIPaddOrigObjoffset", code); }
        for (int i = 0; i < variables.Length; i++) {
            if (cancellationToken.IsCancellationRequested) { return Results.Ok(Cancelled()); }
            ScipColumn column = model.Columns[i];
            code = native.CreateVariable(
                scip: scip,
                variable: ref variables[i],
                name: $"x{i}",
                lower: double.IsNegativeInfinity(column.Lower) ? -infinity : column.Lower,
                upper: double.IsPositiveInfinity(column.Upper) ? infinity : column.Upper,
                objective: model.Objective[i],
                type: column.Type);
            if (code != Okay || variables[i] == IntPtr.Zero) { return NativeFailure("SCIPcreateVarBasic", code); }
            code = native.AddVariable(scip, variables[i]);
            if (code != Okay) { return NativeFailure("SCIPaddVar", code); }
        }
        for (int i = 0; i < model.Rows.Length; i++) {
            if (cancellationToken.IsCancellationRequested) { return Results.Ok(Cancelled()); }
            ScipRow row = model.Rows[i];
            IntPtr[] rowVariables = row.Columns.Select(index => variables[index]).ToArray();
            code = native.CreateLinearConstraint(
                scip: scip,
                constraint: ref constraints[i],
                name: $"r{i}",
                count: rowVariables.Length,
                variables: rowVariables,
                coefficients: row.Coefficients,
                lower: row.Relation == ConstraintRelation.LessEqual ? -infinity : row.Right,
                upper: row.Relation == ConstraintRelation.GreaterEqual ? infinity : row.Right);
            if (code != Okay || constraints[i] == IntPtr.Zero) { return NativeFailure("SCIPcreateConsBasicLinear", code); }
            code = native.AddConstraint(scip, constraints[i]);
            if (code != Okay) { return NativeFailure("SCIPaddCons", code); }
        }
        if (cancellationToken.IsCancellationRequested) { return Results.Ok(Cancelled()); }
        Result<Success, ErrorCode, Error<ErrorCode>> start = SubmitInitialSolution(
            native: native,
            scip: scip,
            columns: model.Columns,
            variables: variables,
            solution: ref initialSolution,
            cancellationToken: cancellationToken,
            stored: out _);
        if (start is Failed<Success, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.ApplicationStopped }
            && cancellationToken.IsCancellationRequested) {
            return Results.Ok(Cancelled());
        }
        if (!start.IsOk) {
            return start.Bind<SolveReport<Flt64>>(_ => Failure(ErrorCode.OREngineSolvingException,
                "SCIP 初始解提交失败 / SCIP initial solution submission failed"));
        }
        if (events is not null) {
            code = events.Install(scip);
            if (code != Okay) { return NativeFailure("SCIP cancellation event setup", code); }
        }
        if (cancellationToken.IsCancellationRequested) { return Results.Ok(Cancelled()); }
        code = native.Solve(scip);
        if (events?.CallbackError is { } callbackError) {
            return Failure(ErrorCode.OREngineSolvingException,
                $"SCIP 取消回调失败：{callbackError.Message} / SCIP cancellation callback failed: {callbackError.Message}");
        }
        if (code != Okay) { return NativeFailure("SCIPsolve", code); }
        if (duals is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> extracted = ExtractDuals(
                native: native,
                scip: scip,
                constraints: constraints,
                targets: duals);
            if (!extracted.IsOk) {
                return extracted.Bind<SolveReport<Flt64>>(_ => Failure(
                    ErrorCode.OREngineSolvingException,
                    "SCIP 对偶提取失败 / SCIP dual extraction failed"));
            }
        }
        return ReadReport(
            native: native,
            scip: scip,
            variables: variables,
            infinity: infinity,
            interruptedByCancellation: events?.Cancelled == true,
            config: config,
            solutionAmount: solutionAmount);
    }

    internal static Result<Success, ErrorCode, Error<ErrorCode>> ConfigureParameters(
        ScipNative native,
        IntPtr scip,
        SolverConfig config,
        int solutionAmount) {
        int code = native.SetRealParameter(
            scip: scip,
            name: "limits/time",
            value: config.TimeLimit.TotalSeconds);
        if (code != Okay) { return ParameterFailure("limits/time", code); }
        code = native.SetRealParameter(
            scip: scip,
            name: "limits/gap",
            value: config.Gap.ToSolverDouble());
        if (code != Okay) { return ParameterFailure("limits/gap", code); }
        code = native.SetIntParameter(
            scip: scip,
            name: "parallel/maxnthreads",
            value: config.NativeThreadCount!.Value);
        if (code != Okay) { return ParameterFailure("parallel/maxnthreads", code); }
        if (solutionAmount > 1) {
            code = native.SetIntParameter(
                scip: scip,
                name: "heuristics/dins/solnum",
                value: solutionAmount);
            if (code != Okay) { return ParameterFailure("heuristics/dins/solnum", code); }
        }
        if (config.BackendConfiguration is not ScipSolverConfig backend) {
            return Results.OkInstance;
        }
        if (backend.Presolve is { } presolve) {
            code = native.SetPresolving(
                scip: scip,
                setting: presolve ? PresolvingDefault : PresolvingOff,
                quiet: 1);
            if (code != Okay) { return ParameterFailure("presolving", code); }
        }
        if (backend.RandomSeed is { } seed) {
            code = native.SetIntParameter(
                scip: scip,
                name: "randomization/randomseedshift",
                value: (int)seed);
            if (code != Okay) { return ParameterFailure("randomization/randomseedshift", code); }
        }
        if (backend.Deterministic == true) {
            foreach (string name in new[] { "randomization/permutevars", "randomization/permuteconss" }) {
                code = native.SetBoolParameter(scip: scip, name: name, value: 0);
                if (code != Okay) { return ParameterFailure(name, code); }
            }
        }
        foreach (KeyValuePair<string, BackendParameterValue> entry in backend.NativeParameters
            .OrderBy(item => item.Key, StringComparer.Ordinal)) {
            Result<Success, ErrorCode, Error<ErrorCode>> applied = ApplyNativeParameter(
                native: native,
                scip: scip,
                name: entry.Key,
                value: entry.Value);
            if (!applied.IsOk) {
                return applied;
            }
        }
        return Results.OkInstance;
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ApplyNativeParameter(
        ScipNative native,
        IntPtr scip,
        string name,
        BackendParameterValue value) {
        if (string.IsNullOrWhiteSpace(name)) {
            return Illegal("SCIP 原生参数名不能为空 / SCIP native parameter names cannot be blank");
        }
        int code = value switch {
            BackendParameterValue.Text text => native.SetStringParameter(scip: scip, name: name, value: text.Value),
            BackendParameterValue.Integer integer when integer.Value is >= int.MinValue and <= int.MaxValue =>
                native.SetIntParameter(scip: scip, name: name, value: (int)integer.Value),
            BackendParameterValue.Integer integer => native.SetLongintParameter(scip: scip, name: name, value: integer.Value),
            BackendParameterValue.BooleanValue boolean => native.SetBoolParameter(scip: scip, name: name, value: boolean.Value ? 1 : 0),
            _ => 0,
        };
        if (value is BackendParameterValue.Decimal decimalValue) {
            if (!double.TryParse(decimalValue.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                !double.IsFinite(parsed)) {
                return Illegal($"SCIP 原生参数 {name} 不是有限十进制 / SCIP native parameter {name} is not a finite decimal");
            }
            code = native.SetRealParameter(scip: scip, name: name, value: parsed);
        }
        return code == Okay ? Results.OkInstance : ParameterFailure(name, code);
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> ParameterFailure(string name, int code) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.OREngineModelingException,
            $"SCIP 原生参数 {name} 设置失败：{code} / Setting SCIP native parameter {name} failed: {code}"));

    /// <summary>
    /// 对偶提取模式：关闭预处理与启发式并固定单线程，使原始约束保留自己的 LP 行，从而可按行顺序读取逐行对偶；
    /// 与 Kotlin 的 LP 配置一致（Kotlin 在 configuration 回调里做同样的设置）。
    /// Dual extraction mode: presolving and heuristics are off with a single thread, so every original constraint keeps
    /// its own LP row and row duals can be read in row order; this matches the Kotlin LP configuration, which applies the
    /// same settings in its configuration callback.
    /// </summary>
    private static Result<Success, ErrorCode, Error<ErrorCode>> ConfigureLpMode(ScipNative native, IntPtr scip) {
        int presolve = native.SetPresolving(scip: scip, setting: PresolvingOff, quiet: 1);
        if (presolve != Okay) { return ParameterFailure("presolving", presolve); }
        int heuristics = native.SetHeuristics(scip: scip, setting: PresolvingOff, quiet: 1);
        if (heuristics != Okay) { return ParameterFailure("heuristics", heuristics); }
        int threads = native.SetIntParameter(scip: scip, name: "parallel/maxnthreads", value: 1);
        return threads == Okay
            ? Results.OkInstance
            : ParameterFailure("parallel/maxnthreads", threads);
    }

    /// <summary>
    /// 按行顺序读取约束对偶值，使用 SCIPgetDualSolVal 原语；LP 模式已关闭预处理与启发式，
    /// 因此原始约束仍有有效的逐行对偶。
    /// Read constraint dual values in row order with the SCIPgetDualSolVal primitive; LP mode
    /// keeps presolving and heuristics off, so the original constraints still carry valid per-row duals.
    /// </summary>
    private static Result<Success, ErrorCode, Error<ErrorCode>> ExtractDuals(
        ScipNative native,
        IntPtr scip,
        IntPtr[] constraints,
        ScipDualTargets targets) {
        if (targets.Diagnostic) {
            return ExtractFarkas(native: native, scip: scip, constraints: constraints, targets: targets);
        }
        var duals = new Flt64[constraints.Length];
        for (int row = 0; row < constraints.Length; row++) {
            int code = native.GetDualSolVal(
                scip: scip,
                cons: constraints[row],
                dualsolval: out double dualsolval,
                boundconstraint: out _);
            if (code != Okay) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    $"SCIPgetDualSolVal 第 {row} 行返回 {code} / SCIPgetDualSolVal returned {code} for row {row}"));
            }
            duals[row] = new Flt64(dualsolval);
        }
        targets.Duals = duals;
        return Results.OkInstance;
    }

    /// <summary>
    /// 诊断模式：逐行读取转换约束的 Farkas 乘子。不可行性在转换或传播阶段判定时没有 LP 行，
    /// 此时 <c>SCIPgetTransformedCons</c> 返回空指针，该行按零乘子处理，由上层如实报告“未返回非零 Farkas 成分”。
    /// Diagnostic mode: read the Farkas multiplier of every transformed constraint. When infeasibility is proven during
    /// transformation or propagation there is no LP row, <c>SCIPgetTransformedCons</c> returns null, and the row is kept
    /// as a zero multiplier so the caller reports the missing nonzero Farkas component honestly.
    /// </summary>
    private static Result<Success, ErrorCode, Error<ErrorCode>> ExtractFarkas(
        ScipNative native,
        IntPtr scip,
        IntPtr[] constraints,
        ScipDualTargets targets) {
        var farkas = new Flt64[constraints.Length];
        for (int row = 0; row < constraints.Length; row++) {
            int code = native.GetTransformedCons(
                scip: scip,
                cons: constraints[row],
                transcons: out IntPtr transformed);
            if (code != Okay) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    $"SCIPgetTransformedCons 第 {row} 行返回 {code} / SCIPgetTransformedCons returned {code} for row {row}"));
            }
            farkas[row] = new Flt64(transformed == IntPtr.Zero
                ? 0d
                : native.GetDualfarkasLinear(scip: scip, cons: transformed));
        }
        targets.Farkas = farkas;
        return Results.OkInstance;
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Illegal(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    internal static Result<Success, ErrorCode, Error<ErrorCode>> SubmitInitialSolution(
        ScipNative native,
        IntPtr scip,
        ScipColumn[] columns,
        IntPtr[] variables,
        ref IntPtr solution,
        CancellationToken cancellationToken,
        out bool stored) {
        stored = false;
        if (!columns.Any(column => column.Initial is not null)) { return Results.OkInstance; }
        if (cancellationToken.IsCancellationRequested) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped,
                "SCIP 初始解提交已取消 / SCIP initial solution submission was cancelled"));
        }
        int code = columns.All(column => column.Initial is not null)
            ? native.CreateFullSolution(
                scip: scip,
                solution: ref solution,
                heuristic: IntPtr.Zero)
            : native.CreatePartialSolution(
                scip: scip,
                solution: ref solution,
                heuristic: IntPtr.Zero);
        if (code != Okay || solution == IntPtr.Zero) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                $"SCIP 创建初始解失败：{code} / SCIP initial solution creation failed: {code}"));
        }
        for (int i = 0; i < columns.Length; i++) {
            if (cancellationToken.IsCancellationRequested) {
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped,
                    "SCIP 初始解提交已取消 / SCIP initial solution submission was cancelled"));
            }
            if (columns[i].Initial is { } initial) {
                code = native.SetInitialValue(
                    scip: scip,
                    solution: solution,
                    variable: variables[i],
                    value: initial);
                if (code != Okay) {
                    return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                        $"SCIP 设置初始值失败：{code} / SCIP setting an initial value failed: {code}"));
                }
            }
        }
        if (cancellationToken.IsCancellationRequested) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped,
                "SCIP 初始解提交已取消 / SCIP initial solution submission was cancelled"));
        }
        code = native.AddInitialSolution(
            scip: scip,
            solution: ref solution,
            stored: out int nativeStored);
        stored = nativeStored != 0;
        return code == Okay ? Results.OkInstance : Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.OREngineSolvingException,
            $"SCIP 添加初始解失败：{code} / SCIP adding an initial solution failed: {code}"));
    }

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ReadReport(
        ScipNative native,
        IntPtr scip,
        IntPtr[] variables,
        double infinity,
        bool interruptedByCancellation,
        SolverConfig config,
        int solutionAmount = 0,
        SolverCapabilities? capabilities = null) {
        int stage = native.GetStage(scip);
        if (stage != 1 && (stage < 3 || stage > 10)) {
            return Failure(ErrorCode.OREngineSolvingException, $"SCIP 求解后阶段无效：{stage} / Invalid SCIP stage after solving: {stage}");
        }
        int status = native.GetStatus(scip);
        // 原始问题阶段的候选尚未验证。 / Original-problem candidates have not been validated.
        IntPtr best = stage == 1 ? IntPtr.Zero : native.GetBestSolution(scip);
        SolveSolution<Flt64>? solution = null;
        if (best != IntPtr.Zero) {
            double objective = native.GetSolutionObjective(scip, best);
            var values = new Flt64[variables.Length];
            for (int i = 0; i < variables.Length; i++) {
                double value = native.GetSolutionValue(
                    scip: scip,
                    solution: best,
                    variable: variables[i]);
                if (!double.IsFinite(value) || System.Math.Abs(value) >= infinity) {
                    return Failure(ErrorCode.ORSolutionInvalid, "SCIP 解值无效 / Invalid SCIP solution value");
                }
                values[i] = new Flt64(value);
            }
            if (!double.IsFinite(objective) || System.Math.Abs(objective) >= infinity) {
                return Failure(ErrorCode.ORSolutionInvalid, "SCIP 目标值无效 / Invalid SCIP objective value");
            }
            var pool = new List<Solution<Flt64>>();
            if (solutionAmount > 1) {
                int nativeCount = native.GetSolutionCount(scip);
                IntPtr nativeSolutions = native.GetSolutions(scip);
                if (nativeCount < 0 || (nativeCount > 0 && nativeSolutions == IntPtr.Zero)) {
                    return Failure(ErrorCode.ORSolutionInvalid, "SCIP 解池无效 / Invalid SCIP solution pool");
                }
                IntPtr[] candidates = new IntPtr[System.Math.Min(nativeCount, solutionAmount)];
                if (candidates.Length > 0) {
                    Marshal.Copy(nativeSolutions, candidates, 0, candidates.Length);
                }
                foreach (IntPtr candidate in candidates) {
                    if (candidate == best) { continue; }
                    if (candidate == IntPtr.Zero) {
                        return Failure(ErrorCode.ORSolutionInvalid, "SCIP 解池包含空解 / SCIP solution pool contains a null solution");
                    }
                    var candidateValues = new Flt64[variables.Length];
                    for (int i = 0; i < variables.Length; i++) {
                        double value = native.GetSolutionValue(
                            scip: scip,
                            solution: candidate,
                            variable: variables[i]);
                        if (!double.IsFinite(value) || System.Math.Abs(value) >= infinity) {
                            return Failure(ErrorCode.ORSolutionInvalid, "SCIP 解池含无效值 / SCIP solution pool contains an invalid value");
                        }
                        candidateValues[i] = new Flt64(value);
                    }
                    if (!pool.Any(item => item.Values.SequenceEqual(candidateValues))) {
                        pool.Add(new Solution<Flt64>(Array.AsReadOnly(candidateValues)));
                    }
                }
            }
            solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(Array.AsReadOnly(values)),
                Objective: new Flt64(objective),
                Pool: Array.AsReadOnly(pool.ToArray()));
        }
        if (status == 11 && solution is null) {
            return Failure(ErrorCode.ORSolutionInvalid, "SCIP 声称最优但没有解 / SCIP claims optimality without a solution");
        }
        double seconds = native.GetSolvingTime(scip);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) {
            return Failure(ErrorCode.OREngineSolvingException, "SCIP 求解时间无效 / Invalid SCIP solving time");
        }
        double bound = stage >= 3 ? native.GetDualBound(scip) : double.NaN;
        double gap = stage >= 5 ? native.GetGap(scip) : double.NaN;
        long nodes = native.GetNodes(scip);
        long iterations = stage is 5 or 7 or 9 or 10 ? native.GetIterations(scip) : -1;
        var descriptor = new SolverDescriptor(
            SolverId: "scip",
            BackendName: "SCIP",
            Capabilities: capabilities ?? LinearCapabilities,
            BackendVersion: $"{native.Major()}.{native.Minor()}.{native.Patch()}");
        // shortcut: 报告枚举缺少间隙、内存及重启限制；互通 schema 支持后细分 / Report enums lack gap, memory and restart limits; refine when the shared schema supports them.
        SolveIssue[] warnings = status is 0 or 6 or 7 or 10 || status < 0 || status > 17
            ? new[] { new SolveIssue(
                Code: "scip-status",
                Category: SolveIssueCategory.Backend,
                Message: $"SCIP status {status}") }
            : Array.Empty<SolveIssue>();
        return Results.Ok(new SolveReport<Flt64> {
            ProblemStatus = status switch {
                11 => ProblemStatus.Feasible,
                12 => ProblemStatus.Infeasible,
                13 => ProblemStatus.Unbounded,
                14 => ProblemStatus.InfeasibleOrUnbounded,
                _ when solution is not null => ProblemStatus.Feasible,
                _ => ProblemStatus.Unknown,
            },
            TerminationReason = Termination(status, interruptedByCancellation),
            SolutionPresence = solution is null ? SolutionPresence.None : status == 11 ? SolutionPresence.Optimal : SolutionPresence.Incumbent,
            Solution = solution,
            Proof = new SolveProof(status is 11 or 12 or 13 or 14 ? ProofStatus.Claimed : ProofStatus.None),
            Statistics = new SolveStatistics(
                SolveTime: TimeSpan.FromSeconds(seconds),
                Iterations: iterations >= 0 ? (ulong)iterations : null,
                Nodes: nodes >= 0 ? (ulong)nodes : null,
                BestBound: double.IsFinite(bound) && System.Math.Abs(bound) < infinity ? new Flt64(bound) : null,
                Gap: double.IsFinite(gap) && gap >= 0 && gap < infinity ? new Flt64(gap) : null),
            Diagnostics = new SolveDiagnostics<Flt64>(Warnings: Array.AsReadOnly(warnings)),
            Provenance = Provenance(
                config: config,
                descriptor: descriptor,
                solutionAmount: solutionAmount),
        });
    }

    private static SolverProvenance Provenance(
        SolverConfig config,
        SolverDescriptor descriptor,
        int solutionAmount) {
        var effective = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in config.Provenance(descriptor).EffectiveParameters) {
            effective[entry.Key] = entry.Value;
        }
        if (solutionAmount > 1) {
            effective["heuristics/dins/solnum"] = solutionAmount.ToString(CultureInfo.InvariantCulture);
        }
        // **更正：这里此前把 `parallel/maxnthreads` 记进 `ignoredParameters`，
        // 而那个字段的语义是"已声明但<b>未生效</b>的参数"（见 `SolverReportMetadata` 的文档）。
        //
        // **它已生效**：上面第 278—280 行确实调用了 `SetIntParameter("parallel/maxnthreads", ...)`
        // 并在失败时返回 `ParameterFailure`。放进 `ignored` 等于在报告里声称一个已应用的参数没有被应用。
        //
        // **真实的差异是另一件事：** **`SCIPsolveConcurrent` 是一个<b>独立的求解入口</b>，
        // 它把多个种子并行跑并返回第一个结果；C# 走的是常规 `SCIPsolve`，只设置线程上限。
        // 两者的区别是"并行几路、结果怎么选"，不是"线程参数有没有生效"。**
        //
        // **所以这条披露改成描述差异，而不是声称参数丢失。** **把"能力差异"写成"参数失效"，
        // 会让一条本来准确的披露变成一条不准确的报告——而这个方向的报告里，
        // `ignoredParameters` 是被下游当作事实读的。**
        //
        // **A correction: this used to record `parallel/maxnthreads` in `ignoredParameters`, whose documented
        // meaning is "requested parameters that were <b>not applied</b>".**
        //
        // **It <b>is</b> applied: lines 278–280 above call `SetIntParameter("parallel/maxnthreads", ...)` and return
        // `ParameterFailure` if it fails. Listing it under `ignored` claims in the report that an applied parameter was
        // not applied.**
        //
        // **The real difference is another thing: `SCIPsolveConcurrent` is a <b>separate solve entry</b> that runs
        // several seeds in parallel and returns the first result; C# takes the ordinary `SCIPsolve` and only sets the
        // thread limit. The difference is "how many parallel paths, and how the result is chosen" — not "whether the
        // thread parameter took effect".**
        //
        // **So the disclosure now describes the difference instead of claiming a parameter was lost.** **Writing a
        // capability difference as a parameter failure turns an accurate disclosure into an inaccurate report — and
        // downstream reads `ignoredParameters` as fact.**
        // **差异只写在注释里，不塞进任何一个参数表。**
        //
        // **理由：`effective` 与 `ignored` 都是"参数 → 值"的表，下游按键读值。
        // 往里塞一个 `concurrentSolving` 键，就制造了一个不是 SCIP 参数的参数名——
        // 而读取方会以为它是某个可以下发的设置。**
        //
        // **一个"不是参数的事实"该写在文档里，不该伪装成参数。**
        // **这里只做一件事：把一条错误的披露从 `ignored` 里删掉。**
        //
        // **The difference lives only in this comment, in no parameter table.**
        //
        // **Because `effective` and `ignored` are both "parameter → value" maps that downstream reads by key. Putting a
        // `concurrentSolving` key in one fabricates a parameter name SCIP does not have, and a reader would take it for a
        // setting that can be sent.**
        //
 // **A fact that is not a parameter belongs in the docs, not disguised as one.** ** does one thing: it
        // removes an inaccurate disclosure from `ignored`.**
        var ignored = new SortedDictionary<string, string>(StringComparer.Ordinal);
        return config.Provenance(descriptor, ignoredParameters: ignored) with {
            EffectiveParameters = effective,
        };
    }

    internal static TerminationReason Termination(int status, bool interruptedByCancellation) => status switch {
        1 when interruptedByCancellation => TerminationReason.Cancelled,
        1 or 15 => TerminationReason.Interrupted,
        2 or 3 or 4 => TerminationReason.NodeLimit,
        5 => TerminationReason.TimeLimit,
        8 or 9 => TerminationReason.SolutionLimit,
        16 or 17 => TerminationReason.ObjectiveLimit,
        11 or 12 or 13 or 14 => TerminationReason.Completed,
        _ => TerminationReason.BackendFailure,
    };

    internal static SolveReport<Flt64> Cancelled() => new() {
        ProblemStatus = ProblemStatus.Unknown,
        TerminationReason = TerminationReason.Cancelled,
        SolutionPresence = SolutionPresence.None,
    };

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> NativeFailure(string operation, int code) =>
        Failure(ErrorCode.OREngineSolvingException, $"{operation} 返回 {code} / {operation} returned {code}");

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Failure(ErrorCode code, string message) =>
        Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(code, message));
}

/// <summary>
/// 行对偶的接收容器，由 <see cref="ScipLinearNativeSolver"/> 在 LP 求解后按行顺序填充。
/// Sink for row duals, filled in row order by <see cref="ScipLinearNativeSolver"/> after an LP solve.
/// </summary>
internal sealed class ScipDualTargets {
    internal Flt64[] Duals = Array.Empty<Flt64>();
    /// <summary>诊断运行标记：读取 Farkas 乘子而不是对偶解 / Diagnostic flag: read Farkas multipliers instead of dual solutions.</summary>
    internal bool Diagnostic;
    /// <summary>逐行 Farkas 乘子 / Per-row Farkas multipliers.</summary>
    internal Flt64[] Farkas = Array.Empty<Flt64>();
}
