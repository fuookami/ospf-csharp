#nullable enable

using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 启发式求解状态枚举 / Heuristic solution status enum.
/// </summary>
public enum HeuristicSolutionStatus {
    /// <summary>可行 / Feasible</summary>
    Feasible,
    /// <summary>不可行 / Infeasible</summary>
    Infeasible
}

/// <summary>
/// 启发式求解结果 / Heuristic solve result.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
/// <param name="BestSolution">最优解向量 / Best solution vector</param>
/// <param name="BestObjective">最优目标值 / Best objective value</param>
/// <param name="Status">求解状态 / Solution status</param>
/// <param name="Iteration">迭代信息 / Iteration info</param>
public sealed record HeuristicResult<ObjValue, V>(
    IReadOnlyList<V>? BestSolution,
    ObjValue? BestObjective,
    HeuristicSolutionStatus Status,
    Iteration Iteration);

/// <summary>
/// 基础启发式策略 / Basic heuristic policy with default limits.
/// </summary>
public sealed class BasicHeuristicPolicy : HeuristicPolicy {
    /// <inheritdoc/>
    public override string Name => "basic";
}

/// <summary>
/// 粒子数据结构 / Particle data structure for PSO.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
/// <param name="Fitness">适应度 / Fitness value</param>
/// <param name="Solution">当前位置（解向量）/ Current position (solution vector)</param>
/// <param name="Velocity">速度向量 / Velocity vector</param>
/// <param name="BestPosition">历史最优位置 / Historical best position</param>
/// <param name="BestFitness">历史最优适应度 / Historical best fitness</param>
public sealed record Particle<ObjValue, V>(
    ObjValue Fitness,
    IReadOnlyList<V> Solution,
    IReadOnlyList<double> Velocity,
    IReadOnlyList<V>? BestPosition,
    ObjValue? BestFitness);

/// <summary>
/// 粒子群启发式求解器 / Particle swarm heuristic solver implementing standard PSO.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型，须满足实数域和数域约束 / Decision variable value type, must satisfy real number and number field constraints</typeparam>
public sealed class ParticleSwarmHeuristicSolver<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {

    /// <summary>求解器名称 / Solver name</summary>
    public string Name => "pso";

    /// <summary>粒子数量 / Number of particles</summary>
    public int ParticleAmount { get; init; } = 100;

    /// <summary>返回解的数量 / Number of solutions to return</summary>
    public int SolutionAmount { get; init; } = 1;

    /// <summary>惯性权重 / Inertia weight</summary>
    public double W { get; init; } = 0.4;

    /// <summary>个体学习因子 / Personal learning factor</summary>
    public double C1 { get; init; } = 2.0;

    /// <summary>社会学习因子 / Social learning factor</summary>
    public double C2 { get; init; } = 2.0;

    /// <summary>最大速度（用于速度钳制）/ Maximum velocity (for velocity clamping)</summary>
    public double MaxVelocity { get; init; } = 10000.0;

    /// <summary>
    /// 当目标函数返回 null 时是否仍继续求解 / Whether to continue solving when the objective function returns null.
    /// </summary>
    public bool SolveOnObjectiveMiss { get; init; } = true;

    private readonly IIntoValue<V> _converter;

    /// <summary>
    /// 构造粒子群求解器 / Construct particle swarm solver.
    /// </summary>
    /// <param name="converter">值类型转换器 / Value type converter</param>
    public ParticleSwarmHeuristicSolver(IIntoValue<V> converter) {
        _converter = converter;
    }

