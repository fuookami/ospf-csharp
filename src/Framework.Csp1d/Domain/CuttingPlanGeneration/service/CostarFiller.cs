#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 配规填充器，为切割方案的剩余宽度填充配规切片 / Costar filler: fills remaining width with costar slices.
///
/// 配规只作为切片加入，不进入 demandContributions。
/// Costars are added as slices only, not in demandContributions.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
internal sealed class CostarFiller<V> where V : struct, IComparable<V> {
    private readonly Func<Quantity<V>, Quantity<V>, Quantity<V>> _subtractQuantity;
    private readonly Func<Quantity<V>, Quantity<V>, bool> _isLessThan;
    private readonly Func<Quantity<V>, bool> _isZero;
    private readonly Func<Quantity<V>, bool> _isPositive;

    /// <summary>
    /// 创建配规填充器 / Create costar filler.
    /// </summary>
    /// <param name="subtractQuantity">量值减法 / Quantity subtraction.</param>
    /// <param name="isLessThan">小于比较 / Less-than comparison.</param>
    /// <param name="isZero">零值判断 / Zero check.</param>
    /// <param name="isPositive">正值判断 / Positive check.</param>
    public CostarFiller(
        Func<Quantity<V>, Quantity<V>, Quantity<V>> subtractQuantity,
        Func<Quantity<V>, Quantity<V>, bool> isLessThan,
        Func<Quantity<V>, bool> isZero,
        Func<Quantity<V>, bool> isPositive) {
        _subtractQuantity = subtractQuantity;
        _isLessThan = isLessThan;
        _isZero = isZero;
        _isPositive = isPositive;
    }

    /// <summary>
    /// 尝试为切割方案填充配规 / Try to fill a cutting plan with costars.
    /// </summary>
    /// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
    /// <typeparam name="TSlice">切片类型 / Slice type.</typeparam>
    /// <typeparam name="TCostar">配规类型 / Costar type.</typeparam>
    /// <param name="plan">原始方案 / Original plan.</param>
    /// <param name="restWidth">剩余宽度 / Rest width.</param>
    /// <param name="costars">可用配规列表 / Available costars.</param>
    /// <param name="getCostarWidths">获取配规宽度列表 / Get costar width list.</param>
    /// <param name="buildPlan">构建新方案 / Build new plan.</param>
    /// <param name="buildSlice">构建切片 / Build slice.</param>
    /// <returns>填充后的方案列表 / Filled plan list.</returns>
    public IReadOnlyList<TPlan> Fill<TPlan, TSlice, TCostar>(
        TPlan plan,
        Quantity<V>? restWidth,
        IReadOnlyList<TCostar> costars,
        Func<TCostar, IReadOnlyList<Quantity<V>>> getCostarWidths,
        Func<TPlan, IReadOnlyList<TSlice>, TPlan> buildPlan,
        Func<TCostar, Quantity<V>, long, TSlice> buildSlice) {
        if (costars.Count == 0) {
            return new[] { plan };
        }

        if (restWidth is not { } rw) {
            return new[] { plan };
        }

        if (_isZero(rw) || !_isPositive(rw)) {
            return new[] { plan };
        }

        var results = new List<TPlan>();
        FillDFS(
            currentSlices: new List<TSlice>(),
            remainingWidth: rw,
            costars: costars,
            costarIndex: 0,
            plan: plan,
            results: results,
            getCostarWidths: getCostarWidths,
            buildPlan: buildPlan,
            buildSlice: buildSlice,
            maxAmount: 2L
        );

        return results.Count == 0 ? new[] { plan } : results;
    }

    private void FillDFS<TPlan, TSlice, TCostar>(
        List<TSlice> currentSlices,
        Quantity<V> remainingWidth,
        IReadOnlyList<TCostar> costars,
        int costarIndex,
        TPlan plan,
        List<TPlan> results,
        Func<TCostar, IReadOnlyList<Quantity<V>>> getCostarWidths,
        Func<TPlan, IReadOnlyList<TSlice>, TPlan> buildPlan,
        Func<TCostar, Quantity<V>, long, TSlice> buildSlice,
        long maxAmount) {
        if (_isZero(remainingWidth) || !_isPositive(remainingWidth)) {
            results.Add(buildPlan(plan, currentSlices));
            return;
        }

        if (costarIndex >= costars.Count) {
            results.Add(buildPlan(plan, currentSlices));
            return;
        }

        TCostar? costar = costars[costarIndex];
        foreach (Quantity<V> costarWidth in getCostarWidths(costar)) {
            if (_isLessThan(remainingWidth, costarWidth)) {
                continue;
            }

            long amountLimit = global::System.Math.Min(ComputeMaxCostarAmount(costarWidth, remainingWidth), maxAmount);
            for (long amount = 1L; amount <= amountLimit; amount++) {
                Quantity<V> totalCostarWidth = RepeatWidth(costarWidth, amount);
                Quantity<V> newRemaining = _subtractQuantity(remainingWidth, totalCostarWidth);

                currentSlices.Add(buildSlice(costar, costarWidth, amount));

                FillDFS(
                    currentSlices: currentSlices,
                    remainingWidth: newRemaining,
                    costars: costars,
                    costarIndex: costarIndex + 1,
                    plan: plan,
                    results: results,
                    getCostarWidths: getCostarWidths,
                    buildPlan: buildPlan,
                    buildSlice: buildSlice,
                    maxAmount: maxAmount
                );

                currentSlices.RemoveAt(currentSlices.Count - 1);
            }
        }

        // Try skipping this costar
        FillDFS(
            currentSlices: currentSlices,
            remainingWidth: remainingWidth,
            costars: costars,
            costarIndex: costarIndex + 1,
            plan: plan,
            results: results,
            getCostarWidths: getCostarWidths,
            buildPlan: buildPlan,
            buildSlice: buildSlice,
            maxAmount: maxAmount
        );
    }

    private long ComputeMaxCostarAmount(Quantity<V> costarWidth, Quantity<V> remainingWidth) {
        if (_isLessThan(remainingWidth, costarWidth)) {
            return 0L;
        }

        long count = 0L;
        Quantity<V> w = remainingWidth;
        while (!_isLessThan(w, costarWidth)) {
            w = _subtractQuantity(w, costarWidth);
            count++;
        }
        return count;
    }

    private Quantity<V> RepeatWidth(Quantity<V> width, long times) {
        Quantity<V> result = width;
        for (long i = 1L; i < times; i++) {
            result = _subtractQuantity(result, width);
        }
        return result;
    }
}
