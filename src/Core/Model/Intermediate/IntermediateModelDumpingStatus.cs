#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Intermediate
{
    /// <summary>中间模型转储状态 / Intermediate model dumping status</summary>
    /// <param name="ModelName">模型名称 / Model name</param>
    /// <param name="Stage">当前阶段 / Current stage</param>
    /// <param name="Ready">已完成数量 / Ready count</param>
    /// <param name="Total">总数量 / Total count</param>
    public sealed record IntermediateModelDumpingStatus(
        string ModelName,
        string Stage,
        UInt64 Ready,
        UInt64 Total)
    {
        /// <summary>进度 / Progress</summary>
        public Flt64 Progress => Total != UInt64.Zero
            ? Ready.ToFlt64() / Total.ToFlt64()
            : Flt64.One;

        /// <inheritdoc/>
        public override string ToString() =>
            $"IntermediateModelDumpingStatus[{ModelName}, {Stage}, {Progress}]";
    }
}
