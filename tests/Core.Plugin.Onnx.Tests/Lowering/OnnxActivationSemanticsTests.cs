#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **激活算子的取值语义与属性默认值** / Activation semantics and attribute defaults.
/// </summary>
/// <remarks>
/// 。**判据钉三件：**
/// **① 规范写死的默认值（漏掉等于把负半轴压成零）；② 边界处的行为（`x = 0` 归属哪一边）；**
/// **③ 数值稳定性（`Softplus` 在大输入上不能溢出成无穷）。**
///
/// Syncs. **Three things are pinned: the defaults the specification fixes (dropping one flattens the negative half to zero), behaviour at the
/// boundary (which side `x = 0` belongs to), and numerical stability (Softplus must not overflow at large inputs).**
/// </remarks>
public class OnnxActivationSemanticsTests
{
    /// <summary>**规范默认值：LeakyRelu 0.01、HardSigmoid 0.2/0.5、Elu 1.0** / The specification defaults.</summary>
    /// <remarks>
    /// **漏掉默认值会让"没写属性"的节点变成 `alpha = 0`：** **负半轴整段压成零，而输出看起来就像模型自己学出来的东西。**
    ///
    /// **Dropping a default turns a node without attributes into `alpha = 0`: the whole negative half is flattened, and the output looks like something the model
    /// learned.**
    /// </remarks>
    [Fact]
    public void TheDefaults_ShouldMatchTheSpecification() {
        OnnxActivationSemantics.CheckAttributes("LeakyRelu").Alpha.Should().Be(0.01, "LeakyRelu defaults to 0.01, not to zero");
        OnnxActivationSemantics.CheckAttributes("HardSigmoid").Alpha.Should().Be(0.2);
        OnnxActivationSemantics.CheckAttributes("HardSigmoid").Beta.Should().Be(0.5);
        OnnxActivationSemantics.CheckAttributes("Elu").Alpha.Should().Be(1.0);
        OnnxActivationSemantics.CheckAttributes("Clip").Failure.Should().BeNull("no bounds means nothing to validate");
    }

    /// <summary>**负的 alpha 被拒绝，而不是取绝对值** / A negative alpha is refused, not made absolute.</summary>
    /// <remarks>
    /// **取绝对值"帮"调用方修好一个它没意识到的问题，** **而两边的行为完全不同：** **负 `alpha` 让 `LeakyRelu` 在负半轴反向，**
    /// **取绝对值后就成了另一个算子。**
    ///
    /// **Taking the absolute value "fixes" a problem the caller did not know about, and the two behave differently: a negative alpha reverses LeakyRelu on the
    /// negative half, while its absolute value is another operator entirely.**
    /// </remarks>
    [Fact]
    public void ANegativeAlpha_ShouldBeRefused() {
        OnnxActivationSemantics.CheckAttributes("LeakyRelu", alpha: -0.5).Failure.Should().NotBeNull();
        OnnxActivationSemantics.CheckAttributes("LeakyRelu", alpha: 0.0).Failure.Should().BeNull("zero alpha is legal; it is the default that must not be invented");
        OnnxActivationSemantics.CheckAttributes("LeakyRelu", alpha: 1.0).Alpha.Should().Be(1.0, "alpha = 1 makes the operator the identity and it is still legal");
    }

    /// <summary>**`x = 0` 归正半轴，两侧取值一致** / At x = 0 the two branches agree.</summary>
    /// <remarks>
    /// **零点归属在实现里是 `>=` 还是 `>` 的差别，而两者在 `x = 0` 处给出同一个值**——**这正是判据要确认的：**
    /// **若某天有人把 `LeakyRelu` 写成 `x > 0 ? x : alpha·x`，零点处就会突然掉到 `alpha·0 = 0`，仍然是 0，**
    /// **但 `Clip` 与 `HardSigmoid` 的边界归属就没这么幸运了。**
    ///
    /// **Which side zero belongs to is a `>=` versus `>` difference, and both give the same value at zero — which is exactly what this confirms. A rewrite to
    /// `x > 0 ? x : alpha·x` would still yield 0 at zero, but the boundary handling of Clip and HardSigmoid is not so forgiving.**
    /// </remarks>
    [Fact]
    public void TheZeroPoint_ShouldBeConsistentOnBothSides() {
        OnnxActivationSemantics.Relu(0.0).Should().Be(0.0);
        OnnxActivationSemantics.LeakyRelu(0.0, 0.1).Should().Be(0.0);
        OnnxActivationSemantics.Elu(0.0, 1.0).Should().Be(0.0);
        OnnxActivationSemantics.Clip(0.0, -1.0, 1.0).Should().Be(0.0);
    }

    /// <summary>**`Clip` 两侧都缺省时是恒等映射** / Clip with neither bound is the identity.</summary>
    [Theory]
    [InlineData(-5.0)]
    [InlineData(0.0)]
    [InlineData(5.0)]
    public void ClipWithoutBounds_ShouldBeTheIdentity(double x) =>
        OnnxActivationSemantics.Clip(x, null, null).Should().Be(x, "with no bounds Clip changes nothing");

