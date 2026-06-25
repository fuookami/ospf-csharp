#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerGeneration;
/// <summary>
/// 程序候选适配器接口 / Program candidate adapter interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IProgramCandidateAdapter<V> where V : struct, IFloatingNumber<V> {
    /// <summary>适配器名称 / Adapter name.</summary>
    string Name { get; }

    /// <summary>
    /// 生成候选 / Generate candidates.
    /// </summary>
    Task<Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>>> GenerateCandidatesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 层生成程序候选适配器 / Layer generation program candidate adapters.
/// 管理多个候选适配器 / Manages multiple candidate adapters.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class LayerGenerationProgramCandidateAdapters<V>
    where V : struct, IFloatingNumber<V> {
    private readonly List<IProgramCandidateAdapter<V>> _adapters = new();

    /// <summary>已注册适配器 / Registered adapters.</summary>
    public IReadOnlyList<IProgramCandidateAdapter<V>> Adapters => _adapters;

    /// <summary>
    /// 注册适配器 / Register adapter.
    /// </summary>
    public void Register(IProgramCandidateAdapter<V> adapter) => _adapters.Add(adapter);

    /// <summary>
    /// 生成所有候选 / Generate all candidates.
    /// </summary>
    public async Task<Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>>> GenerateAllCandidatesAsync(
        CancellationToken cancellationToken = default) {
        var allCandidates = new List<string>();
        foreach (IProgramCandidateAdapter<V> adapter in _adapters) {
            Result<IReadOnlyList<string>, ErrorCode, Error<ErrorCode>> result = await adapter.GenerateCandidatesAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsFailed) {
                return result;
            }
            allCandidates.AddRange(result.Value);
        }
        return Results.Ok<IReadOnlyList<string>>(allCandidates);
    }
}
