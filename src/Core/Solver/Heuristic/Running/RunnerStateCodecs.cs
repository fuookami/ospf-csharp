// 运行器状态编解码器 / Runner state codecs.
// `population`、`islands`、`trajectory`。
// — **only the three factories that do not depend on runner-specific
// state**: `population`, `islands`, `trajectory`.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 运行器状态编解码器 / Runner state codecs.
/// </summary>
/// <remarks>
/// **只交付了种群、岛、轨迹三个工厂**。`particles`/`clpso`/`shade`/`cmaEs` 四个工厂编解码的是
/// `ParticleState`、`ClpsoParticle`、`ShadeState`、`CmaEsState` 这些 **runner 专有状态类型**；本实现还没有这些类型，
/// 因此这四个工厂连同它们共用的 `NativeState` 信封一起，留到各自的 runner 之后。
/// **Only the population, island and trajectory factories are delivered.** Its `particles`/`clpso`/`shade`/`cmaEs` factories
/// encode and decode **runner-specific state types** (`ParticleState`, `ClpsoParticle`, `ShadeState`, `CmaEsState`); this implementation does not
/// have them yet, so those four factories — together with the shared `NativeState` envelope they use — arrive after their runners.
///
/// **`trajectory` 用"可空个体"而不是"空列表"表示未初始化**。用空列表会让"轨迹上没有个体"与"尚未开始"变成同一个编码，
/// **而恢复后的会话无法区分这两者**：前者应当继续推进，后者应当重新初始化。
/// **`trajectory` uses a nullable individual rather than an empty list for the uninitialized state.** An empty list would collapse
/// "no individual on the trajectory" and "not yet started" into the same encoding, **and a restored session cannot tell them apart**:
/// the former should keep advancing, the latter should re-initialize.
///
/// **解码必须重新校验维度与目标值**（由 `SearchCodecs.Individual` 完成）。只校验 JSON 形状会让一份"形状正确、维度错误"的快照
/// 通过解码，**而错误在若干步之后才以"难以置信的目标值"的形式爆发**。
/// **Decoding must re-validate dimensions and objectives** (done by `SearchCodecs.Individual`). Checking only the JSON shape lets a
/// "correctly shaped, wrongly dimensioned" snapshot decode, **and the error only surfaces several steps later as an "impossible
/// objective"**.
/// </remarks>
public static class RunnerStateCodecs {
    /// <summary>
    /// 种群状态编解码器 / The population state codec.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<List<IIndividual<O, V>>> Population<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        return SearchCodecs.List(SearchCodecs.Individual(problem));
    }

    /// <summary>
    /// 岛群状态编解码器 / The island-group state codec.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<List<List<IIndividual<O, V>>>> Islands<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        return SearchCodecs.List(Population(problem));
    }

    /// <summary>
    /// 轨迹状态编解码器 / The trajectory state codec.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<IIndividual<O, V>?> Trajectory<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        return SearchCodecs.Nullable(SearchCodecs.Individual(problem));
    }

    /// <summary>
    /// 原生状态信封 / The native-state envelope.
    /// </summary>
    /// <remarks>
    /// 七种运行状态形状差异太大（粒子有速度、SHADE 有记忆、CMA-ES 有协方差），逐一建记录会让文件膨胀成七份几乎相同的代码。
    /// "一个五字段信封 + 各自的校验"来收拢，本实现如此。
    /// The seven runner states differ too much to share one record (particles carry velocities, SHADE a memory, CMA-ES a covariance), and
 /// writing seven near-identical records would bloat the file. The implementation collapses them into "one five-field envelope plus per-kind
    /// validation", and this implementation follows.
    /// **`Individuals` 与 `Solutions` 里放的是"已经编码好的 JSON"**，而不是原始类型。这样信封本身与目标类型无关，
    /// **七种状态才能共用同一个读写实现**。
    /// **`Individuals` and `Solutions` hold "already-encoded JSON"** rather than raw values. That keeps the envelope independent of the
    /// objective type, **so all seven states can share one reader and one writer**.
    /// </remarks>
    /// <param name="Individuals">个体 JSON / The encoded individuals.</param>
    /// <param name="Vectors">数值向量 / The numeric vectors.</param>
    /// <param name="Counters">计数 / The counters.</param>
    /// <param name="Solutions">解 JSON / The encoded solutions.</param>
    /// <param name="Children">子信封 / The child envelopes.</param>
    private sealed record NativeState(
        JsonElement Individuals,
        IReadOnlyList<IReadOnlyList<double>> Vectors,
        IReadOnlyList<int> Counters,
        JsonElement Solutions,
        IReadOnlyList<NativeState> Children);

    /// <summary>
    /// 粒子群状态编解码器 / The swarm state codec.
    /// </summary>
    /// <remarks>
    /// **个体与个人最优成对存放、长度必须是速度行数的两倍**。只存当前个体会让"个人最优"在恢复后回退成当前位置，
    /// **于是整群在被恢复的那一刻同时忘记了自己的历史最优，而算法看起来只是"退步了一次"**。
    /// **The individuals and the personal bests are stored in pairs, and the length must be twice the number of velocity rows.** Storing
    /// only the current individuals makes every personal best fall back to the current position on restore, **so the whole swarm forgets its
    /// own history at the moment of restoration, while the algorithm merely looks like it "stepped back once"**.
    /// **速度行长度必须与该粒子的维度一致，且每个分量必须有限**。不检查长度会在后续位置更新时抛越界；
    /// 不检查有限性会把 `NaN` 写进种群，**而种群规模与最优值都还正常**。
    /// **Each velocity row must match the particle's dimension and every component must be finite.** Skipping the length check throws on the
    /// next position update; skipping finiteness writes `NaN` into the population, **while its size and best value still look normal**.
    /// </remarks>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<List<ParticleState<O, V>>> Particles<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        ISearchCodec<List<IIndividual<O, V>>> entries = Population(problem);
        return new NativeCodec<List<ParticleState<O, V>>>(
            state => {
                var flat = new List<IIndividual<O, V>>(state.Count * 2);
                foreach (ParticleState<O, V> particle in state) {
                    flat.Add(particle.Individual);
                    flat.Add(particle.Best);
                }
                Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = entries.Encode(flat);
                if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<NativeState>(failure.Error);
                }
                var vectors = new List<IReadOnlyList<double>>(state.Count);
                foreach (ParticleState<O, V> particle in state) {
                    var row = new List<double>(particle.Velocity.Count);
                    foreach (Flt64 value in particle.Velocity) {
                        row.Add(value.ToDouble());
                    }
                    vectors.Add(row);
                }
                return Results.Ok(new NativeState(
                    ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value,
                    vectors, Array.Empty<int>(), EmptyArray(), Array.Empty<NativeState>()));
            },
            saved => {
                Result<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> decoded =
                    entries.Decode(saved.Individuals);
                if (decoded is Failed<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<List<ParticleState<O, V>>>(failure.Error);
                }
                List<IIndividual<O, V>> values =
                    ((Ok<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)decoded).Value;
                if (values.Count != saved.Vectors.Count * 2) {
                    return SearchCodecSupport.SearchInvalid<List<ParticleState<O, V>>>("invalid particles");
                }
                var result = new List<ParticleState<O, V>>(saved.Vectors.Count);
                for (int index = 0; index < saved.Vectors.Count; index++) {
                    IReadOnlyList<double> row = saved.Vectors[index];
                    if (row.Count != values[index * 2].Solution.Count) {
                        return SearchCodecSupport.SearchInvalid<List<ParticleState<O, V>>>("invalid particles");
                    }
                    var velocity = new List<Flt64>(row.Count);
                    foreach (double value in row) {
                        if (!double.IsFinite(value)) {
                            return SearchCodecSupport.SearchInvalid<List<ParticleState<O, V>>>("invalid particles");
                        }
                        velocity.Add(new Flt64(value));
                    }
                    result.Add(new ParticleState<O, V>(
                        values[index * 2], velocity, values[index * 2 + 1]));
                }
                return Results.Ok(result);
            });
    }

    /// <summary>
    /// CLPSO 状态编解码器 / The CLPSO state codec.
    /// </summary>
    /// <remarks>
    /// **学习对象是"逐维下标"，因此每个子信封的计数是"停滞计数 + 每维一个下标"**。少存一个维度会让最后一维永远学自己，
    /// **而停滞与速度都还正常**；不检查下标落在群内会让恢复后取到越界个体，**而错误发生在若干步之后**。
    /// **The exemplars are per-dimension indices, so each child envelope holds "the stagnation count plus one index per dimension".**
    /// Dropping one dimension makes the last one always learn from itself, **while stagnation and velocity still look normal**; not checking
    /// that the indices fall inside the swarm yields out-of-range individuals a few steps after the restore.
    /// </remarks>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<List<ClpsoParticle<O, V>>> Clpso<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        ISearchCodec<List<IIndividual<O, V>>> entries = Population(problem);
        return new NativeCodec<List<ClpsoParticle<O, V>>>(
            state => {
                var flat = new List<IIndividual<O, V>>(state.Count * 2);
                foreach (ClpsoParticle<O, V> particle in state) {
                    flat.Add(particle.Individual);
                    flat.Add(particle.Best);
                }
                Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = entries.Encode(flat);
                if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<NativeState>(failure.Error);
                }
                var vectors = new List<IReadOnlyList<double>>(state.Count);
                var children = new List<NativeState>(state.Count);
                foreach (ClpsoParticle<O, V> particle in state) {
                    var row = new List<double>(particle.Velocity.Count);
                    foreach (Flt64 value in particle.Velocity) {
                        row.Add(value.ToDouble());
                    }
                    vectors.Add(row);
                    var counters = new List<int>(particle.Exemplars.Count + 1) { particle.Stagnation };
                    counters.AddRange(particle.Exemplars);
                    children.Add(new NativeState(EmptyArray(), Array.Empty<IReadOnlyList<double>>(),
                        counters, EmptyArray(), Array.Empty<NativeState>()));
                }
                return Results.Ok(new NativeState(
                    ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value,
                    vectors, Array.Empty<int>(), EmptyArray(), children));
            },
            saved => {
                Result<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> decoded =
                    entries.Decode(saved.Individuals);
                if (decoded is Failed<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<List<ClpsoParticle<O, V>>>(failure.Error);
                }
                List<IIndividual<O, V>> values =
                    ((Ok<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)decoded).Value;
                if (values.Count != saved.Vectors.Count * 2
                    || saved.Children.Count != saved.Vectors.Count) {
                    return SearchCodecSupport.SearchInvalid<List<ClpsoParticle<O, V>>>(
                        "invalid CLPSO state");
                }
                var result = new List<ClpsoParticle<O, V>>(saved.Vectors.Count);
                for (int index = 0; index < saved.Vectors.Count; index++) {
                    IReadOnlyList<double> row = saved.Vectors[index];
                    IReadOnlyList<int> counters = saved.Children[index].Counters;
                    int dimension = values[index * 2].Solution.Count;
                    if (row.Count != dimension || counters.Count != dimension + 1) {
                        return SearchCodecSupport.SearchInvalid<List<ClpsoParticle<O, V>>>(
                            "invalid CLPSO state");
                    }
                    if (counters[0] < 0) {
                        return SearchCodecSupport.SearchInvalid<List<ClpsoParticle<O, V>>>(
                            "invalid CLPSO state");
                    }
                    var exemplars = new List<int>(dimension);
                    for (int axis = 1; axis <= dimension; axis++) {
                        if (counters[axis] < 0 || counters[axis] >= saved.Vectors.Count) {
                            return SearchCodecSupport.SearchInvalid<List<ClpsoParticle<O, V>>>(
                                "invalid CLPSO state");
                        }
                        exemplars.Add(counters[axis]);
                    }
                    var velocity = new List<Flt64>(row.Count);
                    foreach (double value in row) {
                        if (!double.IsFinite(value)) {
                            return SearchCodecSupport.SearchInvalid<List<ClpsoParticle<O, V>>>(
                                "invalid CLPSO state");
                        }
                        velocity.Add(new Flt64(value));
                    }
                    result.Add(new ClpsoParticle<O, V>(
                        values[index * 2], velocity, values[index * 2 + 1],
                        exemplars, counters[0]));
                }
                return Results.Ok(result);
            });
    }

    /// <summary>
    /// SHADE 状态编解码器 / The SHADE state codec.
    /// </summary>
    /// <remarks>
    /// **记忆的两个向量必须等长非空、游标必须落在范围内、每个分量必须在 0 到 1 之间**。越界的 `F` 或 `Cr` 不会让采样失败——
    /// 它会安静地产出一个不可能的权重，**而下一次采样仍然"成功"**。
    /// **The two memory vectors must be non-empty and of equal length, the cursor must be in range, and every component must lie between 0
    /// and 1.** An out-of-range `F` or `Cr` does not make sampling fail — it quietly produces an impossible weight, **and the next sample
    /// still "succeeds"**.
    /// **档案里的解要按问题的编码规则重新校验**。不校验会让一份尺寸不对的档案在恢复后继续参与差分向量，
    /// **而档案只是在"提供多样性"**。
    /// **The archived solutions are re-validated against the problem's encoding rules.** Skipping it lets a wrongly sized archive keep feeding
    /// difference vectors after the restore, **while the archive merely looks like it "provides diversity"**.
    /// </remarks>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<ShadeState<O, V>> Shade<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        ISearchCodec<List<IIndividual<O, V>>> entries = Population(problem);
        ISearchCodec<List<List<V>>> solutions = SearchCodecs.List(SearchCodecs.List(problem.ValueCodec));
        return new NativeCodec<ShadeState<O, V>>(
            state => {
                Result<JsonElement, ErrorCode, Error<ErrorCode>> population =
                    entries.Encode(new List<IIndividual<O, V>>(state.Population));
                if (population is Failed<JsonElement, ErrorCode, Error<ErrorCode>> populationFailure) {
                    return Results.Failed<NativeState>(populationFailure.Error);
                }
                var archived = new List<List<V>>(state.Archive.Count);
                foreach (Solution<V> solution in state.Archive) {
                    archived.Add(new List<V>(solution.Values));
                }
                Result<JsonElement, ErrorCode, Error<ErrorCode>> archive = solutions.Encode(archived);
                if (archive is Failed<JsonElement, ErrorCode, Error<ErrorCode>> archiveFailure) {
                    return Results.Failed<NativeState>(archiveFailure.Error);
                }
                return Results.Ok(new NativeState(
                    ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)population).Value,
                    new List<IReadOnlyList<double>> {
                        new List<double>(state.Memory.MemoryF),
                        new List<double>(state.Memory.MemoryCr),
                    },
                    new List<int> { state.Memory.Cursor },
                    ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)archive).Value,
                    Array.Empty<NativeState>()));
            },
            saved => {
                if (saved.Vectors.Count != 2
                    || saved.Vectors[0].Count == 0
                    || saved.Vectors[0].Count != saved.Vectors[1].Count
                    || saved.Counters.Count != 1
                    || saved.Counters[0] < 0
                    || saved.Counters[0] >= saved.Vectors[0].Count) {
                    return SearchCodecSupport.SearchInvalid<ShadeState<O, V>>("invalid SHADE memory");
                }
                foreach (IReadOnlyList<double> row in saved.Vectors) {
                    foreach (double value in row) {
                        if (!double.IsFinite(value) || value < 0.0 || value > 1.0) {
                            return SearchCodecSupport.SearchInvalid<ShadeState<O, V>>(
                                "invalid SHADE memory");
                        }
                    }
                }
                Result<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> decoded =
                    entries.Decode(saved.Individuals);
                if (decoded is Failed<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<ShadeState<O, V>>(failure.Error);
                }
                Result<List<List<V>>, ErrorCode, Error<ErrorCode>> decodedArchive =
                    solutions.Decode(saved.Solutions);
                if (decodedArchive is Failed<List<List<V>>, ErrorCode, Error<ErrorCode>> archiveFailure) {
                    return Results.Failed<ShadeState<O, V>>(archiveFailure.Error);
                }
                var archive = new List<Solution<V>>();
                foreach (List<V> values in
                    ((Ok<List<List<V>>, ErrorCode, Error<ErrorCode>>)decodedArchive).Value) {
                    if (!problem.ValidSolution(values)) {
                        return SearchCodecSupport.SearchInvalid<ShadeState<O, V>>(
                            "invalid SHADE archive dimensions");
                    }
                    archive.Add(new Solution<V>(values));
                }
                return Results.Ok(new ShadeState<O, V>(
                    ((Ok<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)decoded).Value,
                    new ShadeMemory(saved.Vectors[0], saved.Vectors[1], saved.Counters[0]),
                    archive));
            });
    }

    /// <summary>
    /// CMA-ES 状态编解码器 / The CMA-ES state codec.
    /// </summary>
    /// <remarks>
    /// **向量布局是"均值、步长、步长路径、协方差路径、然后逐行协方差"**。步长单独占一个单元素向量，是为了让整份状态的形状统一。
    /// **The vector layout is "mean, step size, step-size path, covariance path, then the covariance rows".** The step size gets its own
    /// single-element vector so the whole state has one uniform shape.
    /// **均值长度必须等于求解器当前的变量数**。不检查会让一份"维度对不上"的检查点恢复成功，
    /// **而第一次采样才会以越界或错位的方式失败**。
    /// **The mean's length must equal the solver's current variable count.** Skipping it lets a checkpoint with the wrong dimension restore
    /// successfully, **and only the first sample then fails, by overrun or by misalignment**.
    /// **步长必须为正、代数必须非负、逆协方差的每行长度必须等于均值长度**。
    /// **The step size must be positive, the generation non-negative, and every covariance row must match the mean's length.**
    /// </remarks>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <returns>状态编解码器 / The state codec.</returns>
    public static ISearchCodec<CmaEsState<O, V>> CmaEs<O, V>(SearchProblem<O, V> problem)
        where V : struct, IRealNumber<V>, INumberField<V> {
        System.ArgumentNullException.ThrowIfNull(problem);
        ISearchCodec<IIndividual<O, V>?> best = Trajectory(problem);
        return new NativeCodec<CmaEsState<O, V>>(
            state => {
                Result<JsonElement, ErrorCode, Error<ErrorCode>> entry = best.Encode(state.Best);
                if (entry is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<NativeState>(failure.Error);
                }
                var vectors = new List<IReadOnlyList<double>> {
                    new List<double>(state.Mean),
                    new List<double> { state.Sigma },
                    new List<double>(state.PathSigma),
                    new List<double>(state.PathCovariance),
                };
                foreach (IReadOnlyList<double> row in state.Covariance) {
                    vectors.Add(new List<double>(row));
                }
                return Results.Ok(new NativeState(
                    ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)entry).Value,
                    vectors, new List<int> { state.Generation }, EmptyArray(),
                    Array.Empty<NativeState>()));
            },
            saved => {
                IReadOnlyList<IReadOnlyList<double>> vectors = saved.Vectors;
                int dimension = problem.Model.Tokens.TokensInSolver.Count;
                if (vectors.Count < 4
                    || vectors[0].Count != dimension
                    || vectors[1].Count != 1
                    || vectors[1][0] <= 0.0
                    || saved.Counters.Count != 1
                    || saved.Counters[0] < 0
                    || vectors[2].Count != vectors[0].Count
                    || vectors[3].Count != vectors[0].Count
                    || vectors.Count - 4 != vectors[0].Count) {
                    return SearchCodecSupport.SearchInvalid<CmaEsState<O, V>>(
                        "invalid CMA-ES distribution");
                }
                foreach (IReadOnlyList<double> row in vectors) {
                    if (row.Count != vectors[0].Count || row.Count == 0) {
                        if (!ReferenceEquals(row, vectors[1])) {
                            return SearchCodecSupport.SearchInvalid<CmaEsState<O, V>>(
                                "invalid CMA-ES distribution");
                        }
                    }
                    foreach (double value in row) {
                        if (!double.IsFinite(value)) {
                            return SearchCodecSupport.SearchInvalid<CmaEsState<O, V>>(
                                "invalid CMA-ES distribution");
                        }
                    }
                }
                Result<IIndividual<O, V>?, ErrorCode, Error<ErrorCode>> entry =
                    best.Decode(saved.Individuals);
                if (entry is Failed<IIndividual<O, V>?, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<CmaEsState<O, V>>(failure.Error);
                }
                var covariance = new List<IReadOnlyList<double>>(vectors.Count - 4);
                for (int index = 4; index < vectors.Count; index++) {
                    covariance.Add(vectors[index]);
                }
                return Results.Ok(new CmaEsState<O, V>(
                    vectors[0], vectors[1][0], covariance, vectors[2], vectors[3],
                    saved.Counters[0],
                    ((Ok<IIndividual<O, V>?, ErrorCode, Error<ErrorCode>>)entry).Value));
            });
    }

    /// <summary>空的 JSON 数组 / An empty JSON array.</summary>
    /// <returns>空的 JSON 数组 / The empty JSON array.</returns>
    private static JsonElement EmptyArray() => JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>
    /// 把信封写成 JSON / Write the envelope as JSON.
    /// </summary>
    /// <param name="state">信封 / The envelope.</param>
    /// <returns>JSON / The JSON.</returns>
    private static JsonElement Write(NativeState state) {
        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) {
            WriteState(writer, state);
        }
        return JsonDocument.Parse(buffer.ToArray()).RootElement.Clone();
    }

    /// <summary>
    /// 递归写一个信封 / Write one envelope recursively.
    /// </summary>
    /// <param name="writer">写入器 / The writer.</param>
    /// <param name="state">信封 / The envelope.</param>
    private static void WriteState(Utf8JsonWriter writer, NativeState state) {
        writer.WriteStartObject();
        writer.WritePropertyName("individuals");
        state.Individuals.WriteTo(writer);
        writer.WriteStartArray("vectors");
        foreach (IReadOnlyList<double> row in state.Vectors) {
            writer.WriteStartArray();
            foreach (double value in row) {
                writer.WriteNumberValue(value);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("counters");
        foreach (int value in state.Counters) {
            writer.WriteNumberValue(value);
        }
        writer.WriteEndArray();
        writer.WritePropertyName("solutions");
        state.Solutions.WriteTo(writer);
        writer.WriteStartArray("children");
        foreach (NativeState child in state.Children) {
            WriteState(writer, child);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// 从一个 JSON 值读出信封 / Read the envelope from a JSON value.
    /// </summary>
    /// <param name="value">JSON / The JSON.</param>
    /// <returns>信封，形状不对时返回 null / The envelope, or null when the shape is wrong.</returns>
    private static NativeState? Read(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Object) {
            return null;
        }
        if (!value.TryGetProperty("individuals", out JsonElement individuals)
            || !value.TryGetProperty("vectors", out JsonElement vectors)
            || vectors.ValueKind != JsonValueKind.Array
            || !value.TryGetProperty("counters", out JsonElement counters)
            || counters.ValueKind != JsonValueKind.Array
            || !value.TryGetProperty("solutions", out JsonElement solutions)
            || !value.TryGetProperty("children", out JsonElement children)
            || children.ValueKind != JsonValueKind.Array) {
            return null;
        }
        var rows = new List<IReadOnlyList<double>>();
        foreach (JsonElement row in vectors.EnumerateArray()) {
            if (row.ValueKind != JsonValueKind.Array) {
                return null;
            }
            var numbers = new List<double>();
            foreach (JsonElement number in row.EnumerateArray()) {
                if (!number.TryGetDouble(out double parsed)) {
                    return null;
                }
                numbers.Add(parsed);
            }
            rows.Add(numbers);
        }
        var counts = new List<int>();
        foreach (JsonElement counter in counters.EnumerateArray()) {
            if (!counter.TryGetInt32(out int parsed)) {
                return null;
            }
            counts.Add(parsed);
        }
        var nested = new List<NativeState>();
        foreach (JsonElement child in children.EnumerateArray()) {
            NativeState? parsed = Read(child);
            if (parsed is null) {
                return null;
            }
            nested.Add(parsed);
        }
        return new NativeState(individuals.Clone(), rows, counts, solutions.Clone(), nested);
    }

    /// <summary>
    /// 信封式编解码器 / An envelope-shaped codec.
    /// </summary>
    /// <typeparam name="S">状态类型 / The state type.</typeparam>
    private sealed class NativeCodec<S> : ISearchCodec<S> {
        private readonly Func<S, Result<NativeState, ErrorCode, Error<ErrorCode>>> _encode;
        private readonly Func<NativeState, Result<S, ErrorCode, Error<ErrorCode>>> _decode;

        /// <summary>
        /// 构造信封式编解码器 / Construct the envelope-shaped codec.
        /// </summary>
        /// <param name="encode">编码 / The encoder.</param>
        /// <param name="decode">解码 / The decoder.</param>
        public NativeCodec(
            Func<S, Result<NativeState, ErrorCode, Error<ErrorCode>>> encode,
            Func<NativeState, Result<S, ErrorCode, Error<ErrorCode>>> decode) {
            _encode = encode;
            _decode = decode;
        }

        /// <inheritdoc/>
        public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(S value) {
            Result<NativeState, ErrorCode, Error<ErrorCode>> encoded = _encode(value);
            return encoded is Ok<NativeState, ErrorCode, Error<ErrorCode>> ok
                ? Results.Ok(Write(ok.Value))
                : Results.Failed<JsonElement>(
                    ((Failed<NativeState, ErrorCode, Error<ErrorCode>>)encoded).Error);
        }

        /// <inheritdoc/>
        public Result<S, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) {
            NativeState? state = Read(value);
            if (state is null) {
                return SearchCodecSupport.SearchInvalid<S>("malformed native state");
            }
            return _decode(state);
        }
    }
}
