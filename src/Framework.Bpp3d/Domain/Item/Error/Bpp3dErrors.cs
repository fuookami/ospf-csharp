#nullable enable

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error
{
    /// <summary>
    /// BPP3D 领域错误码集合 / BPP3D domain error codes.
    /// 所有 Result.Error 引用其常量 / All Result.Error reference its constants.
    /// </summary>
    public static class Bpp3dErrors
    {
        /// <summary>影子价格键为空 / Null shadow price key.</summary>
        public static readonly ErrorCode NullShadowPriceKey = ErrorCode.IllegalArgument;
        /// <summary>影子价格未找到 / Shadow price not found.</summary>
        public static readonly ErrorCode ShadowPriceNotFound = ErrorCode.DataNotFound;
        /// <summary>非有限求解器值 / Non-finite solver value.</summary>
        public static readonly ErrorCode NonFiniteSolverValue = ErrorCode.IllegalArgument;
        /// <summary>空层列 / Null layer column.</summary>
        public static readonly ErrorCode NullLayerColumn = ErrorCode.IllegalArgument;
        /// <summary>空装载列 / Null load column.</summary>
        public static readonly ErrorCode NullLoadColumn = ErrorCode.IllegalArgument;
        /// <summary>空分配列 / Null assignment column.</summary>
        public static readonly ErrorCode NullAssignmentColumn = ErrorCode.IllegalArgument;
        /// <summary>空块 / Null block.</summary>
        public static readonly ErrorCode NullBlock = ErrorCode.IllegalArgument;
        /// <summary>空物料 / Null material.</summary>
        public static readonly ErrorCode NullMaterial = ErrorCode.IllegalArgument;
        /// <summary>空货物 / Null item.</summary>
        public static readonly ErrorCode NullItem = ErrorCode.IllegalArgument;
        /// <summary>空箱型 / Null bin type.</summary>
        public static readonly ErrorCode NullBinType = ErrorCode.IllegalArgument;
    }
}
