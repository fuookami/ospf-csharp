#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// FullSum 方案生成器，枚举所有宽度求和组合 / FullSum generator: enumerate all width-sum combinations.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    internal sealed class FullSumGenerator<V> : ICsp1dInitialCuttingPlanGenerator<CuttingPlanStub<V>, GenerationInputStub<V>>
        where V : struct, IComparable<V>
    {
        private readonly IReadOnlyList<ICuttingPlanConstraint<V>> _constraints;
        private readonly long _maxPlans;
        private readonly long? _timeoutMs;
        private readonly long _parallelism;
        private readonly bool _enableDominancePruning;
        private readonly DominanceStrategy _dominanceStrategy;

        /// <summary>
        /// 创建 FullSum 生成器 / Create FullSum generator.
        /// </summary>
        public FullSumGenerator(
            IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
            long maxPlans = 1000L,
            long? timeoutMs = null,
            long parallelism = 1L,
            bool enableDominancePruning = false,
            DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution)
        {
            _constraints = constraints;
            _maxPlans = maxPlans;
            _timeoutMs = timeoutMs;
            _parallelism = global::System.Math.Max(1L, parallelism);
            _enableDominancePruning = enableDominancePruning;
            _dominanceStrategy = dominanceStrategy;
        }

        /// <summary>
        /// 从 GenerationConstraints 创建 / Create from GenerationConstraints.
        /// </summary>
        public FullSumGenerator(
            GenerationConstraints<V> constraints,
            long maxPlans = 1000L,
            long? timeoutMs = null)
            : this(
                constraints: constraints.ToConstraints(),
                maxPlans: maxPlans,
                timeoutMs: timeoutMs,
                parallelism: constraints.Parallelism,
                enableDominancePruning: constraints.EnableDominancePruning,
                dominanceStrategy: constraints.DominanceStrategy)
        {
        }

        /// <inheritdoc/>
        public IReadOnlyList<CuttingPlanStub<V>> Generate(GenerationInputStub<V> input)
        {
            return GenerateWithReport(input).Plans;
        }

        /// <inheritdoc/>
        public CuttingPlanGenerationReport<CuttingPlanStub<V>> GenerateWithReport(GenerationInputStub<V> input)
        {
            var startTime = Environment.TickCount64;
            var collector = new GenerationCollector<CuttingPlanStub<V>>(
                maxPlans: _maxPlans,
                deadlineMs: _timeoutMs.HasValue ? startTime + _timeoutMs.Value : null,
                enableDominancePruning: _enableDominancePruning,
                dominanceStrategy: _dominanceStrategy
            );

            // Stub: FullSum search implementation deferred

            return new CuttingPlanGenerationReport<CuttingPlanStub<V>>(
                Plans: collector.Plans,
                Statistics: collector.Report() with
                {
                    ElapsedMilliseconds = Environment.TickCount64 - startTime
                }
            );
        }
    }
}
