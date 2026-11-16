// 一维普通 MILP 入口 / The ordinary BPP1D MILP entry.
// 与固定 Kotlin 的 `Bpp1dSolveConfig` / `Bpp1dSolution` / `Bpp1dMilp` 对应。

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternGeneration;
using Fuookami.Ospf.Core.Solver.Output;
// **`IColumnGenerationSolver` 有两份同名接口**：`Core.Solver` 与 `Framework.Solver`。
// 插件实现的是后者（`ScipColumnGenerationSolver : Framework.Solver.IColumnGenerationSolver`），
// 所以这里只引 `Framework.Solver`，不引 `Core.Solver` —— 否则 CS0104 歧义。
//
// **`IColumnGenerationSolver` exists twice**, in `Core.Solver` and `Framework.Solver`. Plugins implement the latter, so only that one is imported; importing both is CS0104.
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Application;

/// <summary>
/// 一维求解配置 / One-dimensional solve configuration.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <param name="Generation">候选预算 / Candidate budget.</param>
/// <param name="IterationLimit">列生成迭代限制 / Column-generation iteration limit.</param>
/// <param name="ColumnsPerIteration">每轮新增列上限 / Maximum columns added per iteration.</param>
/// <param name="Extensions">建模扩展 / Modeling extensions.</param>
public sealed record Bpp1dSolveConfig<V>(
    PatternGenerationConfig? Generation = null,
    int IterationLimit = 32,
    int ColumnsPerIteration = 32,
    IReadOnlyList<IPatternModelingExtension<V>>? Extensions = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>候选预算，缺省即默认预算 / The candidate budget, defaulting to the standard one.</summary>
    public PatternGenerationConfig GenerationOrDefault => Generation ?? new PatternGenerationConfig();

    /// <summary>建模扩展，缺省即空表 / The extensions, defaulting to none.</summary>
    public IReadOnlyList<IPatternModelingExtension<V>> ExtensionsOrDefault =>
        Extensions ?? new List<IPatternModelingExtension<V>>();
}

/// <summary>
/// 一维求解结果，区分整数最优证书与候选完备性 / Solve result separating the integer proof from candidate completeness.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <param name="Packing">物理装箱结果 / Physical packing.</param>
/// <param name="Status">`Optimal` 或 `Feasible` / `Optimal` or `Feasible`.</param>
/// <param name="Objective">目标值 / Objective value.</param>
/// <param name="CandidateComplete">候选是否完整 / Candidate completeness.</param>
/// <param name="ColumnCount">最终列数 / Final column count.</param>
/// <param name="PricingIterations">定价迭代数 / Pricing iterations.</param>
/// <param name="Enhancements">扩展指标 / Extension indicators.</param>
public sealed record Bpp1dSolution<V>(
    PackingResult<V> Packing,
    string Status,
    V Objective,
    bool CandidateComplete,
    int ColumnCount,
    int PricingIterations,
    IReadOnlyDictionary<string, string>? Enhancements = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>扩展指标，缺省即空表 / The extension indicators, defaulting to none.</summary>
    public IReadOnlyDictionary<string, string> EnhancementsOrEmpty =>
        Enhancements ?? new Dictionary<string, string>();
}

