#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Intermediate
{
    /// <summary>批次切片 / Batch slice</summary>
    /// <param name="StartIndex">起始索引 / Start index</param>
    /// <param name="EndIndex">结束索引（不含）/ End index (exclusive)</param>
    public sealed record BatchSlice(int StartIndex, int EndIndex)
    {
        /// <summary>切片大小 / Slice size</summary>
        public int Size => EndIndex - StartIndex;

        /// <inheritdoc/>
        public override string ToString() => $"[{StartIndex}, {EndIndex})";
    }

    /// <summary>批次调度计划 / Batch dispatch plan</summary>
    /// <param name="Slices">切片列表 / Slice list</param>
    /// <param name="BatchSize">批次大小 / Batch size</param>
    public sealed record BatchDispatchPlan(IReadOnlyList<BatchSlice> Slices, int BatchSize)
    {
        /// <summary>切片数量 / Slice count</summary>
        public int SliceCount => Slices.Count;

        /// <inheritdoc/>
        public override string ToString() => $"BatchDispatchPlan[Slices={SliceCount}, BatchSize={BatchSize}]";
    }

    /// <summary>批次调度策略（内部）/ Batch dispatch policy (internal)</summary>
    internal static class BatchDispatchPolicy
    {
        /// <summary>计算批次调度计划 / Compute batch dispatch plan</summary>
        /// <param name="totalCount">总数量 / Total count</param>
        /// <param name="maxBatchSize">最大批次大小 / Maximum batch size</param>
        /// <returns>批次调度计划 / Batch dispatch plan</returns>
        public static BatchDispatchPlan ComputeBatchDispatchPlan(int totalCount, int maxBatchSize)
        {
            if (totalCount <= 0)
            {
                return new BatchDispatchPlan(Array.Empty<BatchSlice>(), 0);
            }

            if (maxBatchSize <= 0)
            {
                maxBatchSize = totalCount;
            }

            var slices = BuildBatchSlices(totalCount, maxBatchSize);
            return new BatchDispatchPlan(slices, maxBatchSize);
        }

        /// <summary>构建批次切片列表 / Build batch slice list</summary>
        /// <param name="totalCount">总数量 / Total count</param>
        /// <param name="batchSize">批次大小 / Batch size</param>
        /// <returns>切片列表 / Slice list</returns>
        public static IReadOnlyList<BatchSlice> BuildBatchSlices(int totalCount, int batchSize)
        {
            if (totalCount <= 0 || batchSize <= 0)
            {
                return Array.Empty<BatchSlice>();
            }

            var slices = new List<BatchSlice>();
            for (int start = 0; start < totalCount; start += batchSize)
            {
                var end = System.Math.Min(start + batchSize, totalCount);
                slices.Add(new BatchSlice(start, end));
            }
            return slices;
        }
    }
}
