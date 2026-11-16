#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 货物状态。Item status for stowage planning.
/// </summary>
public enum ItemStatus
{
    Reserved,
    Optional,
    Preassigned,
    Loaded,
    AdjustmentNeeded
}

/// <summary>
/// 货位标签 / Item location tag.
/// </summary>
/// <remarks>
/// 逐项对齐的 `ItemLocationTag`（5 项）。位置合法性判断全靠这五个标签：
/// "主舱货不能进下舱"、"散货只能进散货舱"、"头舱货只能进头舱位"，都是标签在不在集合里。
///
/// `ItemLocationTag` member for member (5 entries). Every location-legality rule is a set membership over these five tags: "main cargo does not go to the
/// lower deck", "bulk only into bulk positions", "head cargo only into the head section".
/// </remarks>
public enum ItemLocationTag
{
    /// <summary>主舱位置 / Main deck location</summary>
    Main,

    /// <summary>下舱位置 / Lower deck location</summary>
    Low,

    /// <summary>散货位置 / Bulk cargo location</summary>
    Bulk,

    /// <summary>头部位置 / Head section location</summary>
    Head,

    /// <summary>尾部位置 / Tail section location</summary>
    Tail,
}

/// <summary>
/// 货物位置能力。Item location capability.
/// </summary>
/// <remarks>
/// 两个构造入口：**标签**（口径）与 **甲板集合**（C# 早期调用方的口径）。后者是按标签推导出来的
/// ——`Main` 标签给主甲板，`Low` 标签给两个下货舱——所以两条路走同一套判断，不会各说各话。
///
/// Two constructors: **tags** (its vocabulary) and a **deck set** (what earlier C# callers pass). The latter is derived from tags — the `Main` tag means the main deck and
/// the `Low` tag means both lower holds — so both paths go through the same rules instead of disagreeing.
/// </remarks>
public sealed class ItemLocation
{
    /// <summary>标签集合 / The tag set</summary>
    public IReadOnlySet<ItemLocationTag> Tags { get; }

    /// <summary>启用的甲板位置 / Enabled deck locations</summary>
    public IReadOnlySet<DeckLocation> EnabledLocations { get; }

    /// <summary>按甲板集合构造 / Construct from a deck set.</summary>
    /// <param name="enabledLocations">甲板集合 / The deck set.</param>
    public ItemLocation(IReadOnlySet<DeckLocation> enabledLocations)
    {
        EnabledLocations = enabledLocations;
        var tags = new HashSet<ItemLocationTag>();
        if (enabledLocations.Contains(DeckLocation.Main)) {
            tags.Add(ItemLocationTag.Main);
        }
        if (enabledLocations.Contains(DeckLocation.LowForward) || enabledLocations.Contains(DeckLocation.LowAft)) {
            tags.Add(ItemLocationTag.Low);
        }
        Tags = tags;
    }

    /// <summary>按标签构造 / Construct from tags.</summary>
    /// <param name="tags">标签集合 / The tag set.</param>
    public ItemLocation(IEnumerable<ItemLocationTag> tags)
    {
        Tags = new HashSet<ItemLocationTag>(tags);
        var locations = new HashSet<DeckLocation>();
        if (Tags.Contains(ItemLocationTag.Main)) {
            locations.Add(DeckLocation.Main);
        }
        if (Tags.Contains(ItemLocationTag.Low)) {
            locations.Add(DeckLocation.LowForward);
            locations.Add(DeckLocation.LowAft);
        }
        EnabledLocations = locations;
    }

    /// <summary>主舱货 / Main-deck cargo</summary>
    public static ItemLocation MainDeck { get; } = new(new[] { ItemLocationTag.Main });

    /// <summary>头舱货 / Head-section cargo</summary>
    public static ItemLocation Head { get; } = new(new[] { ItemLocationTag.Main, ItemLocationTag.Head });

    /// <summary>尾舱货 / Tail-section cargo</summary>
    public static ItemLocation Tail { get; } = new(new[] { ItemLocationTag.Main, ItemLocationTag.Tail });

