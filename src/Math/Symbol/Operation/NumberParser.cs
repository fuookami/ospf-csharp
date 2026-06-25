#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Parse;

namespace Fuookami.Ospf.Math.Symbol.Operation
{
    /// <summary>
    /// Flt64 数值解析器 / Flt64 number parser.
    /// 实现 INumberParser&lt;Flt64&gt;，将字符串解析为 Flt64。
    /// Implements INumberParser&lt;Flt64&gt; to parse strings into Flt64.
    /// </summary>
    public sealed class Flt64NumberParser : INumberParser<Flt64>
    {
        /// <summary>单例实例 / Singleton instance.</summary>
        public static readonly Flt64NumberParser Instance = new();

        private Flt64NumberParser() { }

        /// <inheritdoc/>
        public Flt64? Parse(string text)
        {
            if (double.TryParse(text, out var value))
                return new Flt64(value);
            return null;
        }
    }
}
