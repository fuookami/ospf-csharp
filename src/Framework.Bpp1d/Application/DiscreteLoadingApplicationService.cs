// 离散装载应用服务 / The discrete-loading application service.

using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp.Application;

/// <summary>
/// 完整领域模型装配结果 / Complete domain model registration.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <param name="Context">离散上下文 / Discrete context.</param>
/// <param name="Model">完整模型 / Full model.</param>
/// <param name="AdditionalWeights">解提取时归一到问题单位的附加重量 / Additional weights, in the problem unit, resolved from the values.</param>
/// <param name="BendersStages">扩展变量的显式主问题或连续追索阶段 / Explicit master or continuous-recourse stages for extension variables.</param>
public sealed record RegisteredDiscreteLoading<V>(
    DiscreteLoadingContext<V> Context,
    LinearMetaModel<Flt64> Model,
    global::System.Func<IReadOnlyList<Flt64>, Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>>>? AdditionalWeights = null,
    IReadOnlyDictionary<IVariableItem, LinearBendersStage>? BendersStages = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>扩展阶段，缺省即空表 / The stages, defaulting to none.</summary>
    public IReadOnlyDictionary<IVariableItem, LinearBendersStage> StagesOrEmpty =>
        BendersStages ?? new Dictionary<IVariableItem, LinearBendersStage>();
}

/// <summary>
/// 可注入应用配置 / Injectable application configuration.
/// </summary>
/// <param name="PreferBenders">是否启用 Benders / Whether Benders is preferred.</param>
/// <param name="FallbackToMilp">是否在 Benders 失败后重新装配完整 MILP / Whether to rebuild the full MILP after a Benders failure.</param>
/// <param name="Benders">Benders 预算 / Benders budget.</param>
public sealed record DiscreteLoadingSolveConfig(
    bool PreferBenders = false,
    bool FallbackToMilp = true,
    LinearBendersConfig? Benders = null);

/// <summary>
/// 已复核的应用结果 / Verified application result.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <param name="Registration">实际成功路径的注册结果 / Registration of the successful path.</param>
/// <param name="Values">完整模型自身 token 顺序的结果 / Values in full-model token order.</param>
/// <param name="Solution">已复核领域结果 / Verified domain solution.</param>
/// <param name="Status">真实求解状态 / Actual solver status.</param>
/// <param name="Objective">领域目标 / Domain objective.</param>
/// <param name="Path">实际路径 / Actual path.</param>
/// <param name="Trace">Benders 轨迹 / Benders trace.</param>
/// <param name="FallbackReason">可选回退原因 / Optional fallback reason.</param>
/// <param name="Termination">后端终止原因 / Backend termination reason.</param>
public sealed record DiscreteLoadingSolveResult<V>(
    RegisteredDiscreteLoading<V> Registration,
    IReadOnlyList<Flt64> Values,
    DiscreteLoadingSolution Solution,
    string Status,
    Flt64 Objective,
    string Path,
    IReadOnlyList<LinearBendersIteration>? Trace = null,
    string? FallbackReason = null,
    string? Termination = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>轨迹，缺省即空表 / The trace, defaulting to none.</summary>
    public IReadOnlyList<LinearBendersIteration> TraceOrEmpty => Trace ?? new List<LinearBendersIteration>();
}

