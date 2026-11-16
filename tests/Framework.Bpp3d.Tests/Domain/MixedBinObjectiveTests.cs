#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **两个箱型目标：跳过条件与系数，以及那条已经过期的"搬不了"注释** /
/// The two bin objectives — skip conditions and coefficients — and the stale "cannot be ported" note.
/// </summary>
/// <remarks>
/// **这段代码原来是"搬不了"，理由是依赖 `PreciseAssignment`，而那个类一直在仓库里。** **所以这里的第一件事不是写新代码，
/// 是<b>把那句过期结论改对**；**判据顺手把它钉住：** **文件里不得再出现"PreciseAssignment（C# 侧不存在）"这类断言。**
///
/// **其余判据钉基线逐条照搬的三个跳过条件：** **成本表为空、权重为零、该箱型未配置成本——第三种<b>不进多项式**，
/// 而不是以 0 系数进去。**
///
/// **This code used to be "cannot be ported" because of a `PreciseAssignment` dependency that has been in the repository all along.** **So the first job of
/// is not new code but correcting that stale conclusion, and a criterion pins it: the file may no longer assert that
/// `PreciseAssignment` is missing.** **The rest pin the baseline's three skip conditions: empty cost table, zero weight, and a type without a configured
/// cost — the third never enters the polynomial, rather than entering with a coefficient of zero.**
/// </remarks>
public class MixedBinObjectiveTests
{
    /// <summary>**"搬不了"的理由已经不成立，注释也已更正** / The stated blocker no longer holds and the note was corrected.</summary>
    [Fact]
    public void TheStaleBlockerNote_ShouldBeGone() {
        string root = RepositoryRoot();
        string path = System.IO.Path.Combine(
            root, "src", "Framework.Bpp3d", "Domain", "Packing", "MixedBinObjectives.cs");
        System.IO.File.Exists(path).Should().BeTrue("the file holding the waste statistics and cost lookup");

        string text = System.IO.File.ReadAllText(path);
        // **判据禁的是那句<b>断言**（"依赖……（C# 侧不存在），见文末说明"），不是它被引用时的字面。**
        // **更正说明里必须能引用原话，否则"改对了"和"删掉了"就没法区分。**
        //
        // **The criterion forbids the original assertion sentence, not the phrase quoted inside the correction.** **A correction has to be able to quote
        // what it corrects, otherwise "corrected" and "deleted" become indistinguishable.**
        text.Should().NotContain(
            "基线里的两个 `*Minimization` 类依赖 `PreciseAssignment`（C# 侧不存在），见文末说明。",
            "the type exists in this very repository, so the note claiming otherwise is a stale conclusion");
        // **判据要的是"更正被记下来"，不是"某个批次号还在"。** **叙述清理把批次号去掉了，** '
        // **而这条规则本身没变：** **一个被改正过的结论必须留下更正，** **悄悄删掉不教任何人。**
        //
        // **What this needs is "the correction is recorded", not "a batch id is still there".** The narration cleanup removed the batch id and **the rule is unchanged: a claim that was
        // corrected has to keep its correction — deleting it silently teaches nobody.**
        text.Should().Contain(
            "**更正：**",
            "the correction has to be recorded, not silently deleted; a removed claim teaches nobody");

        System.IO.File.Exists(System.IO.Path.Combine(
                root, "src", "Framework.Bpp3d", "Domain", "LayerAssignment", "Model", "PreciseAssignment.cs"))
            .Should().BeTrue("the dependency the old note called missing");
    }

    /// <summary>**成本目标的三个跳过条件** / The three skip conditions of the cost objective.</summary>
    [Fact]
    public void TheCostObjective_ShouldSkipWhatTheBaselineSkips() {
        BinType<Flt64> bundled = BinType("40GP", 2.0, 2.0, 2.0);
        BinType<Flt64> expensive = BinType("40HQ", 3.0, 3.0, 3.0);

        var empty = new BinTypeCostMinimization(new Dictionary<string, Flt64>());
        empty.ShouldRegister.Should().BeFalse("an empty cost table registers nothing");
        empty.CoefficientFor(bundled).Should().BeNull("no table means no term");

        var zeroWeight = new BinTypeCostMinimization(
            new Dictionary<string, Flt64> { ["40GP"] = new Flt64(5.0) },
            new Flt64(0.0));
        zeroWeight.ShouldRegister.Should().BeFalse("a zero weight registers nothing");
        zeroWeight.CoefficientFor(bundled).Should().BeNull();

        var priced = new BinTypeCostMinimization(
            new Dictionary<string, Flt64> { ["40GP"] = new Flt64(5.0) },
            new Flt64(2.0));
        priced.CoefficientFor(bundled).Should().Be(
            new Flt64(10.0),
            "the coefficient is the weight times the configured cost");
        priced.CoefficientFor(expensive).Should().BeNull(
            "an unconfigured type never enters the polynomial; a zero term would read as \"this bin type is free\"");
    }