    /// <summary>
    /// 执行 PSO 求解 / Execute PSO solving.
    /// </summary>
    /// <param name="evaluateFitness">适应度评估函数，返回 null 表示不可行 / Fitness evaluation function, returns null if infeasible</param>
    /// <param name="generateInitialSolution">初始解生成函数，参数为变量维度 / Initial solution generation function, parameter is variable dimension</param>
    /// <param name="compareObjective">目标值比较函数，返回负值表示 a 更优 / Objective comparison function, negative return means a is better</param>
    /// <param name="policy">启发式策略（可选）/ Heuristic policy (optional)</param>
    /// <returns>求解结果 / Solve result</returns>
    public HeuristicResult<ObjValue, V> Solve(
        Func<IReadOnlyList<V>, ObjValue?> evaluateFitness,
        Func<int, IReadOnlyList<V>> generateInitialSolution,
        Func<ObjValue, ObjValue, int> compareObjective,
        HeuristicPolicy? policy = null) {

        HeuristicPolicy effectivePolicy = policy ?? new BasicHeuristicPolicy();
        var iteration = new Iteration();
        Random rng = Random.Shared;

        // Generate first particle to determine dimension
        IReadOnlyList<V> sampleSolution = generateInitialSolution(1);
        int dimension = sampleSolution.Count;

        // Initialize particles
        var particles = new List<Particle<ObjValue, V>>(ParticleAmount);
        ObjValue? globalBestObj = default;
        IReadOnlyList<V>? globalBestPos = null;

        for (int i = 0; i < ParticleAmount; i++) {
            IReadOnlyList<V> position = i == 0 ? sampleSolution : generateInitialSolution(dimension);
            IReadOnlyList<double> velocity = InitializeVelocity(dimension, rng);
            ObjValue? fitness = evaluateFitness(position);

            IReadOnlyList<V>? bestPos = position;
            ObjValue? bestFit = fitness;

            if (fitness is not null) {
                if (globalBestObj is null || compareObjective(fitness, globalBestObj) < 0) {
                    globalBestObj = fitness;
                    globalBestPos = position;
                }
            }

            particles.Add(new Particle<ObjValue, V>(
                fitness!,
                position,
                velocity,
                bestPos,
                bestFit));
        }

        // Main PSO loop
        while (!effectivePolicy.Finished(iteration)) {
            bool improved = false;

            var updatedParticles = new List<Particle<ObjValue, V>>(ParticleAmount);

            for (int i = 0; i < particles.Count; i++) {
                Particle<ObjValue, V> particle = particles[i];
                IReadOnlyList<V> newPosition = UpdatePosition(particle.Solution, particle.Velocity, rng);
                ObjValue? newFitness = evaluateFitness(newPosition);

                if (newFitness is null && !SolveOnObjectiveMiss) {
                    updatedParticles.Add(particle);
                    continue;
                }

                IReadOnlyList<V> personalBestPos = particle.BestPosition ?? particle.Solution;
                ObjValue? personalBestFit = particle.BestFitness;
                IReadOnlyList<V>? currentGlobalBest = globalBestPos;

                IReadOnlyList<double> newVelocity = UpdateVelocity(
                    particle.Velocity,
                    particle.Solution,
                    personalBestPos,
                    currentGlobalBest,
                    rng);

                IReadOnlyList<V> updatedBestPos = personalBestPos;
                ObjValue? updatedBestFit = personalBestFit;

                if (newFitness is not null) {
                    if (personalBestFit is null || compareObjective(newFitness, personalBestFit) < 0) {
                        updatedBestPos = newPosition;
                        updatedBestFit = newFitness;
                    }

                    if (globalBestObj is null || compareObjective(newFitness, globalBestObj) < 0) {
                        globalBestObj = newFitness;
                        globalBestPos = newPosition;
                        improved = true;
                    }
                }

                updatedParticles.Add(new Particle<ObjValue, V>(
                    newFitness!,
                    newPosition,
                    newVelocity,
                    updatedBestPos,
                    updatedBestFit));
            }

            particles = updatedParticles;
            iteration.Next(improved);
        }

        // Determine final status
        HeuristicSolutionStatus status = globalBestObj is not null
            ? HeuristicSolutionStatus.Feasible
            : HeuristicSolutionStatus.Infeasible;

        return new HeuristicResult<ObjValue, V>(
            globalBestPos,
            globalBestObj,
            status,
            iteration);
    }

    private IReadOnlyList<double> InitializeVelocity(int dimension, Random rng) {
        double[] velocity = new double[dimension];
        double range = MaxVelocity * 0.1;
        for (int j = 0; j < dimension; j++) {
            velocity[j] = (rng.NextDouble() * 2.0 - 1.0) * range;
        }
        return velocity;
    }

    private IReadOnlyList<double> UpdateVelocity(
        IReadOnlyList<double> currentVelocity,
        IReadOnlyList<V> currentPosition,
        IReadOnlyList<V> personalBest,
        IReadOnlyList<V>? globalBest,
        Random rng) {

        int dimension = currentVelocity.Count;
        double[] newVelocity = new double[dimension];

        for (int j = 0; j < dimension; j++) {
            double r1 = rng.NextDouble();
            double r2 = rng.NextDouble();

            double currentPosD = _converter.FromValue(currentPosition[j]).ToDouble();
            double personalBestD = _converter.FromValue(personalBest[j]).ToDouble();
            double globalBestD = globalBest is not null
                ? _converter.FromValue(globalBest[j]).ToDouble()
                : currentPosD;

            double v = W * currentVelocity[j]
                + C1 * r1 * (personalBestD - currentPosD)
                + C2 * r2 * (globalBestD - currentPosD);

            // Clamp velocity
            if (v > MaxVelocity) {
                v = MaxVelocity;
            }
            else if (v < -MaxVelocity) {
                v = -MaxVelocity;
            }

            newVelocity[j] = v;
        }

        return newVelocity;
    }

    private IReadOnlyList<V> UpdatePosition(
        IReadOnlyList<V> currentPosition,
        IReadOnlyList<double> velocity,
        Random rng) {

        int dimension = currentPosition.Count;
        var newPosition = new V[dimension];

        for (int j = 0; j < dimension; j++) {
            double currentPosD = _converter.FromValue(currentPosition[j]).ToDouble();
            double newPosD = currentPosD + velocity[j];
            newPosition[j] = _converter.IntoValue(new Flt64(newPosD));
        }

        return newPosition;
    }
}
