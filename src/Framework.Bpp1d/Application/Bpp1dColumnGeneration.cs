// 一维列生成入口：LP 定价循环 / The BPP1D column-generation entry with the LP pricing loop.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternGeneration;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Application;

/// <summary>
/// 一维列生成入口 / BPP1D column-generation entry.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// 与 `Bpp1dMilp` 的差别只有一处：**在最终整数模型之前先跑一轮 LP 定价循环**，
/// 每次把检验数为负的候选加进受限主问题（RMP），直到没有负检验数或预算用尽。
///
/// **最终模型始终建在"已生成池 + 种子"上，不是建在 RMP 上。** 注释写得很直白：
/// 受限池的整数最优**不是全局最优**，拿它当结果就是把一个近似说成最优。
///
/// The only difference from `Bpp1dMilp` is the **LP pricing loop before the final integer model**: each round adds candidates with a negative reduced cost to the
/// restricted master problem (RMP) until there are none left or the budget runs out. **The final model always sits on "generated pool plus seeds", never on the RMP.**
/// The code is explicit about this: an integer optimum over the restricted pool is **not a global optimum**, and reporting it would pass an approximation off as
/// optimal.
/// </remarks>
public sealed class Bpp1dColumnGeneration<V>
    where V : struct, IFloatingNumber<V> {
    private readonly Bpp1dMilp<V> _milp;
    private readonly IColumnGenerationSolver _solver;
    private readonly IPackingValueAdapter<V> _adapter;
    private readonly PatternGenerationContext<V> _generator;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="solver">可注入求解器 / Injectable solver.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="generator">候选生成策略 / Candidate generation strategy.</param>
    public Bpp1dColumnGeneration(
        IColumnGenerationSolver solver,
        IPackingValueAdapter<V> adapter,
        PatternGenerationContext<V>? generator = null) {
        _milp = new Bpp1dMilp<V>(solver, adapter, generator);
        _solver = solver;
        _adapter = adapter;
        _generator = generator ?? new PatternGenerationContext<V>();
    }

    /// <summary>
    /// 求解列生成模型 / Solve with column generation.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="config">配置 / Configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>解或失败 / Solution or failure.</returns>
    /// <remarks>
    /// **六处判断**：
    ///
    /// | 处 | 判断 | 省略后 |
    /// | --- | --- | --- |
    /// | 预算 | `IterationLimit` 或 `ColumnsPerIteration` 小于 1 ⇒ 失败 | 循环一轮都不跑，静默退化成普通 MILP |
    /// | LP 不可行 | **直接跳出循环**，不失败 | 种子被库存耦合卡住时整个求解失败，而本可以用已生成池收口 |
    /// | LP 求解失败（非不可行） | 失败 | 求解器错误被当成"收敛了" |
    /// | 定价筛选 | **检验数必须 `&lt; -1e-6`** | 零检验数的列被反复加进 RMP，循环空转到预算耗尽 |
    /// | 定价排序 | 按检验数**升序**取前 `ColumnsPerIteration` | 取错了列，收敛慢甚至不收敛 |
    /// | 最终模型 | 建在"已生成池 + 种子"，**不是 RMP** | 受限池的整数最优被当成全局最优 |
    ///
    /// **Six judgements** (table above). The third and fourth rows are the ones that turn into silent misbehaviour rather than an error.
    /// </remarks>
    public async Task<Result<Bpp1dSolution<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        PackingProblem<V> problem,
        Bpp1dSolveConfig<V>? config = null,
        CancellationToken cancellationToken = default) {
        config ??= new Bpp1dSolveConfig<V>();
        if (config.IterationLimit < 1 || config.ColumnsPerIteration < 1) {
            return Results.Failed<Bpp1dSolution<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "列生成预算无效 / Invalid column-generation budget"));
        }

        Result<PatternGenerationResult<V>, ErrorCode, Error<ErrorCode>> generated =
            _generator.Generate(problem, config.GenerationOrDefault);
        if (generated is not Ok<PatternGenerationResult<V>, ErrorCode, Error<ErrorCode>> generatedOk) {
            return Propagate<V, PatternGenerationResult<V>>(generated);
        }

        // **种子取两轮**：`bestFit` 关一次、开一次，即 `listOf(false, true)`——
        // 只留其中一轮会让某些算例的 RMP 起始基不可行。
        //
        // **Seeds come from two rounds**, with `bestFit` off and then on, i.e. `listOf(false, true)`; keeping only one can leave the RMP's starting
        // basis infeasible.
        var seeds = new List<PackingPattern<V>>();
        foreach (bool bestFit in new[] { false, true }) {
            Result<IReadOnlyList<PackingPattern<V>>, ErrorCode, Error<ErrorCode>> round =
                _generator.InitialPatterns(problem, bestFit);
            if (round is not Ok<IReadOnlyList<PackingPattern<V>>, ErrorCode, Error<ErrorCode>> roundOk) {
                return Propagate<V, IReadOnlyList<PackingPattern<V>>>(round);
            }
            seeds.AddRange(roundOk.Value);
        }

        LinearMetaModel<Flt64> model = new();
        var context = new PatternAssignmentContext<V>(
            problem, _adapter, config.ExtensionsOrDefault);
        Try registered = context.Register(DistinctByKey(seeds), model);
        if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<V, Success>(registered);
        }

        int iterations = 0;
        for (int iteration = 1; iteration <= config.IterationLimit; ++iteration) {
            Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lp =
                await _solver.SolveLPAsync("bpp1d_rmp", model).ConfigureAwait(false);
            if (lp is Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> failedLp) {
                // **LP 不可行不是失败，是"换个收口方式"。** 种子被库存耦合卡住时 RMP 可能无解，
                // 这里 `break`：用已生成的池去建最终模型，**不宣称定价已收敛**。
                //
                // **An infeasible LP is not a failure but a different way of closing out.** Stock coupling can make the RMP infeasible; the code breaks here and
                // builds the final model from the generated pool, **without claiming pricing convergence**.
                if (failedLp.Error.Code == ErrorCode.ORModelInfeasible) {
                    break;
                }
                return Results.Failed<Bpp1dSolution<V>>(failedLp.Error);
            }
            if (lp is Fatal<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> fatalLp) {
                return new Fatal<Bpp1dSolution<V>, ErrorCode, Error<ErrorCode>>(fatalLp.Errors);
            }

            IColumnGenerationSolver.LpResult result =
                ((Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>)lp).Value;
            iterations = iteration;

            // **对偶解已经是元模型形状**（`MathConstraint` 为键），不需要再"转一次"。
            // **The duals are already in meta shape** (`MathConstraint` keys), so no further conversion is needed.
            var dual = new MetaDualSolution(
                result.DualSolution,
                new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
            Try refreshed = context.RefreshShadowPrice(dual);
            if (refreshed is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<V, Success>(refreshed);
            }

            var existing = new HashSet<string>();
            foreach (string key in context.Aggregation.Pool.Keys) {
                existing.Add(key);
            }
            var unregistered = new List<PackingPattern<V>>();
            foreach (PackingPattern<V> pattern in generatedOk.Value.Patterns) {
                if (!existing.Contains(pattern.Key)) {
                    unregistered.Add(pattern);
                }
            }
            if (unregistered.Count == 0) {
                break;
            }

            // **有扩展时逐候选读取当前价格**：扩展接口没有承诺只读，批量路径会把价格在候选之间共享。
            // **With extensions, prices are read per candidate**: the extension interface promises no read-only behaviour, and the bulk path would share prices across
            // candidates.
            var priced = new List<(PackingPattern<V> Pattern, Flt64 ReducedCost)>();
            if (context.Extensions.Count == 0) {
                IReadOnlyList<Flt64> reducedCosts = context.ReducedCosts(unregistered);
                for (int index = 0; index < unregistered.Count; ++index) {
                    priced.Add((unregistered[index], reducedCosts[index]));
                }
            }
            else {
                foreach (PackingPattern<V> pattern in unregistered) {
                    priced.Add((pattern, context.ReducedCost(pattern)));
                }
            }

            var candidates = new List<PackingPattern<V>>();
            var eligible = new List<(PackingPattern<V> Pattern, Flt64 ReducedCost)>();
            foreach ((PackingPattern<V> pattern, Flt64 reducedCost) in priced) {
                if (reducedCost.ToDouble() < -1e-6) {
                    eligible.Add((pattern, reducedCost));
                }
            }
            eligible.Sort((left, right) => left.ReducedCost.CompareTo(right.ReducedCost));
            int take = global::System.Math.Min(config.ColumnsPerIteration, eligible.Count);
            for (int index = 0; index < take; ++index) {
                candidates.Add(eligible[index].Pattern);
            }
            if (candidates.Count == 0) {
                break;
            }

            Try added = context.AddColumns(candidates, model);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<V, Success>(added);
            }
        }

        var finalPatterns = new List<PackingPattern<V>>();
        finalPatterns.AddRange(generatedOk.Value.Patterns);
        finalPatterns.AddRange(seeds);
        return await Bpp1dMilp<V>.SolveFinalAsync(
            problem,
            DistinctByKey(finalPatterns),
            generatedOk.Value.Complete,
            iterations,
            PackingModelStage.FinalMilp,
            config,
            _solver,
            _adapter,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按键去重 / Deduplicate by key.</summary>
    /// <param name="patterns">候选 / Candidates.</param>
    /// <returns>去重后的候选 / The deduplicated candidates.</returns>
    private static IReadOnlyList<PackingPattern<V>> DistinctByKey(IReadOnlyList<PackingPattern<V>> patterns) {
        var seen = new HashSet<string>();
        var result = new List<PackingPattern<V>>();
        foreach (PackingPattern<V> pattern in patterns) {
            if (seen.Add(pattern.Key)) {
                result.Add(pattern);
            }
        }
        return result;
    }

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
