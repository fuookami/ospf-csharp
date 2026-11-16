// 对称特征分解、岛屿迁移与约束排序 / Symmetric eigendecomposition, island migration and constraint ranking.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 对称特征分解结果 / A symmetric-eigendecomposition result.
/// </summary>
/// <remarks>
/// **`vectors` 是"每项一个特征向量"，不是矩阵的行**。这里显式转置过；按行读取会让调用方拿到的"主方向"其实是另一组正交基，
/// 而每个向量仍然满足正交归一性，**检查向量本身完全看不出异常**。
/// **`vectors` holds one eigenvector per entry, not the matrix's rows.** The transpose is explicit here; reading them as rows gives
/// the caller "principal directions" that are actually a different orthonormal basis while every vector still looks orthonormal —
/// **inspecting the vectors alone reveals nothing**.
/// </remarks>
/// <param name="Values">特征值 / The eigenvalues.</param>
/// <param name="Vectors">特征向量 / The eigenvectors.</param>
public sealed record SymmetricEigenResult(
    IReadOnlyList<double> Values,
    IReadOnlyList<IReadOnlyList<double>> Vectors) {
    /// <summary>是否有效 / Whether the result is valid.</summary>
    public bool IsValid => Values.Count > 0;
}

/// <summary>
/// 对称矩阵特征分解 / Symmetric-matrix eigendecomposition.
/// </summary>
/// <remarks>
/// **用 Jacobi 旋转而不是更"高效"的算法**。Jacobi 对对称矩阵无条件稳定、无需主元选取，且每次旋转都显式保持正交性；
/// 换成三对角化 + QR 会引入位移策略与收敛判据两个新的出错面，而这里只处理小尺寸协方差矩阵。
/// **Jacobi rotations rather than a "faster" algorithm.** Jacobi is unconditionally stable on symmetric matrices, needs no pivoting, and
/// preserves orthogonality explicitly at every rotation; tridiagonalization plus QR would introduce two new failure surfaces (shift
/// strategy and convergence test) for the small covariance matrices handled here.
/// **收敛判据是"非对角元平方和 &lt;= 容差"**，而不是"单次旋转足够小"。用后者会让已经收敛的矩阵继续被旋转，特征向量在
/// 已收敛的子空间里缓慢漂移——**特征值看起来完全正确**，但向量不再对应原来的特征方向。
/// **The convergence test is "the sum of squared off-diagonals &lt;= tolerance"**, not "one rotation is small enough". The latter keeps
/// rotating an already-converged matrix and the eigenvectors drift slowly within the converged subspace — **the eigenvalues look
/// perfect** while the vectors no longer point along the original eigen-directions.
/// **非有限特征值时返回空结果**，而不是把 `NaN` 交给调用方。空结果由 `IsValid` 显式暴露。
/// **Non-finite eigenvalues yield an empty result** instead of handing `NaN` to the caller; `IsValid` exposes it explicitly.
/// </remarks>
public static class SymmetricEigen {
    /// <summary>
    /// 分解对称矩阵 / Decompose a symmetric matrix.
    /// </summary>
    /// <param name="matrix">矩阵 / The matrix.</param>
    /// <param name="maxSweeps">最大扫描次数 / The maximum sweep count.</param>
    /// <param name="tolerance">容差 / The tolerance.</param>
    /// <returns>分解结果 / The decomposition result.</returns>
    public static SymmetricEigenResult Decompose(
        IReadOnlyList<IReadOnlyList<double>> matrix, int maxSweeps = 60, double tolerance = 1.0e-12) {
        ArgumentNullException.ThrowIfNull(matrix);
        int size = matrix.Count;
        if (size == 0) {
            return new SymmetricEigenResult(Array.Empty<double>(), Array.Empty<IReadOnlyList<double>>());
        }
        for (int row = 0; row < size; row++) {
            if (matrix[row].Count != size) {
                return new SymmetricEigenResult(Array.Empty<double>(), Array.Empty<IReadOnlyList<double>>());
            }
        }

        double[][] working = new double[size][];
        double[][] vectors = new double[size][];
        for (int row = 0; row < size; row++) {
            working[row] = new double[size];
            vectors[row] = new double[size];
            for (int column = 0; column < size; column++) {
                working[row][column] = matrix[row][column];
                vectors[row][column] = row == column ? 1.0 : 0.0;
            }
        }

        for (int sweep = 0; sweep < maxSweeps; sweep++) {
            double offDiagonal = 0.0;
            for (int row = 0; row < size; row++) {
                for (int column = row + 1; column < size; column++) {
                    offDiagonal += working[row][column] * working[row][column];
                }
            }
            if (offDiagonal <= tolerance) {
                break;
            }

            for (int first = 0; first < size; first++) {
                for (int second = first + 1; second < size; second++) {
                    double pair = working[first][second];
                    if (global::System.Math.Abs(pair) <= tolerance) {
                        continue;
                    }
                    double theta = (working[second][second] - working[first][first]) / (2.0 * pair);
                    double sign = theta >= 0.0 ? 1.0 : -1.0;
                    double t = sign / (global::System.Math.Abs(theta)
                        + global::System.Math.Sqrt((theta * theta) + 1.0));
                    double cosine = 1.0 / global::System.Math.Sqrt((t * t) + 1.0);
                    double sine = t * cosine;

                    for (int index = 0; index < size; index++) {
                        double left = working[index][first];
                        double right = working[index][second];
                        working[index][first] = (cosine * left) - (sine * right);
                        working[index][second] = (sine * left) + (cosine * right);
                    }
                    for (int index = 0; index < size; index++) {
                        double upper = working[first][index];
                        double lower = working[second][index];
                        working[first][index] = (cosine * upper) - (sine * lower);
                        working[second][index] = (sine * upper) + (cosine * lower);
                    }
                    for (int index = 0; index < size; index++) {
                        double left = vectors[index][first];
                        double right = vectors[index][second];
                        vectors[index][first] = (cosine * left) - (sine * right);
                        vectors[index][second] = (sine * left) + (cosine * right);
                    }
                }
            }
        }

        var values = new List<double>(size);
        for (int index = 0; index < size; index++) {
            values.Add(working[index][index]);
        }
        foreach (double value in values) {
            if (!double.IsFinite(value)) {
                return new SymmetricEigenResult(Array.Empty<double>(), Array.Empty<IReadOnlyList<double>>());
            }
        }

        var columns = new List<IReadOnlyList<double>>(size);
        for (int column = 0; column < size; column++) {
            var eigenvector = new List<double>(size);
            for (int row = 0; row < size; row++) {
                eigenvector.Add(vectors[row][column]);
            }
            columns.Add(eigenvector);
        }
        return new SymmetricEigenResult(values, columns);
    }
}

