#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Monomial;
/// <summary>
/// 规范单项式幂向量的优化键 / Optimized key for canonical monomial power vectors.
/// 稠密模式（单个 int[]）或稀疏模式（索引 int[] + 幂 int[]），按稀疏度自动选择。
/// </summary>
public sealed class PowerVectorKey {
    private const double SparsityThreshold = 0.5;

    private readonly int[]? _denseVec;
    private readonly int[]? _sparseIndices;
    private readonly int[]? _sparsePowers;

    /// <summary>预计算的哈希值 / Precomputed hash value.</summary>
    public int Hash { get; }

    /// <summary>是否为稠密模式 / Whether using dense mode.</summary>
    public bool IsDense => _denseVec is not null;

    /// <summary>是否为稀疏模式 / Whether using sparse mode.</summary>
    public bool IsSparse => _sparseIndices is not null;

    private PowerVectorKey(int[]? denseVec, int[]? sparseIndices, int[]? sparsePowers, int hash) {
        _denseVec = denseVec;
        _sparseIndices = sparseIndices;
        _sparsePowers = sparsePowers;
        Hash = hash;
    }

    /// <summary>创建稠密模式键 / Creates a dense-mode key.</summary>
    public static PowerVectorKey Dense(int[] vec)
        => new(vec, null, null, ComputeArrayHash(vec));

    /// <summary>创建稀疏模式键 / Creates a sparse-mode key.</summary>
    public static PowerVectorKey Sparse(int[] indices, int[] powers) {
        if (indices.Length != powers.Length) {
            throw new ArgumentException("Indices and powers size mismatch");
        }

        return new(null, indices, powers, ComputeSparseHash(indices, powers));
    }

    /// <summary>按稀疏度自动选模式 / Auto-select mode based on sparsity.</summary>
    public static PowerVectorKey Create(
        IReadOnlyDictionary<ISymbol, int> powers,
        IReadOnlyDictionary<ISymbol, int> symbolIndex,
        int totalSymbols) {
        int size = powers.Count;
        double sparsity = (double)size / totalSymbols;
        if (totalSymbols <= 5 || sparsity >= SparsityThreshold) {
            int[] vec = new int[totalSymbols];
            foreach ((ISymbol? s, int p) in powers) {
                vec[symbolIndex[s]] = p;
            }

            return Dense(vec);
        }
        else {
            var entries = powers.OrderBy(kv => symbolIndex[kv.Key]).ToList();
            int[] indices = entries.Select(kv => symbolIndex[kv.Key]).ToArray();
            int[] powersArr = entries.Select(kv => kv.Value).ToArray();
            return Sparse(indices, powersArr);
        }
    }

    /// <summary>从键还原幂映射 / Reconstruct power map from key.</summary>
    public IReadOnlyDictionary<ISymbol, int> ToPowers(IReadOnlyList<ISymbol> symbolList) {
        var result = new Dictionary<ISymbol, int>();
        if (IsDense) {
            for (int i = 0; i < _denseVec!.Length; i++) {
                if (_denseVec[i] != 0) {
                    result[symbolList[i]] = _denseVec[i];
                }
            }
        }
        else if (IsSparse) {
            for (int i = 0; i < _sparseIndices!.Length; i++) {
                result[symbolList[_sparseIndices[i]]] = _sparsePowers![i];
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Hash;

    /// <inheritdoc/>
    public override bool Equals(object? obj) {
        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj is not PowerVectorKey other) {
            return false;
        }

        if (Hash != other.Hash) {
            return false;
        }

        if (IsDense && other.IsDense) {
            return _denseVec!.SequenceEqual(other._denseVec!);
        }

        if (IsSparse && other.IsSparse) {
            return _sparseIndices!.SequenceEqual(other._sparseIndices!)
                && _sparsePowers!.SequenceEqual(other._sparsePowers!);
        }

        return false;   // different modes are never equal
    }

    /// <inheritdoc/>
    public override string ToString() => IsDense
        ? $"PowerVectorKey(dense=[{string.Join(",", _denseVec!)}])"
        : IsSparse
            ? $"PowerVectorKey(sparse=indices=[{string.Join(",", _sparseIndices!)}], powers=[{string.Join(",", _sparsePowers!)}])"
            : "PowerVectorKey(empty)";

    private static int ComputeArrayHash(int[] vec) {
        int h = 1;
        foreach (int v in vec) {
            h = 31 * h + v;
        }

        return h;
    }

    private static int ComputeSparseHash(int[] indices, int[] powers) {
        int h = 1;
        for (int i = 0; i < indices.Length; i++) {
            h = 31 * h + indices[i];
            h = 31 * h + powers[i];
        }
        return h;
    }
}
