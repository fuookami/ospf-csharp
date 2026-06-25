#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Model.Intermediate;
/// <summary>内存清理策略（内部）/ Memory cleanup policy (internal)</summary>
internal static class MemoryCleanupPolicy {
    /// <summary>清理可释放对象 / Clean up disposable objects</summary>
    /// <param name="disposables">可释放对象集合 / Disposable object collection</param>
    public static void CleanupDisposables(IEnumerable<IDisposable> disposables) {
        foreach (IDisposable disposable in disposables) {
            try {
                disposable.Dispose();
            }
            catch {
                // Suppress exceptions during cleanup
            }
        }
    }

    /// <summary>清理可释放对象列表 / Clean up disposable list</summary>
    /// <param name="disposables">可释放对象列表 / Disposable object list</param>
    public static void CleanupDisposables(IList<IDisposable> disposables) {
        for (int i = disposables.Count - 1; i >= 0; i--) {
            try {
                disposables[i].Dispose();
            }
            catch {
                // Suppress exceptions during cleanup
            }
        }
        disposables.Clear();
    }

    /// <summary>清理字典值中的可释放对象 / Clean up disposable values in dictionary</summary>
    /// <typeparam name="TKey">键类型 / Key type</typeparam>
    /// <param name="dictionary">字典 / Dictionary</param>
    public static void CleanupDictionaryValues<TKey>(IDictionary<TKey, IDisposable> dictionary)
        where TKey : notnull {
        foreach (KeyValuePair<TKey, IDisposable> kvp in dictionary) {
            try {
                kvp.Value.Dispose();
            }
            catch {
                // Suppress exceptions during cleanup
            }
        }
        dictionary.Clear();
    }
}