/// <summary>
/// 岛屿迁移 / Island migration.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IIslandMigration<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 执行一次迁移 / Perform one migration.
    /// </summary>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="islands">岛屿集合 / The island collection.</param>
    /// <param name="model">模型 / The model.</param>
    /// <param name="entropy">均匀熵源 / The uniform entropy source.</param>
    /// <param name="fitness">适应度选择器 / The fitness selector.</param>
    /// <returns>迁移后的岛屿 / The islands after migration.</returns>
    IReadOnlyList<IReadOnlyList<T>> Invoke<T>(
        Iteration iteration,
        IReadOnlyList<IReadOnlyList<T>> islands,
        IAbstractCallBackModelInterface<object, ObjValue, V> model,
        Func<Flt64> entropy,
        Func<T, double> fitness)
        where T : IIndividual<ObjValue, V>;
}

/// <summary>
/// 不迁移 / No migration.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class NoMigration<ObjValue, V> : IIslandMigration<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <inheritdoc/>
    public IReadOnlyList<IReadOnlyList<T>> Invoke<T>(
        Iteration iteration,
        IReadOnlyList<IReadOnlyList<T>> islands,
        IAbstractCallBackModelInterface<object, ObjValue, V> model,
        Func<Flt64> entropy,
        Func<T, double> fitness)
        where T : IIndividual<ObjValue, V> {
        ArgumentNullException.ThrowIfNull(islands);
        ArgumentNullException.ThrowIfNull(fitness);
        return islands;
    }
}

