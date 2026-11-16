// CMA-ES 运行器 / The CMA-ES runner.
// 与固定 Kotlin 、（12640 B）对应。
// And (12640 B).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// CMA-ES 数值实现 / The CMA-ES numerical implementation.
/// </summary>
/// <remarks>
/// **两个分支的差别只在"高斯采样怎么做"**，不在超越函数上。`PolarFdlibmV1` 还额外把
/// `ln`/`sqrt`/`exp`/`pow` 换成 Java `StrictMath`（fdlibm）；本实现无法在 .NET 上复现那份逐位结果，
/// 因此两者都用 `System.Math`，**并且配置层对 `numerics: cma-polar-fdlibm-v1` 直接失败**——收下配置却按原生超越函数跑，
/// 会让同一份配置对应两份轨迹。这是已记录的可复现性缺口，不假装两个分支在超越函数上等价。
/// **The two branches differ only in "how the Gaussian is sampled"**, not in the transcendentals. `PolarFdlibmV1` additionally
/// swaps `ln`/`sqrt`/`exp`/`pow` for Java `StrictMath` (fdlibm); this implementation cannot reproduce those bit-exact results on .NET, so both use
/// `System.Math`, **and the configuration layer fails outright on `numerics: cma-polar-fdlibm-v1`** — accepting a config while running native
/// transcendentals would map one config to two trajectories. This is a recorded reproducibility gap, and the two branches are not claimed to be equivalent.
/// **`Native` 的 Box–Muller 把第一个随机数抬到 `1.0e-12` 以上**。不抬会让 `ln(0)` 得到负无穷，
/// **于是整条采样路径产出 `NaN`，而协方差在若干代之后才彻底失效**。
/// **`Native`'s Box–Muller lifts its first draw above `1.0e-12`.** Without it `ln(0)` yields negative infinity,
/// **so the whole sampling path produces `NaN` and the covariance only collapses a few generations later**.
/// **`PolarFdlibmV1` 只用极坐标对的第一分量、不缓存第二分量**。缓存会让检查点多出一份"隐藏采样状态"，
/// **而恢复后的随机流位置无法重建**。
/// **`PolarFdlibmV1` uses only the first polar component and never caches its partner.** Caching would give the checkpoint a hidden
/// sampling state, **and the restored position in the random stream could not be reconstructed**.
/// **极坐标拒绝采样的循环条件同时排除 0 与 1**（开区间）。包含 0 会让 `ln(radius)` 出现负无穷；
/// 包含 1 会让 `(-2 ln 1)/1 = 0`，产出一个恰好为零的样本并把协方差推向奇异。
/// **The polar rejection loop excludes both 0 and 1** (an open interval). Including 0 makes `ln(radius)` infinite; including 1 yields
/// `(-2 ln 1)/1 = 0`, producing a sample that is exactly zero and pushing the covariance toward singularity.
/// </remarks>
public enum CmaNumerics {
    /// <summary>原生实现 / The native implementation.</summary>
    Native,

    /// <summary>极坐标采样 / Polar sampling.</summary>
    PolarFdlibmV1,
}

/// <summary>
/// CMA-ES 数值辅助 / CMA-ES numerical helpers.
/// </summary>
public static class CmaNumericsSupport {
    /// <summary>自然对数 / The natural logarithm.</summary>
    /// <param name="value">输入值 / The input value.</param>
    /// <returns>自然对数 / The natural logarithm.</returns>
    public static double Ln(double value) => global::System.Math.Log(value);

    /// <summary>平方根 / The square root.</summary>
    /// <param name="value">输入值 / The input value.</param>
    /// <returns>平方根 / The square root.</returns>
    public static double Sqrt(double value) => global::System.Math.Sqrt(value);

    /// <summary>自然指数 / The natural exponential.</summary>
    /// <param name="value">输入值 / The input value.</param>
    /// <returns>自然指数 / The natural exponential.</returns>
    public static double Exp(double value) => global::System.Math.Exp(value);

    /// <summary>幂 / The power.</summary>
    /// <param name="baseValue">底数 / The base.</param>
    /// <param name="exponent">指数 / The exponent.</param>
    /// <returns>幂 / The power.</returns>
    public static double Pow(double baseValue, double exponent) =>
        global::System.Math.Pow(baseValue, exponent);

