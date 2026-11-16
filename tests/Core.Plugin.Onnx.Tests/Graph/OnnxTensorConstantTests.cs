#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Graph;

/// <summary>
/// **常量张量：形状、载荷与读取** / Constant tensors: shape, payload and reads.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxTensorConstant.kt`。判据钉的是**几处不抛异常却会读错模型**的地方：
/// 标量的元素个数、载荷与形状不一致、整数转浮点的精度边界。
///
/// Syncs `OnnxTensorConstant.kt`. The criteria pin the places where a model is read wrongly without throwing: a scalar's element count, a payload that
/// disagrees with its shape, and the precision boundary of integer-to-float conversion.
/// </remarks>
public class OnnxTensorConstantTests
{
    /// <summary>**空 shape 是标量，元素个数为 1** / An empty shape is a scalar with one element.</summary>
    [Fact]
    public void AScalar_ShouldHoldOneElement() {
        var scalar = new OnnxTensorConstant("w", OnnxDataType.Float32, System.Array.Empty<int>(), new[] { 3.5 }, System.Array.Empty<long>());

        scalar.ElementCount.Should().Be(1, "zero-dimensional means exactly one element; treating the fold seed as the answer would say zero");
        scalar.DoubleAt(0).Should().Be(3.5, "and that one element is readable");
    }

    /// <summary>**多维形状的元素个数是各维乘积** / A multi-dimensional shape multiplies its dimensions.</summary>
    [Fact]
    public void AShapedTensor_ShouldMultiplyItsDimensions() {
        var tensor = new OnnxTensorConstant(
            "img",
            OnnxDataType.Float32,
            new[] { 1, 3, 2, 2 },
            new double[12],
            System.Array.Empty<long>());

        tensor.ElementCount.Should().Be(12);
        tensor.PayloadMatchesShape(out string? detail).Should().BeTrue();
        detail.Should().BeNull("a matching payload needs no explanation");
    }

    /// <summary>**载荷长度与形状不一致必须报出来** / A payload that disagrees with the shape must be reported.</summary>
    /// <remarks>
    /// **长了比短了更危险：** **短了会在最后一个元素上抛越界；** **长了会静默忽略尾部，而多出来的数是模型的一部分——**
    /// **忽略它等于读错模型而不曾说。**
    ///
    /// **Too long is worse than too short: short throws on the last element, while long silently drops the tail, and those numbers belong to the model —
    /// ignoring them means reading it wrongly without saying so.**
    /// </remarks>
    [Fact]
    public void AMismatchedPayload_ShouldSayWhy() {
        var shortTensor = new OnnxTensorConstant("t", OnnxDataType.Float32, new[] { 2, 3 }, new double[5], System.Array.Empty<long>());
        var longTensor = new OnnxTensorConstant("t", OnnxDataType.Float32, new[] { 2, 3 }, new double[7], System.Array.Empty<long>());

        shortTensor.PayloadMatchesShape(out string? shortDetail).Should().BeFalse("five values cannot fill six slots");
        shortDetail.Should().Contain("5").And.Contain("6", "both sides have to appear, or the reader cannot tell which is which");

        longTensor.PayloadMatchesShape(out string? longDetail).Should().BeFalse("seven values do not fit six slots either");
        longDetail.Should().Contain("7").And.Contain("6");
    }

    /// <summary>**整数载荷按 dtype 读，不按长度猜** / The payload is chosen by dtype, not guessed from length.</summary>
    /// <remarks>
    /// **一个同时带两种载荷的张量在真实模型里并不罕见**（转换工具会写两类字段）；**dtype 是模型自己的声明，是这个函数的唯一依据。**
    ///
    /// **A tensor carrying both payloads is not unusual in real models** (converters write both fields); **dtype is the model's own declaration and the only
    /// thing this method may look at.**
    /// </remarks>
    [Fact]
    public void ThePayload_ShouldBeChosenByDType() {
        var integers = new OnnxTensorConstant("idx", OnnxDataType.Int64, new[] { 3 }, new[] { 99.0, 98.0, 97.0 }, new long[] { 7, 8, 9 });

        integers.DoubleAt(0).Should().Be(7.0, "Int64 reads the integer payload even though a floating one is also present");
        integers.LongAt(2).Should().Be(9);

        var floats = new OnnxTensorConstant("w", OnnxDataType.Float32, new[] { 3 }, new[] { 1.5, 2.5, 3.5 }, new long[] { 7, 8, 9 });
        floats.LongAt(1).Should().Be(2L, "a floating tensor truncates on the integer read path");
    }

    /// <summary>**超过 2^53 的整数转浮点会丢精度，判据把这个边界写下来** / Integer-to-float loses precision past 2^53 and the criterion records it.</summary>
    /// <remarks>
    /// **这不是 bug，是 IEEE-754 的事实**——**但它是"读到的数看着正常、其实已经变了"的那一类。** 判据把它写成显式的期待，
    /// 这样将来若有人以为 `doubleAt` 是精确的，会在这里看到反例。
    ///
    /// **This is not a bug but an IEEE-754 fact** — **and it belongs to the class of "the number looks normal while having changed".** The criterion states it
    /// explicitly so that anyone assuming `DoubleAt` is exact meets the counterexample here.
    /// </remarks>
    [Fact]
    public void ALargeInteger_ShouldLosePrecisionWhenReadAsFloating() {
        long big = (1L << 53) + 1;
        var tensor = new OnnxTensorConstant("big", OnnxDataType.Int64, new[] { 1 }, System.Array.Empty<double>(), new[] { big });

        tensor.LongAt(0).Should().Be(big, "the integer path is exact");

        // 第一版写的是 `DoubleAt(0).Should().NotBe(big)`，而 FluentAssertions 走 double 重载时会把 `big` 隐式转成 double,
        // 两边于是都成了 2^53——**判据自己把要证明的那个区别抹掉了。** 改成转回整数比较。
        //
        // The first version wrote `DoubleAt(0).Should().NotBe(big)`, and FluentAssertions' double overload implicitly converts `big`, making both sides 2^53 —
        // **the criterion erased the very distinction it was proving.** Comparing back in the integer domain fixes it.
        ((long)tensor.DoubleAt(0)).Should().Be(
            big - 1,
            "2^53 + 1 rounds down to 2^53, so casting the floating result back yields one less than the original integer");
    }
}