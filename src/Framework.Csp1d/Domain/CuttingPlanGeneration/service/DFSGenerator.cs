#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// DFS 方案生成器，栈式深度优先搜索枚举多产品组合方案 / DFS generator: stack-based depth-first search for multi-product combination plans.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
internal sealed class DFSGenerator<V> : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<V>, GenerationInput<V>>
    where V : struct, IComparable<V> {
    private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _constraints;
    private readonly long _maxPlans;
    private readonly long? _timeoutMs;
    private readonly long _parallelism;
    private readonly bool _enableDominancePruning;
    private readonly DominanceStrategy _dominanceStrategy;

    private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _pruningConstraints;
    private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _leafConstraints;
    private readonly UInt64? _maxKnifeCount;
    private readonly UInt64? _minKnifeCount;

    /// <summary>
    /// 创建 DFS 生成器 / Create DFS generator.
    /// </summary>
    /// <param name="constraints">约束列表 / Constraint list.</param>
    /// <param name="maxPlans">最大方案数 / Max plans.</param>
    /// <param name="timeoutMs">超时限制（毫秒）/ Timeout limit (milliseconds).</param>
    /// <param name="parallelism">并行度 / Parallelism.</param>
    /// <param name="enableDominancePruning">是否启用 dominance 剪枝 / Enable dominance pruning.</param>
    /// <param name="dominanceStrategy">dominance 策略 / Dominance strategy.</param>
    public DFSGenerator(
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        long maxPlans = 1000L,
        long? timeoutMs = null,
        long parallelism = 1L,
        bool enableDominancePruning = false,
        DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution) {
        _constraints = constraints;
        _maxPlans = maxPlans;
        _timeoutMs = timeoutMs;
        _parallelism = global::System.Math.Max(1L, parallelism);
        _enableDominancePruning = enableDominancePruning;
        _dominanceStrategy = dominanceStrategy;

        _pruningConstraints = constraints.Where(c => c.IsPruning).ToList();
        _leafConstraints = constraints.Where(c => !c.IsPruning).ToList();
        _maxKnifeCount = constraints.OfType<MaxKnifeCountConstraint<V>>().FirstOrDefault()?.Threshold;
        _minKnifeCount = constraints.OfType<MinKnifeCountConstraint<V>>().FirstOrDefault()?.Threshold;
    }

    /// <summary>
    /// 从 GenerationConstraints 创建 DFS 生成器 / Create DFS generator from GenerationConstraints.
    /// </summary>
    public DFSGenerator(
        GenerationConstraints<V> constraints,
        long maxPlans = 1000L,
        long? timeoutMs = null)
        : this(
            constraints: constraints.ToConstraints(),
            maxPlans: maxPlans,
            timeoutMs: timeoutMs,
            parallelism: constraints.Parallelism,
            enableDominancePruning: constraints.EnableDominancePruning,
            dominanceStrategy: constraints.DominanceStrategy) {
    }

    /// <inheritdoc/>
    public IReadOnlyList<CuttingPlan<V>> Generate(GenerationInput<V> input) => GenerateWithReport(input).Plans;

    /// <inheritdoc/>
    public CuttingPlanGenerationReport<CuttingPlan<V>> GenerateWithReport(GenerationInput<V> input) {
        long startTime = Environment.TickCount64;
        // planIndex removed — assigned but never used (CS0219)
        var collector = new GenerationCollector<CuttingPlan<V>>(
            maxPlans: _maxPlans,
            deadlineMs: _timeoutMs.HasValue ? startTime + _timeoutMs.Value : null,
            enableDominancePruning: _enableDominancePruning,
            dominanceStrategy: _dominanceStrategy
        );

        // Stub implementation: DFS search would go here
        // Full implementation deferred to later phase

        return new CuttingPlanGenerationReport<CuttingPlan<V>>(
            Plans: collector.Plans,
            Statistics: collector.Report() with {
                ElapsedMilliseconds = Environment.TickCount64 - startTime
            }
        );
    }
}
