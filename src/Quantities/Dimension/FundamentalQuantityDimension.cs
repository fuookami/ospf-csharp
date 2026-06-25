#nullable enable

using System;

namespace Fuookami.Ospf.Quantities.Dimension
{
    /// <summary>
    /// 基础量纲接口 / Fundamental quantity dimension interface.
    /// </summary>
    public interface IFundamentalQuantityDimension
    {
        /// <summary>量纲符号 / Dimension symbol.</summary>
        string Symbol { get; }

        /// <summary>量纲名称 / Dimension name.</summary>
        string DimensionName { get; }
    }

    /// <summary>
    /// 标准基础量纲枚举（10个SI量纲）/ Standard fundamental quantity dimension enum (10 SI dimensions).
    /// </summary>
    public enum StandardFundamentalQuantityDimension
    {
        /// <summary>长度 / Length.</summary>
        Length,
        /// <summary>质量 / Mass.</summary>
        Mass,
        /// <summary>时间 / Time.</summary>
        Time,
        /// <summary>电流 / Electric current.</summary>
        Current,
        /// <summary>温度 / Temperature.</summary>
        Temperature,
        /// <summary>物质的量 / Amount of substance.</summary>
        SubstanceAmount,
        /// <summary>发光强度 / Luminous intensity.</summary>
        LuminousIntensity,
        /// <summary>平面角 / Plane angle.</summary>
        PlaneAngle,
        /// <summary>立体角 / Solid angle.</summary>
        SolidAngle,
        /// <summary>信息 / Information.</summary>
        Information,
    }

    /// <summary>
    /// StandardFundamentalQuantityDimension 的 IFundamentalQuantityDimension 实现扩展.
    /// Extension implementing IFundamentalQuantityDimension for the enum.
    /// </summary>
    public static class StandardFundamentalQuantityDimensionExtensions
    {
        private static readonly string[] Symbols = { "L", "M", "T", "I", "Θ", "N", "J", "rad", "sr", "B" };
        private static readonly string[] Names = { "length", "mass", "time", "current", "temperature", "amount of substance", "luminous intensity", "plane angle", "solid angle", "information" };

        /// <summary>获取量纲符号 / Get dimension symbol.</summary>
        public static string GetSymbol(this StandardFundamentalQuantityDimension dim) => Symbols[(int)dim];

        /// <summary>获取量纲名称 / Get dimension name.</summary>
        public static string GetDimensionName(this StandardFundamentalQuantityDimension dim) => Names[(int)dim];
    }

    /// <summary>
    /// 标准基础量纲适配器 / Standard fundamental dimension adapter.
    /// Wraps the enum to implement IFundamentalQuantityDimension.
    /// </summary>
    public sealed record StandardDimension(StandardFundamentalQuantityDimension Value) : IFundamentalQuantityDimension
    {
        /// <inheritdoc/>
        public string Symbol => Value.GetSymbol();

        /// <inheritdoc/>
        public string DimensionName => Value.GetDimensionName();

        /// <inheritdoc/>
        public override string ToString() => Symbol;
    }

    /// <summary>
    /// 自定义基础量纲 / Custom fundamental quantity dimension.
    /// </summary>
    public sealed record CustomFundamentalQuantityDimension : IFundamentalQuantityDimension
    {
        public string Symbol { get; }
        public string DimensionName { get; }

        public CustomFundamentalQuantityDimension(string symbol, string dimensionName)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be blank.", nameof(symbol));
            if (string.IsNullOrWhiteSpace(dimensionName))
                throw new ArgumentException("Name cannot be blank.", nameof(dimensionName));
            Symbol = symbol;
            DimensionName = dimensionName;
        }

        /// <inheritdoc/>
        public override string ToString() => Symbol;
    }

    /// <summary>
    /// 标准量纲实例（顶层别名）/ Standard dimension instances (top-level aliases).
    /// </summary>
    public static class Dims
    {
        /// <summary>长度量纲 / Length dimension.</summary>
        public static readonly IFundamentalQuantityDimension L = new StandardDimension(StandardFundamentalQuantityDimension.Length);
        /// <summary>质量量纲 / Mass dimension.</summary>
        public static readonly IFundamentalQuantityDimension M = new StandardDimension(StandardFundamentalQuantityDimension.Mass);
        /// <summary>时间量纲 / Time dimension.</summary>
        public static readonly IFundamentalQuantityDimension T = new StandardDimension(StandardFundamentalQuantityDimension.Time);
        /// <summary>电流量纲 / Current dimension.</summary>
        public static readonly IFundamentalQuantityDimension I = new StandardDimension(StandardFundamentalQuantityDimension.Current);
        /// <summary>温度量纲 / Temperature dimension.</summary>
        public static readonly IFundamentalQuantityDimension Theta = new StandardDimension(StandardFundamentalQuantityDimension.Temperature);
        /// <summary>物质的量量纲 / Amount of substance dimension.</summary>
        public static readonly IFundamentalQuantityDimension N = new StandardDimension(StandardFundamentalQuantityDimension.SubstanceAmount);
        /// <summary>发光强度量纲 / Luminous intensity dimension.</summary>
        public static readonly IFundamentalQuantityDimension J = new StandardDimension(StandardFundamentalQuantityDimension.LuminousIntensity);
        /// <summary>平面角量纲 / Plane angle dimension.</summary>
        public static readonly IFundamentalQuantityDimension rad = new StandardDimension(StandardFundamentalQuantityDimension.PlaneAngle);
        /// <summary>立体角量纲 / Solid angle dimension.</summary>
        public static readonly IFundamentalQuantityDimension sr = new StandardDimension(StandardFundamentalQuantityDimension.SolidAngle);
        /// <summary>信息量纲 / Information dimension.</summary>
        public static readonly IFundamentalQuantityDimension B = new StandardDimension(StandardFundamentalQuantityDimension.Information);

        /// <summary>创建自定义基础量纲 / Create a custom fundamental dimension.</summary>
        public static IFundamentalQuantityDimension CustomDimension(string symbol, string name) =>
            new CustomFundamentalQuantityDimension(symbol, name);
    }
}