/// <summary>
/// 一维普通 MILP 入口 / Ordinary BPP1D MILP entry.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **这里只做 `Bpp1dMilp`**：候选生成 → 最终整数模型。带 LP 定价循环的 `Bpp1dColumnGeneration`
/// 留到后续——两者的差别只在是否跑定价，`SolveFinalAsync` 是共用的。
///
/// **Only `Bpp1dMilp` here**: generate candidates, then solve the final integer model. `Bpp1dColumnGeneration`, which adds the LP pricing loop, comes next — the
/// two differ only in whether pricing runs, and `SolveFinalAsync` is shared.
/// </remarks>
public sealed class Bpp1dMilp<V>
    where V : struct, IFloatingNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly IPackingValueAdapter<V> _adapter;
    private readonly PatternGenerationContext<V> _generator;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="solver">可注入求解器 / Injectable solver.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="generator">候选生成策略 / Candidate generation strategy.</param>
    public Bpp1dMilp(
        IColumnGenerationSolver solver,
        IPackingValueAdapter<V> adapter,
        PatternGenerationContext<V>? generator = null) {
        _solver = solver;
        _adapter = adapter;
        _generator = generator ?? new PatternGenerationContext<V>();
    }

    /// <summary>
    /// 求解普通装箱模型 / Solve an ordinary packing model.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="config">配置 / Configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>解或失败 / Solution or failure.</returns>
    public async Task<Result<Bpp1dSolution<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        PackingProblem<V> problem,
        Bpp1dSolveConfig<V>? config = null,
        CancellationToken cancellationToken = default) {
        config ??= new Bpp1dSolveConfig<V>();

        Result<PatternGenerationResult<V>, ErrorCode, Error<ErrorCode>> generated =
            _generator.Generate(problem, config.GenerationOrDefault);
        if (generated is not Ok<PatternGenerationResult<V>, ErrorCode, Error<ErrorCode>> generatedOk) {
            return Propagate<V, PatternGenerationResult<V>>(generated);
        }

        return await SolveFinalAsync(
            problem,
            generatedOk.Value.Patterns,
            generatedOk.Value.Complete,
            iterations: 0,
            PackingModelStage.Milp,
            config,
            _solver,
            _adapter,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 建最终模型、求解、复核并组装结果 / Build the final model, solve it, verify and assemble the result.
    /// </summary>
    /// <typeparam name="T">领域数值 / Domain number.</typeparam>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="patterns">候选 / Candidates.</param>
    /// <param name="complete">候选是否完整 / Candidate completeness.</param>
    /// <param name="iterations">定价迭代数 / Pricing iterations.</param>
    /// <param name="stage">注册阶段 / Registration stage.</param>
    /// <param name="config">配置 / Configuration.</param>
    /// <param name="solver">求解器 / Solver.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>解或失败 / Solution or failure.</returns>
    /// <remarks>
    /// **三处不可省略的判断**：
    ///
    /// | 处 | 判断 | 省略后 |
    /// | --- | --- | --- |
    /// | 求解失败 | 候选**不完整**且报不可行 ⇒ 改报 `OREngineSolvingException` | 把"**受限池**无解"说成"原问题不可行" |
    /// | 无解 | 候选**完整**且确实不可行 ⇒ 报 `ORModelInfeasible` | 把"没找到解"与"已证明不可行"混为一谈 |
    /// | 扩展指标 | 键重复 ⇒ 失败 | 一个扩展静默覆盖另一个扩展的指标 |
    ///
    /// **第一行是这张表里最要紧的**：受限候选池无解**不等于**原问题不可行。
    /// 把它报成 `ORModelInfeasible` 会让调用方**放弃一个其实有解的问题**。
    ///
    /// **Three judgements that cannot be dropped** (table above). **The first is the most important**: an infeasible **restricted** pool does **not** mean the original
    /// problem is infeasible, and reporting it as `ORModelInfeasible` would make callers **abandon a problem that is actually solvable**.
    /// </remarks>
    internal static async Task<Result<Bpp1dSolution<T>, ErrorCode, Error<ErrorCode>>> SolveFinalAsync<T>(
        PackingProblem<T> problem,
        IReadOnlyList<PackingPattern<T>> patterns,
        bool complete,
        int iterations,
        PackingModelStage stage,
        Bpp1dSolveConfig<T> config,
        IColumnGenerationSolver solver,
        IPackingValueAdapter<T> adapter,
        CancellationToken cancellationToken = default)
        where T : struct, IFloatingNumber<T> {
        LinearMetaModel<Flt64> model = new();
        var context = new PatternAssignmentContext<T>(problem, adapter, config.ExtensionsOrDefault);
        Try registered = context.Register(patterns, model);
        if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<T, Success>(registered);
        }

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.SolveMILPAsync("bpp1d_final", model).ConfigureAwait(false);
        if (solved is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failedSolve) {
            ErrorCode code = TranslateFinalFailure(failedSolve.Error.Code, complete);
            if (code != failedSolve.Error.Code) {
                return Results.Failed<Bpp1dSolution<T>>(new Err<ErrorCode>(
                    code,
                    "受限候选池无解，尚未证明原问题不可行 / Restricted candidate pool is infeasible; original infeasibility is not proven"));
            }
            return Results.Failed<Bpp1dSolution<T>>(failedSolve.Error);
        }
        if (solved is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatalSolve) {
            return new Fatal<Bpp1dSolution<T>, ErrorCode, Error<ErrorCode>>(fatalSolve.Errors);
        }

        FeasibleSolverOutput<Flt64> output =
            ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;

        Result<IReadOnlyList<(PackingPattern<T> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>> extracted =
            context.ExtractSolution(model);
        if (extracted is not Ok<IReadOnlyList<(PackingPattern<T> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>> extractedOk) {
            return Propagate<T, IReadOnlyList<(PackingPattern<T> Pattern, ulong Usage)>>(extracted);
        }

        // **必须用方案实例本身作键。** `PackingContext.Pack` 的签名就是 `IReadOnlyDictionary<PackingPattern<V>, ulong>`；
        // 用字符串键在这里编译不过——而这正是 里绕开的那个"记录类型按引用比较字典"的陷阱。
        // 这里的安全性来自**同一个实例**：字典里的键就是列池里的那些对象。
        //
        // **The pattern instances themselves must be the keys.** `PackingContext.Pack` takes `IReadOnlyDictionary<PackingPattern<V>, ulong>`; a string key would not
        // compile here — which is exactly the "records compare nested dictionaries by reference" trap sidestepped. It is safe because the keys **are the very
        // instances** held in the pool.
        var usages = new Dictionary<PackingPattern<T>, ulong>();
        foreach ((PackingPattern<T> pattern, ulong usage) in extractedOk.Value) {
            usages[pattern] = usage;
        }

        Result<PackingResult<T>, ErrorCode, Error<ErrorCode>> packing =
            new PackingContext<T>().Pack(problem, usages);
        if (packing is not Ok<PackingResult<T>, ErrorCode, Error<ErrorCode>> packingOk) {
            return Propagate<T, PackingResult<T>>(packing);
        }

        var enhancements = new Dictionary<string, string>();
        foreach (IPatternModelingExtension<T> extension in config.ExtensionsOrDefault) {
            // **扩展指标提取不在这里范围内**：`extractSolution(context, values)` 是扩展接口的第三个钩子，
            // 这里的扩展接口只有定价钩子。**这里明确不做**，缺口记在计划文档里。
            _ = extension;
        }

        // **`Optimal` 的判据是"候选完整 + 间隙为零"。** C# 的 `FeasibleSolverOutput` **没有状态字段**，
        // 只能从 `Gap` 推：完整候选池且 `Gap == 0` 才敢说最优，其余一律 `Feasible`。
        // **宁可少说**：把 `Feasible` 说成 `Optimal` 会让调用方以为拿到了最优证书。
        //
        // **`Optimal` requires "candidates complete and gap zero".** The C# `FeasibleSolverOutput` **has no status field**, so it is inferred from `Gap`: only a complete
        // pool with `Gap == 0` may be called optimal, everything else is `Feasible`. **Understating is the safe direction**: calling `Feasible` optimal would tell callers
        // they hold an optimality certificate.
        bool optimal = complete && output.Gap.ToDouble() == 0.0;
        return Results.Ok(new Bpp1dSolution<T>(
            Packing: packingOk.Value,
            Status: optimal ? "Optimal" : "Feasible",
            Objective: adapter.FromSolver(output.Obj),
            CandidateComplete: complete,
            ColumnCount: patterns.Count,
            PricingIterations: iterations,
            Enhancements: enhancements));
    }

    /// <summary>
    /// 把最终模型的失败翻译成对外错误码 / Translate a final-model failure into the outward error code.
    /// </summary>
    /// <param name="code">求解器报的错误码 / The error code the solver reported.</param>
    /// <param name="complete">候选是否完整 / Whether the candidates are complete.</param>
    /// <returns>对外报出的错误码 / The error code to surface.</returns>
    /// <remarks>
    /// **候选不完整时报的"不可行"只说明这一池装不下，不等于原问题不可行。** 透出原文会让调用方
    /// **放弃一个其实有解的问题**。判据直接打在这个函数上——它是这段逻辑的全部，接线只有一行。
    ///
    /// **An "infeasible" from an incomplete pool only says this pool cannot pack the problem, not that the problem is infeasible.** Passing it through would make
    /// callers **abandon a solvable problem**. The criterion hits this function directly: it is the whole of the logic, and the wiring is one line.
    /// </remarks>
    internal static ErrorCode TranslateFinalFailure(ErrorCode code, bool complete) =>
        !complete && code == ErrorCode.ORModelInfeasible ? ErrorCode.OREngineSolvingException : code;

    /// <summary>把结果类型换成目标类型 / Re-shape a result into the target type.</summary>
    /// <typeparam name="T">目标域数值 / Target domain number.</typeparam>
    /// <typeparam name="TSource">源域数值 / Source domain number.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<Bpp1dSolution<T>, ErrorCode, Error<ErrorCode>> Propagate<T, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where T : struct, IFloatingNumber<T>
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<Bpp1dSolution<T>>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<Bpp1dSolution<T>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<Bpp1dSolution<T>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
