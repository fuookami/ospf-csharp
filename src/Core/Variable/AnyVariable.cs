#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Variable
{
    /// <summary>
    /// 类型擦除的变量包装器 / Type-erased variable wrapper
    /// </summary>
    /// <typeparam name="V">数值类型 / The number type</typeparam>
    public sealed class AnyVariable<V> where V : struct, IRealNumber<V>
    {
        /// <summary>原始变量数据 / Original variable data</summary>
        public IVariableItem Data { get; }

        /// <summary>键 / Key</summary>
        public VariableItemKey Id => Data.Key;
        /// <summary>索引 / Index</summary>
        public int Index => Data.Index;
        /// <summary>名称 / Name</summary>
        public string Name => Data.Name;
        /// <summary>显示名称 / Display name</summary>
        public string? DisplayName => Data.DisplayName;
        /// <summary>变量类型 / Variable type</summary>
        public IVariableTypeKind VarType => Data.TypeKind;
        /// <summary>下界（Flt64 视图）/ Lower bound (Flt64 view)</summary>
        public Flt64? LowerBoundFlt64 => Data.LowerBound?.Value?.ToFlt64();
        /// <summary>上界（Flt64 视图）/ Upper bound (Flt64 view)</summary>
        public Flt64? UpperBoundFlt64 => Data.UpperBound?.Value?.ToFlt64();

        public AnyVariable(IVariableItem data) => Data = data;

        /// <summary>检查 Flt64 值是否在边界内 / Check whether a Flt64 value is within bounds</summary>
        public bool IsValidValue(Flt64 value)
        {
            var lb = LowerBoundFlt64;
            var ub = UpperBoundFlt64;
            if (lb is not null && value < lb) return false;
            if (ub is not null && value > ub) return false;
            return true;
        }

        /// <summary>检查 V 值是否在边界内 / Check whether a V value is within bounds</summary>
        public bool IsValidValue(V value) => IsValidValue(value.ToFlt64());

        /// <summary>从变量项创建 / Create from variable item</summary>
        public static AnyVariable<V> From(IVariableItem item) => new(item);

        public override int GetHashCode() => Data.GetHashCode();
        public override bool Equals(object? obj) =>
            this == obj || (obj is AnyVariable<V> other && Data.Equals(other.Data));
        public override string? ToString() => Data.ToString();
    }
}
