// 包装分类 / Package classification.
// 与固定 Kotlin 基线 `infrastructure/PackageType.kt` 中 `PackageClassification` 部分对应，语义取自
// `item/model/Package.kt` 的推导规则（`packages.isNullOrEmpty() ? Inner : Outer`）。
// Mirrors `PackageClassification` from the pinned baseline's `PackageType.kt`; the derivation rule comes from
// `Package.kt` (`packages.isNullOrEmpty() ? Inner : Outer`).
//
// **与基线的一处已记录分歧**：基线的 `PackageType` 是七种包装形式（DutyCorrugatedBoardPedal／WoodenContainer／
// HoneycombBox／Pallet／CartonPallet／CartonContainer／PackingFoam）加一个 `category` 映射，而 C# 侧早已存在一个**不同概念**
// 的同名枚举（`Box`／`Bag`／`Barrel`／`Bundle`／`Pallet`／`Roll`，按形状分类）。两者不是同一个东西，不能合并：
// 前者描述"用的是什么包装工艺"，后者描述"包装的几何形态"。C# 的 `PackageCategory`（Standard／Irregular／Cylindrical）
// 服务于几何与排布判断，因此**保留现状**，只在注释里记下分歧，不引入第二套 `PackageType` 去撞名。
//
// **A recorded divergence from the baseline**: the baseline's `PackageType` is seven packaging processes
// (DutyCorrugatedBoardPedal / WoodenContainer / HoneycombBox / Pallet / CartonPallet / CartonContainer / PackingFoam) plus a
// `category` mapping, whereas C# already has a **different concept** under the same name (`Box` / `Bag` / `Barrel` /
// `Bundle` / `Pallet` / `Roll`, classified by shape). They are not the same thing and cannot be merged: the first describes
// "which packaging process", the second "the package's geometric form". C#'s `PackageCategory` (Standard / Irregular /
// Cylindrical) serves geometry and layout decisions, so the current state is **kept**, the divergence is recorded here, and
// no second `PackageType` is introduced to collide with the name.

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 包装分类，表示包装是外包装还是内包装 / Package classification: whether the package is for outer or inner use.
/// </summary>
public enum PackageClassification {
    /// <summary>外包装 / Outer packaging.</summary>
    Outer,

    /// <summary>内包装 / Inner packaging.</summary>
    Inner,
}

/// <summary>
/// 包装分类推导 / Package classification derivation.
/// </summary>
public static class PackageClassificationRules {
    /// <summary>
    /// 根据子包装列表判断是内层还是外层包装 / Derive the classification from whether sub-packages exist.
    /// </summary>
    /// <remarks>
    /// **有子包装 = 外包装，无子包装 = 内包装**。这条规则来自基线 `Package.kt`：外包装本身不承载物料，它只是把若干内包装
    /// 聚合成一个装载单元，因此其"实际物料用量"要靠子包装折算；内包装才是物料的真正持有者。判反会让物料用量折算到错误的
    /// 层级上，表现为用料统计整体偏大或偏小。
    /// **Sub-packages present = outer, absent = inner.** The rule comes from the baseline's `Package.kt`: an outer package
    /// carries no material itself and only aggregates inner packages into one loading unit, so its "actual material usage"
    /// must be derived from its children; an inner package is the real material holder. Getting this backwards charges
    /// material usage to the wrong level, showing up as usage totals that are globally too high or too low.
    /// </remarks>
    /// <param name="hasSubPackages">是否存在子包装 / Whether any sub-package exists.</param>
    /// <returns>包装分类 / The package classification.</returns>
    public static PackageClassification Of(bool hasSubPackages) =>
        hasSubPackages ? PackageClassification.Outer : PackageClassification.Inner;

    /// <summary>
    /// 根据子包装数量判断分类，null 与空列表都视为无子包装 / Derive from a sub-package count; null and empty both mean none.
    /// </summary>
    /// <remarks>
    /// null 与空列表**必须同等对待**：基线用的是 `packages.isNullOrEmpty()`，若把 null 当成"未知"再单独分支，会多出一条
    /// 与空列表不一致的路径。
    /// null and an empty list **must be treated identically**: the baseline uses `packages.isNullOrEmpty()`, and treating null
    /// as "unknown" with its own branch would create a path inconsistent with the empty case.
    /// </remarks>
    /// <param name="subPackageCount">子包装数量，null 视为无 / Sub-package count, null meaning none.</param>
    /// <returns>包装分类 / The package classification.</returns>
    public static PackageClassification OfCount(int? subPackageCount) => Of(subPackageCount is { } count && count > 0);

    /// <summary>
    /// 判断是否为外包装 / Whether the classification is outer.
    /// </summary>
    /// <param name="classification">包装分类 / Package classification.</param>
    /// <returns>是否为外包装 / Whether outer.</returns>
    public static bool IsOuter(PackageClassification classification) =>
        classification == PackageClassification.Outer;
}