    /// <summary>对称矩阵特征分解 / Symmetric-matrix eigendecomposition.</summary>
    /// <param name="matrix">对称矩阵 / The symmetric matrix.</param>
    /// <returns>分解结果 / The decomposition.</returns>
    public static SymmetricEigenResult Eigen(IReadOnlyList<IReadOnlyList<double>> matrix) =>
        SymmetricEigen.Decompose(matrix);

    /// <summary>
    /// 采样一个标准正态数 / Sample one standard normal.
    /// </summary>
    /// <param name="numerics">数值实现 / The numerical implementation.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <returns>标准正态样本 / A standard normal sample.</returns>
    public static double Gaussian(CmaNumerics numerics, Func<Flt64> entropy) {
        ArgumentNullException.ThrowIfNull(entropy);
        if (numerics == CmaNumerics.Native) {
            double first = global::System.Math.Max(entropy().ToDouble(), 1.0e-12);
            double second = entropy().ToDouble();
            return Sqrt(-2.0 * Ln(first))
                * global::System.Math.Cos(2.0 * global::System.Math.PI * second);
        }
        while (true) {
            double x = (2.0 * entropy().ToDouble()) - 1.0;
            double y = (2.0 * entropy().ToDouble()) - 1.0;
            double radius = (x * x) + (y * y);
            if (radius > 0.0 && radius < 1.0) {
                return x * Sqrt(-2.0 * Ln(radius) / radius);
            }
        }
    }
}

/// <summary>
/// CMA-ES 运行状态 / The CMA-ES run state.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Mean">分布均值 / The distribution mean.</param>
/// <param name="Sigma">步长 / The step size.</param>
/// <param name="Covariance">协方差矩阵 / The covariance matrix.</param>
/// <param name="PathSigma">步长路径 / The step-size path.</param>
/// <param name="PathCovariance">协方差路径 / The covariance path.</param>
/// <param name="Generation">代数 / The generation.</param>
/// <param name="Best">至今最优 / The best so far.</param>
public sealed record CmaEsState<ObjValue, V>(
    IReadOnlyList<double> Mean,
    double Sigma,
    IReadOnlyList<IReadOnlyList<double>> Covariance,
    IReadOnlyList<double> PathSigma,
    IReadOnlyList<double> PathCovariance,
    int Generation,
    IIndividual<ObjValue, V>? Best)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// CMA-ES 运行器 / The CMA-ES runner.
