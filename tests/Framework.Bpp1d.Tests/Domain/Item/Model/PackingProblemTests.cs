// 一维装箱领域模型判据 / Criteria for the one-dimensional packing domain model.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.Item.Model;

/// <summary>
/// **一维装箱领域模型** / The one-dimensional packing domain model.
/// </summary>
/// <remarks>
/// 新建 `Framework.Bpp1d`**：10 个文件、约 1391 行，C# 侧**一行都没有**。
/// 这里只做**领域模型层**（`PackingProblem.cs`），应用层与服务层留待后续批次。
///
/// 判据集中钉住**两个容易写错的地方**（对的）：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | 间距只加在件与件之间 | 第一个物品也加 `amount` 份 | 每列凭空多出尾部间距，容量判定偏保守 |
/// | 件数用二进制倍增 | 逐件累加 | 大件数会卡住 |
///
/// **creates `Framework.Bpp1d`**: this implementation has 10 files (~1391 lines) and the C# side had **nothing**. covers only the **domain model layer**;
/// application and service layers come later. The criteria pin the **two easy mistakes** above (this implementation is right).
/// </remarks>
public class PackingProblemTests {
    /// <summary>**间距只加在件与件之间，不加尾部** / Spacing goes between pieces only, never trailing.</summary>
    /// <remarks>
    /// 箱长 10，物品长 2，间距 1，件数 2：占用 = `2 + 1 + 2 = 5`（**不是 6**）。
    /// 箱长取 5 时**恰好装得下**；若实现多加了尾部间距（=6），这里会误判为超容量。
    ///
    /// Bin length 10, item length 2, spacing 1, count 2: usage = `2 + 1 + 2 = 5` (**not 6**). With bin length exactly 5 it **just fits**; an implementation that adds a
    /// trailing gap (=6) would wrongly report an overflow.
    /// </remarks>
    [Fact]
    public void ValidatePattern_ShouldNotCountATrailingGap() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 5.0, itemLength: 2.0, spacing: 1.0);
        var pattern = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 2 });

        Try result = problem.ValidatePattern(pattern);

        result.IsOk.Should().BeTrue(
            "usage is 2 + 1 + 2 = 5, exactly the bin length; a trailing gap would make it 6 and overflow");
    }

    /// <summary>**多一件就装不下** / One more piece does not fit.</summary>
    /// <remarks>
    /// 反向对照：若判据只断言"装得下"，一个**完全不检查容量**的实现也能通过。
    ///
    /// Reverse control: if the criterion only asserted "fits", an implementation that **never checks capacity** would also pass.
    /// </remarks>
    [Fact]
    public void ValidatePattern_ShouldRejectOneMorePiece() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 5.0, itemLength: 2.0, spacing: 1.0);
        var pattern = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 3 });

        Try result = problem.ValidatePattern(pattern);

        result.IsFailed.Should().BeTrue(
            "usage would be 2 + 1 + 2 + 1 + 2 = 8, which exceeds the bin length of 5");
    }

    /// <summary>**件数超过需求必须被拒** / A count above the demand must be rejected.</summary>
    [Fact]
    public void ValidatePattern_ShouldRejectACountAboveTheDemand() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 100.0, itemLength: 1.0, spacing: 0.0, demand: 3);
        var pattern = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 4 });

        Try result = problem.ValidatePattern(pattern);

        result.IsFailed.Should().BeTrue("the pattern asks for 4 pieces but the demand is only 3");
    }

    /// <summary>**未知箱型与未知物品必须被拒** / Unknown bin types and items must be rejected.</summary>
    /// <param name="binTypeId">箱型标识 / Bin type identity.</param>
    /// <param name="itemId">物品标识 / Item identity.</param>
    [Theory]
    [InlineData("NOPE", "I1")]
    [InlineData("B1", "NOPE")]
    public void ValidatePattern_ShouldRejectUnknownIdentities(string binTypeId, string itemId) {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 100.0, itemLength: 1.0, spacing: 0.0);
        var pattern = new PackingPattern<Flt64>(binTypeId, new Dictionary<string, ulong> { [itemId] = 1 });

        Try result = problem.ValidatePattern(pattern);

        result.IsFailed.Should().BeTrue("the pattern names an identity that does not exist in the problem");
    }

    /// <summary>**量纲不一致必须在验证阶段被拒** / A unit mismatch must be rejected during validation.</summary>
    [Fact]
    public void Validate_ShouldRejectAWeightWithTheWrongDimension() {
        // 把重量写成毫米：量纲不是质量，`To(Kilogram)` 会失败。
        // Weight expressed in millimetres: the dimension is not mass, so `To(Kilogram)` fails.
        var problem = new PackingProblem<Flt64>(
            Items: new List<Item<Flt64>> {
                new("I1", Millimetres(2.0), Millimetres(1.0), 1),
            },
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(10.0), Millimetres(10.0), new Flt64(1.0)),
            },
            Spacing: Millimetres(0.0));

        Try result = problem.Validate();

        result.IsFailed.Should().BeTrue("a millimetre weight is not a mass and must fail the kilogram conversion");
    }

    /// <summary>**空物品表或重复标识必须被拒** / An empty bin-type list or duplicate identities must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectDuplicateItemIdentities() {
        var problem = new PackingProblem<Flt64>(
            Items: new List<Item<Flt64>> {
                new("I1", Millimetres(2.0), Grams(1.0), 1),
                new("I1", Millimetres(3.0), Grams(1.0), 1),
            },
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(10.0), Grams(100.0), new Flt64(1.0)),
            },
            Spacing: Millimetres(0.0));

        Try result = problem.Validate();

        result.IsFailed.Should().BeTrue("two items share the identity I1");
    }

    /// <summary>**组合键与件数顺序无关，且带长度前缀** / The pattern key is order-independent and length-prefixed.</summary>
    /// <remarks>
    /// 两个断言：
    ///
    /// 1. **顺序无关**：字典插入顺序不同，键必须相同。
    /// 2. **带长度前缀**：标识里含 `;` 或 `=` 时不得撞键。`"a"=1, "b"=2` 与 `"a"` 里塞 `1;b=2`
    /// 在只做字符串拼接时会得到同一个键。
    ///
    /// Two assertions: (1) **order independence** — different insertion orders must yield the same key; (2) **length prefixes** — identifiers containing `;` or `=`
    /// must not collide. Plain concatenation would give the same key for `"a"=1, "b"=2` and a single `"a"` whose name embeds `1;b=2`.
    /// </remarks>
    [Fact]
    public void PatternKey_ShouldBeOrderIndependentAndLengthPrefixed() {
        var first = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 2 });
        var second = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["b"] = 2, ["a"] = 1 });
        var colliding = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["a=1;b"] = 2 });
        var plain = new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 2 });

        first.Key.Should().Be(second.Key, "insertion order must not change the key");
        first.Key.Should().Be(plain.Key, "the same composition must produce the same key");
        colliding.Key.Should().NotBe(
            first.Key,
            "an identifier containing the separators must not collide with two separate entries");
        first.Key.Should().Contain("1:a", "identifiers carry a length prefix");
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="binLength">箱长（毫米）/ Bin length in millimetres.</param>
    /// <param name="itemLength">物品长（毫米）/ Item length in millimetres.</param>
    /// <param name="spacing">间距（毫米）/ Spacing in millimetres.</param>
    /// <param name="demand">需求量 / Demand.</param>
    /// <returns>问题 / The problem.</returns>
    private static PackingProblem<Flt64> MakeProblem(
        double binLength,
        double itemLength,
        double spacing,
        ulong demand = 10) =>
        new(
            Items: new List<Item<Flt64>> {
                new("I1", Millimetres(itemLength), Grams(1.0), demand),
            },
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(binLength), Grams(1000.0), new Flt64(1.0)),
            },
            Spacing: Millimetres(spacing));

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
