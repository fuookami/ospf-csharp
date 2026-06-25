#nullable enable

using System;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Core.Solver
{
    /// <summary>
    /// 求解选项，封装求解过程中的可选参数。
    /// Solve options, encapsulating optional parameters for the solving process.
    /// </summary>
    public sealed record SolveOptions(
        ulong? SolutionAmount = null,
        ModelBuildingStatusCallBack? ModelBuildingStatusCallBack = null,
        SolvingStatusCallBack? SolvingStatusCallBack = null,
        SolveValueConversionPolicy? ValueConversionPolicy = null)
    {
        /// <summary>有效的值转换策略 / Effective value conversion policy</summary>
        public SolveValueConversionPolicy EffectiveValueConversionPolicy
            => ValueConversionPolicy ?? SolveValueConversionPolicy.AllowRounding;

        /// <summary>
        /// 求解选项构建器，通过链式调用逐步配置各项参数。
        /// Solve options builder that configures parameters step by step via chaining.
        /// </summary>
        public sealed class Builder
        {
            public ulong? SolutionAmount { get; set; }
            public ModelBuildingStatusCallBack? ModelBuildingStatusCallBack { get; set; }
            public SolvingStatusCallBack? SolvingStatusCallBack { get; set; }
            public SolveValueConversionPolicy? ValueConversionPolicy { get; set; }

            public SolveOptions Build() => new(
                SolutionAmount,
                ModelBuildingStatusCallBack,
                SolvingStatusCallBack,
                ValueConversionPolicy);
        }

        /// <summary>创建构建器 / Create builder</summary>
        public static Builder CreateBuilder() => new();

        /// <summary>通过配置委托构建 / Build via configuration delegate</summary>
        public static SolveOptions Build(Action<Builder> configure)
        {
            var b = new Builder();
            configure(b);
            return b.Build();
        }
    }
}
