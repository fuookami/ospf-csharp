#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Algebra.Law
{
    /// <summary>
    /// 群定律验证器 / Group law validator.
    /// 通过采样元素验证结合律、单位元、逆元。
    /// Validates group axioms via sampled elements: associativity, identity, and inverse.
    /// </summary>
    /// <typeparam name="TSelf">群元素类型 / Group element type.</typeparam>
    public sealed class GroupLaw<TSelf>
    {
        private readonly IReadOnlyCollection<TSelf> _samples;
        private readonly Func<TSelf, TSelf, TSelf> _add;
        private readonly TSelf _zero;
        private readonly Func<TSelf, TSelf> _negate;
        private readonly Func<TSelf, TSelf, bool> _equal;

        public GroupLaw(
            IReadOnlyCollection<TSelf> samples,
            Func<TSelf, TSelf, TSelf> add,
            TSelf zero,
            Func<TSelf, TSelf> negate,
            Func<TSelf, TSelf, bool> equal)
        {
            _samples = samples;
            _add = add;
            _zero = zero;
            _negate = negate;
            _equal = equal;
        }

        /// <summary>验证结合律 / Verify associativity.</summary>
        public bool CheckAssociative()
        {
            foreach (var lhs in _samples)
                foreach (var mid in _samples)
                    foreach (var rhs in _samples)
                        if (!_equal(_add(_add(lhs, mid), rhs), _add(lhs, _add(mid, rhs)))) return false;
            return true;
        }

        /// <summary>验证单位元存在性 / Verify identity element existence.</summary>
        public bool CheckIdentity()
        {
            foreach (var v in _samples)
            {
                if (!_equal(_add(v, _zero), v)) return false;
                if (!_equal(_add(_zero, v), v)) return false;
            }
            return true;
        }

        /// <summary>验证逆元存在性 / Verify inverse element existence.</summary>
        public bool CheckInverse()
        {
            foreach (var v in _samples)
            {
                var inv = _negate(v);
                if (!_equal(_add(v, inv), _zero)) return false;
                if (!_equal(_add(inv, v), _zero)) return false;
            }
            return true;
        }

        /// <summary>验证所有群定律 / Validate all group laws.</summary>
        public bool Validate() => CheckAssociative() && CheckIdentity() && CheckInverse();
    }
}
