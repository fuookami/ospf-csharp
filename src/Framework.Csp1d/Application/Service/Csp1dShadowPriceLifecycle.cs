#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model {
    /// <summary>
    /// CSP1D 默认影子价格映射 / CSP1D default shadow price map
    ///
    /// 无额外提取逻辑的空实现，作为 Csp1dShadowPriceLifecycle 的默认影子价格容器。
    /// Empty implementation with no additional extraction logic,
    /// serves as the default shadow price container for Csp1dShadowPriceLifecycle.
    /// </summary>
    public sealed class Csp1dDefaultShadowPriceMap
        : AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments> {
    }
}

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service {
    /// <summary>
    /// CSP1D 影子价格生命周期 / CSP1D shadow price lifecycle
    ///
    /// 管理列生成影子价格的提取和转换。通过 CGPipeline 刷新影子价格映射，
    /// 并将 solver 值转换为领域数值类型。
    ///
    /// Manages extraction and conversion of column generation shadow prices.
    /// Refreshes shadow price map via CGPipeline and converts solver values
    /// to domain numeric type.
    /// </summary>
    /// <typeparam name="V">领域数值类型 / Domain numeric value type.</typeparam>
    public sealed class Csp1dShadowPriceLifecycle<V>
        where V : struct {
        private readonly V _domainValueSample;
        private readonly IReadOnlyList<ICGPipeline<AbstractCsp1dShadowPriceArguments, IAbstractLinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>> _cgPipelines;

        /// <summary>
        /// 框架影子价格映射 / Framework shadow price map.
        /// </summary>
        public AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments> FrameworkShadowPriceMap { get; }
            = new Csp1dDefaultShadowPriceMap();

        /// <summary>
        /// 构造影子价格生命周期 / Construct shadow price lifecycle.
        /// </summary>
        /// <param name="domainValueSample">领域数值样本，用于类型转换 / Domain value sample for type conversion.</param>
        /// <param name="cgPipelines">列生成管线列表 / Column generation pipeline list.</param>
        public Csp1dShadowPriceLifecycle(
            V domainValueSample,
            IReadOnlyList<ICGPipeline<AbstractCsp1dShadowPriceArguments, IAbstractLinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>>? cgPipelines = null) {
            _domainValueSample = domainValueSample;
            _cgPipelines = cgPipelines ?? Array.Empty<ICGPipeline<AbstractCsp1dShadowPriceArguments, IAbstractLinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>>();
        }

        /// <summary>
        /// 从对偶解提取影子价格 / Extract shadow prices from dual solution.
        ///
        /// 通过 CGPipeline 刷新框架影子价格映射，然后转换为轻量级领域影子价格映射。
        /// Refreshes framework shadow price map via CGPipeline,
        /// then converts to lightweight domain shadow price map.
        /// </summary>
        /// <param name="model">线性元模型 / Linear meta model.</param>
        /// <param name="dualSolution">对偶解 / Dual solution.</param>
        /// <returns>领域影子价格映射 / Domain shadow price map.</returns>
        public Result<ShadowPriceMap<V>, ErrorCode, Error<ErrorCode>> ExtractFromDualSolution(
            IAbstractLinearMetaModel<Flt64> model,
            IReadOnlyDictionary<MathConstraint, Flt64> dualSolution) {
            if (_cgPipelines.Count > 0) {
                var shadowPrices = new MetaDualSolution(
                    dualSolution,
                    new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory>, Flt64)>>());
                Try result = ExtractShadowPrices(model, shadowPrices);
                if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) {
                    return Results.Failed<ShadowPriceMap<V>>(f.Error);
                }
                if (result is Fatal<Success, ErrorCode, Error<ErrorCode>> ft) {
                    return new Fatal<ShadowPriceMap<V>, ErrorCode, Error<ErrorCode>>(ft.Errors);
                }
            }
            return Results.Ok(FrameworkShadowPriceMap.ToShadowPriceMap(ConvertSolverValue));
        }

        /// <summary>
        /// 转换对偶值 / Convert dual value.
        /// </summary>
        /// <param name="dualValue">对偶值 / Dual value.</param>
        /// <returns>领域数值 / Domain value.</returns>
        public V ConvertDualValue(Flt64 dualValue) => ConvertSolverValue(dualValue);

        private V ConvertSolverValue(Flt64 value) {
            Result<V, ErrorCode, Error<ErrorCode>> result = DomainValueConversion.ConvertSolverValue(_domainValueSample, value);
            if (result is Ok<V, ErrorCode, Error<ErrorCode>> ok) {
                return ok.Value;
            }
            throw new InvalidOperationException(
                $"Failed to convert solver value {value} to domain type {typeof(V).Name}.");
        }

        private Try ExtractShadowPrices(
            IAbstractLinearMetaModel<Flt64> model,
            MetaDualSolution shadowPrices) {
            foreach (ICGPipeline<AbstractCsp1dShadowPriceArguments, IAbstractLinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> pipeline in _cgPipelines) {
                Try ret = pipeline.Refresh(FrameworkShadowPriceMap, model, shadowPrices);
                if (ret.IsFailed) {
                    return ret;
                }

                ShadowPriceExtractor<AbstractCsp1dShadowPriceArguments, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>>? extractor = pipeline.Extractor();
                if (extractor is not null) {
                    FrameworkShadowPriceMap.Put(extractor);
                }
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }
}