    /// <summary>下舱散货 / Lower-deck bulk cargo</summary>
    public static ItemLocation LowBulk { get; } = new(new[] { ItemLocationTag.Low, ItemLocationTag.Bulk });

    /// <summary>下舱非散货 / Lower-deck non-bulk cargo</summary>
    public static ItemLocation LowNotBulk { get; } = new(new[] { ItemLocationTag.Low });

    /// <summary>是否带给定标签 / Whether the tag is present.</summary>
    /// <param name="tag">标签 / The tag.</param>
    /// <returns>是否带 / Whether present.</returns>
    public bool Contains(ItemLocationTag tag) => Tags.Contains(tag);

    /// <summary>主舱 / Main deck</summary>
    public bool IsMain => Contains(ItemLocationTag.Main);

    /// <summary>下舱 / Lower deck</summary>
    public bool IsLow => Contains(ItemLocationTag.Low);

    /// <summary>散货 / Bulk</summary>
    public bool IsBulk => Contains(ItemLocationTag.Bulk);

    /// <summary>头舱 / Head section</summary>
    public bool IsHead => Contains(ItemLocationTag.Head);

    /// <summary>尾舱 / Tail section</summary>
    public bool IsTail => Contains(ItemLocationTag.Tail);

    /// <summary>普通主舱（非头非尾）/ Plain main deck (neither head nor tail)</summary>
    public bool IsNormalMain => IsMain && !IsHead && !IsTail;

    /// <summary>特殊主舱（头或尾）/ Special main deck (head or tail)</summary>
    public bool IsSpecialMain => IsHead || IsTail;

    /// <summary>下舱非散货 / Lower deck, not bulk</summary>
    public bool IsLowNotBulk => IsLow && !IsBulk;

    /// <summary>是否在指定甲板位置启用 / Whether enabled in specified deck location.</summary>
    /// <param name="location">甲板位置 / The deck location.</param>
    /// <returns>是否启用 / Whether enabled.</returns>
    public bool EnabledIn(DeckLocation location) => EnabledLocations.Contains(location);

}

/// <summary>
/// 货物项。Cargo item for stowage planning.
/// </summary>
public sealed class Item
{
    /// <summary>货物名称 / Item name</summary>
    public string Name { get; }

    /// <summary>货物标识 / Item identifier</summary>
    /// <remarks>
    /// `Id` 是**标识**；C# 早期只有 `Name`，于是装载矩阵用名称做键，
    /// 同名两件货会共用一列。`Id` 与 `Name` 分开是为了让"显示用的名字"和"身份"不再纠缠。
    /// 不给 `id` 时回落到名称，老调用方一行都不用改——但**同名两件货仍然会共用一列**，这是回落口径的已知上限。
    ///
    /// `Id` is the identity; C# only had `Name`, so the stowage matrix keyed by name and two items sharing a name shared a
    /// column. Splitting `Id` from `Name` separates "display label" from "identity". Omitting `id` falls back to the name so existing callers need no change — but **two items with
    /// the same name still share a column**, a known ceiling of that fallback.
    /// </remarks>
    public string Id { get; }

    /// <summary>集装器 / The unit load device</summary>
    /// <remarks>
    /// `enabled` 的两条规则要用它（`enabledULDs` 与禁忌区间），此前 C# 的 `Item` 没有 `Uld`，两条都无从查起。
    ///
    /// `enabled` needs it for two rules (the `enabledULDs` check and the taboo range); C#'s `Item` had no `Uld`, so neither could be checked.
    /// </remarks>
    public ULD? Uld { get; }
    /// <summary>重量 / Weight (kg)</summary>
    public double Weight { get; }
    /// <summary>优先级 / Priority</summary>
    public CargoPriority Priority { get; }
    /// <summary>货物类型 / Cargo type</summary>
    /// <remarks>
    /// 单个货类是**主货类**的快捷方式，判定要用 <see cref="Cargos"/>：`enabled` 反复问
    /// "这件货是不是 AOG **或** MAT"，而一件货可以同时是两者（`AOGMATAppointed` 那条规则就是为此存在的）。
    ///
    /// The single type is a shortcut for the **primary** class; decide with <see cref="Cargos"/> instead: its `enabled` repeatedly asks "is this item AOG **or** MAT",
    /// and one item can be both — that is exactly why the `AOGMATAppointed` rule exists.
    /// </remarks>
    public CargoType CargoType { get; }

