// 装箱程序工厂与材料合并的判据 / Criteria for the packing-program factories and material merging.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Tests.Application;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **外层材料是子包装的合并，缺值不等于零，量纲不符必须报错** / An outer map merges its children, absent is not zero, and a dimension clash must surface.
/// </summary>
/// <remarks>
/// 三条都是"静默做错也看不出来"的地方：覆盖式合并留下最后一箱、缺值当零让总重凭空变小、
/// 量纲不符时取一侧让总重凭空变化。判据分别打这三处。
///
/// All three are places where a wrong implementation is silent: an overwriting merge keeps the last box, treating absent as zero shrinks the total, and taking one side of
/// a dimension clash changes the total for no reason. Each criterion hits one of them.
/// </remarks>
public class PackingProgramTests {
    /// **外层材料表是子包装的数量之和** / An outer material map sums its children's amounts.
    [Fact]
    public void OuterPackage_ShouldSumAmountsAcrossChildren() {
        var first = PackingProgram.InnerPackage(
            Shape(), new Dictionary<string, ulong> { ["M1"] = 2UL, ["M2"] = 1UL });
        var second = PackingProgram.InnerPackage(
            Shape(), new Dictionary<string, ulong> { ["M1"] = 3UL });

        PackingProgram outer = PackingProgram.OuterPackage(Shape(), new List<PackingProgram> { first, second })
            .Unwrap();

        outer.Materials.Should().NotBeNull();
        outer.Materials!["M1"].Amount.Should().Be(5UL, "2 plus 3 of the same material");
        outer.Materials["M2"].Amount.Should().Be(1UL);
        outer.MaterialAmount().Should().Be(6UL);
        outer.Packages.Should().HaveCount(2);
    }

    /// **同一材料的重量量纲安全相加** / Weights of the same material add dimension-safely.
    [Fact]
    public void OuterPackage_ShouldAddWeights() {
        PackingProgram first = PackingProgram.InnerPackage(
            Shape(), new Dictionary<string, ulong> { ["M1"] = 1UL })
            with { Materials = Values(("M1", 1UL, Kilograms(2.0))) };
        PackingProgram second = PackingProgram.InnerPackage(
            Shape(), new Dictionary<string, ulong> { ["M1"] = 1UL })
            with { Materials = Values(("M1", 1UL, Kilograms(3.0))) };

        PackingProgram outer = PackingProgram.OuterPackage(Shape(), new List<PackingProgram> { first, second })
            .Unwrap();

        outer.Materials!["M1"].Weight!.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-12);
        outer.Materials["M1"].Weight!.Unit.Should().Be(SIBaseUnits.Kilogram);
    }

    /// <summary>
    /// **两侧都缺时结果是"未知"，而不是零** / When both amounts are absent the result is "unknown", not zero.
    /// </summary>
    /// <remarks>
    /// 这条判据的第一版**没抓住任何东西**：它只测"有值 + 缺值"，而"把缺值当 0"在这种输入下算出来一模一样
    /// （`2 + 0 == 2`），变异体照样通过。真正的分界在**两侧都缺**：正确结果不带数量（未知），
    /// 错误结果是数量 `0`——而 `0` 会被读成"这个材料一箱都没有"，混进下游总量。
    ///
    /// The first version of this criterion **caught nothing**: it only exercised "present + absent", where treating absent as zero computes exactly the same answer
    /// (`2 + 0 == 2`), so the mutant passed. The real divider is **both sides absent**: the correct result carries no amount (unknown), the wrong one carries `0` — and
    /// that `0` reads as "this material has none in the box" and leaks into downstream totals.
    /// </remarks>
    [Fact]
    public void MergeMaterialValue_ShouldYieldUnknownWhenBothAmountsAreAbsent() {
        PackingProgramMaterialValue missing = new(null);

        PackingProgram.MergeMaterialValue(missing, missing).Unwrap().Amount
            .Should().BeNull("unknown plus unknown is still unknown, not zero");
        PackingProgram.MergeMaterialValue(null, missing).Unwrap().Amount
            .Should().BeNull("a missing left-hand side must not become zero either");
    }

    /// **有值时缺值不改变结果** / When a value is present, an absent side leaves it alone.
    [Fact]
    public void MergeMaterialValue_ShouldKeepTheKnownAmount() {
        PackingProgramMaterialValue existing = new(2UL);
        PackingProgramMaterialValue missingAmount = new(null);

        PackingProgram.MergeMaterialValue(existing, missingAmount).Unwrap().Amount.Should().Be(2UL);
        PackingProgram.MergeMaterialValue(missingAmount, existing).Unwrap().Amount
            .Should().Be(2UL, "the order of the operands must not matter");
    }

    /// **重量量纲不兼容必须失败而不是取一侧** / A weight dimension clash must fail instead of taking one side.
    [Fact]
    public void MergeMaterialValue_ShouldFailOnIncompatibleWeightDimensions() {
        PackingProgramMaterialValue kilograms = new(1UL, Kilograms(2.0));
        PackingProgramMaterialValue metres = new(1UL, Metres(3.0));

        Result<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> merged =
            PackingProgram.MergeMaterialValue(kilograms, metres);

        merged.Should().BeOfType<Failed<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>>>(
            "kg plus m is not a weight; picking one side would change the total silently");
        ((Failed<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>>)merged).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// **内层包装没有子包装，材料来自 ulong 映射** / An inner program has no children and takes its materials from an amount map.
    [Fact]
    public void InnerPackage_ShouldHaveNoChildren() {
        var inner = PackingProgram.InnerPackage(
            Shape(), new Dictionary<string, ulong> { ["M1"] = 4UL });

        inner.Packages.Should().BeNull("an inner program has no children");
        inner.Pattern.Should().BeNull();
        inner.Materials!["M1"].Amount.Should().Be(4UL);
        inner.Materials["M1"].Weight.Should().BeNull("only the amount was given");
    }

    /// <summary>构造包装形状 / Build a package shape.</summary>
    /// <returns>形状 / The shape.</returns>
    private static PackageShape Shape() => new(
        Metres(1.0), Metres(1.0), Metres(1.0), Kilograms(1.0), PackageType.Box);

    /// <summary>构造材料值映射 / Build a material value map.</summary>
    /// <param name="entries">键、数量与重量 / Key, amount and weight.</param>
    /// <returns>映射 / The map.</returns>
    private static IReadOnlyDictionary<string, PackingProgramMaterialValue> Values(
        params (string Key, ulong Amount, Quantity<FltX>? Weight)[] entries) {
        Dictionary<string, PackingProgramMaterialValue> map = new();
        foreach ((string key, ulong amount, Quantity<FltX>? weight) in entries) {
            map[key] = new PackingProgramMaterialValue(amount, weight);
        }
        return map;
    }

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Metres(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Kilograms(double value) => new(new FltX(value), SIBaseUnits.Kilogram);
}
