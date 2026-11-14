#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;

/// <summary>
/// 一个候选方案的跨贡献判定投影：**余宽**与**每个产品的贡献**。
/// The cross-contribution projection of one candidate: its **rest width** and its **contribution per product**.
/// </summary>
/// <remarks>
/// 基线 `canCrossContributionDominate` 直接读切割方案的余宽与需求贡献。C# 侧生成收集器是**泛型**，
/// 无法直接读那些字段。
///
/// **本类型是绕开那个障碍的最小手段**：判定逻辑只需要"余宽 + 产品贡献表"，不需要整个切割方案。
/// 把它抽成一个不依赖具体方案类型的投影，判定就能被单独实现、单独测试，
/// **而不必先给三个空壳生成器加泛型约束**。
///
/// The baseline's `canCrossContributionDominate` reads the plan's rest width and demand contributions directly. On the C# side the generation collector is
/// **generic** and cannot read those fields.
///
/// **This type is the minimal way around that obstacle**: the decision needs only "rest width + contribution table", not the whole plan. Extracting it as
/// a projection independent of the concrete plan type lets the decision be implemented and tested **without first adding generic constraints to the three
/// shell generators**.
/// </remarks>
/// <param name="RestWidth">余宽的规范量（含单位与数值）/ Canonical measure of the rest width (unit and value).</param>
/// <param name="ContributionsByProduct">按产品 ID 的贡献规范量 / Contribution canonical measure by product id.</param>
public sealed record CrossContributionProjection(
    CrossContributionMeasure? RestWidth,
    IReadOnlyDictionary<string, CrossContributionMeasure> ContributionsByProduct
);

/// <summary>
/// 一个可比较的物理量：单位键 + 数值文本 + 可比较数值。
/// A comparable measure: unit key, value text, and a comparable value.
/// </summary>
/// <remarks>
/// **为什么带可比较数值而不是只用 double 存原始值**：物理量的数值类型是泛型，
/// 直接转 double 会丢精度且需要额外的类型约束。调用方自己提供"可比较数值"最省事，
/// 而单位键保留为字符串用于**单位一致性检查**。
///
/// **Why carry a comparable value rather than a raw double**: the physical quantity's value type is generic; converting to double loses precision and needs
/// extra constraints. Letting the caller supply the comparable value is cheapest, while the unit key stays a string for the **unit-consistency check**.
/// </remarks>
/// <param name="UnitKey">单位键 / Unit key.</param>
/// <param name="ValueText">数值文本（用于消息与诊断）/ Value text (for messages and diagnostics).</param>
/// <param name="Comparable">可比较数值 / Comparable value.</param>
public sealed record CrossContributionMeasure(
    string UnitKey,
    string ValueText,
    double Comparable
);
