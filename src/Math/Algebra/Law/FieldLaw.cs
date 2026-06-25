#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Algebra.Law
{
    /// <summary>
    /// 域定律验证器 / Field law validator.
    /// 继承 RingLaw 的环验证，增加乘法交换律、乘法逆元。
    /// Inherits RingLaw for ring verification, adds multiplicative commutativity and multiplicative inverse.
    /// </summary>
    /// <typeparam name="TSelf">域元素类型 / Field element type.</typeparam>
    public sealed class FieldLaw<TSelf>
    {
        private readonly RingLaw<TSelf> _ring;
        private readonly IReadOnlyCollection<TSelf> _samples;
        private readonly Func<TSelf, TSelf, TSelf> _mul;
        private readonly TSelf _one;
        private readonly Func<TSelf, TSelf> _reciprocal;
        private readonly Func<TSelf, bool> _isZero;
        private readonly Func<TSelf, TSelf, bool> _equal;

        public FieldLaw(
            IReadOnlyCollection<TSelf> samples,
            Func<TSelf, TSelf, TSelf> add,
            Func<TSelf, TSelf, TSelf> mul,
            TSelf zero,
            TSelf one,
            Func<TSelf, TSelf> negate,
            Func<TSelf, TSelf> reciprocal,
            Func<TSelf, bool> isZero,
            Func<TSelf, TSelf, bool> equal)
        {
            _samples = samples;
            _mul = mul;
            _one = one;
            _reciprocal = reciprocal;
            _isZero = isZero;
            _equal = equal;
            _ring = new RingLaw<TSelf>(samples, add, mul, zero, one, negate, equal);
        }

        /// <summary>验证乘法交换律 / Verify multiplicative commutativity.</summary>
        public bool CheckMultiplicativeCommutative()
        {
            foreach (var lhs in _samples)
                foreach (var rhs in _samples)
                    if (!_equal(_mul(lhs, rhs), _mul(rhs, lhs))) return false;
            return true;
        }

        /// <summary>验证乘法逆元存在性 / Verify multiplicative inverse existence.</summary>
        public bool CheckMultiplicativeInverse()
        {
            foreach (var v in _samples)
            {
                if (_isZero(v)) continue;
                var inv = _reciprocal(v);
                if (!_equal(_mul(v, inv), _one)) return false;
                if (!_equal(_mul(inv, v), _one)) return false;
            }
            return true;
        }

        /// <summary>验证所有域定律（含环定律）/ Validate all field laws (including ring laws).</summary>
        public bool Validate() => _ring.Validate() && CheckMultiplicativeCommutative() && CheckMultiplicativeInverse();
    }
}
