#nullable enable

namespace Fuookami.Ospf.Core.Token;

/// <summary>
/// 符号表缓存键，用于复合缓存查找。
/// Token table cache key for composite cache lookups.
/// </summary>
/// <param name="SymbolIndex">符号索引 / Symbol index</param>
/// <param name="CacheDimension">缓存维度 / Cache dimension</param>
public sealed record TokenCacheKey(
    int SymbolIndex,
    string CacheDimension);
