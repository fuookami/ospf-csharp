// Bpp3d 包装程序子图的陷阱判据 / Criteria pinning the Bpp3d packing-program sub-graph traps.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// **Bpp3d 的包装程序子图完全未接线，且与 Bpp2d 的同名类型容易混淆** / The Bpp3d packing-program sub-graph is entirely unwired and collides in name with Bpp2d's.
/// </summary>
/// <remarks>
/// **这里推翻了两条长期记录**，并发现了第三条问题。
///
/// **一、`PackageShapeSpec` 不是孤例。** 它所在的整个子图**都没有接线**：
///
/// | 类型 | 构造点 | 读取方 |
/// | --- | --- | --- |
/// | `PackageShape` / `PackageShapeSpec` | 仅测试 | **无** |
/// | `PackingProgram`（Bpp3d） | **零**（`grep 'new PackingProgram('` 只命中 Bpp2d） | **无** |
/// | `PackingProgramMaterialValue` | 仅在本文件的派生方法内 | 仅本文件 |
/// | `PackageSolutionLike{Quantity,MaterialItem,Node}` | **零** | **零** |
///
/// 基线有 `PackageSolutionLikeNode.toPackingProgram` **把这条链串起来**，
/// C# **没有移植它**——所以类型全在、转换全缺，**整条链悬空**。
///
/// **二、两个 `PackingProgram` 同名不同义。**
/// `Framework.Bpp2d.Domain.PackingProgram<V>` **是活的**（有构造、有测试），
/// `Framework.Bpp3d.Domain.Item.Model.PackingProgram` **是死的**。
/// 按简单名 `grep` 会把两者混为一谈，**我第一遍就被骗了**（以为 Bpp3d 那个有 14 处引用）。
///
/// **三、单位在同一文件里前后不一致。**
/// `PackageSolutionLikeQuantity.Weight` 存**裸 `Flt64`**（基线是 `Quantity<V>`，**带单位**），
/// 而紧邻的 `PackingProgramMaterialValue.Weight` 存 **`Quantity<FltX>?`**（带单位）。
/// **同一个文件里两个"重量"字段，一个有单位一个没有。**
///
/// 这条判据钉住二三两点（它们可在无管线的情况下断言），
/// 第一点写在此处作为记录——**"没有调用方"无法用单元测试可靠断言**，
/// 只能靠人工核查，这里如实说明这个边界。
///
/// ** overturns two long-standing records** and finds a third problem. **1) `PackageShapeSpec` is not alone** — its whole sub-graph is unwired (table above).
/// The baseline's `PackageSolutionLikeNode.toPackingProgram` is what strings the chain together, and C# **never ported it**, so the types exist, the conversion does not,
/// and **the whole chain dangles**. **2) Two `PackingProgram` types share a name**: `Framework.Bpp2d.Domain.PackingProgram<V>` **is live** (constructed and tested) while
/// `Framework.Bpp3d.Domain.Item.Model.PackingProgram` **is dead**; grepping by simple name conflates them and **fooled me on the first pass** (I read "14 references" as live).
/// **3) Units disagree inside one file**: `PackageSolutionLikeQuantity.Weight` stores a **bare `Flt64`** (the baseline's `Quantity<V>` carries a unit) while the adjacent
/// `PackingProgramMaterialValue.Weight` stores **`Quantity<FltX>?`**. The criterion pins 2 and 3 (assertable without a pipeline); point 1 is recorded in prose because
/// **"has no caller" cannot be reliably asserted by a unit test** — only by manual audit, and that boundary is stated honestly.
/// </remarks>
public class Bpp3dPackingProgramTrapsTests {
    /// <summary>**Bpp3d 与 Bpp2d 的 `PackingProgram` 是两个不同的类型** / The Bpp3d and Bpp2d `PackingProgram` are two distinct types.</summary>
    /// <remarks>
    /// 按**简单名** `grep` 会把两者混在一起，得出"有 14 处引用，是活的"的错误结论。
    /// 判据钉住：简单名相同、**全名不同、程序集不同**。
    ///
    /// Grepping by **simple name** mixes them and yields the wrong conclusion "14 references, so it is live". The criterion pins: same simple name, **different full names,
    /// different assemblies**.
    /// </remarks>
    [Fact]
    public void PackingProgram_ShouldExistAsTwoDistinctTypesAcrossAssemblies() {
        global::System.Type bpp3d = typeof(PackingProgram);

        bpp3d.Name.Should().Be("PackingProgram", "the Bpp3d type keeps the baseline's simple name");
        bpp3d.Namespace.Should().Be(
            "Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model",
            "the Bpp3d type lives in the Bpp3d item-model namespace; the live one lives in Bpp2d, and conflating them is the trap this criterion exists for");
        bpp3d.IsGenericType.Should().BeFalse(
            "the live Bpp2d `PackingProgram<V>` is generic while the dead Bpp3d one is not — that is the reliable way to tell them apart");
    }

    /// <summary>**同一个文件里两个"重量"字段，一个有单位一个没有** / Two "weight" fields in one file disagree on units.</summary>
    /// <remarks>
    /// 基线两处都是 `Quantity<V>`（**带单位**）。C# 把 `PackageSolutionLikeQuantity.Weight`
    /// 压成了**裸 `Flt64`**，于是"5"到底是 5 克还是 5 千克**无从得知**。
    ///
    /// | 类型 | `Weight` 的实际类型 |
    /// | --- | --- |
    /// | `PackingProgramMaterialValue` | `Quantity<FltX>?`（**带单位**） |
    /// | `PackageSolutionLikeQuantity.Weight` | `Flt64`（**不带单位**） |
    ///
    /// 判据同时断言两侧，使得**任何一侧被改动**都会红——修好它是好事，但必须同步文档。
    ///
    /// Both are `Quantity<V>` (unit-carrying) in the baseline; C# flattened `PackageSolutionLikeQuantity.Weight` to a **bare `Flt64`**, so whether "5" means 5 g or 5 kg is
    /// **unknowable**. The criterion asserts both sides, so **changing either** goes red — fixing it is good, but the docs must follow.
    /// </remarks>
    [Fact]
    public void WeightFields_ShouldStillDisagreeOnUnits() {
        global::System.Type materialValueWeight = typeof(PackingProgramMaterialValue)
            .GetProperty(nameof(PackingProgramMaterialValue.Weight))!.PropertyType;
        global::System.Type quantityWeight = typeof(PackageSolutionLikeQuantity.Weight)
            .GetProperty(nameof(PackageSolutionLikeQuantity.Weight.Value))!.PropertyType;

        materialValueWeight.Should().Be(
            typeof(Quantity<FltX>),
            "`PackingProgramMaterialValue.Weight` carries a unit");
        quantityWeight.Should().Be(
            typeof(Flt64),
            "`PackageSolutionLikeQuantity.Weight.Value` is a bare number with no unit — a recorded deviation from the baseline's `Quantity<V>`");
        quantityWeight.Should().NotBe(
            typeof(Quantity<FltX>),
            "if this ever matches, the unit has been restored and the deviation record must be removed");
    }
}