    /// <summary>**配置为零成本的箱型同样不进多项式** / A type priced at zero is skipped the same way.</summary>
    /// <remarks>
    /// **这是基线 `mapIndexedNotNull` 的另一半：** **它先判"未配置"，再判"系数为零"，两件事都会让该项消失。**
    /// **只判前者会让"配了零价"变成一条 0 系数项，而求和里它是噪声。**
    ///
    /// **This is the other half of the baseline's `mapIndexedNotNull`: it checks "not configured" and then "coefficient is zero", and either removes the
    /// term.** **Checking only the first would turn "priced at zero" into a zero coefficient, which is noise in the sum.**
    /// </remarks>
    [Fact]
    public void AZeroCost_ShouldAlsoBeSkipped() {
        var objective = new BinTypeCostMinimization(
            new Dictionary<string, Flt64> { ["free"] = Flt64.Zero },
            new Flt64(3.0));

        objective.ShouldRegister.Should().BeTrue("the table is non-empty and the weight is non-zero");
        objective.CoefficientFor(BinType("free", 1.0, 1.0, 1.0)).Should().BeNull(
            "a zero coefficient is left out of the polynomial rather than added as a noise term");
    }

    /// <summary>**体积目标的系数是"权重 × 体积"** / The volume objective's coefficient is weight times volume.</summary>
    [Fact]
    public void TheVolumeObjective_ShouldScaleTheVolume() {
        BinType<Flt64> type = BinType("40GP", 2.0, 2.0, 3.0);

        new BinVolumeMinimization().CoefficientFor(type).Should().Be(
            new Flt64(12.0),
            "the default weight is one and the volume is 2 * 2 * 3");
        new BinVolumeMinimization(new Flt64(0.5)).CoefficientFor(type).Should().Be(
            new Flt64(6.0),
            "a half weight halves the coefficient");
        new BinVolumeMinimization(Flt64.Zero).ShouldRegister.Should().BeFalse(
            "a zero weight registers nothing");
        new BinVolumeMinimization(Flt64.Zero).CoefficientFor(type).Should().BeNull();
    }

    /// <summary>**按箱型取价的缺省值由调用方决定** / The per-type cost default is the caller's decision.</summary>
    [Fact]
    public void ThePerTypeCost_ShouldUseTheCallersDefault() {
        Func<BinType<Flt64>, Flt64> cost = PerTypeBinCost.Of(new Dictionary<string, Flt64> { ["40GP"] = new Flt64(5.0) });

        cost(BinType("40GP", 1.0, 1.0, 1.0)).Should().Be(new Flt64(5.0), "the configured price is used");
        cost(BinType("unknown", 1.0, 1.0, 1.0)).Should().Be(
            Flt64.Zero,
            "with no default given, an unconfigured type costs zero — the caller decides, not the helper");

        Func<BinType<Flt64>, Flt64> withDefault = PerTypeBinCost.Of(
            new Dictionary<string, Flt64> { ["40GP"] = new Flt64(5.0) },
            new Flt64(9.0));
        withDefault(BinType("unknown", 1.0, 1.0, 1.0)).Should().Be(new Flt64(9.0), "an explicit default applies");
    }

    private static BinType<Flt64> BinType(string typeCode, double width, double height, double depth) => new(
        typeCode,
        new Quantity<Flt64>(new Flt64(width), SIBaseUnits.Meter),
        new Quantity<Flt64>(new Flt64(height), SIBaseUnits.Meter),
        new Quantity<Flt64>(new Flt64(depth), SIBaseUnits.Meter),
        new Quantity<Flt64>(new Flt64(width * height * depth), VolumeUnits.CubicMeter));

    private static string RepositoryRoot() {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the repository root holds the solution file");
        return directory!.FullName;
    }
}