#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra
{
    /// <summary>
    /// 需求影子价格键 / Demand shadow-price key.
    /// 区分按物料与按包规格的影子价格 / Distinguishes per-material vs per-package-attribute prices.
    /// </summary>
    public abstract record DemandShadowPriceKey
    {
        /// <summary>物料编号 / Material number.</summary>
        public abstract string MaterialNo { get; }

        /// <summary>按物料 / By material only.</summary>
        public sealed record ByMaterial(string MaterialNo) : DemandShadowPriceKey
        {
            public override string MaterialNo { get; } = MaterialNo;
        }

        /// <summary>按包规格 / By material + package attribute.</summary>
        public sealed record ByPackage(string MaterialNo, string PackageAttributeKey) : DemandShadowPriceKey
        {
            public override string MaterialNo { get; } = MaterialNo;
            public string PackageAttributeKey { get; } = PackageAttributeKey;
        }
    }

    /// <summary>
    /// BPP3D 影子价格映射抽象基类 / Abstract shadow-price map for BPP3D.
    /// 由领域层继承以注入列生成对偶信息 / Subclassed by domain to feed CG dual info.
    /// </summary>
    public abstract class AbstractBpp3dShadowPriceMap
    {
        private readonly Dictionary<DemandShadowPriceKey, Flt64> _prices = new();

        /// <summary>已注册影子价格视图 / Registered price view.</summary>
        public IReadOnlyDictionary<DemandShadowPriceKey, Flt64> Prices => _prices;

        /// <summary>
        /// 注册/更新影子价格 / Register or update a shadow price.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> Register(DemandShadowPriceKey key, Flt64 price)
        {
            if (key is null)
            {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "Shadow price key is null."));
            }
            _prices[key] = price;
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>
        /// 查询影子价格 / Query the shadow price for a key.
        /// 未注册返回失败 / Returns error if not registered.
        /// </summary>
        public Result<Flt64, ErrorCode, Error<ErrorCode>> PriceOf(DemandShadowPriceKey key)
        {
            if (key is null)
            {
                return new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "Shadow price key is null."));
            }
            return _prices.TryGetValue(key, out var value)
                ? Results.Ok(value)
                : new Failed<Flt64, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.DataNotFound, $"Shadow price not found for key: {key}"));
        }

        /// <summary>
        /// 批量注册 / Register multiple prices.
        /// </summary>
        public Result<Success, ErrorCode, Error<ErrorCode>> RegisterRange(
            IEnumerable<KeyValuePair<DemandShadowPriceKey, Flt64>> entries)
        {
            foreach (var (key, price) in entries)
            {
                var r = Register(key, price);
                if (r.IsFailed)
                {
                    return r;
                }
            }
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        /// <summary>
        /// 清空 / Clear all prices (called on CG re-solve).
        /// </summary>
        public void Clear() => _prices.Clear();
    }
}
