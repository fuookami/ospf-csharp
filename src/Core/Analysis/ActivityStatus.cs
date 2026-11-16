#nullable enable

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 原始模型成员的活动性状态 / Activity state of an original model member.
/// </summary>
/// <remarks>
/// 与**逐成员对应，顺序也一致**——它是枚举，顺序会进序列化与比较。
///
/// **违规与非活动明确分开**：非活动成员满足约束且离边界远，违规成员超出了声明的关系或值域。
/// 把两者合成一个"不活跃"，会让"这条约束没起作用"和"这条约束被违反了"看起来一样。
///
/// **member for member, order included** — it is an enum, and the order reaches serialization and comparison.
/// **A violation is kept distinct from an inactive member**: an inactive one is satisfied and far from its boundary, a violated one is outside the declared relation or domain.
/// Merging them into "not active" makes "this constraint did nothing" look like "this constraint was broken".
/// </remarks>
public enum ActivityStatus
{
    /// <summary>在配置容差内达到边界 / The boundary is reached within the configured tolerance.</summary>
    Active,

    /// <summary>接近边界但超出活动容差 / The boundary is close but outside the active tolerance.</summary>
    NearlyActive,

    /// <summary>满足约束且距离边界较远 / The member is satisfied and has material distance from its boundary.</summary>
    Inactive,

    /// <summary>成员满足，但不存在自然标量松弛 / The member is satisfied, but no natural scalar slack exists.</summary>
    SatisfiedWithoutSlackMetric,

    /// <summary>基线赋值违反成员语义 / The member is violated by the supplied baseline assignment.</summary>
    Violated,

    /// <summary>缺少赋值或无法求值 / The assignment was absent or could not be evaluated.</summary>
    Unknown,
}