/// <summary>
/// 离散装载 MILP 编排 / Discrete-loading MILP orchestration.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **这里只做 MILP 路径，Benders 路径未实现。** 两条路径，`preferBenders` 时先试 Benders、
/// 失败后可回退。Benders 那半边的依赖（`LinearBendersPartition.BuildAsync`、`LinearBendersAlgorithm`、
/// `LinearBendersResult`）**C# 侧都在**，但这里预算不够把两条路径都做完——**所以显式拒绝 `preferBenders`，
/// 而不是静默走 MILP**。静默降级会让调用方以为拿到了 Benders 的轨迹与终止原因。
///
/// **`finish` 里的**目标复核**是本类最要紧的部分**：求解器报的目标与**按完整模型重新算一遍**的目标
/// 必须一致（相对容差 `1e-6`）。少了它，一个**模型与求解器错位**的错误会被当成正常结果返回——
/// 那类错误最难在业务侧发现，因为结果"看起来合理"。
///
/// **Only the MILP path is implemented here; the Benders path is not.** The code tries Benders first when `preferBenders` is set and may fall back. All the Benders
/// dependencies exist on the C# side, but the budget did not cover both paths — **so `preferBenders` is refused explicitly rather than silently degraded**, which
/// would let callers believe they hold a Benders trace and termination reason. **The objective re-check in `finish` is the most important part**: the solver's
/// objective must agree with one **recomputed from the full model** (relative tolerance `1e-6`), because otherwise a **model/solver mismatch** is returned as a
/// normal answer — the hardest class to spot in business code, since it "looks reasonable".
/// </remarks>
public sealed class DiscreteLoadingApplicationService<V>
    where V : struct, IFloatingNumber<V> {
    private readonly global::System.Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
        _milpSolver;
    private readonly IBendersDecompositionSolver? _bendersSolver;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="milpSolver">MILP 执行器 / MILP executor.</param>
    /// <param name="bendersSolver">可选 Benders 后端 / Optional Benders backend.</param>
    public DiscreteLoadingApplicationService(
        global::System.Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
            milpSolver,
        IBendersDecompositionSolver? bendersSolver = null) {
        _milpSolver = milpSolver;
        _bendersSolver = bendersSolver;
    }

    /// <summary>
    /// 求解 / Solve.
    /// </summary>
    /// <param name="factory">完整模型工厂；**每次调用都必须返回全新的变量、上下文与模型** / Full-model factory; **each call must return fresh variables, context and model**.</param>
    /// <param name="config">求解配置 / Solve configuration.</param>
    /// <returns>已复核结果或失败 / Verified result or failure.</returns>
    public async Task<Result<DiscreteLoadingSolveResult<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        global::System.Func<Result<RegisteredDiscreteLoading<V>, ErrorCode, Error<ErrorCode>>> factory,
        DiscreteLoadingSolveConfig? config = null) {
        config ??= new DiscreteLoadingSolveConfig();
        string? fallback = null;
        LinearMetaModel<Flt64>? rejectedModel = null;
        if (config.PreferBenders) {
            Result<RegisteredDiscreteLoading<V>, ErrorCode, Error<ErrorCode>> bendersRegistration = factory();
            if (bendersRegistration is not Ok<RegisteredDiscreteLoading<V>, ErrorCode, Error<ErrorCode>> bendersOk) {
                return Propagate<DiscreteLoadingSolveResult<V>, RegisteredDiscreteLoading<V>>(bendersRegistration);
            }
            rejectedModel = bendersOk.Value.Model;

            Result<LinearBendersResult, ErrorCode, Error<ErrorCode>> benders = await RunBendersAsync(
                bendersOk.Value, config).ConfigureAwait(false);
            if (benders is Ok<LinearBendersResult, ErrorCode, Error<ErrorCode>> bendersResult) {
                Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> merged = MergeBendersValues(
                    bendersOk.Value.Model, bendersResult.Value.Values);
                if (merged is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> mergedOk) {
                    return Propagate<DiscreteLoadingSolveResult<V>, IReadOnlyList<Flt64>>(merged);
                }
                return await FinishAsync(
                    bendersOk.Value,
                    mergedOk.Value,
                    "Benders",
                    bendersResult.Value.Objective,
                    "benders",
                    null,
                    bendersResult.Value.Iterations,
                    bendersResult.Value.TerminationDetail).ConfigureAwait(false);
            }

            if (!config.FallbackToMilp) {
                return Propagate<DiscreteLoadingSolveResult<V>, LinearBendersResult>(benders);
            }
            fallback = benders is Failed<LinearBendersResult, ErrorCode, Error<ErrorCode>> failedBenders
                ? failedBenders.Error.Message
                : "Benders 致命错误 / Fatal Benders failure";
        }

        Result<RegisteredDiscreteLoading<V>, ErrorCode, Error<ErrorCode>> registration = factory();
        if (registration is not Ok<RegisteredDiscreteLoading<V>, ErrorCode, Error<ErrorCode>> registrationOk) {
            return Propagate<DiscreteLoadingSolveResult<V>, RegisteredDiscreteLoading<V>>(registration);
        }

        // **回退必须拿到全新的模型与上下文。** 工厂复用同一个实例会让"回退"实际还在用被 Benders 改过的模型——
        // 而结果看上去正常。这条判断在这里同样必要。
        //
        // **The fallback must receive a fresh model and context.** Reusing one instance through the factory would mean the "fallback" still works on the model
        // Benders already mutated — and the result would look normal.
        if (RequiresFreshModel(rejectedModel, registrationOk.Value.Model)) {
            return Results.Failed<DiscreteLoadingSolveResult<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "回退必须创建新模型与上下文 / Fallback requires a fresh model and context"));
        }

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await _milpSolver(registrationOk.Value.Model).ConfigureAwait(false);
        if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
            return Propagate<DiscreteLoadingSolveResult<V>, FeasibleSolverOutput<Flt64>>(solved);
        }

        Flt64 objective = solvedOk.Value.Obj;
        var values = new List<Flt64>();
        foreach (Flt64 value in solvedOk.Value.Solution.Values) {
            values.Add(value);
        }

        return await FinishAsync(
            registrationOk.Value,
            values,
            "Feasible",
            objective,
            fallback is null ? "milp_direct" : "milp_fallback_after_benders",
            fallback,
            null,
            null).ConfigureAwait(false);
    }

    /// <summary>
    /// 复核并组装结果 / Verify and assemble the result.
    /// </summary>
    /// <param name="registration">注册结果 / Registration.</param>
    /// <param name="values">完整模型 token 顺序的结果 / Values in full-model token order.</param>
    /// <param name="status">状态 / Status.</param>
    /// <param name="objective">求解器报的目标 / The solver's objective.</param>
    /// <param name="path">实际路径 / Actual path.</param>
    /// <param name="fallbackReason">回退原因 / Fallback reason.</param>
    /// <param name="trace">Benders 轨迹 / Benders trace.</param>
    /// <param name="termination">后端终止原因 / Backend termination reason.</param>
    /// <returns>已复核结果或失败 / Verified result or failure.</returns>
    /// <summary>
    /// 复核并组装结果 / Verify and assemble the result.
    /// </summary>
    /// <remarks>
    /// **`static` 是有意的**：它不碰任何实例状态，所以**列生成那条链可以复用同一份复核**，
    /// 而不必复制一遍"重算目标 + 完整模型复核"。**复制会让两处慢慢分叉**，而分叉的那一侧就是这个会话反复抓的错位。
    ///
    /// **`static` is deliberate**: it touches no instance state, so **the column-generation path can reuse the very same re-check** instead of duplicating "recompute the
    /// objective + verify against the full model". **A copy would drift**, and a drifted copy is exactly the mismatch this session keeps catching.
    /// </remarks>
    internal static async Task<Result<DiscreteLoadingSolveResult<V>, ErrorCode, Error<ErrorCode>>> FinishAsync(
        RegisteredDiscreteLoading<V> registration,
        IReadOnlyList<Flt64> values,
        string status,
        Flt64 objective,
        string path,
        string? fallbackReason,
        IReadOnlyList<LinearBendersIteration>? trace,
        string? termination) {
        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> built =
            await LinearBendersPartition.BuildAsync(registration.Model, registration.StagesOrEmpty).ConfigureAwait(false);
        if (built is not Ok<LinearBendersModels, ErrorCode, Error<ErrorCode>> builtOk) {
            return Propagate<DiscreteLoadingSolveResult<V>, LinearBendersModels>(built);
        }

        // **按 `TokensInSolver` 的顺序取解。** 求解器返回值就是按这个顺序排的——
        // 用别的顺序去索引会**静默取错变量的值**，而错位后的结果仍然"看起来合理"。
        //
        // **Values are read in `TokensInSolver` order**, which is the order the solver returns them in. Indexing by any other order **silently pairs the wrong
        // variable with the wrong value**, and the misaligned result still "looks reasonable".
        var byVariable = new Dictionary<IVariableItem, Flt64>();
        IReadOnlyList<Fuookami.Ospf.Core.Token.Token<Flt64>> tokens = registration.Model.Tokens.TokensInSolver;
        for (int index = 0; index < tokens.Count; ++index) {
            if (index >= values.Count) {
                return Results.Failed<DiscreteLoadingSolveResult<V>>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, "缺少完整模型值 / Missing full-model value"));
            }
            byVariable[tokens[index].Variable] = values[index];
        }

        Try verification = builtOk.Value.Verify(byVariable);
        if (verification is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<DiscreteLoadingSolveResult<V>, Success>(verification);
        }

        // **目标必须按完整模型重算一遍再和求解器的数对上。**
        // 逐目标子项累加；**与主目标同类取正、异类取负**（最大最小化方向的换算）。
        //
        // **The objective is recomputed from the full model and must match the solver's number.** Each sub-objective is accumulated with its sign: **same category as
        // the model's objective is added, the opposite is subtracted**.
        // **必须先把解写回模型 token**：`DiscreteLoadingContext.ExtractSolution` 读的是**模型 token 上的值**，
        // 不是传进来的 `values`。少了这一句，取解会报"缺少装载变量值"——**的端到端判据就是这样抓到的**，
        // 而只走桩求解器的判据**全都抓不到**，因为那些桩自己写了 token。
        //
        // **The solution must first be written back onto the model tokens**: `DiscreteLoadingContext.ExtractSolution` reads **values held by the model's tokens**, not the
        // `values` handed in. Without this line extraction fails with "missing loading value" — **which the earlier end-to-end criterion caught**, while the stub-solver
        // criteria/ **could never catch it**, because those stubs wrote the tokens themselves.
        registration.Model.Tokens.SetSolution(byVariable);

        // **目标子项必须先展平到变量级**：`MetaSubObjects` 的单项式里可以有**表达式符号**
        // （`stowage_{i}_{j}` 这类 `LinearExpressionSymbol`），它们本身不是变量、也不在解表里。
        // 的是 `model.objectiveSnapshot`，那一步就是展平；C# 这边一直缺这一步，于是只要某个域的目标
        // 引用表达式符号，这里就报"目标引用缺少变量值"——**报的措辞还会让人去查解表，而解表是全的**。
        //
        // 复现： 把分区列生成接上之后，真 SCIP 后端跑完链，停在这一句上（`rows52,symbols76`）。
        //
        // **Objective sub-objects must be flattened to variable level first**: a monomial inside `MetaSubObjects` may carry an **expression symbol**
        // (`LinearExpressionSymbol` such as `stowage_{i}_{j}`), which is not a variable and is not in the solution table. The code goes through
        // `model.objectiveSnapshot`, and that step *is* the flattening; this implementation never had it, so any domain objective referencing an expression symbol failed here
        // with "missing objective variable" — **a message that sends you to inspect the solution table, which is complete**.
        // Reproduced: with partition column generation wired, the real SCIP backend ran the chain and stopped exactly here (`rows52,symbols76`).
        Flt64 evaluated = Flt64.Zero;
        IReadOnlyList<MetaSubObject<Flt64>>? snapshot = registration.Model.ObjectiveSnapshot();
        if (snapshot is null) {
            return Results.Failed<DiscreteLoadingSolveResult<V>>(new Err<ErrorCode>(
                ErrorCode.DataNotFound,
                "目标子项无法展平到变量级 / An objective sub-object cannot be flattened to variables"));
        }
        foreach (MetaSubObject<Flt64> part in snapshot) {
            Flt64 value = part.Polynomial.Constant;
            foreach (LinearMonomial<Flt64> term in part.Polynomial.Monomials) {
                if (term.Symbol is not IVariableItem variable || !byVariable.TryGetValue(variable, out Flt64 selected)) {
                    return Results.Failed<DiscreteLoadingSolveResult<V>>(new Err<ErrorCode>(
                        ErrorCode.DataNotFound,
                        "目标引用缺少变量值 / Missing objective variable: " + (term.Symbol?.Name ?? "<null>")));
                }
                value = value.Plus(term.Coefficient.Times(selected));
            }
            evaluated = evaluated.Plus(part.Category == registration.Model.ObjectCategory
                ? value
                : value.Negate());
        }

        // **相对容差 `1e-6`，下界是 1**：目标接近 0 时绝对误差必须仍然很小，
        // 目标很大时允许按比例放大。**只写绝对容差**会让大目标的正常浮点误差被判成错位。
        //
        // **Relative tolerance `1e-6` with a floor of 1**: near zero the absolute error must stay small, while a large objective may scale. **An absolute-only
        // tolerance** would flag normal floating noise on large objectives as a mismatch.
        Flt64 scale = evaluated.Abs().CompareTo(Flt64.One) > 0 ? evaluated.Abs() : Flt64.One;
        Flt64 difference = objective.Minus(evaluated).Abs();
        if (!double.IsFinite(objective.ToDouble())
            || !double.IsFinite(evaluated.ToDouble())
            || difference.ToDouble() > 1e-6 * scale.ToDouble()) {
            return Results.Failed<DiscreteLoadingSolveResult<V>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                $"求解目标与完整模型不一致 / Solver objective differs from the full model: {objective} / {evaluated}"));
        }

        Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> weights =
            registration.AdditionalWeights is { } resolve
                ? resolve(values)
                : Results.Ok<IReadOnlyDictionary<string, Flt64>>(new Dictionary<string, Flt64>());
        if (weights is not Ok<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> weightsOk) {
            return Propagate<DiscreteLoadingSolveResult<V>, IReadOnlyDictionary<string, Flt64>>(weights);
        }

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> extracted =
            registration.Context.ExtractSolution(registration.Model, weightsOk.Value);
        if (extracted is not Ok<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> extractedOk) {
            return Propagate<DiscreteLoadingSolveResult<V>, DiscreteLoadingSolution>(extracted);
        }

        return Results.Ok(new DiscreteLoadingSolveResult<V>(
            Registration: registration,
            Values: values,
            Solution: extractedOk.Value,
            Status: status,
            Objective: objective,
            Path: path,
            Trace: trace,
            FallbackReason: fallbackReason,
            Termination: termination));
    }

    /// <summary>
    /// 回退是否拿回了被 Benders 改过的那个模型 / Whether the fallback received the model Benders already touched.
    /// </summary>
    /// <param name="rejected">Benders 用过的模型，未启用即 `null` / The model Benders used; `null` when Benders never ran.</param>
    /// <param name="current">回退拿到的模型 / The model the fallback received.</param>
    /// <returns>判断结果 / The verdict.</returns>
    /// <remarks>
    /// **这是纯判断，所以判据可以直接打在它上面。** 第一版想用一个桩 Benders 后端端到端测这条，
    /// 而 `IBendersDecompositionSolver` 有六个必须实现的方法——**为了测一个 `ReferenceEquals` 写六个 `throw`**。
    ///
    /// **This is a pure judgement, so the criterion hits it directly.** The first version tried to test it end to end with a stub Benders backend, and
    /// `IBendersDecompositionSolver` has six members that must be implemented — **six `throw`s to test one `ReferenceEquals`**.
    /// </remarks>
    internal static bool RequiresFreshModel(LinearMetaModel<Flt64>? rejected, LinearMetaModel<Flt64> current) =>
        rejected is not null && ReferenceEquals(current, rejected);

    /// <summary>
    /// 跑一次 Benders / Run one Benders pass.
    /// </summary>
    /// <param name="registration">注册结果 / Registration.</param>
    /// <param name="config">配置 / Configuration.</param>
    /// <returns>Benders 结果或失败 / The Benders result or a failure.</returns>
    private async Task<Result<LinearBendersResult, ErrorCode, Error<ErrorCode>>> RunBendersAsync(
        RegisteredDiscreteLoading<V> registration,
        DiscreteLoadingSolveConfig config) {
        if (_bendersSolver is null) {
            return Results.Failed<LinearBendersResult>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "未配置 Benders 后端 / Benders backend is not configured"));
        }

        Result<LinearBendersModels, ErrorCode, Error<ErrorCode>> built =
            await LinearBendersPartition.BuildAsync(registration.Model, registration.StagesOrEmpty).ConfigureAwait(false);
        if (built is not Ok<LinearBendersModels, ErrorCode, Error<ErrorCode>> builtOk) {
            return Propagate<LinearBendersResult, LinearBendersModels>(built);
        }

        using LinearBendersModels models = builtOk.Value;
        return await new LinearBendersAlgorithm(_bendersSolver)
            .SolveAsync(models, config.Benders).ConfigureAwait(false);
    }

    /// <summary>
    /// 把按变量身份合并的 Benders 解整理成完整模型 token 顺序 / Turn the by-variable Benders solution into full-model token order.
    /// </summary>
    /// <param name="model">完整模型 / Full model.</param>
    /// <param name="merged">按变量身份合并的值 / Values merged by variable identity.</param>
    /// <returns>值或失败 / Values or failure.</returns>
    /// <remarks>
    /// **缺一个值就是失败，不是 0。** Benders 只合并它认识的那些变量；把它不认识的位置填 0 会让
    /// 一个"没装东西"的解看起来可行。这里也是直接 `Failed(DataNotFound)`。
    ///
    /// **A missing value is a failure, never zero.** Benders merges only the variables it knows; filling the rest with 0 would make a "loads nothing" solution look
    /// feasible. The code fails with `DataNotFound` here too.
    /// </remarks>
    internal static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> MergeBendersValues(
        LinearMetaModel<Flt64> model,
        IReadOnlyDictionary<IVariableItem, Flt64> merged) {
        var values = new List<Flt64>();
        foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
            if (!merged.TryGetValue(token.Variable, out Flt64 value)) {
                return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, "缺少合并结果 / Missing merged value: " + token.Name));
            }
            values.Add(value);
        }
        return Results.Ok<IReadOnlyList<Flt64>>(values);
    }

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
