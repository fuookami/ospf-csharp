#nullable enable

using System;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Framework.Solver
{
    /// <summary>
    /// Framework 层统一求解选项 / Framework-level solve options.
    /// </summary>
    public sealed record FrameworkSolveOptions
    {
        /// <summary>求解名称 / Solve name</summary>
        public string? Name { get; init; }
        /// <summary>是否输出模型日志 / Whether to log model</summary>
        public bool ToLogModel { get; init; }
        /// <summary>解池数量 / Solution pool amount</summary>
        public ulong? SolutionAmount { get; init; }
        /// <summary>模型构建状态回调 / Model building status callback</summary>
        public ModelBuildingStatusCallBack? ModelBuildingStatusCallBack { get; init; }
        /// <summary>注册状态回调 / Registration status callback</summary>
        public RegistrationStatusCallBack? RegistrationStatusCallBack { get; init; }
        /// <summary>求解状态回调 / Solving status callback</summary>
        public SolvingStatusCallBack? SolvingStatusCallBack { get; init; }
        /// <summary>值转换策略 / Value conversion policy</summary>
        public SolveValueConversionPolicy? ValueConversionPolicy { get; init; }
        /// <summary>Benders 迭代上限 / Benders iteration limit</summary>
        public ulong? BendersIterationLimit { get; init; }
        /// <summary>Benders 停滞迭代上限 / Benders stall iteration limit</summary>
        public ulong? BendersStallIterationLimit { get; init; }

        /// <summary>生效的值转换策略，默认为 Strict / Effective policy, defaults to Strict.</summary>
        public SolveValueConversionPolicy EffectiveValueConversionPolicy =>
            ValueConversionPolicy ?? SolveValueConversionPolicy.Strict;

        /// <summary>获取求解名称（带回退默认值）/ Get solve name with fallback default.</summary>
        public string SolveName(string defaultName) => Name ?? defaultName;

        /// <summary>转换为核心层求解选项 / Convert to core-level solve options.</summary>
        public SolveOptions ToCoreSolveOptions() => new()
        {
            SolutionAmount = SolutionAmount,
            ModelBuildingStatusCallBack = ModelBuildingStatusCallBack,
            SolvingStatusCallBack = SolvingStatusCallBack,
            ValueConversionPolicy = ValueConversionPolicy,
        };

        /// <summary>创建构建器 / Create builder.</summary>
        public static Builder CreateBuilder() => new();

        /// <summary>通过配置委托构建 / Build via configuration delegate.</summary>
        public static FrameworkSolveOptions Build(Action<Builder> block)
        {
            var b = new Builder();
            block(b);
            return b.Build();
        }

        /// <summary>
        /// 构建器 / Builder.
        /// </summary>
        public sealed class Builder
        {
            /// <summary>求解名称 / Solve name</summary>
            public string? Name { get; set; }
            /// <summary>是否输出模型日志 / Whether to log model</summary>
            public bool ToLogModel { get; set; }
            /// <summary>解池数量 / Solution pool amount</summary>
            public ulong? SolutionAmount { get; set; }
            /// <summary>模型构建状态回调 / Model building status callback</summary>
            public ModelBuildingStatusCallBack? ModelBuildingStatusCallBack { get; set; }
            /// <summary>注册状态回调 / Registration status callback</summary>
            public RegistrationStatusCallBack? RegistrationStatusCallBack { get; set; }
            /// <summary>求解状态回调 / Solving status callback</summary>
            public SolvingStatusCallBack? SolvingStatusCallBack { get; set; }
            /// <summary>值转换策略 / Value conversion policy</summary>
            public SolveValueConversionPolicy? ValueConversionPolicy { get; set; }
            /// <summary>Benders 迭代上限 / Benders iteration limit</summary>
            public ulong? BendersIterationLimit { get; set; }
            /// <summary>Benders 停滞迭代上限 / Benders stall iteration limit</summary>
            public ulong? BendersStallIterationLimit { get; set; }

            /// <summary>构建 FrameworkSolveOptions / Build FrameworkSolveOptions.</summary>
            public FrameworkSolveOptions Build() => new()
            {
                Name = Name,
                ToLogModel = ToLogModel,
                SolutionAmount = SolutionAmount,
                ModelBuildingStatusCallBack = ModelBuildingStatusCallBack,
                RegistrationStatusCallBack = RegistrationStatusCallBack,
                SolvingStatusCallBack = SolvingStatusCallBack,
                ValueConversionPolicy = ValueConversionPolicy,
                BendersIterationLimit = BendersIterationLimit,
                BendersStallIterationLimit = BendersStallIterationLimit,
            };
        }
    }
}
