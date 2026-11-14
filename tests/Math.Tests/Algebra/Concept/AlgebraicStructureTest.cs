#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Concept;

/// <summary>
/// **接口继承关系由编译器保证，这里只做记录** / The interface relations are compiler-guaranteed; this file only records them.
/// </summary>
/// <remarks>
/// **这十二条断言一条也不会独立变红。** 实测：把 `IFloatingNumber<Flt64>` 从 `Flt64` 的基接口列表里删掉，
/// **`tests/Math.Tests` 立刻产生 1734 个编译错误**——也就是说，只要代码能编译，这些断言必然为真。
///
/// **它们的价值是"清单"而不是"判据"**：读的人能一眼看到 `Flt64` 声称实现了哪些代数结构。
/// **真正的行为判据在 `FieldLawTest`（域公理 `Validate()` 走真实算术）与 `Flt64Test`（19 条数值判据）里。**
///
/// **一个永远不能失败的断言，如果被当成判据来计数，会让整张表看起来比实际更可信。**
/// **这条注释就是防止那件事。**
///
/// **None of these twelve can ever go red on its own.** Measured: removing `IFloatingNumber<Flt64>` from the base-interface list of `Flt64`
/// **produces 1734 compile errors in `tests/Math.Tests` at once** — meaning that if the code compiles, these assertions are necessarily true.
///
/// **Their value is an inventory, not a criterion**: a reader sees at a glance which algebraic structures `Flt64` claims.
/// **The real behavioural criteria live in `FieldLawTest` (field axioms via `Validate()`, which does actual arithmetic) and `Flt64Test` (19 numeric criteria).**
///
/// **An assertion that can never fail, counted as a criterion, makes the whole table look more trustworthy than it is.**
/// **This comment exists to prevent exactly that.**
/// </remarks>
public class AlgebraicStructureTest {
    [Fact]
    public void Flt64_ImplementsIFloatingNumber() =>
        // Flt64 should implement IFloatingNumber<Flt64>
        typeof(IFloatingNumber<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIRealNumber() => typeof(IRealNumber<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIScalar() => typeof(IScalar<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIField() => typeof(IField<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Int64_ImplementsIIntegerNumber() => typeof(IIntegerNumber<Int64>).IsAssignableFrom(typeof(Int64)).Should().BeTrue();

    [Fact]
    public void Int64_ImplementsIInteger() => typeof(IInteger<Int64>).IsAssignableFrom(typeof(Int64)).Should().BeTrue();

    [Fact]
    public void Int64_ImplementsIRealNumber() => typeof(IRealNumber<Int64>).IsAssignableFrom(typeof(Int64)).Should().BeTrue();

    [Fact]
    public void UInt8_ImplementsIUIntegerNumber() => typeof(IUIntegerNumber<UInt8>).IsAssignableFrom(typeof(UInt8)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIBounded() => typeof(IBounded<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIInfinite() => typeof(IInfinite<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void Flt64_ImplementsIEpsilon() => typeof(IEpsilon<Flt64>).IsAssignableFrom(typeof(Flt64)).Should().BeTrue();

    [Fact]
    public void RtnX_ImplementsIFloatingNumber() => typeof(IFloatingNumber<RtnX>).IsAssignableFrom(typeof(RtnX)).Should().BeTrue();
}
