#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Functional;
// ==================== Type aliases (mirrors ospf-kotlin Predicate.kt typealiases) ====================
// These are C# delegates (using Func/Action) — no typealias needed.
// Listed here for documentation of the 1:1 mapping.

// Predicate<T>                    = Func<T, bool>               (BCL)
// TryPredicate<T>                 = Func<T, Ret<bool>>
// SuspendPredicate<T>             = Func<T, Task<bool>>
// SuspendTryPredicate<T>          = Func<T, Task<Ret<bool>>>
// IndexedPredicate<T>             = Func<int, T, bool>
// TryIndexedPredicate<T>          = Func<int, T, Ret<bool>>
// SuspendIndexedPredicate<T>      = Func<int, T, Task<bool>>
// SuspendTryIndexedPredicate<T>   = Func<int, T, Task<Ret<bool>>>
// Extractor<R,T>                  = Func<T, R>
// TryExtractor<R,T>               = Func<T, Ret<R>>
// SuspendExtractor<R,T>           = Func<T, Task<R>>
// SuspendTryExtractor<R,T>        = Func<T, Task<Ret<R>>>
// IndexedExtractor<R,T>           = Func<int, T, R>
// TryIndexedExtractor<R,T>        = Func<int, T, Ret<R>>
// SuspendIndexedExtractor<R,T>    = Func<int, T, Task<R>>
// SuspendTryIndexedExtractor<R,T> = Func<int, T, Task<Ret<R>>>
// Mapper<R,T>                     = Func<T, R>
// TryMapper<R,T>                  = Func<T, Ret<R>>
// KComparator<T>                  = IComparer<T>
// Comparator<T>                   = Func<T, T, bool>
// PartialComparator<T>            = Func<T, T, bool?>
// TryComparator<T>                = Func<T, T, Ret<bool>>
// Generator<R>                    = Func<R?>
// ThreeWayComparator<T>           = Func<T, T, Order>
// PartialThreeWayComparator<T>    = Func<T, T, Order?>
// TryThreeWayComparator<T>        = Func<T, T, Ret<Order>>

/// <summary>Predicate 辅助方法 / Predicate helper methods.</summary>
public static class PredicateHelpers {
    /// <summary>从 IComparer 创建三路比较器 / Create three-way comparator from IComparer.</summary>
    public static Func<T, T, Order> ThreeWay<T>(IComparer<T> comparer) =>
        (lhs, rhs) => OrderHelpers.OrderOf(comparer.Compare(lhs, rhs));
}
