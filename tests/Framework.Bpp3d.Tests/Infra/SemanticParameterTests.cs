// 语义参数与 PWL 配置的判据 / Criteria for semantic parameters and the PWL configuration.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// **包含方向与断点有效性** / Containment direction and breakpoint validity.
/// </summary>
/// <remarks>
/// 这里修掉了一个真实的方向错误：`PackagePattern.Belong` 原本写成 `Code.StartsWith(other.Code)`，
/// 而`ano.code.startsWith(code)`——**包含关系整个颠倒**，且因为当时没有任何调用点而无人发现。
///
/// fixes a real direction bug: `PackagePattern.Belong` read `Code.StartsWith(other.Code)` while this implementation reads `ano.code.startsWith(code)` — the
/// containment was **inverted**, and nothing caught it because there were no call sites.
/// </remarks>
public class SemanticParameterTests {
    /// **短模式属于长模式，反过来不成立** / The short pattern belongs to the long one, never the reverse.
    /// <remarks>
    /// 这条判据专门抓"方向写反"：**两个方向都断言**，只断言一个方向的话，反转后的实现仍能通过。
    ///
    /// This criterion targets the inverted direction: **both directions are asserted**, because asserting only one lets the inverted implementation pass.
    /// </remarks>
    [Fact]
    public void PackagePattern_BelongShouldPointFromTheShortToTheLong() {
        var shortPattern = new PackagePattern("A");
        var longPattern = new PackagePattern("AB");

        shortPattern.Belong(longPattern).Should().BeTrue("A is a prefix of AB");
        longPattern.Belong(shortPattern).Should().BeFalse("AB is not a prefix of A");
    }

    /// **等长但不同内容时不属于** / Equal-length but different content does not belong.
    [Fact]
    public void PackagePattern_BelongShouldRejectDifferentEqualLengthCodes() {
        new PackagePattern("AB").Belong(new PackagePattern("AC")).Should().BeFalse();
        new PackagePattern("AB").Belong(new PackagePattern("AB")).Should().BeTrue("a code is its own prefix");
    }

    /// **包装编码与包装模式同语义** / The package code follows the same semantics as the pattern.
    [Fact]
    public void PackageCode_BelongShouldPointFromTheShortToTheLong() {
        var shortCode = new PackageCode("P1");
        var longCode = new PackageCode("P1-2");

        shortCode.Belong(longCode).Should().BeTrue();
        longCode.Belong(shortCode).Should().BeFalse();
    }

}
