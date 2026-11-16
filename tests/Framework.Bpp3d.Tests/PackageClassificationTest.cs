// 包装分类测试 / Tests for the package classification.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests;

/// <summary>
/// 包装分类 / Package classification.
/// </summary>
public sealed class PackageClassificationTest {
    /// <summary>有子包装即为外包装 / Sub-packages present means outer.</summary>
    [Fact]
    public void Of_ShouldReturnOuterWhenSubPackagesExist() =>
        PackageClassificationRules.Of(true).Should().Be(PackageClassification.Outer);

    /// <summary>无子包装即为内包装 / No sub-package means inner.</summary>
    [Fact]
    public void Of_ShouldReturnInnerWhenNoSubPackage() =>
        PackageClassificationRules.Of(false).Should().Be(PackageClassification.Inner);

    /// <summary>
    /// **null 与空列表同等对待**：`isNullOrEmpty`，若把 null 当"未知"再单独分支，会多出一条与空列表不一致的路径。
 /// **null and empty are treated identically**: this implementation uses `isNullOrEmpty`, and a separate null branch would create
    /// a path inconsistent with the empty case.
    /// </summary>
    [Fact]
    public void OfCount_ShouldTreatNullAndEmptyAlike() {
        PackageClassificationRules.OfCount(null).Should().Be(PackageClassification.Inner);
        PackageClassificationRules.OfCount(0).Should().Be(PackageClassification.Inner);
    }

    /// <summary>非零子包装数量为外包装 / A positive sub-package count means outer.</summary>
    [Fact]
    public void OfCount_ShouldReturnOuterForPositiveCount() =>
        PackageClassificationRules.OfCount(3).Should().Be(PackageClassification.Outer);

    /// <summary>负数子包装数量视为无子包装 / A negative count counts as none.</summary>
    [Fact]
    public void OfCount_ShouldTreatNegativeAsNone() =>
        PackageClassificationRules.OfCount(-1).Should().Be(PackageClassification.Inner);

    /// <summary>外包装判定 / The outer check.</summary>
    [Fact]
    public void IsOuter_ShouldDistinguish() {
        PackageClassificationRules.IsOuter(PackageClassification.Outer).Should().BeTrue();
        PackageClassificationRules.IsOuter(PackageClassification.Inner).Should().BeFalse();
    }

    /// <summary>
    /// **枚举顺序（Outer 在前）**：若有代码按整数值持久化分类，顺序变化会让历史数据被读成相反语义。
 /// **The enum order matches the implementation (Outer first)**: if any code persists the classification by integer value, a
    /// reordering would make historical data read with the opposite meaning.
    /// </summary>
    [Fact]
    public void Enum_ShouldKeepBaselineOrder() {
        ((int)PackageClassification.Outer).Should().Be(0);
        ((int)PackageClassification.Inner).Should().Be(1);
    }
}
