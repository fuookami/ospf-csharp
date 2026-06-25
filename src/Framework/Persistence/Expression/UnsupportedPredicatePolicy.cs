#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Persistence.Expression;
/// <summary>
/// 不支持的谓词策略 / Unsupported predicate policy.
/// </summary>
public enum UnsupportedPredicatePolicy {
    /// <summary>快速失败 / Fail fast</summary>
    FailFast,
    /// <summary>始终返回 false / Always return false</summary>
    AlwaysFalse,
    /// <summary>客户端过滤 / Client-side filter</summary>
    ClientFilter
}

/// <summary>
/// 谓词翻译结果（密封）/ Predicate translation result (sealed).
/// </summary>
public abstract record PredicateTranslation<T> where T : class {
    /// <summary>成功翻译 / Successful translation.</summary>
    public sealed record Success : PredicateTranslation<T> {
        public object? TranslatedPredicate { get; init; }
    }

    /// <summary>不支持的谓词 / Unsupported predicate.</summary>
    public sealed record Unsupported : PredicateTranslation<T> {
        public IReadOnlyList<UnsupportedPredicateDetail> Details { get; init; } = [];
    }
}

/// <summary>
/// 不支持的谓词详情 / Unsupported predicate detail.
/// </summary>
public sealed record UnsupportedPredicateDetail(
    string Predicate,
    string Reason,
    UnsupportedPredicatePolicy Policy);