    /// <summary>全部货类 / Every cargo class of this item</summary>
    public IReadOnlySet<CargoType> Cargos { get; }
    /// <summary>始发站 / Source station</summary>
    public string Source { get; }
    /// <summary>目的站 / Destination station</summary>
    public string Destination { get; }
    /// <summary>状态 / Status</summary>
    public ItemStatus Status { get; }
    /// <summary>位置能力 / Location capability</summary>
    public ItemLocation Location { get; }
    /// <summary>是否必须装载 / Whether loading is mandatory</summary>
    public bool Required { get; }

    /// <summary>
    /// 是否属于给定货类码 / Whether this item belongs to the given cargo code.
    /// </summary>
    /// <param name="code">货类码 / The cargo code.</param>
    /// <returns>是否属于 / Whether it belongs.</returns>
    public bool Contains(CargoCode code) {
        foreach (CargoType cargo in Cargos) {
            if (cargo.Code == code) {
                return true;
            }
        }
        return false;
    }

    public Item(
        string name,
        double weight,
        CargoPriority priority,
        CargoType cargoType,
        string source,
        string destination,
        ItemStatus status,
        ItemLocation location,
        IReadOnlySet<CargoType>? cargos = null,
        string? id = null,
        ULD? uld = null,
        Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model.ItemOrder? order = null,
        bool required = false)
    {
        Name = name;
        Id = id ?? name;
        Uld = uld;
        Weight = weight;
        Priority = priority;
        CargoType = cargoType;
        // 不给就和主货类一致：这样老调用方一行都不用改，也没有"空货类"这种第三种状态。
        // Falls back to the primary class, so existing callers need no change and there is no third state called "no cargo class".
        Cargos = cargos ?? new HashSet<CargoType> { cargoType };
        Source = source;
        Destination = destination;
        Status = status;
        Location = location;
        // **显式的必装标志**：状态推断不出"必须装载"（`Optional` 的货可以是必装、`Loaded` 的货也可以只是已装）。
        // **An explicit required flag**: status cannot infer "must load" (an `Optional` cargo can still be mandatory).
        Required = required || status is not (ItemStatus.Optional or ItemStatus.Reserved);
        Order = order;
    }

    /// <summary>
    /// 作业顺序 / The operational sequence.
    /// </summary>
    /// <remarks>
    /// **可空**：不是每件货物都有作业时刻（的 `loading_effectiveness` 域引入）。为 `null` 时
    /// `ItemAheadLoadLimit` 的三个谓词全不成立，`ItemReweighNeededLimit` 把它当作"尚未复重"收进目标。
    ///
    /// **Nullable**: not every item carries operational instants (introduced by the `loading_effectiveness` domain). When null, `ItemAheadLoadLimit`'s three predicates all
    /// hold false and `ItemReweighNeededLimit` treats it as "not yet reweighed" and includes it in the objective.
    /// </remarks>
    public Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model.ItemOrder? Order { get; }

    /// <summary>
    /// 名字 / The name.
    /// </summary>
    /// <remarks>
    /// **必须覆写**：Kotlin data class 自动生成基于构造属性的 `toString`，而 C# 的 `class` 默认给类型全名。
    /// 中间符号名是"货物 + 舱位"拼出来的，不覆写会让**每个**货物得到同一个名字，模型注册时直接报
    /// `Repeated "…"`（实测）。这不是命名美观问题，是符号唯一性问题。
    ///
    /// **Must override**: its Kotlin data class generates `toString` from its constructor properties, while a C# `class` defaults to the full type name. Intermediate symbol
    /// names concatenate item and position, so not overriding gives **every** item the same name and model registration fails with `Repeated "…"` (measured). This is symbol uniqueness,
    /// not cosmetics.
    /// </remarks>
    /// <returns>货物名 / The item name.</returns>
    public override string ToString() => Name;
}
