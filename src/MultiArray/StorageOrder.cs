#nullable enable

using System;

namespace Fuookami.Ospf.MultiArray;
/// <summary>
/// 存储顺序枚举
/// Storage order enum
/// </summary>
public enum StorageOrder {
    /// <summary>
    /// 行主序（C 风格），最后一个维度变化最快
    /// Row-major (C style), last dimension varies fastest
    /// </summary>
    RowMajor,

    /// <summary>
    /// 列主序（Fortran 风格），第一个维度变化最快
    /// Column-major (Fortran style), first dimension varies fastest
    /// </summary>
    ColumnMajor
}

/// <summary>
/// 存储顺序默认值提供者
/// Storage order default value provider
/// </summary>
public static class StorageOrders {
    /// <summary>
    /// 默认存储顺序（行主序）
    /// Default storage order (row-major)
    /// </summary>
    public const StorageOrder Default = StorageOrder.RowMajor;
}