/// </summary>
/// <remarks>
/// **退化状态原样返回，而不是"继续跑一代"**：维度为 0、步长非有限或不大于 0、特征分解无效、一个样本都没评估成功——
/// 这四种情形下继续推进只会把 `NaN` 写进协方差，**而"运行仍在推进"这件事看起来完全正常**。
/// **Degenerate states are returned unchanged rather than "run one more generation"**: zero dimension, a non-finite or non-positive step
/// size, an invalid eigendecomposition, and zero successfully evaluated samples. Advancing through any of them only writes `NaN` into
/// the covariance, **while "the run is still progressing" looks perfectly normal**.
/// **秩 μ 更新用的是"重加权均值位移"（`ranked[i].Y`），不是重加权后的解**。用重加权后的解会把分布锚点从均值挪到样本上，
/// **于是协方差逐渐与均值脱钩，而收敛曲线只是"抖动变大"**。
/// **The rank-μ update uses the reweighted mean shift (`ranked[i].Y`), not the reweighted solutions.** Using the solutions moves the
/// distribution's anchor from the mean onto the samples, **so the covariance drifts away from the mean while the convergence curve merely
/// looks "more jittery"**.
/// **`hSigma` 门控同时作用于协方差路径与秩一修正项**。只作用于路径会让一次"步长异常"永久污染协方差，
/// **而此后每一代都在一个错误的方向上做更精细的搜索**。
/// **The `hSigma` gate applies to both the covariance path and the rank-one correction.** Applying it only to the path lets one
/// "abnormal step" permanently contaminate the covariance, **and every later generation refines the search in a wrong direction**.
/// **步长被截断到 `1.0e-12` 到 `1.0e12`**。不截断会让一次指数更新把步长推到 0 或无穷，
/// **而两个极端都表现为"种群停止移动"，且不再有任何信号**。
/// **The step size is clamped between `1.0e-12` and `1.0e12`.** Without it one exponential update drives it to zero or infinity,
/// **and both extremes look like "the population stopped moving" with no further signal**.
/// **`MembersOf` 只返回"至今最优"这一个个体**，不是当前分布的样本。返回样本会让档案记录"最近采样到了什么"，
/// **而不是"搜索过程中找到过什么"**。
/// **`MembersOf` returns only the "best so far"**, not the current distribution's samples. Returning the samples makes the archive record
/// "what was sampled recently" **instead of "what was ever found"**.
/// **最优只在严格改进时替换**。用"不差于"会让等价样本反复覆盖，**而检查点里记录的"最优"其实只是"最近一个等价样本"**。
/// **The best is replaced only on strict improvement.** Using "not worse" lets equivalent samples overwrite it repeatedly, **so the "best"
/// in a checkpoint is really just "the most recent equivalent sample"**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class CmaEsRunner<ObjValue, V> : AbstractRunner<CmaEsState<ObjValue, V>, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IFitnessEvaluator _fitnessEvaluator;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造 CMA-ES 运行器 / Construct the CMA-ES runner.
    /// </summary>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="initialSigma">初始步长 / The initial step size.</param>
    /// <param name="fitnessEvaluator">适应度评估器 / The fitness evaluator.</param>
    /// <param name="numerics">数值实现 / The numerical implementation.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public CmaEsRunner(
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        double initialSigma = 0.3,
        IFitnessEvaluator? fitnessEvaluator = null,
        CmaNumerics numerics = CmaNumerics.Native,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(entropy);
        if (!double.IsFinite(initialSigma) || initialSigma <= 0.0) {
            throw new ArgumentOutOfRangeException(
                nameof(initialSigma), "the initial step size must be finite and positive");
        }
        _entropy = entropy;
        _fitnessEvaluator = fitnessEvaluator ?? DefaultOnMissFitnessEvaluator.Instance;
        InitialSigma = initialSigma;
        Numerics = numerics;
    }

    /// <summary>初始步长 / The initial step size.</summary>
    public double InitialSigma { get; }

    /// <summary>数值实现 / The numerical implementation.</summary>
    public CmaNumerics Numerics { get; }

    /// <inheritdoc/>
    protected override Task<CmaEsState<ObjValue, V>> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        IReadOnlyList<IReadOnlyList<V>> provided = model.InitialSolutions(1UL);
        if (provided.Count == 0) {
            return Task.FromResult(new CmaEsState<ObjValue, V>(
                Array.Empty<double>(), InitialSigma,
                Array.Empty<IReadOnlyList<double>>(), Array.Empty<double>(),
                Array.Empty<double>(), 0, null));
        }
        var solution = new Solution<V>(provided[0]);
        var mean = new List<double>(solution.Values.Count);
        foreach (V value in solution.Values) {
            mean.Add(value.ToFlt64().ToDouble());
        }
        int dimension = mean.Count;
        return Task.FromResult(new CmaEsState<ObjValue, V>(
            mean,
            InitialSigma,
            Identity(dimension),
            Zeroes(dimension),
            Zeroes(dimension),
            0,
            ToIndividual(model, solution)));
    }

    /// <inheritdoc/>
    protected override Task<CmaEsState<ObjValue, V>> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        CmaEsState<ObjValue, V> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        int dimension = state.Mean.Count;
        if (dimension == 0 || !double.IsFinite(state.Sigma) || state.Sigma <= 0.0) {
            return Task.FromResult(state);
        }

        int lambda = 4 + (int)global::System.Math.Floor(
            3.0 * CmaNumericsSupport.Ln(dimension));
        int mu = lambda / 2;
        var rawWeights = new List<double>(mu);
        for (int index = 0; index < mu; index++) {
            rawWeights.Add(CmaNumericsSupport.Ln(mu + 0.5) - CmaNumericsSupport.Ln(index + 1.0));
        }
        double weightTotal = 0.0;
        foreach (double weight in rawWeights) {
            weightTotal += weight;
        }
        var weights = new List<double>(mu);
        foreach (double weight in rawWeights) {
            weights.Add(weight / weightTotal);
        }
        double squaredTotal = 0.0;
        foreach (double weight in weights) {
            squaredTotal += weight * weight;
        }
        double effectiveMass = 1.0 / squaredTotal;
        double n = dimension;
        double cc = (4.0 + (effectiveMass / n)) / (n + 4.0 + (2.0 * effectiveMass / n));
        double cs = (effectiveMass + 2.0) / (n + effectiveMass + 5.0);
        double c1 = 2.0 / (((n + 1.3) * (n + 1.3)) + effectiveMass);
        double cmu = global::System.Math.Min(
            1.0 - c1,
            2.0 * (effectiveMass - 2.0 + (1.0 / effectiveMass))
                / (((n + 2.0) * (n + 2.0)) + effectiveMass));
        double damps = 1.0
            + (2.0 * global::System.Math.Max(
                0.0, CmaNumericsSupport.Sqrt((effectiveMass - 1.0) / (n + 1.0)) - 1.0))
            + cs;
        double chiN = CmaNumericsSupport.Sqrt(n)
            * (1.0 - (1.0 / (4.0 * n)) + (1.0 / (21.0 * n * n)));

        SymmetricEigenResult eigen = CmaNumericsSupport.Eigen(state.Covariance);
        if (!eigen.IsValid) {
            return Task.FromResult(state);
        }
        var axes = new List<double>(dimension);
        foreach (double eigenvalue in eigen.Values) {
            axes.Add(CmaNumericsSupport.Sqrt(
                global::System.Math.Max(eigenvalue, 1.0e-20)));
        }

        var samples = new List<(double[] Y, IIndividual<ObjValue, V> Individual)>(lambda);
        for (int index = 0; index < lambda; index++) {
            double[] z = new double[dimension];
            for (int axis = 0; axis < dimension; axis++) {
                z[axis] = CmaNumericsSupport.Gaussian(Numerics, _entropy);
            }
            double[] y = new double[dimension];
            for (int row = 0; row < dimension; row++) {
                double total = 0.0;
                for (int column = 0; column < dimension; column++) {
                    total += eigen.Vectors[column][row] * axes[column] * z[column];
                }
                y[row] = total;
            }
            var point = new List<V>(dimension);
            for (int axis = 0; axis < dimension; axis++) {
                double? lower = NumericSupport.LowerBoundOf(model, axis, Converter);
                double? upper = NumericSupport.UpperBoundOf(model, axis, Converter);
                point.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(
                        state.Mean[axis] + (state.Sigma * y[axis]), lower, upper),
                    Converter));
            }
            IIndividual<ObjValue, V>? individual =
                ToIndividual(model, new Solution<V>(point));
            if (individual is null) {
                continue;
            }
            samples.Add((y, individual));
        }
        if (samples.Count == 0) {
            return Task.FromResult(state);
        }

        samples.Sort((lhs, rhs) =>
            HeuristicOrdering.CompareStrictly(model, lhs.Individual.Objective, rhs.Individual.Objective));

        double[] newMean = new double[dimension];
        int reweighted = global::System.Math.Min(mu, samples.Count);
        for (int index = 0; index < reweighted; index++) {
            double weight = weights[index];
            for (int axis = 0; axis < dimension; axis++) {
                newMean[axis] += weight
                    * samples[index].Individual.Solution[axis].ToFlt64().ToDouble();
            }
        }
        double[] meanShift = new double[dimension];
        for (int axis = 0; axis < dimension; axis++) {
            meanShift[axis] = (newMean[axis] - state.Mean[axis]) / state.Sigma;
        }

        double sigmaCoefficient = CmaNumericsSupport.Sqrt(
            cs * (2.0 - cs) * effectiveMass);
        double[] projected = new double[dimension];
        for (int column = 0; column < dimension; column++) {
            double total = 0.0;
            for (int row = 0; row < dimension; row++) {
                total += eigen.Vectors[column][row] * meanShift[row];
            }
            projected[column] = total;
        }
        double[] normalizedShift = new double[dimension];
        for (int row = 0; row < dimension; row++) {
            double total = 0.0;
            for (int column = 0; column < dimension; column++) {
                double axis = axes[column];
                if (axis > 1.0e-20) {
                    total += eigen.Vectors[column][row] * projected[column] / axis;
                }
            }
            normalizedShift[row] = total;
        }
        double[] newPathSigma = new double[dimension];
        for (int axis = 0; axis < dimension; axis++) {
            newPathSigma[axis] = ((1.0 - cs) * state.PathSigma[axis])
                + (sigmaCoefficient * normalizedShift[axis]);
        }
        double pathNorm = 0.0;
        foreach (double value in newPathSigma) {
            pathNorm += value * value;
        }
        pathNorm = CmaNumericsSupport.Sqrt(pathNorm);

        int generation = state.Generation + 1;
        bool hSigma = pathNorm
                / CmaNumericsSupport.Sqrt(1.0 - CmaNumericsSupport.Pow(1.0 - cs, 2.0 * generation))
            < (1.4 + (2.0 / (n + 1.0))) * chiN;
        double covCoefficient = CmaNumericsSupport.Sqrt(cc * (2.0 - cc) * effectiveMass)
            * (hSigma ? 1.0 : 0.0);
        double[] newPathCovariance = new double[dimension];
        for (int axis = 0; axis < dimension; axis++) {
            newPathCovariance[axis] = ((1.0 - cc) * state.PathCovariance[axis])
                + (covCoefficient * meanShift[axis]);
        }

        var newCovariance = new List<IReadOnlyList<double>>(dimension);
        for (int row = 0; row < dimension; row++) {
            var rowValues = new List<double>(dimension);
            for (int column = 0; column < dimension; column++) {
                double value = (1.0 - c1 - cmu) * state.Covariance[row][column];
                double rankOne = newPathCovariance[row] * newPathCovariance[column];
                double correction = (1.0 - (hSigma ? 1.0 : 0.0)) * cc * (2.0 - cc)
                    * state.Covariance[row][column];
                value += c1 * (rankOne + correction);
                double rankMu = 0.0;
                for (int index = 0; index < reweighted; index++) {
                    rankMu += weights[index] * samples[index].Y[row] * samples[index].Y[column];
                }
                value += cmu * rankMu;
                rowValues.Add(value);
            }
            newCovariance.Add(rowValues);
        }

        double newSigma = global::System.Math.Clamp(
            state.Sigma * CmaNumericsSupport.Exp(
                (cs / damps) * ((pathNorm / chiN) - 1.0)),
            1.0e-12, 1.0e12);

        IIndividual<ObjValue, V> bestCandidate = samples[0].Individual;
        IIndividual<ObjValue, V>? best = state.Best is null
            || HeuristicOrdering.CompareStrictly(
                model, bestCandidate.Objective, state.Best.Objective) < 0
                ? bestCandidate
                : state.Best;

        return Task.FromResult(new CmaEsState<ObjValue, V>(
            newMean, newSigma, newCovariance, newPathSigma, newPathCovariance,
            generation, best));
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        CmaEsState<ObjValue, V> state) =>
        state.Best is null
            ? Array.Empty<IIndividual<ObjValue, V>>()
            : new[] { state.Best };

    /// <summary>单位矩阵 / The identity matrix.</summary>
    /// <param name="dimension">维度 / The dimension.</param>
    /// <returns>单位矩阵 / The identity matrix.</returns>
    private static IReadOnlyList<IReadOnlyList<double>> Identity(int dimension) {
        var matrix = new List<IReadOnlyList<double>>(dimension);
        for (int row = 0; row < dimension; row++) {
            var rowValues = new List<double>(dimension);
            for (int column = 0; column < dimension; column++) {
                rowValues.Add(row == column ? 1.0 : 0.0);
            }
            matrix.Add(rowValues);
        }
        return matrix;
    }

    /// <summary>零向量 / A zero vector.</summary>
    /// <param name="dimension">维度 / The dimension.</param>
    /// <returns>零向量 / The zero vector.</returns>
    private static IReadOnlyList<double> Zeroes(int dimension) {
        var values = new List<double>(dimension);
        for (int index = 0; index < dimension; index++) {
            values.Add(0.0);
        }
        return values;
    }

    /// <summary>
    /// 把一个解包装成个体 / Wrap a solution into an individual.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="solution">解 / The solution.</param>
    /// <returns>个体，评估失败时返回 null / The individual, or null when evaluation fails.</returns>
    private IIndividual<ObjValue, V>? ToIndividual(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Solution<V> solution) {
        ObjValue? objective = _fitnessEvaluator.Evaluate<object?, ObjValue, V>(model, solution);
        if (objective is null) {
            return null;
        }
        return new SolutionWithFitness<ObjValue, V>(solution.Values, objective, 0.0);
    }
}
