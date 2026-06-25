#nullable enable

namespace Fuookami.Ospf.Quantities.Dimension
{
    /// <summary>
    /// 取值域枚举 / Value domain enumeration.
    /// </summary>
    public enum QuantityDomain
    {
        /// <summary>连续量 / Continuous quantity.</summary>
        Continuous,

        /// <summary>离散量 / Discrete quantity.</summary>
        Discrete,
    }

    /// <summary>
    /// 取值域合成运算 / Value domain composition operations.
    /// </summary>
    public static class QuantityDomainOps
    {
        /// <summary>乘法合成：只有离散 x 离散保持离散 / Multiplication: only discrete x discrete stays discrete.</summary>
        public static QuantityDomain Multiply(QuantityDomain lhs, QuantityDomain rhs) =>
            (lhs, rhs) == (QuantityDomain.Discrete, QuantityDomain.Discrete)
                ? QuantityDomain.Discrete
                : QuantityDomain.Continuous;

        /// <summary>除法结果恒为连续 / Division result is always continuous.</summary>
        public static QuantityDomain Divide(QuantityDomain lhs, QuantityDomain rhs) =>
            QuantityDomain.Continuous;

        /// <summary>正整数幂按乘法合成，否则连续 / Positive-int power composes by multiplication, else continuous.</summary>
        public static QuantityDomain Pow(QuantityDomain domain, int index)
        {
            if (index <= 0) return QuantityDomain.Continuous;
            if (index == 1) return domain;
            var result = domain;
            for (int i = 1; i < index; i++)
            {
                result = Multiply(result, domain);
            }
            return result;
        }
    }
}