/// <summary>
/// 岛屿迁移辅助 / Island-migration helpers.
/// </summary>
public static class IslandMigrationSupport {
    /// <summary>
    /// 按适应度升序（优者在前）排序 / Rank ascending, best first.
    /// </summary>
    /// <remarks>
    /// **用逐次选择最小元，而不是 `List.Sort`**。排序比较器一旦不满足传递性（例如比较器里带了随机数或容差），
    /// `Sort` 会静默产出未定义顺序；逐次选择最小元的结果虽然慢，但**在不可传递的比较器下仍然是可复现的**。
    /// **Repeated minimum selection rather than `List.Sort`.** Once a comparator violates transitivity (say it embeds randomness or a
    /// tolerance), `Sort` silently produces an undefined order; repeated minimum selection is slower but **stays reproducible under a
    /// non-transitive comparator**.
    /// </remarks>
    /// **比较的是 `fitness` 而不是 `objective`**：`compareStrictly(model, fitness, ...)`，因为目标值类型是任意的，
    /// 而适应度是标量。比较目标值需要目标值类型自带序，而它并没有。
 /// **The comparison uses `fitness`, not `objective`**: this implementation uses `compareStrictly(model, fitness, ...)` because the objective
    /// type is arbitrary while fitness is a scalar. Comparing objectives would require the objective type to be ordered, which it is not.
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="island">岛屿 / The island.</param>
    /// <param name="fitness">适应度选择器 / The fitness selector.</param>
    /// <returns>排序后的个体 / The ranked individuals.</returns>
    public static List<T> RankAscending<T>(IReadOnlyList<T> island, Func<T, double> fitness) {
        ArgumentNullException.ThrowIfNull(island);
        ArgumentNullException.ThrowIfNull(fitness);
        var remaining = new List<T>(island);
        var ranked = new List<T>(remaining.Count);
        while (remaining.Count > 0) {
            int bestIndex = 0;
            for (int index = 1; index < remaining.Count; index++) {
                if (fitness(remaining[index]) < fitness(remaining[bestIndex])) {
                    bestIndex = index;
                }
            }
            ranked.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }
        return ranked;
    }
}

/// <summary>
/// 环形迁移 / Ring migration.
/// </summary>
/// <remarks>
/// **迁入者是"上一座岛"的最优若干个体，被替换的是本岛最差的若干个体**，岛屿规模保持不变。方向写反（从下一座岛迁入）
/// 会让信息沿环反向流动，收敛顺序整体反转，而每座岛的规模与最优值看起来都正常。
/// **The migrants are the previous island's best individuals and the replaced ones are this island's worst**, keeping the island size
/// unchanged. Reversing the direction (migrating from the next island) makes information flow backwards around the ring and inverts the
/// convergence order while every island's size and best value looks normal.
/// **迁移量至少 1、且不超过两座岛的最小规模**。用 `rate * size` 直接取整会在小岛上得到 0——迁移静默变成无操作，
/// 而配置里的 `rate` 看起来完全正确。
/// **The migration amount is at least 1 and never exceeds the smaller island's size.** Taking `rate * size` directly yields 0 on small
/// islands — migration silently becomes a no-op while the configured `rate` looks perfectly correct.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class RingMigration<ObjValue, V>(double rate) : IIslandMigration<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public RingMigration() : this(0.1) { }

    /// <summary>迁移率 / The migration rate.</summary>
    public double Rate { get; } = rate;

    /// <inheritdoc/>
    public IReadOnlyList<IReadOnlyList<T>> Invoke<T>(
        Iteration iteration,
        IReadOnlyList<IReadOnlyList<T>> islands,
        IAbstractCallBackModelInterface<object, ObjValue, V> model,
        Func<Flt64> entropy,
        Func<T, double> fitness)
        where T : IIndividual<ObjValue, V> {
        ArgumentNullException.ThrowIfNull(islands);
        if (islands.Count < 2) {
            return islands;
        }

        var ranked = new List<List<T>>(islands.Count);
        foreach (IReadOnlyList<T> island in islands) {
            ranked.Add(IslandMigrationSupport.RankAscending<T>(island, fitness));
        }

        var migrated = new List<IReadOnlyList<T>>(islands.Count);
        for (int index = 0; index < islands.Count; index++) {
            List<T> incoming = ranked[(index - 1 + islands.Count) % islands.Count];
            List<T> local = ranked[index];
            int amount = global::System.Math.Clamp(
                (int)(local.Count * Rate), 1,
                global::System.Math.Min(local.Count, incoming.Count));
            if (amount <= 0) {
                migrated.Add(islands[index]);
                continue;
            }
            // **保留的是 `local` 的"最优部分"（前段），被替换的是末尾的"最差部分"**。这里写 `local.dropLast(amount)`，
            // 即丢掉末尾；写成 `local.Take(local.Count - amount)` 在这里恰好等价，但若误取 `Skip(amount)` 就会丢掉**最优个体**
            // 而保留最差——岛屿的最优值单调变差，而规模与迁移率完全正常。
            // **The retained part is `local`'s best prefix and the replaced part is the worst suffix.** This writes
            // `local.dropLast(amount)`, i.e. drop the tail; `Take(local.Count - amount)` is equivalent here, but `Skip(amount)` would drop
            // the **best** individuals and keep the worst — the island's best value degrades monotonically while size and migration
            // rate look perfectly normal.
            var merged = new List<T>(local.Count);
            for (int position = 0; position < local.Count - amount; position++) {
                merged.Add(local[position]);
            }
            for (int position = 0; position < amount; position++) {
                merged.Add(incoming[position]);
            }
            migrated.Add(merged);
        }
        return migrated;
    }
}

