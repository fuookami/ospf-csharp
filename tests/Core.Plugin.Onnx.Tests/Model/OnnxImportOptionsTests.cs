#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Model;

/// <summary>
/// **导入选项是"能不能表达"的边界，不是调参** / Import options bound what is expressible, they do not tune.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxImportOptions`。**每一条选项错了的表现都是"模型跑不通"或更糟——"跑通了但是另一个模型"。**
///
/// Syncs `OnnxImportOptions`. **Every wrong option shows up as "the model does not run" or worse, "it runs and it is a different model".**
/// </remarks>
public class OnnxImportOptionsTests
{
    /// <summary>**倒挂或非有限的区间在导入时被拒绝** / An inverted or non-finite range is refused at import.</summary>
    /// <remarks>
    /// **留给求解器的后果是误导性的：** **求解器拿到 `[5, 1]` 会报"不可行"，** **而真实原因在选项里，离报错点隔着一整个模型。**
    ///
    /// **Leaving it to the solver is misleading: a solver handed `[5, 1]` reports "infeasible" while the real cause sits in the options, a whole model away from
    /// where the error appears.**
    /// </remarks>
    [Fact]
    public void AnInvertedRange_ShouldBeRefused() {
        var options = new OnnxImportOptions(inputBounds: new Dictionary<string, OnnxBounds> {
            ["x"] = new(5.0, 1.0),
        });

        options.Validate(out string? failure).Should().BeFalse("a model cannot be built over an empty range");
        failure.Should().Contain("x", "the failing input has to be named, or the reader has to search every binding");

        new OnnxBounds(double.NaN, 1.0).Validate(out string? nan).Should().BeFalse();
        nan.Should().NotBeNull();
        new OnnxBounds(0.0, double.PositiveInfinity).Validate(out string? infinity).Should().BeFalse();
        infinity.Should().NotBeNull();
        new OnnxBounds(0.0, 0.0).Validate(out _).Should().BeTrue("a degenerate but finite range is legal, if useless");
    }

    /// <summary>**非正维度的输入形状被拒绝** / A non-positive input shape dimension is refused.</summary>
    /// <remarks>
    /// **0 或负维度的形状会让元素数为 0，** **于是下游每个张量都"合法但为空"，** **而空张量上的约束一条也不会加进去——**
    /// **模型安静地变成无约束问题。**
    ///
    /// **A zero or negative dimension makes the element count zero, so every downstream tensor is "legal but empty", no constraint survives onto an empty tensor, and
    /// the model quietly becomes an unconstrained problem.**
    /// </remarks>
    [Fact]
    public void ANonPositiveShape_ShouldBeRefused() {
        var options = new OnnxImportOptions(inputShapes: new Dictionary<string, IReadOnlyList<long>> {
            ["x"] = new long[] { 1, 0, 3 },
        });

        options.Validate(out string? failure).Should().BeFalse();
        failure.Should().Contain("x");
        failure.Should().Contain("0");
    }

    /// <summary>**分段数区间与 opset 区间各自校验** / Segment count and opset range are validated separately.</summary>
    [Fact]
    public void TheRanges_ShouldBeValidated() {
        new OnnxImportOptions(segments: 1).Validate(out _).Should().BeFalse();
        new OnnxImportOptions(segments: 1025).Validate(out _).Should().BeFalse();
        new OnnxImportOptions(segments: 2).Validate(out _).Should().BeTrue();
        new OnnxImportOptions(segments: 1024).Validate(out _).Should().BeTrue();

        new OnnxImportOptions(minOpset: 20, maxOpset: 10).Validate(out _).Should().BeFalse("an inverted opset range matches nothing");
        new OnnxImportOptions(minOpset: 13, maxOpset: 24).Validate(out _).Should().BeTrue();
    }

    /// <summary>**opset 版本判定是闭区间** / The opset check is inclusive on both ends.</summary>
    /// <remarks>
    /// **端点必须都算支持：** **把边界写成开区间会让恰好等于 `maxOpset` 的模型被当成"太新"——** **而那是我们声明支持的最高版本，**
    /// **拒绝它等于把声明作废。**
    ///
    /// **Both endpoints have to count: an open interval makes a model at exactly `maxOpset` look "too new", while that is the highest version this plugin
    /// claims, and refusing it voids the claim.**
    /// </remarks>
    [Fact]
    public void TheOpsetCheck_ShouldIncludeBothEnds() {
        var options = new OnnxImportOptions(minOpset: 13, maxOpset: 24);

        options.SupportsOpset(13, out _).Should().BeTrue("the declared minimum is supported");
        options.SupportsOpset(24, out _).Should().BeTrue("and so is the declared maximum");
        options.SupportsOpset(12, out string? old).Should().BeFalse();
        old.Should().NotBeNull();
        options.SupportsOpset(25, out string? tooNew).Should().BeFalse();
        tooNew.Should().NotBeNull();
    }

    /// <summary>**模式决定允许的多项式阶数** / The mode fixes the allowed degree.</summary>
    /// <remarks>
    /// **阶数是导入选项里唯一"改变模型表达力"的东西：** **线性模式下一条二次约束无法表达，** **而若实现悄悄允许它，**
    /// **求解器拿到的是自己说不支持的模型。**
    ///
    /// **The degree is the one option that changes what can be expressed: a quadratic constraint is inexpressible in linear mode, and allowing it silently hands the
    /// solver a model it does not claim to support.**
    /// </remarks>
    [Fact]
    public void TheMode_ShouldFixTheDegree() {
        new OnnxImportOptions(OnnxImportMode.Linear).MaxDegree.Should().Be(1);
        new OnnxImportOptions(OnnxImportMode.Quadratic).MaxDegree.Should().Be(2);
        new OnnxImportOptions(OnnxImportMode.Cubic).MaxDegree.Should().Be(3);
    }

    /// <summary>**默认值就是基线的那一套** / The defaults are the baseline's.</summary>
    [Fact]
    public void TheDefaults_ShouldMatchTheBaseline() {
        var options = new OnnxImportOptions();

        options.Mode.Should().Be(OnnxImportMode.Linear, "the baseline defaults to linear import");
        options.Segments.Should().Be(8);
        options.MinOpset.Should().Be(13);
        options.MaxOpset.Should().Be(24);
        options.InputBounds.Should().BeEmpty();
        options.InputShapes.Should().BeEmpty();
    }

    /// <summary>**选项是不可变的：传进去的字典改了不影响已构造的选项** / The options are immutable against later mutation of the caller's dictionaries.</summary>
    /// <remarks>
    /// **共享字典会让"构造时校验通过、使用时内容变了"成为可能：** **于是校验过的东西和实际用的东西不是同一份，**
    /// **而两次检查都不会报错。**
    ///
    /// **A shared dictionary makes "validated at construction, different at use" possible: what was checked and what is used are then not the same object, and neither
    /// check reports anything.**
    /// </remarks>
    [Fact]
    public void TheOptions_ShouldCopyTheirDictionaries() {
        var bounds = new Dictionary<string, OnnxBounds> { ["x"] = new(-1.0, 1.0) };
        var options = new OnnxImportOptions(inputBounds: bounds);

        bounds["x"] = new OnnxBounds(5.0, 1.0);

        options.InputBounds["x"].Should().Be(new OnnxBounds(-1.0, 1.0), "the copy must not follow the caller's later mutation");
        options.Validate(out _).Should().BeTrue("and the validated content is still the content in use");
    }
}