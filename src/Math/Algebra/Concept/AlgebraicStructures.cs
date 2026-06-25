#nullable enable

using Fuookami.Ospf.Math.Operator;

namespace Fuookami.Ospf.Math.Algebra.Concept;
// ===== Additive algebraic structures =====

/// <summary>
/// 半群接口 / Semigroup interface
/// </summary>
public interface ISemigroup<TSelf> : IPlus<TSelf, TSelf>
    where TSelf : ISemigroup<TSelf> {
}

/// <summary>
/// 幺半群接口 / Monoid interface
/// </summary>
public interface IMonoid<TSelf> : ISemigroup<TSelf>, IInc<TSelf>
    where TSelf : IMonoid<TSelf> {
}

/// <summary>
/// 群接口 / Group interface
/// </summary>
public interface IGroup<TSelf> : IMonoid<TSelf>, INeg<TSelf>, IMinus<TSelf, TSelf>, IDec<TSelf>
    where TSelf : IGroup<TSelf> {
}

/// <summary>
/// 阿贝尔群接口（交换律）/ Abelian group interface (commutativity)
/// </summary>
public interface IAbelianGroup<TSelf> : IGroup<TSelf>
    where TSelf : IAbelianGroup<TSelf> {
}

// ===== Multiplicative algebraic structures =====

/// <summary>
/// 乘法半群接口 / Multiplicative semigroup interface
/// </summary>
public interface IMultiplicativeSemigroup<TSelf> : ITimes<TSelf, TSelf>
    where TSelf : IMultiplicativeSemigroup<TSelf> {
}

/// <summary>
/// 乘法幺半群接口 / Multiplicative monoid interface
/// </summary>
public interface IMultiplicativeMonoid<TSelf> : IMultiplicativeSemigroup<TSelf>
    where TSelf : IMultiplicativeMonoid<TSelf> {
}

/// <summary>
/// 乘法群接口 / Multiplicative group interface
/// </summary>
public interface IMultiplicativeGroup<TSelf> : IMultiplicativeMonoid<TSelf>,
    IReciprocal<TSelf>, IDiv<TSelf, TSelf>, IIntDiv<TSelf, TSelf>, IRem<TSelf, TSelf>
    where TSelf : IMultiplicativeGroup<TSelf> {
}

// ===== Ring and field structures =====

/// <summary>
/// 环接口 / Ring interface
/// </summary>
public interface IRing<TSelf> : IAbelianGroup<TSelf>, IMultiplicativeSemigroup<TSelf>
    where TSelf : IRing<TSelf> {
}

/// <summary>
/// 交换环接口 / Commutative ring interface
/// </summary>
public interface ICommutativeRing<TSelf> : IRing<TSelf>
    where TSelf : ICommutativeRing<TSelf> {
}

/// <summary>
/// 域接口 / Field interface
/// </summary>
public interface IField<TSelf> : ICommutativeRing<TSelf>, IMultiplicativeGroup<TSelf>
    where TSelf : IField<TSelf> {
}

// ===== Composite convenience interfaces =====

/// <summary>
/// 加法半群复合接口 / Plus semigroup composite interface
/// </summary>
public interface IPlusSemigroup<TSelf> : ISemigroup<TSelf>, IPlus<TSelf, TSelf>, IInc<TSelf>
    where TSelf : IPlusSemigroup<TSelf> {
}

/// <summary>
/// 加法群复合接口 / Plus group composite interface
/// </summary>
public interface IPlusGroup<TSelf> : IAbelianGroup<TSelf>,
    INeg<TSelf>, IMinus<TSelf, TSelf>, IDec<TSelf>
    where TSelf : IPlusGroup<TSelf> {
}

/// <summary>
/// 乘法半群复合接口 / Times semigroup composite interface
/// </summary>
public interface ITimesSemigroup<TSelf> : IMultiplicativeSemigroup<TSelf>, ITimes<TSelf, TSelf>
    where TSelf : ITimesSemigroup<TSelf> {
}

/// <summary>
/// 乘法群复合接口 / Times group composite interface
/// </summary>
public interface ITimesGroup<TSelf> : IMultiplicativeGroup<TSelf>,
    IReciprocal<TSelf>, IDiv<TSelf, TSelf>, IIntDiv<TSelf, TSelf>, IRem<TSelf, TSelf>
    where TSelf : ITimesGroup<TSelf> {
}