/// <summary>
/// 最优到最差迁移 / Best-to-worst migration.
/// </summary>
/// <remarks>
/// **最优岛与最差岛逐个挑选，不依赖比较器传递性**。`ArgMin`/`ArgMax` 一类实现只做一次线性扫描，这没问题；但把"最优"和"最差"
/// 交给同一个 `OrderBy` 再取首尾，会在比较器不可传递时给出彼此矛盾的结论（首尾可能都不是极值）。
/// **The best and worst islands are picked one at a time without relying on comparator transitivity.** A single linear scan is fine; but
/// sorting once and taking the first and last elements yields contradictory conclusions when the comparator is non-transitive (neither
/// end need be an extremum).
/// **`source == target` 时不迁移**。只有一座岛有效时"最优岛"就是"最差岛"，迁移会把最优个体复制到自己身上。
/// **No migration when `source == target`.** With only one valid island the "best" island is also the "worst", and migrating would copy
/// the best individual onto itself.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class BestToWorstMigration<ObjValue, V>(int amount) : IIslandMigration<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public BestToWorstMigration() : this(1) { }

    /// <summary>迁移个体数 / The migrant count.</summary>
    public int Amount { get; } = amount;

    /// <inheritdoc/>
    public IReadOnlyList<IReadOnlyList<T>> Invoke<T>(
        Iteration iteration,
        IReadOnlyList<IReadOnlyList<T>> islands,
        IAbstractCallBackModelInterface<object, ObjValue, V> model,
        Func<Flt64> entropy,
        Func<T, double> fitness)
        where T : IIndividual<ObjValue, V> {
        ArgumentNullException.ThrowIfNull(islands);
        if (islands.Count < 2) {
            return islands;
        }
        foreach (IReadOnlyList<T> island in islands) {
            if (island.Count == 0) {
                return islands;
            }
        }

        var ranked = new List<List<T>>(islands.Count);
        foreach (IReadOnlyList<T> island in islands) {
            ranked.Add(IslandMigrationSupport.RankAscending<T>(island, fitness));
        }

        int source = 0;
        int target = 0;
        for (int index = 0; index < ranked.Count; index++) {
            if (fitness(ranked[index][0]) < fitness(ranked[source][0])) {
                source = index;
            }
            if (fitness(ranked[index][0]) > fitness(ranked[target][0])) {
                target = index;
            }
        }
        if (source == target) {
            return islands;
        }

        int take = global::System.Math.Min(Amount, ranked[source].Count);
        if (take <= 0) {
            return islands;
        }
        var migrants = new List<T>(take);
        for (int index = 0; index < take; index++) {
            migrants.Add(ranked[source][index]);
        }

        var migrated = new List<IReadOnlyList<T>>(islands.Count);
        for (int index = 0; index < islands.Count; index++) {
            if (index != target) {
                migrated.Add(islands[index]);
                continue;
            }
            int keep = ranked[target].Count - global::System.Math.Min(migrants.Count, ranked[target].Count);
            var merged = new List<T>(ranked[target].Count);
            for (int position = 0; position < keep; position++) {
                merged.Add(ranked[target][position]);
            }
            foreach (T migrant in migrants) {
                merged.Add(migrant);
            }
            migrated.Add(merged);
        }
        return migrated;
    }
}

