#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 圆柱能力状态 / Cylinder capability status.
/// 描述系统对圆柱体货物的支持程度。
/// </summary>
public enum CylinderCapabilityStatus {
    /// <summary>仅支持长方体 / Cuboid only.</summary>
    CuboidOnly,
    /// <summary>仅支持垂直候选 / Vertical candidate only.</summary>
    VerticalCandidateOnly,
    /// <summary>轴感知候选 / Axis-aware candidate.</summary>
    AxisAwareCandidate,
    /// <summary>已验证生成放置 / Verified generated placement.</summary>
    VerifiedGeneratedPlacement,
    /// <summary>仅正立垂直支撑 / Upright vertical support only.</summary>
    UprightVerticalSupportOnly,
    /// <summary>已知坐标最终验证 / Known coordinate final validation.</summary>
    KnownCoordinateFinalValidation,
    /// <summary>深度边界最终验证 / Depth boundary final validation.</summary>
    DepthBoundaryFinalValidation
}

/// <summary>
/// 圆柱能力路径 / Cylinder capability path.
/// 标识圆柱能力检查的来源。
/// </summary>
public enum CylinderCapabilityPath {
    DefaultLayerCandidate,
    CirclePackingCandidate,
    ApplicationLayerPlacementCandidate,
    PileSupportCandidate,
    PackageAttributeSupport,
    SimpleBlockCandidate,
    DfsMlhsCuboidSearch,
    ItemMerge,
    ItemMergePiles,
    ItemMergeBlocks,
    ItemMergePatternBlocks,
    ItemMergeHollowSquareBlocks,
    PatternPlacement,
    KnownCoordinateFinalPacking,
    RendererFinalPacking,
    DepthBoundaryFinalValidation
}

/// <summary>
/// 连续圆柱半径优化差距 / Continuous cylinder radius optimization gap.
/// </summary>
public enum ContinuousCylinderRadiusOptimizationGap {
    MissingSelectedRadius,
    DiscreteRadiusMetadataConflict,
    SolverNativeRadiusIntervalUnsupported,
    SolverNativeDiameterIntervalUnsupported
}

/// <summary>
/// 圆柱能力合约辅助方法 / Cylinder capability contract helpers.
/// </summary>
public static class CylinderShapeContractHelpers {
    /// <summary>是否有圆柱货物 / Whether items contain cylinders.</summary>
    public static bool HasCylinderItem(System.Collections.Generic.IEnumerable<ActualItem> items) {
        foreach (ActualItem item in items) {
            if (item.PackageCategory == PackageCategory.Cylindrical) return true;
        }
        return false;
    }

    /// <summary>不支持的圆柱轴消息 / Unsupported cylinder axis message.</summary>
    public static string UnsupportedCylinderAxisMessage(Axis3 axis) =>
        $"Cylinder axis {axis} is not supported for this operation.";

    /// <summary>不支持的圆柱方向消息 / Unsupported cylinder orientation message.</summary>
    public static string UnsupportedCylinderOrientationMessage(Orientation orientation) =>
        $"Cylinder orientation {orientation} is not supported.";

    /// <summary>仅长方体路径不支持圆柱 / Cuboid-only path does not support cylinders.</summary>
    public static string UnsupportedCylinderCuboidOnlyPathMessage(CylinderCapabilityPath path) =>
        $"Path {path} does not support cylinder items (cuboid-only).";
}
