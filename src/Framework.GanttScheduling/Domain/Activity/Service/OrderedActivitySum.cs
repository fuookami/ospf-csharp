// 按原序求和 / Sum in original occurrence order.

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 按原 occurrence 顺序求和 / Sum the terms in their original occurrence order.
/// </summary>
/// <remarks>
/// **不做减法撤销旧值**。增量更新时，被移除的项不参与"从旧和里减掉"，而是重新按原序从头累加。
/// 理由不是精度洁癖：浮点减法的**消去与溢出行为**与加法并不对称——`a + b - b` 并不总等于 `a`。
/// 若用减法撤销，一个被反复增删的项会不断把误差注入总和，而且总和大小依赖**更新历史**，
/// 于是"同一组项、不同的更新路径"会得到不同的和。判据就是要钉住这一点。
///
/// 另外，"仅当新项的序号**严格大于**已有最大序号时才走追加快路"：否则必须重排重算。
/// 这个条件写松了（比如允许相等），重复的序号会被加两次。
///
/// **No subtraction to undo old values.** On an incremental update the removed terms are not "subtracted from the old total"; the total is recomputed from scratch in
/// original order. The reason is not numerical fastidiousness: floating-point subtraction's **cancellation and overflow behaviour** is not symmetric with addition —
/// `a + b - b` is not always `a`. Undoing by subtraction injects error into the running total every time a term is added and removed, and makes the total depend on the
/// **update history**, so "the same set of terms via a different update path" gives a different sum. That is precisely what the criteria pin down.
///
/// Also, the append fast path is taken **only when the new indices are strictly greater than the current maximum**: otherwise a full re-sort and recompute is required.
/// Relaxing that condition (allowing equality, say) would add a duplicated index twice.
/// </remarks>
public sealed class OrderedActivitySum {
    private readonly System.Collections.Generic.SortedDictionary<int, Flt64> _terms = new();
    private readonly Flt64 _initial;

    /// <summary>
    /// 构造求和器 / Construct the accumulator.
    /// </summary>
    /// <param name="initial">初始值 / The initial value.</param>
    public OrderedActivitySum(Flt64 initial = default) {
        _initial = initial;
        Value = initial;
    }

    /// <summary>当前和 / The current sum.</summary>
    public Flt64 Value { get; private set; }

    /// <summary>
    /// 增量更新 / Apply an incremental update.
    /// </summary>
    /// <param name="added">新增项（序号，值）/ The added terms (index, value).</param>
    /// <param name="removed">移除项的序号 / The indices of the removed terms.</param>
    /// <remarks>
    /// **空更新直接返回**：这既是性能，也是语义——两次更新之间若状态没变，`Value` 不该有机会变。
    ///
    /// **An empty update returns immediately**: that is both a performance and a semantic point — if nothing changed between two updates, `Value` should not get a chance to
    /// change.
    /// </remarks>
    public void Update(
        System.Collections.Generic.IReadOnlyList<System.Tuple<int, Flt64>> added,
        System.Collections.Generic.IReadOnlyList<int>? removed = null) {
        if (added.Count == 0 && (removed is null || removed.Count == 0)) {
            return;
        }

        var ordered = new System.Collections.Generic.List<System.Tuple<int, Flt64>>(added);
        ordered.Sort((lhs, rhs) => lhs.Item1.CompareTo(rhs.Item1));

        bool append = (removed is null || removed.Count == 0)
            && (_terms.Count == 0 || ordered[0].Item1 > MaxKey());

        if (removed is not null) {
            foreach (int index in removed) {
                _terms.Remove(index);
            }
        }
        foreach (System.Tuple<int, Flt64> term in ordered) {
            _terms[term.Item1] = term.Item2;
        }

        if (append) {
            Flt64 sum = Value;
            foreach (System.Tuple<int, Flt64> term in ordered) {
                sum += term.Item2;
            }
            Value = sum;
        } else {
            Flt64 sum = _initial;
            foreach (Flt64 quantity in _terms.Values) {
                sum += quantity;
            }
            Value = sum;
        }
    }

    /// <summary>当前最大序号 / The current maximum index.</summary>
    /// <returns>最大序号 / The maximum index.</returns>
    private int MaxKey() {
        int max = int.MinValue;
        foreach (int key in _terms.Keys) {
            max = key;
        }
        return max;
    }
}