/// <summary>
/// 约束评估条目 / A constraint-assessment entry.
/// </summary>
/// <typeparam name="T">载荷类型 / The payload type.</typeparam>
/// <param name="Value">载荷 / The payload.</param>
/// <param name="Objective">目标值 / The objective value.</param>
/// <param name="Violation">约束违反量 / The constraint violation.</param>
public sealed record ConstraintAssessment<T>(T Value, Flt64 Objective, Flt64 Violation);

/// <summary>
/// ε 约束排序 / ε-constraint ranking.
/// </summary>
/// <remarks>
/// **ε 随迭代收缩到 0，排序判据才从"先看违反量"过渡到"先看目标值"**。固定 ε 会让算法永远优先满足约束，
/// 后期无法在可行域内继续优化；ε 直接取 0 则一开始就只看目标值，前期会大量产生不可行解。
/// **ε shrinks to 0 across the horizon so the ordering transitions from "violation first" to "objective first".** A fixed ε keeps the
/// algorithm prioritizing feasibility forever and prevents late-stage refinement inside the feasible region; an ε of 0 from the start
/// looks only at the objective and floods the early stages with infeasible solutions.
/// **`av == bv` 时也按目标值比较**。两个违反量相同时按违反量比较没有意义（相等），所以显式把这种情况并入目标值分支——
/// 少了这个条件，排序会退化成"保持原序"，看起来只是"稳定排序"。
/// **Equal violations also compare by objective.** Comparing by violation is meaningless when both are equal, so that case is folded
/// that case into the objective branch; without it the sort degenerates into "keep the input order", which merely looks like a stable
/// sort.
/// **`epsilon` 的夹取在指数之前**：`(1 - step/horizon)` 夹到 `[0, 1]`，避免 `step &gt; horizon` 时出现负底数的幂。
/// **The clamp happens before the exponent**: `(1 - step/horizon)` is clamped into `[0, 1]` so `step &gt; horizon` never raises a negative
/// base to a fractional power.
/// </remarks>
public sealed class EpsilonConstraintEvaluator {
    /// <summary>
    /// 构造评估器 / Construct the evaluator.
    /// </summary>
    /// <param name="initialEpsilon">初始 ε / The initial ε.</param>
    /// <param name="horizon">收缩跨度 / The shrinking horizon.</param>
    /// <param name="exponent">收缩指数 / The shrinking exponent.</param>
    public EpsilonConstraintEvaluator(double initialEpsilon, long horizon, double exponent = 2.0) {
        InitialEpsilon = initialEpsilon;
        Horizon = horizon;
        Exponent = exponent;
    }

    /// <summary>初始 ε / The initial ε.</summary>
    public double InitialEpsilon { get; }

    /// <summary>收缩跨度 / The shrinking horizon.</summary>
    public long Horizon { get; }

    /// <summary>收缩指数 / The shrinking exponent.</summary>
    public double Exponent { get; }

    /// <summary>
    /// 排序候选 / Rank the candidates.
    /// </summary>
    /// <typeparam name="T">载荷类型 / The payload type.</typeparam>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <param name="step">当前步 / The current step.</param>
    /// <returns>排序结果 / The ranked result.</returns>
    public Result<IReadOnlyList<ConstraintAssessment<T>>, ErrorCode, Error<ErrorCode>> Rank<T>(
        IReadOnlyList<ConstraintAssessment<T>> candidates, long step) {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!double.IsFinite(InitialEpsilon) || InitialEpsilon < 0 || Horizon <= 0
            || !double.IsFinite(Exponent) || Exponent <= 0 || step < 0
            || !ConstraintRankingSupport.IsValid(candidates)) {
            return Results.Failed<IReadOnlyList<ConstraintAssessment<T>>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "invalid epsilon constraint inputs"));
        }

        double epsilon = InitialEpsilon * global::System.Math.Pow(
            global::System.Math.Clamp(1.0 - ((double)step / Horizon), 0.0, 1.0), Exponent);
        var ranked = new List<ConstraintAssessment<T>>(candidates);
        ranked.Sort((a, b) => {
            double av = a.Violation.ToDouble();
            double bv = b.Violation.ToDouble();
            bool bothFeasible = av <= epsilon && bv <= epsilon;
            if (bothFeasible || av == bv) {
                return a.Objective.ToDouble().CompareTo(b.Objective.ToDouble());
            }
            return av.CompareTo(bv);
        });
        return Results.Ok<IReadOnlyList<ConstraintAssessment<T>>>(ranked);
    }
}

