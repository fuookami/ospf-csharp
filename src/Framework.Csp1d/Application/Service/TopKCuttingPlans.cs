#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// 切割方案 Top-K 容器 / Cutting plan Top-K container.
///
/// 使用优先队列维护评分最高的 K 个方案。
/// Uses a priority queue to maintain the top-K plans by score.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class TopKCuttingPlans<V> where V : struct {
    private readonly Int64 _limit;
    private readonly ScoreComparer _comparer = new();
    private readonly PriorityQueue<CuttingPlan<V>, Flt64> _heap;

    /// <summary>
    /// 创建 Top-K 容器 / Create Top-K container.
    /// </summary>
    /// <param name="limit">保留上限 / Top-K limit.</param>
    public TopKCuttingPlans(Int64 limit) {
        _limit = limit;
        _heap = new PriorityQueue<CuttingPlan<V>, Flt64>(new ScoreMinComparer());
    }

    /// <summary>
    /// 插入单个方案 / Offer one plan.
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    public void Offer(CuttingPlan<V> plan) {
        if (_limit <= Int64.Zero) {
            return;
        }

        Flt64 score = ComputeScore(plan);

        if (new Int64(_heap.Count) < _limit) {
            _heap.Enqueue(plan, score);
            return;
        }

        // Peek at the worst (min-score) element
        if (_heap.Count > 0 && _heap.TryPeek(out _, out Flt64 worstScore)) {
            if (_comparer.CompareScores(score, worstScore) > 0) {
                _heap.Dequeue();
                _heap.Enqueue(plan, score);
            }
        }
    }

    /// <summary>
    /// 批量插入方案 / Offer plans in batch.
    /// </summary>
    /// <param name="plans">方案集合 / Plan collection.</param>
    public void OfferAll(IEnumerable<CuttingPlan<V>> plans) {
        foreach (CuttingPlan<V> plan in plans) {
            Offer(plan);
        }
    }

    /// <summary>
    /// 导出排序结果 / Export sorted result.
    /// </summary>
    /// <returns>按评分降序的方案列表 / Plans sorted by score in descending order.</returns>
    public List<CuttingPlan<V>> ToSortedList() {
        var items = new List<(CuttingPlan<V> Plan, Flt64 Score)>();
        while (_heap.Count > 0) {
            if (_heap.TryDequeue(out CuttingPlan<V>? plan, out Flt64 score)) {
                items.Add((plan, score));
            }
        }

        // Re-enqueue and also build sorted list (descending by score)
        foreach ((CuttingPlan<V> Plan, Flt64 Score) item in items) {
            _heap.Enqueue(item.Plan, item.Score);
        }

        items.Sort((a, b) => _comparer.CompareScores(b.Score, a.Score));
        return items.Select(i => i.Plan).ToList();
    }

    private static Flt64 ComputeScore(CuttingPlan<V> plan) {
        V? v = plan.UsedWidth?.Value;
        if (v is Flt64 f) {
            return f;
        }

        if (v is FltX x) {
            return x.ToFlt64();
        }

        return Flt64.Zero;
    }

    /// <summary>
    /// 分数比较器（支持 null 值）/ Score comparer supporting null values.
    /// </summary>
    internal sealed class ScoreComparer {
        public int CompareScores(Flt64? left, Flt64? right) {
            return (left, right) switch {
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                _ => left.Value.CompareTo(right.Value)
            };
        }
    }

    /// <summary>
    /// 最小堆比较器 / Min-heap comparer for PriorityQueue.
    /// </summary>
    private sealed class ScoreMinComparer : IComparer<Flt64> {
        public int Compare(Flt64 x, Flt64 y) => x.CompareTo(y);
    }
}