    /// <summary>**`Clip` 只有一个界时只截一边** / Clip with one bound clamps only that side.</summary>
    [Fact]
    public void ClipWithOneBound_ShouldClampOnlyThatSide() {
        OnnxActivationSemantics.Clip(10.0, null, 3.0).Should().Be(3.0);
        OnnxActivationSemantics.Clip(-10.0, null, 3.0).Should().Be(-10.0, "no lower bound means the negative side is untouched");
        OnnxActivationSemantics.Clip(-10.0, -3.0, null).Should().Be(-3.0);
        OnnxActivationSemantics.Clip(10.0, -3.0, null).Should().Be(10.0);
    }

    /// <summary>**`Clip` 下界大于上界被拒绝** / An inverted Clip range is refused.</summary>
    [Fact]
    public void AnInvertedClipRange_ShouldBeRefused() {
        OnnxActivationSemantics.CheckAttributes("Clip", lower: 5.0, upper: 1.0).Failure.Should().NotBeNull();
        OnnxActivationSemantics.CheckAttributes("Clip", lower: 1.0, upper: 5.0).Failure.Should().BeNull();
    }

    /// <summary>**`HardSigmoid` 的输出钳在 [0, 1]** / HardSigmoid stays inside [0, 1].</summary>
    [Fact]
    public void HardSigmoid_ShouldStayWithinTheUnitInterval() {
        OnnxActivationSemantics.HardSigmoid(100.0, 0.2, 0.5).Should().Be(1.0);
        OnnxActivationSemantics.HardSigmoid(-100.0, 0.2, 0.5).Should().Be(0.0);
        OnnxActivationSemantics.HardSigmoid(0.0, 0.2, 0.5).Should().Be(0.5, "the offset alone survives inside the interval");
    }

    /// <summary>**`Softplus` 在大输入上不溢出** / Softplus does not overflow at large inputs.</summary>
    /// <remarks>
    /// **直接写 `ln(1 + e^x)` 在 `x = 1000` 时就会得到无穷：** **`e^710` 已经是 `double` 的上限。** **而激活输入常常超过这个数。**
    /// **判据钉的就是"不变成无穷"，以及大输入下 `Softplus(x) ≈ x`——**这是这个等价形式的定义性行为。
    ///
    /// **Writing `ln(1 + e^x)` directly yields infinity at `x = 1000`, since `e^710` is already the `double` limit, and activation inputs often exceed it.**
    /// **What is pinned is "it stays finite" and that `Softplus(x) ≈ x` for large inputs — the defining behaviour of the stable form.**
    /// </remarks>
    [Fact]
    public void Softplus_ShouldStayFinite() {
        double.IsFinite(OnnxActivationSemantics.Softplus(1000.0)).Should().BeTrue("e^1000 overflows, so the naive form would return infinity");
        OnnxActivationSemantics.Softplus(1000.0).Should().BeApproximately(1000.0, 1e-9, "for large x the softplus is essentially x");
        OnnxActivationSemantics.Softplus(-1000.0).Should().BeApproximately(0.0, 1e-9);
        OnnxActivationSemantics.Softplus(0.0).Should().BeApproximately(System.Math.Log(2.0), 1e-12, "at zero it is ln 2");
    }

    /// <summary>**`Elu` 在负半轴趋近 `-alpha`，不会跌破** / Elu approaches -alpha from above.</summary>
    [Fact]
    public void Elu_ShouldApproachNegativeAlpha() {
        OnnxActivationSemantics.Elu(-1000.0, 1.0).Should().BeApproximately(-1.0, 1e-12);

        // 第一版在这里断言"趋近但不达到 -alpha"，而 x = -1000 时 e^x 已下溢成 0，结果恰好等于 -1.0。
        // 判据写错了，不是实现写错了：趋近而非命中要在一个还留有位差的地方看。
        //
        // The first version asserted "approaches -alpha without reaching it" here, but e^-1000 underflows to zero so the value is exactly -1.0.
        // The criterion was wrong, not the implementation: look for the approach where a gap still exists.
        OnnxActivationSemantics.Elu(-10.0, 1.0).Should().BeGreaterThan(-1.0, "at x = -10 the gap to -alpha is still visible");
        OnnxActivationSemantics.Elu(-10.0, 1.0).Should().BeLessThan(0.0);
        OnnxActivationSemantics.Elu(5.0, 1.0).Should().Be(5.0, "the positive half is the identity");
    }

    /// <summary>**未知算子被拒绝，不猜成恒等** / An unknown operator is refused, not silently the identity.</summary>
    [Fact]
    public void AnUnknownOperator_ShouldBeRefused() {
        OnnxActivationSemantics.CheckAttributes("Gelu").Failure.Should().NotBeNull(
            "Gelu is not in this plugin's set; treating it as the identity would return the input as the activation output");
    }
}