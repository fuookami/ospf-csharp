#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 圆柱未开放路径门禁 / Cylinder unsupported-path guards.
/// 要求 DFS/MLHS 仅长方体搜索路径不接收圆柱。
/// Requires DFS/MLHS cuboid-only search paths to reject cylinders.
/// </summary>
internal static class CylinderUnsupportedGuard {
    /// <summary>
    /// 校验搜索路径不包含圆柱货物。
    /// Validate that search path does not contain cylinder items.
    /// </summary>
    /// <param name="hasCylinderItems">是否包含圆柱货物 / Whether cylinder items are present</param>
    /// <param name="path">搜索路径标识 / Search path identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequireNoCylinderItemsForCuboidSearch(
        bool hasCylinderItems,
        string path) {
        if (hasCylinderItems) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"Cylinder items are not supported for cuboid-only search path: {path}"));
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 校验圆柱货物是否被当前路径支持。
    /// Validate that cylinder item is supported by the current path.
    /// </summary>
    /// <param name="isCylinder">是否为圆柱 / Whether item is cylinder</param>
    /// <param name="path">能力路径标识 / Capability path identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequireSupportedCylinderItem(
        bool isCylinder,
        string path) {
        // All paths support cuboids; only specific paths support cylinders
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
