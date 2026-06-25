#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Model
{
    // ===== IPipeline<M> =====

    /// <summary>
    /// 约束管线接口 / Constraint pipeline interface.
    /// </summary>
    /// <typeparam name="M">模型类型 / Model type</typeparam>
    public interface IPipeline<in M> : IMetaConstraintGroup where M : class
    {
        /// <summary>惰性标志 / Lazy flag</summary>
        bool IMetaConstraintGroup.Lazy => false;

        /// <summary>管线名称 / Pipeline name</summary>
        string IMetaConstraintGroup.Name => GetType().Name;

        /// <summary>注册管线到模型 / Register pipeline to model.</summary>
        void Register(M model)
        {
            if (model is IMetaModel<Flt64> metaModel)
            {
                metaModel.RegisterConstraintGroup(this);
            }
        }

        /// <summary>执行管线 / Execute pipeline.</summary>
        Try Invoke(M model);

        /// <summary>获取线性模型不可行原因 / Get linear model infeasible reasons.</summary>
        IReadOnlyList<string> InfeasibleReasonsLinear(ILinearTriadModelView iis) => Array.Empty<string>();

        /// <summary>获取二次模型不可行原因 / Get quadratic model infeasible reasons.</summary>
        IReadOnlyList<string> InfeasibleReasonsQuadratic(IQuadraticTetradModelView iis) => Array.Empty<string>();
    }

    // ===== ICGPipeline<Args,MModel,TMap> =====

    /// <summary>
    /// 列生成管线接口 / Column generation pipeline interface.
    /// </summary>
    public interface ICGPipeline<Args, in MModel, TMap> : IPipeline<MModel>
        where Args : class
        where MModel : class
        where TMap : AbstractShadowPriceMap<Args, TMap>
    {
        /// <summary>获取影子价格提取器 / Get shadow price extractor, may be null.</summary>
        ShadowPriceExtractor<Args, TMap>? Extractor() => null;

        /// <summary>刷新影子价格 / Refresh shadow prices.</summary>
        Try Refresh(TMap shadowPriceMap, MModel model, MetaDualSolution shadowPrices)
        {
            // Default implementation: iterate constraints of this group,
            // collect shadow prices by ShadowPriceKey, and put them into the map.
            if (model is not IMetaModel<Flt64> metaModel) return Results.Ok<Success>(Results.SuccessInstance);

            var prices = new Dictionary<ShadowPriceKey, Flt64>();
            foreach (var constraint in metaModel.ConstraintsOfGroup(this))
            {
                if (constraint.Args is ShadowPriceKey key)
                {
                    if (shadowPrices.Constraints.TryGetValue(constraint, out var price))
                    {
                        prices[key] = (prices.TryGetValue(key, out var p) ? p : Flt64.Zero) + price;
                    }
                }
            }
            foreach (var (key, value) in prices)
            {
                shadowPriceMap.Put(new ShadowPrice(key, value));
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>按键刷新影子价格 / Refresh shadow prices by key (static helper).</summary>
        static Try RefreshByKeyAsArgs<TMapT>(
            ICGPipeline<object, object, TMapT> pipeline,
            TMapT shadowPriceMap,
            object model,
            MetaDualSolution shadowPrices)
            where TMapT : AbstractShadowPriceMap<object, TMapT>
        {
            if (model is not IMetaModel<Flt64> metaModel) return Results.Ok<Success>(Results.SuccessInstance);

            var thisShadowPrices = new Dictionary<ShadowPriceKey, Flt64>();
            foreach (var constraint in metaModel.ConstraintsOfGroup(pipeline))
            {
                if (constraint.Args is not ShadowPriceKey key) continue;
                if (shadowPrices.Constraints.TryGetValue(constraint, out var price))
                {
                    thisShadowPrices[key] = (thisShadowPrices.TryGetValue(key, out var p) ? p : Flt64.Zero) + price;
                }
            }
            foreach (var (key, value) in thisShadowPrices)
            {
                shadowPriceMap.Put(new ShadowPrice(key, value));
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }

    // ===== HAPipelineObj =====

    /// <summary>
    /// 启发式分析目标值 / Heuristic analysis objective value.
    /// </summary>
    public sealed record HAPipelineObj(string Tag, Flt64 Value);

    // ===== IHAPipeline<M> =====

    /// <summary>
    /// 启发式分析管线接口 / Heuristic analysis pipeline interface.
    /// </summary>
    /// <typeparam name="M">模型类型 / Model type</typeparam>
    public interface IHAPipeline<M> : IPipeline<M> where M : class
    {
        /// <summary>执行管线（默认空操作）/ Execute pipeline (default no-op).</summary>
        Try IPipeline<M>.Invoke(M model) => Results.Ok<Success>(Results.SuccessInstance);

        /// <summary>执行启发式分析 / Execute heuristic analysis.</summary>
        Result<HAPipelineObj, ErrorCode, Error<ErrorCode>> Analyze(M model, IReadOnlyList<Flt64> solution)
        {
            var obj = Calculate(model, solution);
            if (obj is Ok<Flt64?, ErrorCode, Error<ErrorCode>> ok && ok.Value is { } v)
            {
                return Results.Ok(new HAPipelineObj(((IMetaConstraintGroup)this).Name, v));
            }
            return new Failed<HAPipelineObj, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, ((IMetaConstraintGroup)this).Name));
        }

        /// <summary>计算目标值 / Calculate objective value, may be null.</summary>
        Result<Flt64?, ErrorCode, Error<ErrorCode>> Calculate(M model, IReadOnlyList<Flt64> solution);

        /// <summary>检查解的有效性 / Check solution validity.</summary>
        Try Check(M model, IReadOnlyList<Flt64> solution)
        {
            var obj = Calculate(model, solution);
            if (obj is Ok<Flt64?, ErrorCode, Error<ErrorCode>> ok && ok.Value is not null)
            {
                return Results.Ok<Success>(Results.SuccessInstance);
            }
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ORSolutionInvalid, ((IMetaConstraintGroup)this).Name));
        }
    }

    // ===== Pipeline list helpers =====

    /// <summary>
    /// 管线列表辅助方法 / Pipeline list helper methods.
    /// </summary>
    public static class PipelineListInvoker
    {
        /// <summary>执行管线列表中的所有管线 / Execute all pipelines in pipeline list.</summary>
        public static Try Invoke<M>(IReadOnlyList<IPipeline<M>> pipelineList, M model) where M : class
        {
            foreach (var pipeline in pipelineList)
            {
                pipeline.Register(model);
                var ret = pipeline.Invoke(model);
                if (ret.IsFailed) return ret;
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>执行启发式分析管线列表 / Execute HA pipeline list.</summary>
        public static Result<IReadOnlyList<HAPipelineObj>, ErrorCode, Error<ErrorCode>> InvokeHA<M>(
            IReadOnlyList<IHAPipeline<M>> pipelineList,
            M model,
            IReadOnlyList<Flt64> solution) where M : class
        {
            var results = new List<HAPipelineObj>();
            foreach (var pipeline in pipelineList)
            {
                var ret = pipeline.Analyze(model, solution);
                if (ret is Ok<HAPipelineObj, ErrorCode, Error<ErrorCode>> ok)
                {
                    results.Add(ok.Value);
                }
            }
            return Results.Ok<IReadOnlyList<HAPipelineObj>>(results);
        }
    }
}
