// 分区局部 MILP 定价器 / The local MILP pricer for one partition.
// 与固定 Kotlin 的 `MilpPartitionPricingOracle` 对应。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp.Application;

/// <summary>定价返回值；定价证明不等同于整数最优证明 / A pricing result; a pricing proof is not an integer-optimality proof.</summary>
/// <param name="Values">一个或多个局部配置 / One or more local configurations.</param>
/// <param name="Optimal">是否证明本次线性定价最优 / Whether this pricing problem was proven optimal.</param>
public sealed record PartitionPricingResult(
    IReadOnlyList<IReadOnlyDictionary<string, Flt64>> Values,
    bool Optimal);

/// <summary>可替换局部 MILP、搜索或组合定价器 / Replaceable local MILP, search or combinatorial pricer.</summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
public interface IPartitionPricingOracle<V>
    where V : struct, IFloatingNumber<V> {
    Task<Result<PartitionPricingResult, ErrorCode, Error<ErrorCode>>> PriceAsync(
        PartitionLoadingContext<V> context,
        ColumnPricingRequest<Flt64> request);
}

/// <summary>
/// 默认局部 MILP 定价器 / The default local MILP pricer.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **取值按 `model.Tokens.IndexOf` 定位，缺一个就是失败。** 局部模型的 token 顺序和完整模型不同，
/// 用完整模型的顺序去索引会**静默把值配给错误的决策**——而错配后的检验数看起来仍然正常。
///
/// **这里没有给"最优凭证"足够强的判据**：`FeasibleSolverOutput` 没有状态字段，也没有 `BestBound`，
/// 所以用 `Gap` 代替`SolverStatus.Optimal` 且 `|cost - bestBound| &lt;= 1e-6`。**这是等价替换，不是实测等价。**
///
/// **Values are located through `model.Tokens.IndexOf`; a missing one is a failure.** The local model's token order differs from the full model's, so indexing by the
/// full-model order would **silently pair values with the wrong decisions** — and the resulting reduced costs still look normal. **The optimality certificate is not
/// strongly criterion-backed here**: `FeasibleSolverOutput` has neither a status field nor a `BestBound`, so `Gap` stands in for its
/// `SolverStatus.Optimal` 且 `|cost - bestBound| &lt;= 1e-6`. **That is an equivalent substitution, not a measured equivalence.**
/// </remarks>
public sealed class MilpPartitionPricingOracle<V> : IPartitionPricingOracle<V>
    where V : struct, IFloatingNumber<V> {
    private readonly IColumnGenerationSolver _solver;

    /// <summary>构造 / Constructor.</summary>
    /// <param name="solver">可注入、无商业绑定的执行器 / Injectable backend without commercial binding.</param>
    public MilpPartitionPricingOracle(IColumnGenerationSolver solver) {
        _solver = solver;
    }

    public async Task<Result<PartitionPricingResult, ErrorCode, Error<ErrorCode>>> PriceAsync(
        PartitionLoadingContext<V> context,
        ColumnPricingRequest<Flt64> request) {
        Result<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await context.PricingModelAsync(request).ConfigureAwait(false);
        if (built is not Ok<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>> builtOk) {
            return Propagate<PartitionPricingResult, LinearMetaModel<Flt64>>(built);
        }

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveMILPAsync("partition_pricing", builtOk.Value).ConfigureAwait(false);
        if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
            return Propagate<PartitionPricingResult, FeasibleSolverOutput<Flt64>>(solved);
        }

        Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> values = CollectValues(
            context, request, builtOk.Value, solvedOk.Value);
        if (values is not Ok<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> valuesOk) {
            return Propagate<PartitionPricingResult, IReadOnlyDictionary<string, Flt64>>(values);
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = request.ReducedCost(valuesOk.Value);
        if (cost is not Ok<Flt64, ErrorCode, Error<ErrorCode>> costOk) {
            return Propagate<PartitionPricingResult, Flt64>(cost);
        }

        // **`Gap` 代替状态与 `BestBound`**：`Gap` 已闭合即视为本次定价已证明。
        // **`Gap` stands in for the status and `BestBound`**: a closed gap counts as a proven pricing result.
        bool optimal = solvedOk.Value.Gap.ToDouble() <= 1e-6;
        return Results.Ok(new PartitionPricingResult(
            new List<IReadOnlyDictionary<string, Flt64>> { valuesOk.Value }, optimal));
    }

    /// <summary>
    /// 用准备好的模型定价 / Price one partition with an already prepared model.
    /// </summary>
    /// <param name="context">完整行和局部模型边界 / Full-row and local-model boundary.</param>
    /// <param name="entry">批次里的一项 / One entry of the batch.</param>
    /// <param name="batch">批次本身，用于复核它仍然对应当前模型 / The batch itself, used to re-check it still matches the current model.</param>
    /// <returns>可行配置及定价证明 / Feasible configurations and the pricing certificate.</returns>
    /// <remarks>
    /// **与逐分区定价只差"模型从哪来"**：这里用批次里预先建好的模型，不再重新建一遍。求解之后仍然要复核批次没有过期——
    /// 期间加了列，这批模型与请求就都作废了，而拿旧模型解出的检验数**看起来完全正常**。
    ///
    /// **The only difference from per-partition pricing is where the model comes from**: this uses the model the batch built up front instead of building it again. The batch is still
    /// re-checked after the solve — if columns were added meanwhile both the models and the requests are stale, and the reduced costs from an old model **look entirely normal**.
    /// </remarks>
    public async Task<Result<PartitionPricingResult, ErrorCode, Error<ErrorCode>>> PricePreparedAsync(
        PartitionLoadingContext<V> context,
        ColumnPricingEntry<Flt64> entry,
        ColumnPricingBatch<Flt64> batch) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(batch);

        Result<Success, ErrorCode, Error<ErrorCode>> current = await context.RequireCurrentAsync(batch).ConfigureAwait(false);
        if (current is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PartitionPricingResult, Success>(current);
        }

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveMILPAsync("partition_pricing", entry.Model).ConfigureAwait(false);
        if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
            return Propagate<PartitionPricingResult, FeasibleSolverOutput<Flt64>>(solved);
        }

        Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> values =
            CollectValues(context, entry.Request, entry.Model, solvedOk.Value);
        if (values is not Ok<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> valuesOk) {
            return Propagate<PartitionPricingResult, IReadOnlyDictionary<string, Flt64>>(values);
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = entry.Request.ReducedCost(valuesOk.Value);
        if (cost is not Ok<Flt64, ErrorCode, Error<ErrorCode>> costOk) {
            return Propagate<PartitionPricingResult, Flt64>(cost);
        }

        bool optimal = solvedOk.Value.Gap.ToDouble() <= 1e-6;
        return Results.Ok(new PartitionPricingResult(
            new List<IReadOnlyDictionary<string, Flt64>> { valuesOk.Value }, optimal));
    }

    /// <summary>执行一次分区定价 / Price one partition.</summary>
    /// <param name="context">完整行和局部模型边界 / Full-row and local-model boundary.</param>
    /// <param name="request">当前影子价格 / Current shadow prices.</param>
    /// <returns>可行配置及定价证明 / Feasible configurations and the pricing certificate.</returns>
    /// <summary>执行一次分区定价 / Price one partition.</summary>
    /// <returns>可行配置及定价证明 / Feasible configurations and the pricing certificate.</returns>
    /// <summary>
    /// 把局部模型解整理成"决策标识 → 值" / Turn the local model's solution into "decision identity to value".
    /// </summary>
    /// <param name="model">局部模型 / The local model.</param>
    /// <param name="solved">局部解 / The local solution.</param>
    /// <returns>取值或失败 / The values or a failure.</returns>
    /// <remarks>
    /// **`IndexOf` 返回 `null` 与"值越界"都必须是 `DataNotFound`。** 把它们折成 0 会造出一个"什么都没装"的列，
    /// 而那个列**在完整模型里可能可行**——于是错误静默进入下一轮迭代。
    ///
    /// **Both a `null` from `IndexOf` and an out-of-range index must be `DataNotFound`.** Folding them to zero would produce a "loads nothing" column, and that column
    /// **may well be feasible in the full model** — so the error enters the next iteration silently.
    /// </remarks>
    internal static Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> CollectValues(
        PartitionLoadingContext<V> context,
        ColumnPricingRequest<Flt64> request,
        LinearMetaModel<Flt64> model,
        FeasibleSolverOutput<Flt64> solved) {
        var values = new Dictionary<string, Flt64>();
        foreach (PartitionDecision decision in context.Decisions) {
            if (decision.PartitionId != request.BlockId) {
                continue;
            }
            int? index = model.Tokens.IndexOf(decision.Variable);
            if (index is null) {
                return Results.Failed<IReadOnlyDictionary<string, Flt64>>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, "缺少局部 token / Missing local token: " + decision.Id));
            }
            if (index.Value < 0 || index.Value >= solved.Solution.Values.Count) {
                return Results.Failed<IReadOnlyDictionary<string, Flt64>>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, "缺少局部变量值 / Missing local value: " + decision.Id));
            }
            values[decision.Id] = solved.Solution.Values[index.Value];
        }
        return Results.Ok<IReadOnlyDictionary<string, Flt64>>(values);
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
