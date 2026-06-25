#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 生成收集器，负责收集、去重和统计生成的切割方案 / Generation collector for collecting, deduplicating, and tracking generated cutting plans.
    /// </summary>
    /// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
    internal sealed class GenerationCollector<TPlan>
    {
        private readonly long _maxPlans;
        private readonly long? _deadlineMs;
        private readonly bool _enableDominancePruning;
        private readonly DominanceStrategy _dominanceStrategy;
        private readonly Func<TPlan, CuttingPlanCanonicalKey>? _canonicalKeyResolver;
        private readonly Func<TPlan, IReadOnlyList<TPlan>, bool>? _dominanceAcceptOverride;

        private readonly HashSet<CuttingPlanCanonicalKey> _canonicalKeys = new();
        private readonly List<TPlan> _acceptedPlans = new();

        private long _visitedNodes;
        private long _generatedCandidates;
        private long _infeasibleCandidates;
        private long _duplicateCandidates;
        private long _dominatedCandidates;
        private long _widthBoundPrunedNodes;
        private long _knifeBoundPrunedNodes;
        private long _lengthBoundPrunedEntries;
        private long _materialWidthIndexCacheHits;
        private long _materialSliceTemplateCacheHits;
        private long _materialSliceTemplateCacheMisses;
        private long _crossContributionDominated;
        private bool _timedOut;

        /// <summary>
        /// 创建生成收集器 / Create generation collector.
        /// </summary>
        /// <param name="maxPlans">最大方案数 / Max plans.</param>
        /// <param name="deadlineMs">截止时间（毫秒，可空）/ Deadline in milliseconds (nullable).</param>
        /// <param name="enableDominancePruning">是否启用 dominance 剪枝 / Enable dominance pruning.</param>
        /// <param name="dominanceStrategy">dominance 策略 / Dominance strategy.</param>
        /// <param name="canonicalKeyResolver">自定义 canonical key 解析器 / Custom canonical key resolver.</param>
        /// <param name="dominanceAcceptOverride">自定义 dominance 接受函数 / Custom dominance acceptance function.</param>
        public GenerationCollector(
            long maxPlans,
            long? deadlineMs = null,
            bool enableDominancePruning = false,
            DominanceStrategy dominanceStrategy = DominanceStrategy.SameContribution,
            Func<TPlan, CuttingPlanCanonicalKey>? canonicalKeyResolver = null,
            Func<TPlan, IReadOnlyList<TPlan>, bool>? dominanceAcceptOverride = null)
        {
            _maxPlans = maxPlans;
            _deadlineMs = deadlineMs;
            _enableDominancePruning = enableDominancePruning;
            _dominanceStrategy = dominanceStrategy;
            _canonicalKeyResolver = canonicalKeyResolver;
            _dominanceAcceptOverride = dominanceAcceptOverride;
        }

        /// <summary>已接受的方案列表 / Accepted plan list.</summary>
        public IReadOnlyList<TPlan> Plans => _acceptedPlans;

        /// <summary>记录访问节点 / Record visited node.</summary>
        public void VisitNode() => _visitedNodes++;

        /// <summary>记录宽度剪枝节点 / Record width-bound pruned node.</summary>
        public void RecordWidthBoundPrunedNode() => _widthBoundPrunedNodes++;

        /// <summary>记录刀数剪枝节点 / Record knife-bound pruned node.</summary>
        public void RecordKnifeBoundPrunedNode() => _knifeBoundPrunedNodes++;

        /// <summary>记录长度剪枝条目 / Record length-bound pruned entries.</summary>
        public void RecordLengthBoundPrunedEntries(long count = 1L) => _lengthBoundPrunedEntries += count;

        /// <summary>记录宽度索引缓存命中 / Record width index cache hit.</summary>
        public void RecordMaterialWidthIndexCacheHit() => _materialWidthIndexCacheHits++;

        /// <summary>记录切片模板缓存命中 / Record slice template cache hit.</summary>
        public void RecordMaterialSliceTemplateCacheHit() => _materialSliceTemplateCacheHits++;

        /// <summary>记录切片模板缓存未命中 / Record slice template cache miss.</summary>
        public void RecordMaterialSliceTemplateCacheMiss() => _materialSliceTemplateCacheMisses++;

        /// <summary>
        /// 检查是否应停止 / Check whether should stop.
        /// </summary>
        public bool ShouldStop() => IsFull() || IsTimedOut();

        /// <summary>
        /// 检查是否超时 / Check whether timed out.
        /// </summary>
        public bool IsTimedOut()
        {
            if (_timedOut) return true;
            if (_deadlineMs.HasValue && Environment.TickCount64 > _deadlineMs.Value)
            {
                _timedOut = true;
            }
            return _timedOut;
        }

        /// <summary>
        /// 记录候选方案 / Record a candidate plan.
        /// </summary>
        /// <param name="plan">候选方案 / Candidate plan.</param>
        /// <param name="getCanonicalKey">获取 canonical key 的函数 / Function to get canonical key.</param>
        /// <param name="feasible">是否可行 / Whether feasible.</param>
        /// <returns>true 如果方案被接受 / true if plan was accepted.</returns>
        public bool Record(
            TPlan plan,
            Func<TPlan, CuttingPlanCanonicalKey> getCanonicalKey,
            bool feasible)
        {
            _generatedCandidates++;
            if (!feasible)
            {
                _infeasibleCandidates++;
                return false;
            }

            var key = getCanonicalKey(plan);
            if (!_canonicalKeys.Add(key))
            {
                _duplicateCandidates++;
                return false;
            }

            if (_dominanceAcceptOverride is not null && !_dominanceAcceptOverride(plan, _acceptedPlans))
            {
                _canonicalKeys.Remove(key);
                _dominatedCandidates++;
                return false;
            }

            if (IsFull()) return false;

            _acceptedPlans.Add(plan);
            return true;
        }

        /// <summary>
        /// 生成报告 / Generate report.
        /// </summary>
        /// <returns>生成统计 / Generation statistics.</returns>
        public CuttingPlanGenerationStatistics Report()
        {
            return new CuttingPlanGenerationStatistics(
                VisitedNodes: _visitedNodes,
                GeneratedCandidates: _generatedCandidates,
                AcceptedPlans: _acceptedPlans.Count,
                InfeasibleCandidates: _infeasibleCandidates,
                DuplicateCandidates: _duplicateCandidates,
                DominatedCandidates: _dominatedCandidates,
                WidthBoundPrunedNodes: _widthBoundPrunedNodes,
                KnifeBoundPrunedNodes: _knifeBoundPrunedNodes,
                LengthBoundPrunedEntries: _lengthBoundPrunedEntries,
                MaterialWidthIndexCacheHits: _materialWidthIndexCacheHits,
                MaterialSliceTemplateCacheHits: _materialSliceTemplateCacheHits,
                MaterialSliceTemplateCacheMisses: _materialSliceTemplateCacheMisses,
                CrossContributionDominated: _crossContributionDominated,
                StopReason: StopReason()
            );
        }

        private bool IsFull() => _acceptedPlans.Count >= _maxPlans;

        private CuttingPlanGenerationStopReason StopReason()
        {
            if (_timedOut) return CuttingPlanGenerationStopReason.Timeout;
            if (IsFull()) return CuttingPlanGenerationStopReason.MaxPlans;
            return CuttingPlanGenerationStopReason.Exhausted;
        }
    }
}