/// <summary>
/// 约束排序辅助 / Constraint-ranking helpers.
/// </summary>
public static class ConstraintRankingSupport {
    /// <summary>
    /// 候选是否合法 / Whether the candidates are valid.
    /// </summary>
    /// <typeparam name="T">载荷类型 / The payload type.</typeparam>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <returns>是否合法 / Whether they are valid.</returns>
    public static bool IsValid<T>(IReadOnlyList<ConstraintAssessment<T>> candidates) {
        foreach (ConstraintAssessment<T> candidate in candidates) {
            if (!double.IsFinite(candidate.Objective.ToDouble())
                || !double.IsFinite(candidate.Violation.ToDouble())
                || candidate.Violation.ToDouble() < 0.0) {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 随机排序 / Stochastic ranking.
/// </summary>
/// <remarks>
/// **两个都可行时无条件按目标值比较，否则按 `objectiveProbability` 抽签**。把抽签也用在这条分支上会让可行解之间的顺序
/// 随机化，收敛曲线出现无法解释的抖动。
/// **When both are feasible the comparison is unconditionally by objective; otherwise it draws at `objectiveProbability`.** Drawing in
/// that branch too randomizes the order among feasible solutions and the convergence curve jitters for no visible reason.
/// **冒泡一轮无交换即提前退出**。缺少这个退出条件会让每一轮都跑满，`repeat(size)` 在已排好序的输入上仍然做 `O(n²)` 次比较，
/// 而结果完全正确——**只是慢，且看不出异常**。
/// **Bail out as soon as one bubble pass makes no swap.** Without it every pass runs to completion and `repeat(size)` performs `O(n²)`
/// comparisons even on sorted input while the result is perfectly correct — **just slow, with nothing looking wrong**.
/// **每轮之后重新判断是否需要继续**：`repeat(size)` 是上界，真正的退出靠 `swapped`。
/// **Re-evaluate after each pass**: its `repeat(size)` is the upper bound and the real exit is `swapped`.
/// </remarks>
/// <param name="ObjectiveProbability">按目标值比较的概率 / The probability of comparing by objective.</param>
public sealed record StochasticRanking(double ObjectiveProbability = 0.45) {
    /// <summary>
    /// 排序候选 / Rank the candidates.
    /// </summary>
    /// <typeparam name="T">载荷类型 / The payload type.</typeparam>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <param name="random">随机数源 / The randomness source.</param>
    /// <returns>排序结果 / The ranked result.</returns>
    public Result<IReadOnlyList<ConstraintAssessment<T>>, ErrorCode, Error<ErrorCode>> Rank<T>(
        IReadOnlyList<ConstraintAssessment<T>> candidates, Func<Flt64> random) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(random);
        if (!double.IsFinite(ObjectiveProbability) || ObjectiveProbability < 0.0 || ObjectiveProbability > 1.0
            || !ConstraintRankingSupport.IsValid(candidates)) {
            return Results.Failed<IReadOnlyList<ConstraintAssessment<T>>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "invalid stochastic ranking inputs"));
        }

        var ranked = new List<ConstraintAssessment<T>>(candidates);
        for (int pass = 0; pass < candidates.Count; pass++) {
            bool swapped = false;
            for (int index = 0; index + 1 < ranked.Count; index++) {
                ConstraintAssessment<T> first = ranked[index];
                ConstraintAssessment<T> second = ranked[index + 1];
                bool bothFeasible = first.Violation.ToDouble() == 0.0 && second.Violation.ToDouble() == 0.0;
                bool useObjective = bothFeasible || NumericSupport.Unit(random) < ObjectiveProbability;
                bool reverse = useObjective
                    ? first.Objective.ToDouble() > second.Objective.ToDouble()
                    : first.Violation.ToDouble() > second.Violation.ToDouble();
                if (reverse) {
                    ranked[index] = second;
                    ranked[index + 1] = first;
                    swapped = true;
                }
            }
            if (!swapped) {
                return Results.Ok<IReadOnlyList<ConstraintAssessment<T>>>(ranked);
            }
        }
        return Results.Ok<IReadOnlyList<ConstraintAssessment<T>>>(ranked);
    }
}
